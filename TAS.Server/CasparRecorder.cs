﻿using Newtonsoft.Json;
using Svt.Caspar;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Serialization;
using TAS.Common;
using TAS.Remoting.Server;
using TAS.Server.Common;
using TAS.Server.Interfaces;

namespace TAS.Server
{

    public class CasparRecorder: DtoBase, IRecorder
    {
        private TVideoFormat _tcFormat = TVideoFormat.PAL;
        private Recorder _recorder;
        private IMedia _recordingMedia;
        internal IArchiveDirectory ArchiveDirectory;
        private static NLog.Logger Logger = NLog.LogManager.GetLogger(nameof(CasparRecorder));
        private CasparServer _ownerServer;

        internal void SetRecorder(Recorder value)
        {
            var oldRecorder = _recorder;
            if (_recorder != value)
            {
                if (oldRecorder != null)
                {
                    oldRecorder.Tc -= _recorder_Tc;
                    oldRecorder.FramesLeft -= _recorder_FramesLeft;
                    oldRecorder.DeckConnected -= _recorder_DeckConnected;
                    oldRecorder.DeckControl -= _recorder_DeckControl;
                    oldRecorder.DeckState -= _recorder_DeckState;
                }
                _recorder = value;
                if (value != null)
                {
                    value.Tc += _recorder_Tc;
                    value.DeckConnected += _recorder_DeckConnected;
                    value.DeckControl += _recorder_DeckControl;
                    value.DeckState += _recorder_DeckState;
                    value.FramesLeft += _recorder_FramesLeft;
                    IsDeckConnected = value.IsConnected;
                    DeckState = TDeckState.Unknown;
                    DeckControl = TDeckControl.None;
                }
            }
        }

        internal void SetOwner(CasparServer owner)
        {
            _ownerServer = owner;
            CaptureChannel = owner.Channels.LastOrDefault();
        }

        private void _recorder_FramesLeft(object sender, FramesLeftEventArgs e)
        {
            var media = _recordingMedia;
            if (media != null)
                TimeLimit = e.FramesLeft.SMPTEFramesToTimeSpan(media.VideoFormat);
        }

        private void _recorder_DeckState(object sender, DeckStateEventArgs e)
        {
            DeckState = (TDeckState)e.State;
        }

        private void _recorder_DeckControl(object sender, DeckControlEventArgs e)
        {
            if (e.ControlEvent == Svt.Caspar.DeckControl.capture_complete)
                _captureCompleted();
            if (e.ControlEvent == Svt.Caspar.DeckControl.aborted)
                _captureAborted();
            DeckControl = (TDeckControl)e.ControlEvent;
        }

        private void _captureAborted()
        {
            _recordingMedia?.Delete();
            RecordingMedia = null;
        }

        private void _captureCompleted()
        {
            var media = _recordingMedia;
            if (media?.MediaStatus == TMediaStatus.Copying)
            {
                media.MediaStatus = TMediaStatus.Copied;
                ThreadPool.QueueUserWorkItem((o) =>
                {
                    Thread.Sleep(500);
                    media.Verify();
                    if (media.MediaStatus == TMediaStatus.Available)
                        CaptureSuccess?.Invoke(this, new MediaEventArgs(media));
                });
            }            
        }

        private void _recorder_DeckConnected(object sender, DeckConnectedEventArgs e)
        {
            IsDeckConnected = e.IsConnected;
        }

        private void _recorder_Tc(object sender, TcEventArgs e)
        {
            if (e.Tc.IsValidSMPTETimecode(_tcFormat))
                CurrentTc = e.Tc.SMPTETimecodeToTimeSpan(_tcFormat);
        }

        public event EventHandler<MediaEventArgs> CaptureSuccess;

        #region Deserialized properties
        public int RecorderNumber { get; set; }
        public int Id { get; set; }
        public string RecorderName { get; set; }
        #endregion Deserialized properties

        #region IRecorder
        private TimeSpan _currentTc;
        [JsonProperty, XmlIgnore]
        public TimeSpan CurrentTc { get { return _currentTc; }  private set { SetField(ref _currentTc, value); } }

        private TimeSpan _timeLimit;
        [JsonProperty, XmlIgnore]
        public TimeSpan TimeLimit { get { return _timeLimit; }  private set { SetField(ref _timeLimit, value); } }

        private TDeckState _deckState;
        [JsonProperty, XmlIgnore]
        public TDeckState DeckState { get { return _deckState; } private set { SetField(ref _deckState, value); } }

        private TDeckControl _deckControl;
        [JsonProperty, XmlIgnore]
        public TDeckControl DeckControl { get { return _deckControl; }  private set { SetField(ref _deckControl, value); } }

        private bool _isDeckConnected;
        [JsonProperty, XmlIgnore]
        public bool IsDeckConnected { get { return _isDeckConnected; } private set { SetField(ref _isDeckConnected, value); } }

        private bool _isServerConnected;
        [JsonProperty, XmlIgnore]
        public bool IsServerConnected { get { return _isServerConnected; } internal set { SetField(ref _isServerConnected, value); } }

