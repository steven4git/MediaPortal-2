﻿#region Copyright (C) 2007-2017 Team MediaPortal

/*
    Copyright (C) 2007-2017 Team MediaPortal
    http://www.team-mediaportal.com

    This file is part of MediaPortal 2

    MediaPortal 2 is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    MediaPortal 2 is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with MediaPortal 2. If not, see <http://www.gnu.org/licenses/>.
*/

#endregion

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaPortal.Common;
using MediaPortal.Common.Logging;
using MediaPortal.Common.PluginManager;
using MediaPortal.Common.Settings;
using MediaPortal.Plugins.InputDeviceManager.Models;
using MediaPortal.Plugins.InputDeviceManager.RawInput;
using MediaPortal.UI.Control.InputManager;
using MediaPortal.UI.Presentation.Screens;
using MediaPortal.UI.Presentation.Workflow;

namespace MediaPortal.Plugins.InputDeviceManager
{
  public class InputDeviceManager : IPluginStateTracker
  {
    private static InputDeviceManager _instance;
    private static RawInputHandler _rawInput;
    private const bool CAPTURE_ONLY_IN_FOREGROUND = false;
    private static readonly Dictionary<string, InputDevice> _inputDevices = new Dictionary<string, InputDevice>();
    private Thread _startupThread;
    private static IScreenControl _screenControl;
    private static readonly ConcurrentDictionary<string, int> _pressedKeys = new ConcurrentDictionary<string, int>();
    private static readonly TaskCompletionSource<bool> _initComplete = new TaskCompletionSource<bool>();

    public InputDeviceManager()
    {
      _instance = this;
    }

    public static InputDeviceManager Instance
    {
      get { return _instance; }
    }

    public static RawInputHandler RawInput
    {
      get { return _rawInput; }
    }

    public static Task InitComplete
    {
      get { return _initComplete.Task; }
    }

    public static IDictionary<string, InputDevice> InputDevices
    {
      get { return _inputDevices; }
    }

    private static void OnKeyPressed(object sender, RawInputEventArg e)
    {
      try
      {
        switch (e.KeyPressEvent.Message)
        {
          case Win32.WM_KEYDOWN:
          case Win32.WM_SYSKEYDOWN:
            _pressedKeys.GetOrAdd(e.KeyPressEvent.VKeyName, e.KeyPressEvent.VKey);
            break;
          case Win32.WM_KEYUP:
            int tmp;
            _pressedKeys.TryRemove(e.KeyPressEvent.VKeyName, out tmp);
            break;
        }
      }
      catch
      {
        ServiceRegistration.Get<ILogger>().Debug("stateNr {0}", e.KeyPressEvent.Message);
      }

      InputDevice device;
      if (_inputDevices.TryGetValue(e.KeyPressEvent.Source, out device))
      {
        var keyMapping = device.KeyMap.FirstOrDefault(m => m.Code.SequenceEqual(_pressedKeys.Values));
        if(keyMapping != null)
        {
          string[] actionArray = keyMapping.Key.Split('.');
          if (actionArray.Length >= 2)
          {
            if (keyMapping.Key.StartsWith(InputDeviceModel.KEY_PREFIX, StringComparison.InvariantCultureIgnoreCase))
            {
              ServiceRegistration.Get<ILogger>().Debug("Executing key action: " + actionArray[1]);
              ServiceRegistration.Get<IInputManager>().KeyPress(Key.GetSpecialKeyByName(actionArray[1]));
              e.Handled = true;
            }
            else if (keyMapping.Key.StartsWith(InputDeviceModel.MENU_PREFIX, StringComparison.InvariantCultureIgnoreCase))
            {
              WorkflowAction action;
              if (ServiceRegistration.Get<IWorkflowManager>().CurrentNavigationContext.MenuActions.TryGetValue(Guid.Parse(actionArray[1]), out action))
              {
                ServiceRegistration.Get<ILogger>().Debug("Executing menu action: " + actionArray[1]);
                action.Execute();
                e.Handled = true;
              }
              //ServiceRegistration.Get<IWorkflowManager>().NavigatePush(Guid.Parse(actionArray[1]));
            }
          }
        }
      }
      //ServiceRegistration.Get<ILogger>().Debug(e.KeyPressEvent.DeviceHandle.ToString());
      //ServiceRegistration.Get<ILogger>().Debug(e.KeyPressEvent.DeviceType);
      //ServiceRegistration.Get<ILogger>().Debug(e.KeyPressEvent.DeviceName);
      //ServiceRegistration.Get<ILogger>().Debug(e.KeyPressEvent.Name);
      //ServiceRegistration.Get<ILogger>().Debug(e.KeyPressEvent.VKey.ToString(CultureInfo.InvariantCulture));
      //ServiceRegistration.Get<ILogger>().Debug(_rawinput.NumberOfKeyboards.ToString(CultureInfo.InvariantCulture));
      //ServiceRegistration.Get<ILogger>().Debug(e.KeyPressEvent.VKeyName);
      //ServiceRegistration.Get<ILogger>().Debug(e.KeyPressEvent.Source);
      //ServiceRegistration.Get<ILogger>().Debug(e.KeyPressEvent.KeyPressState);
      //ServiceRegistration.Get<ILogger>().Debug("0x{0:X4} ({0})", e.KeyPressEvent.Message);
    }

