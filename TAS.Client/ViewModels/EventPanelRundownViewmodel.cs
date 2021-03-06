﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Input;
using TAS.Client.Common;
using TAS.Common;
using TAS.Server.Common;
using TAS.Server.Interfaces;
using resources = TAS.Client.Common.Properties.Resources;

namespace TAS.Client.ViewModels
{
    public class EventPanelRundownViewmodel: EventPanelRundownElementViewmodelBase
    {
        public EventPanelRundownViewmodel(IEvent ev, EventPanelViewmodelBase parent) : base(ev, parent)
        {
            CommandAddSubMovie = new UICommand { ExecuteDelegate = _addSubMovie, CanExecuteDelegate = o => _event.SubEventsCount == 0 };
            CommandAddSubRundown = new UICommand { ExecuteDelegate = _addSubRundown, CanExecuteDelegate = o => _event.SubEventsCount == 0 };
            CommandAddSubLive = new UICommand { ExecuteDelegate = _addSubLive, CanExecuteDelegate = o => _event.SubEventsCount == 0 };
        }

        private void _addSubLive(object obj)
        {
            _engineViewmodel.AddSimpleEvent(_event, TEventType.Live, true);
        }

        protected override void OnSubeventChanged(object o, CollectionOperationEventArgs<IEvent> e)
        {
            base.OnSubeventChanged(o, e);
            InvalidateRequerySuggested();
        }

        protected override bool CanAddNextMovie(object o)
        {
            return _parent is EventPanelRundownViewmodel && base.CanAddNextMovie(o);
        }
        protected override bool CanAddNewLive(object o)
        {
            return _parent is EventPanelRundownViewmodel && base.CanAddNewLive(o);
        }

        private void _addSubRundown(object obj)
        {
            _engineViewmodel.AddSimpleEvent(_event, TEventType.Rundown, true);
        }

        private void _addSubMovie(object obj)
        {
            _engineViewmodel.AddMediaEvent(_event, TStartType.WithParent, TMediaType.Movie, VideoLayer.Program, false);
        }
        protected override void OnDispose()
        {
            if (IsSelected)
            {
                var p = Prior;
                if (p != null)
                    p.IsSelected = true;
            }
            base.OnDispose();
        }

        public ICommand CommandAddSubRundown { get; }
        public ICommand CommandAddSubMovie { get; }
        public ICommand CommandAddSubLive { get; }
    }
}
