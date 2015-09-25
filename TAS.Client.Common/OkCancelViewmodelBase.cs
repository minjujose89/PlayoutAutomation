﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Controls;
using System.Windows.Input;
using TAS.Client.Common;

namespace TAS.Client.Common
{
    public abstract class OkCancelViewmodelBase<M> : ViewModels.ViewmodelBase
    {
        public readonly M Model;
        public readonly OkCancelView View;
        public readonly UserControl _editor;

        public event Action Applied;

        public OkCancelViewmodelBase(M model, UserControl editor, string windowTitle)
        {
            Model = model;
            _editor = editor;
            Load(model);
            _modified = false;
            CommandClose = new UICommand() { CanExecuteDelegate = CanClose, ExecuteDelegate = Close };
            CommandApply = new UICommand() { CanExecuteDelegate = o => Modified == true, ExecuteDelegate = Apply };
            CommandOK = new UICommand() { CanExecuteDelegate = o => Modified == true, ExecuteDelegate = Ok };
            View = new OkCancelView() { 
                DataContext = this, 
                Title = windowTitle, 
                Owner = System.Windows.Application.Current.MainWindow, 
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner, 
                ShowInTaskbar = false };
        }
        
        public UserControl Editor { get { return _editor; } }

        protected virtual void Ok(object o)
        {
            Apply(null);
            View.DialogResult = true;
        }

        public virtual bool? Show()
        {
            return View.ShowDialog();
        }

        protected virtual void Load(object source)
        {
            PropertyInfo[] copiedProperties = this.GetType().GetProperties();
            foreach (PropertyInfo copyPi in copiedProperties)
            {
                PropertyInfo sourcePi = source.GetType().GetProperty(copyPi.Name);
                if (sourcePi != null)
                    copyPi.SetValue(this, sourcePi.GetValue(source, null), null);
            }
        }

        protected virtual void Apply(object destObject)
        {
            if (Modified && Model != null)
            {
                PropertyInfo[] copiedProperties = this.GetType().GetProperties();
                foreach (PropertyInfo copyPi in copiedProperties)
                {
                    PropertyInfo destPi = (destObject??Model).GetType().GetProperty(copyPi.Name);
                    if (destPi != null)
                    {
                        if (destPi.GetValue(destObject??Model, null) != copyPi.GetValue(this, null)
                            && destPi.CanWrite)
                            destPi.SetValue(destObject??Model, copyPi.GetValue(this, null), null);
                    }
                }
                Modified = false;
                if (Applied != null)
                    Applied();
            }
        }

        protected virtual void Close(object parameter)
        {
            View.DialogResult = false;
        }
        
        protected virtual bool CanClose(object parameter)
        {
            return true;
        }

        protected override bool SetField<T>(ref T field, T value, string propertyName)
        {
            bool modified = base.SetField<T>(ref field, value, propertyName);
            if (modified) Modified = true;
            return modified;
        }

        protected bool _modified;

        public virtual bool Modified
        {
            get { return _modified; }
            protected set
            {
                if (base.SetField(ref _modified, value, "Modified"))
                {
                    NotifyPropertyChanged("CommandApply");
                    NotifyPropertyChanged("CommandOk");
                    NotifyPropertyChanged("CommandClose");
                }
            }
        }

        public ICommand CommandClose { get; protected set; }
        public ICommand CommandApply { get; protected set; }
        public ICommand CommandOK { get; protected set; }

       
    }
}
