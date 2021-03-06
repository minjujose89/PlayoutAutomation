﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using TAS.Server;
using System.Windows;
using System.Reflection;
using System.ComponentModel;
using System.Runtime.Remoting.Messaging;
using System.Windows.Input;
using TAS.Common;
using TAS.Client.Common;
using TAS.Server.Interfaces;
using TAS.Server.Common;
using resources = TAS.Client.Common.Properties.Resources;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace TAS.Client.ViewModels
{
    public class EventEditViewmodel : ViewmodelBase, ITemplatedEdit, IDataErrorInfo
    {
        private readonly IEngine _engine;
        private readonly EngineViewmodel _engineViewModel;
        private readonly PreviewViewmodel _previewViewModel;
        public EventEditViewmodel(EngineViewmodel engineViewModel, PreviewViewmodel previewViewModel)
        {
            _engineViewModel = engineViewModel;
            _previewViewModel = previewViewModel; 
            if (previewViewModel != null)
                previewViewModel.PropertyChanged += PreviewViewModel_PropertyChanged;
            _engine = engineViewModel.Engine;
            _fields.CollectionChanged += _fields_or_commands_CollectionChanged;
            CommandSaveEdit = new UICommand() { ExecuteDelegate = _save, CanExecuteDelegate = _canSave };
            CommandUndoEdit = new UICommand() { ExecuteDelegate = _load, CanExecuteDelegate = o => IsModified };
            CommandChangeMovie = new UICommand() { ExecuteDelegate = _changeMovie, CanExecuteDelegate = _canChangeMovie };
            CommandEditMovie = new UICommand() { ExecuteDelegate = _editMovie, CanExecuteDelegate = _canEditMovie };
            CommandCheckVolume = new UICommand() { ExecuteDelegate = _checkVolume, CanExecuteDelegate = _canCheckVolume };
            CommandEditField = new UICommand { ExecuteDelegate = _editField };
            CommandTriggerStartType = new UICommand { ExecuteDelegate = _triggerStartType, CanExecuteDelegate = _canTriggerStartType };
            CommandMoveUp = new UICommand() { ExecuteDelegate = o => _event?.MoveUp(), CanExecuteDelegate = _canMoveUp };
            CommandMoveDown = new UICommand() { ExecuteDelegate = o => _event?.MoveDown(), CanExecuteDelegate = _canMoveDown };
            CommandDelete = new UICommand
            {
                ExecuteDelegate = o =>
                {
                    if (_event != null && MessageBox.Show(resources._query_DeleteItem, resources._caption_Confirmation, MessageBoxButton.OKCancel) == MessageBoxResult.OK)
                    {
                        EventClipboard.SaveUndo(new IEvent[] { _event }, _event.StartType == TStartType.After ? _event.Prior : _event.Parent );
                        _event.Delete();
                    }
                },
                CanExecuteDelegate = o => _event?.AllowDelete() == true
            };
        }

        private void _fields_or_commands_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            IsModified = true;
        }

        protected override void OnDispose()
        {
            if (_event != null)
                Event = null;
            if (_previewViewModel != null)
                _previewViewModel.PropertyChanged -= PreviewViewModel_PropertyChanged;
            _fields.CollectionChanged -= _fields_or_commands_CollectionChanged;
        }

        private void PreviewViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_previewViewModel.LoadedMedia == this.Media
                && IsEditEnabled
                && (e.PropertyName == nameof(PreviewViewmodel.TcIn) || e.PropertyName == nameof(PreviewViewmodel.TcOut))
                && _previewViewModel.SelectedSegment == null)
            {
                ScheduledTc = _previewViewModel.TcIn;
                Duration = _previewViewModel.DurationSelection;
            }
        }
        
        public ICommand CommandUndoEdit { get; private set; }
        public ICommand CommandSaveEdit { get; private set; }
        public ICommand CommandMoveUp { get; private set; }
        public ICommand CommandMoveDown { get; private set; }
        public ICommand CommandChangeMovie { get; private set; }
        public ICommand CommandEditMovie { get; private set; }
        public ICommand CommandCheckVolume { get; private set; }
        public ICommand CommandToggleEnabled { get; private set; }
        public ICommand CommandToggleHold { get; private set; }
        public ICommand CommandTriggerStartType { get; private set; }
        public ICommand CommandDelete { get; private set; }


        private IEvent _event;
        public IEvent Event
        {
            get { return _event; }
            set
            {
                IEvent ev = _event;
                if (ev != null && ev.Engine != _engine)
                    throw new InvalidOperationException("Edit event engine invalid");
                if (value != ev)
                {
                    if (this.IsModified
                    && MessageBox.Show(String.Format(resources._query_SaveChangedData, this), resources._caption_Confirmation, MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                        _save(null);
                    if (ev != null)
                    {
                        ev.PropertyChanged -= _eventPropertyChanged;
                        ev.SubEventChanged -= _onSubeventChanged;
                        ev.Relocated -= _onRelocated;
                    }
                    _event = value;
                    if (value != null)
                    {
                        value.PropertyChanged += _eventPropertyChanged;
                        value.SubEventChanged += _onSubeventChanged;
                        value.Relocated += _onRelocated;
                    }
                    _load(null);
                }
            }
        }

        void _save(object o)
        {
            IEvent e2Save = Event;
            if (IsModified && e2Save != null)
            {
                PropertyInfo[] copiedProperties = this.GetType().GetProperties();
                foreach (PropertyInfo copyPi in copiedProperties)
                {
                    PropertyInfo destPi = e2Save.GetType().GetProperty(copyPi.Name);
                    if (destPi != null)
                    {
                        if (destPi.GetValue(e2Save, null) != copyPi.GetValue(this, null)
                            && destPi.CanWrite
                            && destPi.PropertyType.Equals(copyPi.PropertyType))
                            destPi.SetValue(e2Save, copyPi.GetValue(this, null), null);
                    }
                }
                IsModified = false;
            }
            if (e2Save != null && e2Save.IsModified)
            {
                e2Save.Save();
                _load(null);
            }
        }

        void _load(object o)
        {
            _isLoading = true;
            try
            {
                IEvent e2Load = _event;
                if (e2Load != null)
                {
                    PropertyInfo[] copiedProperties = this.GetType().GetProperties();
                    foreach (PropertyInfo copyPi in copiedProperties)
                    {
                        PropertyInfo sourcePi = e2Load.GetType().GetProperty(copyPi.Name);
                        if (sourcePi != null
                            && copyPi.Name != nameof(IsModified)
                            && sourcePi.PropertyType.Equals(copyPi.PropertyType)
                            && copyPi.CanWrite
                            && sourcePi.CanRead)
                            copyPi.SetValue(this, sourcePi.GetValue(e2Load, null), null);
                    }
                }
                else // _event is null
                {
                    PropertyInfo[] zeroedProperties = this.GetType().GetProperties();
                    foreach (PropertyInfo zeroPi in zeroedProperties)
                    {
                        PropertyInfo sourcePi = typeof(IEvent).GetProperty(zeroPi.Name);
                        if (sourcePi != null)
                            zeroPi.SetValue(this, null, null);
                    }
                }
            }
            finally
            {
                _isLoading = false;
                IsModified = false;
            }
            NotifyPropertyChanged(null);
        }

        private void CommandScriptEdit_Modified(object sender, EventArgs e)
        {
            IsModified = true;
        }

        private void _readProperty(string propertyName)
        {
            IEvent e2Read = _event;
            PropertyInfo writingProperty = this.GetType().GetProperty(propertyName);
            if (e2Read != null)
            {
                PropertyInfo sourcePi = e2Read.GetType().GetProperty(propertyName);
                if (sourcePi != null
                    && writingProperty.Name != nameof(IsModified)
                    && sourcePi.PropertyType.Equals(writingProperty.PropertyType))
                    writingProperty.SetValue(this, sourcePi.GetValue(e2Read, null), null);
            }
            else
                writingProperty.SetValue(this, null, null);
        }

        bool _isLoading;
        protected override bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (base.SetField(ref field, value, propertyName))
            {
                if (!_isLoading &&
                    (propertyName != nameof(ScheduledTime) || IsStartEvent))
                    IsModified = true;
                return true;
            }
            return false;
        }

        public string Error
        {
            get { return String.Empty;}
        }

        public string this[string propertyName]
        {
            get
            {
                string validationResult = null;
                switch (propertyName)
                {
                    case nameof(Duration):
                        validationResult = _validateDuration();
                        break;
                    case nameof(ScheduledTc):
                        validationResult = _validateScheduledTc();
                        break;
                    case nameof(ScheduledTime):
                    case nameof(ScheduledTimeOfDay):
                    case nameof(ScheduledDate):
                        validationResult = _validateScheduledTime();
                        break;
                    case nameof(TransitionTime):
                        validationResult = _validateTransitionTime();
                        break;
                    case nameof(TransitionPauseTime):
                        validationResult = _validateTransitionPauseTime();
                        break;
                    case nameof(ScheduledDelay):
                        validationResult = _validateScheduledDelay();
                        break;
                    case nameof(Command):
                        validationResult = EventExtensions.IsValidCommand(_command) ? string.Empty : resources._validate_CommandSyntax;
                        break;
                }
                return validationResult;
            }
        }

        private string _validateScheduledDelay()
        {
            var ev = _event;
            if (ev != null && (ev.EventType == TEventType.StillImage || ev.EventType == TEventType.CommandScript))
            {
                IEvent parent = ev.Parent;
                if (parent != null && _duration + _scheduledDelay > parent.Duration)
                    return resources._validate_ScheduledDelayInvalid;
            }
            return null;
        }

        private string _validateScheduledTime()
        {
            IEvent ev = _event;
            if (ev != null
                && (((_startType == TStartType.OnFixedTime && ((_autoStartFlags & AutoStartFlags.Daily) == AutoStartFlags.None))
                    || _startType == TStartType.Manual)
                    && ev.PlayState == TPlayState.Scheduled && _scheduledTime < ev.Engine.CurrentTime))
                return resources._validate_StartTimePassed;
            return string.Empty;
        }

        private string _validateScheduledTc()
        {
            IEvent ev = _event;
            if (ev != null)
            {
                IMedia media = _event.Media;
                if (ev.EventType == TEventType.Movie && media != null)
                {
                    if (_scheduledTc > media.Duration + media.TcStart)
                        return string.Format(resources._validate_StartTCAfterFile, (media.Duration + media.TcStart).ToSMPTETimecodeString(_engine.VideoFormat));
                    if (_scheduledTc < media.TcStart)
                        return string.Format(resources._validate_StartTCBeforeFile, media.TcStart.ToSMPTETimecodeString(_engine.VideoFormat));
                }
            }
            return null;
        }

        private string _validateDuration()
        {
            IEvent ev = _event;
            if (ev != null)
            {
                IMedia media = _event.Media;
                if (ev.EventType == TEventType.Movie && media != null
                    && _duration + _scheduledTc > media.Duration + media.TcStart)
                    return resources._validate_DurationInvalid;
                if (ev.EventType == TEventType.StillImage || ev.EventType == TEventType.CommandScript)
                {
                    IEvent parent = ev.Parent;
                    if (parent != null && _duration + _scheduledDelay > parent.Duration)
                        return resources._validate_ScheduledDelayInvalid;
                }
            }
            return null;
        }

        private string _validateTransitionPauseTime()
        {
            string validationResult = string.Empty;
            if (_transitionPauseTime > _transitionTime)
                validationResult = resources._validate_TransitionPauseTimeInvalid;
            return validationResult;
        }

        private string _validateTransitionTime()
        {
            string validationResult = string.Empty;
            if (_transitionTime > _duration)
                    validationResult = resources._validate_TransitionTimeInvalid;
            return validationResult;
        }

        MediaSearchViewmodel _mediaSearchViewModel;
        private void _chooseMedia(TMediaType mediaType, IEvent baseEvent, TStartType startType, VideoFormatDescription videoFormatDescription = null)
        {
            if (_mediaSearchViewModel == null)
            {
                _mediaSearchViewModel = new MediaSearchViewmodel(_engineViewModel.Engine, _event.Engine.MediaManager, mediaType, VideoLayer.Program, true, videoFormatDescription);
                _mediaSearchViewModel.BaseEvent = baseEvent;
                _mediaSearchViewModel.NewEventStartType = startType;
                _mediaSearchViewModel.MediaChoosen += _mediaSearchViewModelMediaChoosen;
                _mediaSearchViewModel.SearchWindowClosed += _searchWindowClosed;
            }
        }
        
        private void _searchWindowClosed(object sender, EventArgs e)
        {
            MediaSearchViewmodel mvs = (MediaSearchViewmodel)sender;
            mvs.SearchWindowClosed -= _searchWindowClosed;
            mvs.MediaChoosen -= _mediaSearchViewModelMediaChoosen;
            _mediaSearchViewModel.Dispose();
            _mediaSearchViewModel = null;
        }        

        private void _mediaSearchViewModelMediaChoosen(object o, MediaSearchEventArgs e)
        {
            if (e.Media != null)
            {
                if (e.Media.MediaType == TMediaType.Movie)
                {
                    Media = e.Media;
                    Duration = e.Duration;
                    ScheduledTc = e.TCIn;
                    AudioVolume = null;
                    EventName = e.MediaName;
                    _setCGElements(e.Media);
                }
            }
        }

        private IMedia _media;
        public IMedia Media
        {
            get { return _media; }
            set
            {
                SetField(ref _media, value);
            }
        }

        #region Command methods


        private void _triggerStartType(object obj)
        {
            if (StartType == TStartType.Manual)
                StartType = TStartType.OnFixedTime;
            else
            if (StartType == TStartType.OnFixedTime)
                StartType = TStartType.Manual;
        }

        private bool _canTriggerStartType(object obj)
        {
            return StartType == TStartType.Manual || StartType == TStartType.OnFixedTime;
        }

        private void _editField(object obj)
        {
            var editObject = obj ?? SelectedField;
            if (editObject != null)
            {
                var kv = (KeyValuePair<string, string>)editObject;
                var kve = new KeyValueEditViewmodel((KeyValuePair<string, string>)editObject, true);
                if (kve.ShowDialog() == true)
                    _fields[kve.Key] = kve.Value;
            }
        }


        void _changeMovie(object o)
        {
            IEvent ev = _event;
            if (ev != null
                && ev.EventType == TEventType.Movie)
            {
                _chooseMedia(TMediaType.Movie, ev, ev.StartType);
            }
        }

        private void _editMovie(object obj)
        {
            using (var evm = new MediaEditWindowViewmodel(_event.Media, _engine.MediaManager))
                evm.ShowDialog();
        }

        private void _checkVolume(object obj)
        {
            if (_media == null)
                return;
            IsVolumeChecking = true;
            var fileManager = _engine.MediaManager.FileManager;
            var operation = fileManager.CreateLoudnessOperation();
            operation.SourceMedia = _event.Media;
            operation.MeasureStart = _event.StartTc - _media.TcStart;
            operation.MeasureDuration = _event.Duration;
            operation.AudioVolumeMeasured += _audioVolumeMeasured;
            operation.Finished += _audioVolumeFinished;
            fileManager.Queue(operation, true);
        }

        private void _audioVolumeFinished(object sender, EventArgs e)
        {
            IsVolumeChecking = false;
            ((ILoudnessOperation)sender).Finished -= _audioVolumeFinished;
            ((ILoudnessOperation)sender).AudioVolumeMeasured -= _audioVolumeFinished;
        }

        private void _audioVolumeMeasured(object sender, AudioVolumeEventArgs e)
        {
            AudioVolume = e.AudioVolume;
        }

        bool _canReschedule(object o)
        {
            IEvent ev = _event;
            return ev != null
                && (ev.PlayState == TPlayState.Played || ev.PlayState == TPlayState.Aborted);
        }
        bool _canChangeMovie(object o)
        {
            IEvent ev = _event;
            return ev != null
                && ev.PlayState == TPlayState.Scheduled
                && ev.EventType == TEventType.Movie;
        }
        bool _canEditMovie(object o)
        {
            IEvent ev = _event;
            return ev != null
                && ev.Media != null
                && ev.PlayState == TPlayState.Scheduled
                && ev.EventType == TEventType.Movie;
        }
        bool _canCheckVolume(object o)
        {
            return !_isVolumeChecking && _canChangeMovie(o);
        }
        bool _canSave(object o)
        {
            IEvent ev = _event;
            return ev != null
                && (IsModified || ev.IsModified);
        }
        
        void _setCGElements(IMedia media)
        {
            IsCGEnabled = _engine.EnableCGElementsForNewEvents;
            if (media != null)
            {
                var category = media.MediaCategory;
                Logo = (byte)(category == TMediaCategory.Fill || category == TMediaCategory.Show || category == TMediaCategory.Promo || category == TMediaCategory.Insert || category == TMediaCategory.Jingle ? 1 : 0);
                Parental = media.Parental;
            }
        }


        bool _canMoveUp(object o)
        {
            IEvent prior = _event?.Prior;
            return prior != null && prior.PlayState == TPlayState.Scheduled && _event.PlayState == TPlayState.Scheduled && !IsLoop
                && (prior.StartType == TStartType.After || !IsHold);
        }

        bool _canMoveDown(object o)
        {
            IEvent next = _event?.Next;
            return next != null && next.PlayState == TPlayState.Scheduled && _event.PlayState == TPlayState.Scheduled && !next.IsLoop
                && (_event.StartType == TStartType.After || !next.IsHold);
        }
        #endregion // command methods

        private bool _isVolumeChecking;
        public bool IsVolumeChecking
        {
            get { return _isVolumeChecking; }
            set
            {
                if (base.SetField(ref _isVolumeChecking, value)) //not set Modified
                    InvalidateRequerySuggested();
            }
        }
        
        private bool _isModified;
        public bool IsModified
        {
            get { return _isModified; }
            private set
            {
                if (_isModified != value)
                    _isModified = value;
                if (value)
                    InvalidateRequerySuggested();
            }
        }

        private TEventType _eventType;
        public TEventType EventType
        {
            get { return _eventType; }
            set { SetField(ref _eventType, value); }
        }

        private string _eventName;
        public string EventName
        {
            get { return _eventName; }
            set { SetField(ref _eventName, value); }
        }

        public bool IsEditEnabled
        {
            get
            {
                var ev = _event;
                return ev != null && ev.PlayState == TPlayState.Scheduled;
            }
        }

        public bool IsAutoStartEvent { get { return _startType == TStartType.OnFixedTime; } }

        public bool IsMovieOrLive
        {
            get
            {
                var et = _event?.EventType;
                return et == TEventType.Movie || et == TEventType.Live;
            }
        }

        public bool IsMovieOrLiveOrRundown
        {
            get
            {
                var et = _event?.EventType;
                var st = _event?.StartType;
                return (et == TEventType.Movie || et == TEventType.Live || et == TEventType.Rundown) 
                    && (st == TStartType.After || st == TStartType.WithParent || st == TStartType.WithParentFromEnd);
            }
        }

        public bool IsAnimation
        {
            get { return _eventType == TEventType.Animation; }
        }

        public bool IsCommandScript { get { return _event is ICommandScript; } }


        #region ICGElementsState
        bool _isCGEnabled;
        byte _crawl;
        byte _logo;
        byte _parental;           
        public bool IsCGEnabled { get { return _isCGEnabled; } set { SetField(ref _isCGEnabled, value); } }
        public byte Crawl { get { return _crawl; } set { SetField(ref _crawl, value); } }
        public byte Logo { get { return _logo; } set { SetField(ref _logo, value); } }
        public byte Parental { get { return _parental; } set { SetField(ref _parental, value); } }
        #endregion ICGElementsState

        #region ITemplatedEdit

        private int _templateLayer;
        public int TemplateLayer { get { return _templateLayer; } set { SetField(ref _templateLayer, value); } }

        public object SelectedField { get; set; }

        private readonly ObservableDictionary<string, string> _fields = new ObservableDictionary<string, string>();
        public IDictionary<string, string> Fields
        {
            get { return _fields; }
            set
            {
                _fields.Clear();
                if (value != null)
                    _fields.AddRange(value);
            }
        }

        static readonly Array _methods = Enum.GetValues(typeof(TemplateMethod));
        public Array Methods { get { return _methods; } }

        private TemplateMethod _method;
        public TemplateMethod Method { get { return _method; }  set { SetField(ref _method, value); } }

        public bool KeyIsReadOnly { get { return true; } }

        public ICommand CommandEditField { get; private set; }
        public ICommand CommandAddField { get; private set; }
        public ICommand CommandDeleteField { get; private set; }

        #endregion //ITemplatedEdit

        private string _command;
        public string Command { get { return _command; } set { SetField(ref _command, value); } }

        public bool IsMovie { get { return _event?.EventType == TEventType.Movie; } }

        public bool IsStillImage { get { return _event?.EventType == TEventType.StillImage; } }

        public bool IsTransitionPanelEnabled
        {
            get { 
                var et = _event?.EventType;
                return !_isHold && (et == TEventType.Live || et == TEventType.Movie);
                }
        }

        public bool IsTransitionPropertiesVisible { get { return _transitionType != TTransitionType.Cut; } }

        public bool IsNotContainer
        {
            get { 
                var ev = _event;
                return ev != null && ev.EventType != TEventType.Container;
                }
        }

        public bool CanHold { get { return _event != null && _event.Prior != null; } }
        public bool CanLoop { get { return _event != null && _event.GetSuccessor() == null; } }

        private bool _isEnabled;
        public bool IsEnabled
        {
            get { return _isEnabled; }
            set { SetField(ref _isEnabled, value); }
        }

        private bool _isHold;
        public bool IsHold
        {
            get { return _isHold; }
            set
            {
                if (SetField(ref _isHold, value))
                {
                    if (value)
                        TransitionTime = TimeSpan.Zero;
                    NotifyPropertyChanged(nameof(IsTransitionPanelEnabled));
                }
            }
        }

        private bool _isLoop;
        public bool IsLoop
        {
            get { return _isLoop; }
            set { SetField(ref _isLoop, value); }
        }

        private TStartType _startType;
        public TStartType StartType
        {
            get { return _startType; }
            set
            {
                if (SetField(ref _startType, value))
                {
                    BindToEnd = value == TStartType.WithParentFromEnd;
                    NotifyPropertyChanged(nameof(IsAutoStartEvent));
                }
            }
        }

        private AutoStartFlags _autoStartFlags;
        public AutoStartFlags AutoStartFlags
        {
            get { return _autoStartFlags; }
            set
            {
                if (SetField(ref _autoStartFlags, value))
                {
                    NotifyPropertyChanged(nameof(AutoStartForced));
                    NotifyPropertyChanged(nameof(AutoStartDaily));
                    NotifyPropertyChanged(nameof(IsScheduledDateVisible));
                }
            }
        }

        public bool AutoStartForced
        {
            get { return (_autoStartFlags & AutoStartFlags.Force) != AutoStartFlags.None; }
            set
            {
                if (value)
                    AutoStartFlags = AutoStartFlags | AutoStartFlags.Force;
                else
                    AutoStartFlags = AutoStartFlags & ~AutoStartFlags.Force;
            }
        }

        public bool AutoStartDaily
        {
            get { return (_autoStartFlags & AutoStartFlags.Daily) != AutoStartFlags.None; }
            set
            {
                if (value != AutoStartDaily)
                {
                    if (value)
                        AutoStartFlags = AutoStartFlags | AutoStartFlags.Daily;
                    else
                        AutoStartFlags = AutoStartFlags & ~AutoStartFlags.Daily;
                }
            }
        }

        public string BoundEventName
        {
            get
            {
                IEvent ev = Event;
                if (ev == null)
                    return string.Empty;
                TStartType st = ev.StartType;
                IEvent boundEvent = ev == null ? null : (st == TStartType.WithParent || st == TStartType.WithParentFromEnd) ? ev.Parent : (st == TStartType.After) ? ev.Prior : null;
                return boundEvent == null ? string.Empty : boundEvent.EventName;
            }
        }

        private bool _bindToEnd;
        public bool BindToEnd
        {
            get { return _bindToEnd; }
            set
            {
                if (SetField(ref _bindToEnd, value))
                {
                    if (_startType == TStartType.WithParent || _startType == TStartType.WithParentFromEnd)
                    {
                        if (value)
                        {
                            StartType = TStartType.WithParentFromEnd;
                        }
                        else
                        {
                            StartType = TStartType.WithParent;
                        }
                    }
                }
            }
        }

        public bool IsDisplayBindToEnd
        {
            get
            {
                return (_eventType == TEventType.Animation || _eventType == TEventType.CommandScript || _eventType == TEventType.StillImage) 
                    && (_startType == TStartType.WithParent || _startType == TStartType.WithParentFromEnd);
            }
        }

        private TimeSpan _scheduledTc;
        public TimeSpan ScheduledTc
        {
            get { return _scheduledTc; }
            set { 
                SetField(ref _scheduledTc, value);
                NotifyPropertyChanged(nameof(Duration));
            }
        }

        static readonly Array _transitionTypes = Enum.GetValues(typeof(TTransitionType));
        public Array TransitionTypes { get { return _transitionTypes; } }

        static readonly Array _transitionEasings = Enum.GetValues(typeof(TEasing));
        public Array TransitionEasings { get { return _transitionEasings; } }

        private TTransitionType _transitionType;
        public TTransitionType TransitionType
        {
            get { return _transitionType; }
            set
            {
                if (SetField(ref _transitionType, value))
                {
                    if (value == TTransitionType.Cut)
                    {
                        TransitionTime = TimeSpan.Zero;
                        TransitionPauseTime = TimeSpan.Zero;
                    }
                    NotifyPropertyChanged(nameof(IsTransitionPropertiesVisible));
                }
            }
        }

        private TEasing _transitionEasing;
        public TEasing TransitionEasing
        {
            get { return _transitionEasing; }
            set { SetField(ref _transitionEasing, value); }
        }

        private TimeSpan _transitionTime;
        public TimeSpan TransitionTime
        {
            get { return _transitionTime; }
            set { SetField(ref _transitionTime, value); }
        }

        private TimeSpan _transitionPauseTime;
        public TimeSpan TransitionPauseTime
        {
            get { return _transitionPauseTime; }
            set { SetField(ref _transitionPauseTime, value); }
        }


        private decimal? _audioVolume;
        public decimal? AudioVolume
        {
            get { return _audioVolume; }
            set
            {
                if (SetField(ref _audioVolume, value))
                {
                    NotifyPropertyChanged(nameof(HasAudioVolume));
                    NotifyPropertyChanged(nameof(AudioVolumeLevel));
                }
            }
        }
        
        public decimal AudioVolumeLevel
        {
            get { return _audioVolume != null ? (decimal)_audioVolume : _media != null ? _media.AudioVolume : 0m; }
            set
            {
                if (SetField(ref _audioVolume, value))
                {
                    NotifyPropertyChanged(nameof(HasAudioVolume));
                    NotifyPropertyChanged(nameof(AudioVolume));
                }
            }
        }

        public bool HasAudioVolume
        {
            get { return _audioVolume != null; }
            set
            {
                if (SetField(ref _audioVolume, value? (_media != null ? (decimal?)_media.AudioVolume : 0m) : null))
                {
                    NotifyPropertyChanged(nameof(AudioVolume));
                    NotifyPropertyChanged(nameof(AudioVolumeLevel));
                }
            }
        }

        private DateTime _scheduledTime;
        public DateTime ScheduledTime
        {
            get { return _scheduledTime; }
            set
            {
                if (SetField(ref _scheduledTime, value))
                {
                    NotifyPropertyChanged(nameof(ScheduledDate));
                    NotifyPropertyChanged(nameof(ScheduledTimeOfDay));
                }
            }
        }

        public DateTime ScheduledDate
        {
            get { return _scheduledTime.ToLocalTime().Date; }
            set
            {
                if (!value.Equals(ScheduledDate))
                    ScheduledTime = (value.Date + ScheduledTimeOfDay).ToUniversalTime();
            }
        }

        public TimeSpan ScheduledTimeOfDay
        {
            get { return _scheduledTime.ToLocalTime().TimeOfDay; }
            set
            {
                if (!value.Equals(ScheduledTimeOfDay))
                    ScheduledTime = new DateTime(value.Ticks + ScheduledDate.Ticks).ToUniversalTime();
            }
        }

        public bool IsScheduledDateVisible { get { return _startType != TStartType.OnFixedTime || !AutoStartDaily; } }

        private TimeSpan? _requestedStartTime;
        public TimeSpan? RequestedStartTime
        {
            get { return _requestedStartTime; }
            set { SetField(ref _requestedStartTime, value); }
        }


        private TimeSpan _duration;
        public TimeSpan Duration
        {
            get { return _duration; }
            set
            {
                SetField(ref _duration, value);
                NotifyPropertyChanged(nameof(ScheduledTc));
            }
        }

        public TVideoFormat VideoFormat { get { return _engineViewModel.VideoFormat; } }

        private TimeSpan _scheduledDelay;
        public TimeSpan ScheduledDelay
        {
            get { return _scheduledDelay; }
            set { SetField(ref _scheduledDelay, value); }
        }

        private sbyte _layer;
        public sbyte Layer
        {
            get { return _layer; }
            set { SetField(ref _layer, value); }
        }

        public bool HasSubItemOnLayer1
        {
            get
            {
                IEvent ev = Event;
                return (ev == null) ? false : (ev.EventType == TEventType.StillImage) ? ev.Layer == VideoLayer.CG1 : ev.SubEvents.Any(e => e.Layer == VideoLayer.CG1 && e.EventType == TEventType.StillImage);
            }
        }
        public bool HasSubItemOnLayer2
        {
            get
            {
                IEvent ev = Event;
                return (ev == null) ? false : (ev.EventType == TEventType.StillImage) ? ev.Layer == VideoLayer.CG2 : ev.SubEvents.Any(e => e.Layer == VideoLayer.CG2 && e.EventType == TEventType.StillImage);
            }
        }
        public bool HasSubItemOnLayer3
        {
            get
            {
                IEvent ev = Event;
                return (ev == null) ? false : (ev.EventType == TEventType.StillImage) ? ev.Layer == VideoLayer.CG3 : ev.SubEvents.Any(e => e.Layer == VideoLayer.CG3 && e.EventType == TEventType.StillImage);
            }
        }

        public bool HasSubItems
        {
            get
            {
                IEvent ev = Event;
                return (ev == null || ev.EventType == TEventType.Live || ev.EventType == TEventType.Movie) ? false : ev.SubEvents.Any(e => e.EventType == TEventType.StillImage);
            }
        }

        public bool IsStartEvent
        {
            get
            {
                var st = Event?.StartType;
                return (st == TStartType.OnFixedTime || st == TStartType.Manual);
            }
        }

        public bool IsDurationEnabled
        {
            get
            {
                IEvent ev = Event;
                return (ev != null) && ev.EventType != TEventType.Rundown;
            }
        }

        public bool IsCGElementsEnabled
        {
            get
            {
                IEvent ev = Event;
                if (ev != null)
                {
                    IEngine engine = ev.Engine;
                    return (engine != null
                        && (ev.EventType == TEventType.Live || ev.EventType == TEventType.Movie)
                        && (engine.CGElementsController != null));
                }
                return false;
            }
        }

        public bool IsDisplayCGElements
        {
            get { return _engine.CGElementsController != null; }
        }

        public IEnumerable<ICGElement> Logos { get { return _engine.CGElementsController?.Logos; } }
        public IEnumerable<ICGElement> Crawls { get { return _engine.CGElementsController?.Crawls; } }
        public IEnumerable<ICGElement> Parentals { get { return _engine.CGElementsController?.Parentals; } }


        private void _cGElementsViewmodel_Modified(object sender, EventArgs e)
        {
            IsModified = true;
        }

        internal void _previewPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Media))
                InvalidateRequerySuggested();
        }

        private void _eventPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke((Action)delegate()
            {
                bool oldModified = _isModified;
                PropertyInfo sourcePi = sender.GetType().GetProperty(e.PropertyName);
                PropertyInfo destPi = this.GetType().GetProperty(e.PropertyName);
                if (sourcePi != null && destPi != null
                    && sourcePi.PropertyType.Equals(destPi.PropertyType))
                    destPi.SetValue(this, sourcePi.GetValue(sender, null), null);
                _isModified = oldModified;
            });
            if (e.PropertyName == nameof(IEvent.PlayState))
            {
                NotifyPropertyChanged(nameof(IsEditEnabled));
                NotifyPropertyChanged(nameof(IsMovieOrLive));
                InvalidateRequerySuggested();
            }
            if (e.PropertyName == nameof(IEvent.AudioVolume))
            {
                NotifyPropertyChanged(nameof(AudioVolumeLevel));
                NotifyPropertyChanged(nameof(HasAudioVolume));
                NotifyPropertyChanged(nameof(AudioVolume));
            }
            if (e.PropertyName == nameof(IEvent.IsLoop))
            {
                InvalidateRequerySuggested();
            }
            if (e.PropertyName == nameof(IEvent.Next))
            {
                IsLoop = false;
                NotifyPropertyChanged(nameof(CanLoop));
            }
            if (e.PropertyName == nameof(IEvent.StartType))
                NotifyPropertyChanged(nameof(IsAutoStartEvent));
            if (e.PropertyName == nameof(IEvent.AutoStartFlags))
            {
                NotifyPropertyChanged(nameof(AutoStartForced));
                NotifyPropertyChanged(nameof(AutoStartDaily));
            }
        }

        private void _onSubeventChanged(object o, CollectionOperationEventArgs<IEvent> e)
        {
        }

        private void _onRelocated(object o, EventArgs e)
        {
            NotifyPropertyChanged(nameof(StartType));
            NotifyPropertyChanged(nameof(BoundEventName));
            NotifyPropertyChanged(nameof(ScheduledTime));
            NotifyPropertyChanged(nameof(ScheduledTimeOfDay));
            NotifyPropertyChanged(nameof(ScheduledDate));
            NotifyPropertyChanged(nameof(IsStartEvent));
        }

        public override string ToString()
        {
            return $"{Infralution.Localization.Wpf.ResourceEnumConverter.ConvertToString(EventType)} - {_event}";
        }

    }

}