        [JsonProperty(ItemTypeNameHandling = TypeNameHandling.Objects), XmlIgnore]
        public IEnumerable<IPlayoutServerChannel> Channels { get { return _ownerServer.Channels; } }

        private string _captureFileName;
        [JsonProperty, XmlIgnore]
        public string CaptureFileName { get { return _captureFileName; } private set { SetField(ref _captureFileName, value); } }

        private TimeSpan _captureTcIn;
        [JsonProperty, XmlIgnore]
        public TimeSpan CaptureTcIn { get { return _captureTcIn; } private set { SetField(ref _captureTcIn, value); } }

        private TimeSpan _captureTcOut;
        [JsonProperty, XmlIgnore]
        public TimeSpan CaptureTcOut { get { return _captureTcOut; } private set { SetField(ref _captureTcOut, value); } }

        private TimeSpan _captureTimeLimit = TimeSpan.FromHours(2);
        [JsonProperty, XmlIgnore]
        public TimeSpan CaptureTimeLimit { get { return _captureTimeLimit; } private set { SetField(ref _captureTimeLimit, value); } }

        private bool _captureNarrowMode;
        [JsonProperty, XmlIgnore]
        public bool CaptureNarrowMode { get { return _captureNarrowMode; } private set { SetField(ref _captureNarrowMode, value); } }

        private IPlayoutServerChannel _captureChannel;
        [XmlIgnore]
        public IPlayoutServerChannel CaptureChannel { get { return _captureChannel; } set { SetField(ref _captureChannel, value); } }


        public IMedia Capture(IPlayoutServerChannel channel, TimeSpan tcIn, TimeSpan tcOut, bool narrowMode, string fileName)
        {
            _tcFormat = channel.VideoFormat;
            var directory = (ServerDirectory)_ownerServer.MediaDirectory;
            var newMedia = new ServerMedia(directory, Guid.NewGuid(), 0, ArchiveDirectory) { FileName = fileName, MediaName = fileName, TcStart = tcIn, TcPlay = tcIn, Duration = tcOut - tcIn, MediaStatus = TMediaStatus.Copying, LastUpdated = DateTime.UtcNow, MediaType = TMediaType.Movie };
            if (_recorder?.Capture(channel.Id, tcIn.ToSMPTETimecodeString(channel.VideoFormat), tcOut.ToSMPTETimecodeString(channel.VideoFormat), narrowMode, fileName) == true)
            {
//                directory.MediaAdd(newMedia);
                RecordingMedia = newMedia;
                CaptureFileName = fileName;
                CaptureTcIn = tcIn;
                CaptureTcOut = tcOut;
                CaptureTimeLimit = tcOut - tcIn;
                CaptureNarrowMode = narrowMode;
                return newMedia;
            }
            return null;
        }

        public IMedia Capture(IPlayoutServerChannel channel, TimeSpan timeLimit, bool narrowMode, string fileName)
        {
            _tcFormat = channel.VideoFormat;
            var directory = (ServerDirectory)_ownerServer.MediaDirectory;
            var newMedia = new ServerMedia(directory, Guid.NewGuid(), 0, ArchiveDirectory) { FileName = fileName, MediaName = fileName, TcStart = TimeSpan.Zero, TcPlay=TimeSpan.Zero, Duration = timeLimit, MediaStatus = TMediaStatus.Copying, LastUpdated = DateTime.UtcNow, MediaType = TMediaType.Movie };
            if (_recorder?.Capture(channel.Id,  timeLimit.ToSMPTEFrames(channel.VideoFormat), narrowMode, fileName) == true)
            {
                RecordingMedia = newMedia;
                CaptureFileName = fileName;
                CaptureTcIn = TimeSpan.Zero;
                CaptureTcOut = TimeSpan.Zero;
                CaptureTimeLimit = timeLimit;
                CaptureNarrowMode = narrowMode;
                return newMedia;
            }
            return null;

        }

        public void SetTimeLimit(TimeSpan limit)
        {
            var videoFormat = _recordingMedia?.VideoFormat;
            if (videoFormat.HasValue)
            {
                _recorder.SetTimeLimit(limit.ToSMPTEFrames(videoFormat.Value));
                CaptureTimeLimit = limit;
            }
        }

        public void Finish()
        {
            _recorder?.Finish();
        }
        
        public void Abort()
        {
            _recorder?.Abort();
        }

        public void DeckPlay()
        {
            _recorder?.Play();
        }
        public void DeckStop()
        {
            _recorder?.Stop();
        }

        public void DeckFastForward()
        {
            _recorder.FastForward();
        }

        public void DeckRewind()
        {
            _recorder.Rewind();
        }

        public void GoToTimecode(TimeSpan tc, TVideoFormat format)
        {
            _recorder?.GotoTimecode(tc.ToSMPTETimecodeString(format));
        }


        [XmlIgnore]
        public IMedia RecordingMedia { get { return _recordingMedia; } private set { SetField(ref _recordingMedia, value); } }

        [XmlIgnore]
        public IMediaDirectory RecordingDirectory { get { return _ownerServer.MediaDirectory; } }

        #endregion IRecorder


    }
}
