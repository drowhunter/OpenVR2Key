﻿using BOLL7708;
using GregsStack.InputSimulatorStandard;
using GregsStack.InputSimulatorStandard.Native;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Input;
using Valve.VR;

namespace OpenVR2Key
{
    class MainController
    {
        private EasyOpenVRSingleton ovr = EasyOpenVRSingleton.Instance;
        private InputSimulator sim = new InputSimulator();

        // Active key registration
        private int registeringKey = 0;
        private object registeringElement = null;
        private HashSet<Key> keys = new HashSet<Key>();
        private HashSet<Key> keysDown = new HashSet<Key>();

        // Binding storage, move to model
        private Dictionary<int, Tuple<VirtualKeyCode[], VirtualKeyCode[]>> bindings = new Dictionary<int, Tuple<VirtualKeyCode[], VirtualKeyCode[]>>();

        // Actions
        private readonly object bindingsLock = new object();
        public Action<bool> statusUpdateAction { get; set; } = (status) => { Debug.WriteLine("No status action set."); };
        public Action<string> appUpdateAction { get; set; } = (appId) => { Debug.WriteLine("No appID action set."); };
        public Action<string> keyTextUpdateAction { get; set; } = (status) => { Debug.WriteLine("No key text action set."); };

        // Other
        private string currentApplicationId = "";
        ulong inputSourceHandleLeft = 0, inputSourceHandleRight = 0;
        ulong notificationOverlayHandle = 0;
        NotificationBitmap_t notificationBitmap = new NotificationBitmap_t();

        public MainController()
        {
        }

        public void Init()
        {
            statusUpdateAction.Invoke(false);
            appUpdateAction.Invoke(currentApplicationId);
            var workerThread = new Thread(WorkerThread);
            workerThread.Start();
        }
        public void SetDebugLogAction(Action<string> action)
        {
            ovr.SetDebugLogAction(action);
        }

        #region bindings
        public bool ToggleRegisteringKey(int index, object sender, out object activeElement)
        {
            var active = registeringKey == 0;
            if (active)
            {
                registeringKey = index;
                registeringElement = sender;
                keysDown.Clear();
                keys.Clear();
                activeElement = sender;
            }
            else
            {
                activeElement = registeringElement;
                RegisterKeyBinding(registeringKey, keys);
                registeringKey = 0;
                registeringElement = null;
            }
            return active;
        }
        public void OnKeyDown(Key key)
        {
            if (MainUtils.MatchVirtualKey(key) != null)
            {
                if (keysDown.Count == 0) keys.Clear();
                keys.Add(key);
                keysDown.Add(key);
                UpdateCurrentObject();
            }
        }
        public void OnKeyUp(Key key)
        {
            if (key == Key.RightAlt) keysDown.Remove(Key.LeftCtrl); // Because AltGr records as RightAlt+LeftCtrl
            keysDown.Remove(key);
            UpdateCurrentObject();
        }
        private void UpdateCurrentObject()
        {
            keyTextUpdateAction.Invoke(GetKeysLabel());
        }

        private string GetKeysLabel()
        {
            List<string> result = new List<string>();
            foreach (Key k in keys)
            {
                result.Add(k.ToString());
            }
            return String.Join(" + ", result.ToArray());
        }

        /**
         * Store key codes as virtual key codes.
         */
        public void RegisterKeyBinding(int keyNumber, HashSet<Key> keys)
        {
            // TODO: Store original key codes as well
            var keysArr = new Key[keys.Count];
            keys.CopyTo(keysArr);
            var binding = MainUtils.ConvertKeys(keysArr);
            lock (bindingsLock)
            {
                bindings[keyNumber] = binding;
            }
        }

        public void RegisterKeyBindings(Dictionary<int, Tuple<VirtualKeyCode[], VirtualKeyCode[]>> bindings)
        {
            lock (bindingsLock)
            {
                this.bindings = bindings;
            }
        }

        public void ClearBindings()
        {
            lock (bindingsLock)
            {
                bindings.Clear();
            }
        }

        public void RemoveBinding(int index)
        {
            lock (bindingsLock)
            {
                bindings.Remove(index);
            }
        }

        #endregion

