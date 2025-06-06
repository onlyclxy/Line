using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Line_wpf
{
    public partial class MonitoredAppsWindow : Window
    {
        public ObservableCollection<MainWindow.MonitoredApp> MonitoredApps { get; private set; }
        public bool DialogResultOK { get; private set; }
        
        // Windows API for window detection and global mouse hooks
        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr SetCapture(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr GetCapture();

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_MOUSEMOVE = 0x0200;

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // Window picker state
        private bool isPickingWindow = false;
        private bool isDragging = false;
        private System.Windows.Input.Cursor originalCursor;
        private IntPtr hookID = IntPtr.Zero;
        private LowLevelMouseProc hookProc;

        public MonitoredAppsWindow(List<MainWindow.MonitoredApp> currentApps)
        {
            InitializeComponent();
            
            // 深拷贝当前应用程序列表
            MonitoredApps = new ObservableCollection<MainWindow.MonitoredApp>();
            foreach (var app in currentApps)
            {
                MonitoredApps.Add(new MainWindow.MonitoredApp(app.Name, app.IsEnabled));
            }
            
            // 设置数据源
            dataGrid.ItemsSource = MonitoredApps;
            
            // 初始化鼠标钩子回调
            hookProc = HookCallback;
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var inputDialog = new InputDialog("添加监控程序", "请输入要监控的程序窗口标题：");
            if (inputDialog.ShowDialog() == true)
            {
                string appName = inputDialog.InputText.Trim();
                if (!string.IsNullOrWhiteSpace(appName))
                {
                    // 检查是否已存在
                    if (!MonitoredApps.Any(a => a.Name.Equals(appName, StringComparison.OrdinalIgnoreCase)))
                    {
                        MonitoredApps.Add(new MainWindow.MonitoredApp(appName, true));
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("该程序已在列表中！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (dataGrid.SelectedItem is MainWindow.MonitoredApp selectedApp)
            {
                var result = System.Windows.MessageBox.Show($"确定要删除 \"{selectedApp.Name}\" 吗？", 
                    "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    MonitoredApps.Remove(selectedApp);
                }
            }
            else
            {
                System.Windows.MessageBox.Show("请先选择要删除的程序！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void WindowPickerButton_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                StartWindowPicking();
            }
        }

        private void WindowPickerButton_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (isPickingWindow && isDragging)
            {
                StopWindowPicking();
            }
        }

        private void StartWindowPicking()
        {
            isPickingWindow = true;
            isDragging = false;
            originalCursor = this.Cursor;
            
            // 设置全局鼠标钩子
            hookID = SetWindowsHookEx(WH_MOUSE_LL, hookProc, GetModuleHandle("user32"), 0);
            
            // 更新按钮状态
            windowPickerButton.Content = "松开获取";
            windowPickerButton.Background = System.Windows.Media.Brushes.LightCoral;
            
            // 设置十字光标
            this.Cursor = System.Windows.Input.Cursors.Cross;
            
            // 直接开始拖拽
            isDragging = true;
        }

        private void StopWindowPicking()
        {
            if (hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hookID);
                hookID = IntPtr.Zero;
            }
            
            isPickingWindow = false;
            isDragging = false;
            
            // 恢复按钮状态
            windowPickerButton.Content = "🎯 拖拽拾取";
            windowPickerButton.Background = System.Windows.SystemColors.ControlBrush;
            
            // 恢复光标
            this.Cursor = originalCursor;
            
            // 获取鼠标位置并检测窗口
            if (GetCursorPos(out POINT point))
            {
                IntPtr hwnd = WindowFromPoint(point);
                if (hwnd != IntPtr.Zero)
                {
                    StringBuilder title = new StringBuilder(256);
                    GetWindowText(hwnd, title, title.Capacity);
                    string windowTitle = title.ToString();
                    
                    ProcessCapturedWindow(windowTitle);
                }
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && isPickingWindow)
            {
                if (wParam == (IntPtr)WM_LBUTTONUP && isDragging)
                {
                    // 在UI线程中停止窗口拾取
                    this.Dispatcher.Invoke(() => {
                        StopWindowPicking();
                    });
                    return (IntPtr)1; // 阻止消息传递
                }
            }
            
            return CallNextHookEx(hookID, nCode, wParam, lParam);
        }

        private void ProcessCapturedWindow(string title)
        {
            if (!string.IsNullOrWhiteSpace(title))
            {
                // 询问用户是否添加
                var result = System.Windows.MessageBox.Show($"检测到窗口标题：\n\n\"{title}\"\n\n是否添加到监控列表？", 
                    "确认添加", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    // 检查是否已存在
                    if (!MonitoredApps.Any(a => a.Name.Equals(title, StringComparison.OrdinalIgnoreCase)))
                    {
                        MonitoredApps.Add(new MainWindow.MonitoredApp(title, true));
                        System.Windows.MessageBox.Show("已成功添加到监控列表！", "添加成功", 
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("该程序已在列表中！", "提示", 
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            else
            {
                System.Windows.MessageBox.Show("无法获取窗口标题，请重试。", "获取失败", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResultOK = true;
            this.DialogResult = true;
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResultOK = false;
            this.DialogResult = false;
            this.Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            if (isPickingWindow)
            {
                StopWindowPicking();
            }
            base.OnClosed(e);
        }
    }

    // 简单的输入对话框
    public partial class InputDialog : Window
    {
        public string InputText { get; private set; } = "";

        public InputDialog(string title, string prompt)
        {
            InitializeInputDialog(title, prompt);
        }

        private void InitializeInputDialog(string title, string prompt)
        {
            this.Title = title;
            this.Width = 400;
            this.Height = 150;
            this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            this.ResizeMode = ResizeMode.NoResize;

            var grid = new System.Windows.Controls.Grid();
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(40) });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(40) });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(40) });

            var label = new System.Windows.Controls.Label
            {
                Content = prompt,
                Margin = new Thickness(12, 10, 12, 0)
            };
            System.Windows.Controls.Grid.SetRow(label, 0);

            var textBox = new System.Windows.Controls.TextBox
            {
                Margin = new Thickness(12, 5, 12, 5),
                VerticalAlignment = VerticalAlignment.Center
            };
            System.Windows.Controls.Grid.SetRow(textBox, 1);

            var buttonPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Margin = new Thickness(12, 5, 12, 5)
            };

            var okButton = new System.Windows.Controls.Button
            {
                Content = "确定",
                Width = 75,
                Height = 25,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };
            okButton.Click += (s, e) => {
                InputText = textBox.Text;
                this.DialogResult = true;
            };

            var cancelButton = new System.Windows.Controls.Button
            {
                Content = "取消",
                Width = 75,
                Height = 25,
                IsCancel = true
            };
            cancelButton.Click += (s, e) => {
                this.DialogResult = false;
            };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            System.Windows.Controls.Grid.SetRow(buttonPanel, 2);

            grid.Children.Add(label);
            grid.Children.Add(textBox);
            grid.Children.Add(buttonPanel);

            this.Content = grid;
            
            // 设置焦点到文本框
            this.Loaded += (s, e) => textBox.Focus();
        }
    }
} 