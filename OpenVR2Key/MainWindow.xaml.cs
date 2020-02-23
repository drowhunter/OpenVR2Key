﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace OpenVR2Key
{
    public partial class MainWindow : Window
    {
        private readonly static int NO_OF_KEYS = 32;
        private readonly static string DEFAULT_KEY_LABEL = "Unbound: Click to bind keys to simulate";
        private MainController _controller;
        private List<BindingItem> _items = new List<BindingItem>();
        private object _activeElement;
        private string _currentlyRunningAppId = MainModel.CONFIG_DEFAULT;
        private System.Windows.Forms.NotifyIcon _notifyIcon;
        private HashSet<int> _activeKeys = new HashSet<int>();

        public MainWindow()
        {
            InitWindow();
            InitializeComponent();
            _controller = new MainController
            {
                // Reports on the status of OpenVR
                StatusUpdateAction = (connected) =>
                {
                    Debug.WriteLine($"Status Update Action: connected={connected}");
                    var message = connected ? "Connected" : "Disconnected";
                    var color = connected ? Brushes.OliveDrab : Brushes.Tomato;
                    Dispatcher.Invoke(() =>
                    {
                        Label_OpenVR.Content = message;
                        Label_OpenVR.Background = color;
                    });
                },

                // Triggered when a new scene app is detected
                AppUpdateAction = (appId) =>
                {
                    Debug.WriteLine($"App Update Action: appId={appId}");
                    _currentlyRunningAppId = appId;
                    var color = Brushes.OliveDrab;
                    if (appId == MainModel.CONFIG_DEFAULT)
                    {
                        color = Brushes.Tomato;
                    }
                    Dispatcher.Invoke(() =>
                    {
                        Debug.WriteLine($"Setting AppID to: {appId}");
                        Label_Application.Content = appId;
                        Label_Application.Background = color;
                    });

                },

                // We should update the text on the current binding we are recording
                KeyTextUpdateAction = (keyText, cancel) =>
                {
                    Debug.WriteLine($"Key Text Update Action: keyText={keyText}");
                    Dispatcher.Invoke(() =>
                    {
                        if (_activeElement != null)
                        {
                            (_activeElement as Label).Content = keyText;
                            if(cancel) UpdateLabel(_activeElement as Label, false);
                        }
                    });
                },

                // We have loaded a config
                ConfigRetrievedAction = (config, forceButtonOff) =>
                {
                    var loaded = config != null;
                    if(loaded) Debug.WriteLine($"Config Retrieved Action: count()={config.Count}");
                    Dispatcher.Invoke(() =>
                    {
                        if(loaded) InitList(config);
                        UpdateConfigButton(loaded, forceButtonOff);
                    });
                },

                KeyActivatedAction = (index, on) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (on) _activeKeys.Add(index);
                        else _activeKeys.Remove(index);
                        if (_activeKeys.Count > 0) Label_Keys.Content = string.Join(", ", _activeKeys);
                        else Label_Keys.Content = "None";
                    });
                }
            };

            // Receives error messages from OpenVR
            _controller.SetDebugLogAction((message) =>
            {
                Dispatcher.Invoke(() =>
                {
                    var time = DateTime.Now.ToString("HH:mm:ss");
                    var oldLog = TextBox_Log.Text;
                    var lines = oldLog.Split('\n');
                    Array.Resize(ref lines, 3);
                    var newLog = string.Join("\n", lines);
                    TextBox_Log.Text = $"{time}: {message}\n{newLog}";
                });
            });

            // Init the things
            InitList();
            _controller.Init();
            InitSettings();
            InitTrayIcon();
        }

        private void InitWindow()
        {
            var shouldMinimize = MainModel.LoadSetting(MainModel.Setting.Minimize);
            var onlyInTray = MainModel.LoadSetting(MainModel.Setting.Tray);

            if (shouldMinimize)
            {
                WindowState = WindowState.Minimized;
                ShowInTaskbar = !onlyInTray;
            }
        }

        private void InitTrayIcon()
        {
            var icon = Properties.Resources.app_icon.Clone() as System.Drawing.Icon;
            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            _notifyIcon.Click += NotifyIcon_Click;
            _notifyIcon.Icon = icon;
            _notifyIcon.Visible = true;            
        }

        #region bindings

        // Fill list with entries
        private void InitList(Dictionary<int, Key[]> config = null)
        {
            if (config == null) config = new Dictionary<int, Key[]>();
            _items.Clear();
            for (var i = 1; i <= NO_OF_KEYS; i++)
            {
                var text = config.ContainsKey(i) ? _controller.GetKeysLabel(config[i]) : string.Empty;
                if (text == string.Empty) text = DEFAULT_KEY_LABEL;
                _items.Add(new BindingItem()
                {
                    Index = i,
                    Label = GetKeyLabelText(i),
                    Text = text
                });
            }
            ItemsControl_Bindings.ItemsSource = null;
            ItemsControl_Bindings.ItemsSource = _items;
        }

        private string GetKeyLabelText(int index)
        {
            return $"Key {index}";
        }

        // Binding data class
        public class BindingItem
        {
            public int Index { get; set; }
            public string Label { get; set; }
            public string Text { get; set; }
        }
        #endregion

        #region events
        // All key down events in the app
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            _controller.OnKeyDown(key);
        }

        // All key up events in the app
        private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            _controller.OnKeyUp(key);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            _notifyIcon.Dispose();
            base.OnClosing(e);
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            var onlyInTray = MainModel.LoadSetting(MainModel.Setting.Tray);
            switch (WindowState)
            {
                case WindowState.Minimized: ShowInTaskbar = !onlyInTray; break;
                default: ShowInTaskbar = true; break;
            }
        }

        #endregion

        #region actions
        private void UpdateConfigButton(bool hasConfig, bool forceButtonOff=false)
        {
            Debug.WriteLine($"Update Config Button: {hasConfig}");
            if(!forceButtonOff && _controller.AppIsRunning())
            {
                Button_AppBinding.Content = hasConfig ? "Remove app-specific config" : "Add app-specific config";
                Button_AppBinding.IsEnabled = true;
                Button_AppBinding.Tag = hasConfig;
            } else
            {
                Button_AppBinding.Content = "No application running right now";
                Button_AppBinding.IsEnabled = false;
                Button_AppBinding.Tag = null;
            }
        }

        // Click to either create new config for current app or remote the existing config.
        private void Button_AppBinding_Click(object sender, RoutedEventArgs e)
        {
            var tag = (sender as Button).Tag;
            switch(tag)
            {
                case null:
                    // This should never happen as the button cannot be pressed while disabled.
                    break;
                case true:
                    MainModel.ClearBindings();
                    MainModel.DeleteConfig();
                    _controller.LoadConfig(true); // Loads default
                    UpdateConfigButton(false);
                    break;
                case false:
                    MainModel.SetConfigName(_currentlyRunningAppId);
                    MainModel.StoreConfig(new Dictionary<int, Key[]>());
                    _controller.LoadConfig(); // Loads the empty new one
                    UpdateConfigButton(true);
                    break;
            }
        }

        // This should clear all bindings from the current config
        private void Button_ClearAll_Click(object sender, RoutedEventArgs e)
        {
            MainModel.ClearBindings();
            InitList();
        }
        #endregion

        #region bindings
        // Main action that is clicked from the list to start and end registration of keys
        private void Label_RecordSave_Click(object sender, MouseButtonEventArgs e)
        {
            var element = sender as Label;
            var dataItem = element.DataContext as BindingItem;
            var active = _controller.ToggleRegisteringKey(dataItem.Index, element, out object activeElement);
            UpdateLabel(activeElement as Label, active);
            if (active) _activeElement = activeElement;
        }

        private void UpdateLabel(Label label, bool active)
        {
            {
                label.Foreground = active ? Brushes.DarkRed : Brushes.Black;
                label.BorderBrush = active ? Brushes.Tomato : Brushes.DarkGray;
                label.Background = active ? Brushes.LightPink : Brushes.LightGray;
            }
        }

        private void Label_HighlightOn(object sender, RoutedEventArgs e)
        {
            if (_activeElement != sender) (sender as Label).Background = Brushes.WhiteSmoke;
        }

        private void Label_HighlightOff(object sender, RoutedEventArgs e)
        {
            if (_activeElement != sender) (sender as Label).Background = Brushes.LightGray;
        }

        // Clear the current binding completely, TODO: cancel recording?
        private void Button_ClearCancel_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var dataItem = button.DataContext as BindingItem;
            MainModel.RemoveBinding(dataItem.Index);
            DockPanel sp = VisualTreeHelper.GetParent(button) as DockPanel;
            var element = sp.Children[2] as Label;
            element.Content = DEFAULT_KEY_LABEL;
        }
        #endregion

        #region settings
        // Load settings and apply them to the checkboxes
        private void InitSettings()
        {
            CheckBox_Minimize.IsChecked = MainModel.LoadSetting(MainModel.Setting.Minimize);
            CheckBox_Tray.IsChecked = MainModel.LoadSetting(MainModel.Setting.Tray);
            CheckBox_DebugNotifications.IsChecked = MainModel.LoadSetting(MainModel.Setting.Notification);
            CheckBox_HapticFeedback.IsChecked = MainModel.LoadSetting(MainModel.Setting.Haptic);
        }
        private bool CheckboxValue(RoutedEventArgs e)
        {
            var name = e.RoutedEvent.Name;
            return name == "Checked";
        }
        private void CheckBox_Minimize_Checked(object sender, RoutedEventArgs e)
        {
            MainModel.UpdateSetting(MainModel.Setting.Minimize, CheckboxValue(e));
        }

        private void CheckBox_Tray_Checked(object sender, RoutedEventArgs e)
        {
            MainModel.UpdateSetting(MainModel.Setting.Tray, CheckboxValue(e));
        }

        private void CheckBox_DebugNotifications_Checked(object sender, RoutedEventArgs e)
        {
            MainModel.UpdateSetting(MainModel.Setting.Notification, CheckboxValue(e));
        }

        private void CheckBox_HapticFeedback_Checked(object sender, RoutedEventArgs e)
        {
            MainModel.UpdateSetting(MainModel.Setting.Haptic, CheckboxValue(e));
        }
        #endregion

        #region trayicon
        private void NotifyIcon_Click(object sender, EventArgs e)
        {
            WindowState = WindowState.Normal;
            Activate();
        }
        #endregion
    }
}