        private void WorkerThread()
        {
            Thread.CurrentThread.IsBackground = true;
            bool initComplete = false;
            while (true)
            {
                Thread.Sleep(10);
                if (ovr.IsInitialized())
                {
                    if (!initComplete)
                    {
                        ovr.LoadAppManifest("./app.vrmanifest");
                        ovr.LoadActionManifest("./actions.json");
                        RegisterActions();
                        currentApplicationId = ovr.GetRunningApplicationId();
                        appUpdateAction.Invoke(currentApplicationId);
                        statusUpdateAction.Invoke(true);
                        UpdateInputSourceHandles();
                        notificationOverlayHandle = ovr.InitNotificationOverlay("OpenVR2Key");
                        /* // TODO: Bitmap loads but it crashes on trying to use it for the notification. Cannot read from protected memory.
                        var bitmapPath = $"{Directory.GetCurrentDirectory()}\\icon.png";
                        notificationBitmap = EasyOpenVRSingleton.BitmapUtils.NotificationBitmapFromBitmap(new System.Drawing.Bitmap(bitmapPath));
                        */
                        initComplete = true;
                    }

                    var vrEvents = ovr.GetNewEvents();
                    foreach (var e in vrEvents)
                    {
                        var message = Enum.GetName(typeof(EVREventType), e.eventType);
                        Debug.WriteLine(message);

                        switch ((EVREventType)e.eventType)
                        {
                            case EVREventType.VREvent_Quit:
                                initComplete = false;
                                ovr.AcknowledgeShutdown();
                                ovr.Shutdown();
                                statusUpdateAction.Invoke(false);
                                break;
                            case EVREventType.VREvent_SceneApplicationChanged:
                                currentApplicationId = ovr.GetRunningApplicationId();
                                appUpdateAction.Invoke(currentApplicationId);
                                break;
                            case EVREventType.VREvent_TrackedDeviceRoleChanged:
                            case EVREventType.VREvent_TrackedDeviceUpdated:
                            case EVREventType.VREvent_TrackedDeviceActivated:
                                UpdateInputSourceHandles();
                                break;
                        }
                    }
                    
                    ovr.UpdateActionStates(new ulong[] {
                        inputSourceHandleLeft,
                        inputSourceHandleRight
                    });
                }
                else
                {
                    ovr.Init();
                    Thread.Sleep(1000);
                }
            }
        }

        private void UpdateInputSourceHandles()
        {
            inputSourceHandleLeft = ovr.GetInputSourceHandle("/user/hand/left");
            inputSourceHandleRight = ovr.GetInputSourceHandle("/user/hand/right");
        }

        #region vr_input
        private void RegisterActions()
        {
            ovr.RegisterActionSet("/actions/default");
            for (var i = 1; i <= 32; i++)
            {
                int localI = i;
                ovr.RegisterDigitalAction($"/actions/default/in/key{i}", (data, handle) => { OnAction(localI, data, handle); });
            }
        }

        private void OnAction(int index, InputDigitalActionData_t data, ulong inputSourceHandle)
        {
            var inputName = inputSourceHandle == inputSourceHandleLeft ? "Left" :
                inputSourceHandle == inputSourceHandleRight ? "Right" :
                "N/A";
            Debug.WriteLine($"Key{index} - {inputName} : " + (data.bState ? "PRESSED" : "RELEASED"));
            lock (bindingsLock)
            {
                // TODO: Move this inside bottom if block as soon as we have actual buttons bound...
                if (bindings.ContainsKey(index))
                {
                    if(data.bState)
                    {
                        if(MainModel.LoadSetting(MainModel.Setting.Haptic))
                        {
                            if (inputSourceHandle == inputSourceHandleLeft) ovr.TriggerHapticPulseInController(ETrackedControllerRole.LeftHand);
                            if (inputSourceHandle == inputSourceHandleRight) ovr.TriggerHapticPulseInController(ETrackedControllerRole.RightHand);
                        }
                        if (MainModel.LoadSetting(MainModel.Setting.Notification))
                        {
                            ovr.EnqueueNotification(notificationOverlayHandle, $"Activated: Key {index}", notificationBitmap);
                        }
                    }
                    var binding = bindings[index];
                    SimulateKeyPress(data, binding);
                }
            }
        }
        #endregion

        #region keyboard_out
        private void SimulateKeyPress(InputDigitalActionData_t data, Tuple<VirtualKeyCode[], VirtualKeyCode[]> binding)
        {
            if (data.bState)
            {
                foreach (var vk in binding.Item1) sim.Keyboard.KeyDown(vk);
                foreach (var vk in binding.Item2) sim.Keyboard.KeyDown(vk);
            }
            else
            {
                foreach (var vk in binding.Item2) sim.Keyboard.KeyUp(vk);
                foreach (var vk in binding.Item1) sim.Keyboard.KeyUp(vk);
            }
        }
        #endregion

        public void TestStuff()
        {
            var values = Enum.GetValues(typeof(ETrackedDeviceProperty));
            foreach (ETrackedDeviceProperty i in values)
            {
                var name = Enum.GetName(typeof(ETrackedDeviceProperty), i);
                if (name.Contains("_String")) ovr.GetStringTrackedDeviceProperty(0, i);
                else if (name.Contains("_Float")) ovr.GetFloatTrackedDeviceProperty(0, i);
                else if (name.Contains("_Bool")) ovr.GetBooleanTrackedDeviceProperty(0, i);
            }
        }
    }
}