    public static void ThreadProc()
    {
      while (_screenControl == null)
      {
        try
        {
          if (ServiceRegistration.IsRegistered<IScreenControl>())
          {
            _screenControl = ServiceRegistration.Get<IScreenControl>();

            _rawInput = new RawInputHandler(_screenControl.MainWindowHandle, CAPTURE_ONLY_IN_FOREGROUND);
            _rawInput.AddMessageFilter(); // Adding a message filter will cause keypresses to be handled
            _rawInput.KeyPressed += OnKeyPressed;
            _initComplete.SetResult(true);
          }
        }
        catch
        {
          // ignored
        }
        Thread.Sleep(500);
      }
    }

    public void LoadSettings()
    {
      ISettingsManager settingsManager = ServiceRegistration.Get<ISettingsManager>();
      InputManagerSettings settings = settingsManager.Load<InputManagerSettings>();

      UpdateLoadedSettings(settings);
    }

    /// <summary>
    /// This function updates the local variable "_inputDevices"
    /// </summary>
    /// <param name="settings"></param>
    public void UpdateLoadedSettings(InputManagerSettings settings)
    {
      _inputDevices.Clear();
      if (settings != null)
        try
        {
          foreach (InputDevice device in settings.InputDevices)
            _inputDevices.Add(device.DeviceID, device);
        }
        catch
        {
          // ignored
        }
    }

    #region Implementation of IPluginStateTracker

    /// <summary>
    /// Will be called when the plugin is started. This will happen as a result of a plugin auto-start
    /// or an item access which makes the plugin active.
    /// This method is called after the plugin's state was set to <see cref="PluginState.Active"/>.
    /// </summary>
    public void Activated(PluginRuntime pluginRuntime)
    {
      LoadSettings();
      _startupThread = new Thread(ThreadProc);
      _startupThread.Start();
    }

    /// <summary>
    /// Schedules the stopping of this plugin. This method returns the information
    /// if this plugin can be stopped. Before this method is called, the plugin's state
    /// will be changed to <see cref="PluginState.EndRequest"/>.
    /// </summary>
    /// <remarks>
    /// This method is part of the first phase in the two-phase stop procedure.
    /// After this method returns <c>true</c> and all item's clients also return <c>true</c>
    /// as a result of their stop request, the plugin's state will change to
    /// <see cref="PluginState.Stopping"/>, then all uses of items by clients will be canceled,
    /// then this plugin will be stopped by a call to method <see cref="IPluginStateTracker.Stop"/>.
    /// If either this method returns <c>false</c> or one of the items clients prevent
    /// the stopping, the plugin will continue to be active and the method <see cref="IPluginStateTracker.Continue"/>
    /// will be called.
    /// </remarks>
    /// <returns><c>true</c>, if this plugin can be stopped at this time, else <c>false</c>.
    /// </returns>
    public bool RequestEnd()
    {
      return true;
    }

    /// <summary>
    /// Second step of the two-phase stopping procedure. This method stops this plugin,
    /// i.e. removes the integration of this plugin into the system, which was triggered
    /// by the <see cref="IPluginStateTracker.Activated"/> method.
    /// </summary>
    public void Stop()
    {
    }

    /// <summary>
    /// Revokes the end request which was triggered by a former call to the
    /// <see cref="IPluginStateTracker.RequestEnd"/> method and restores the active state. After this call, the plugin remains active as
    /// it was before the call of <see cref="IPluginStateTracker.RequestEnd"/> method.
    /// </summary>
    public void Continue()
    {
    }

    /// <summary>
    /// Will be called before the plugin manager shuts down. The plugin can perform finalization
    /// tasks here. This method will called independently from the plugin state, i.e. it will also be called when the plugin
    /// was disabled or not started at all.
    /// </summary>
    public void Shutdown()
    {
    }

    #endregion
  }
}
