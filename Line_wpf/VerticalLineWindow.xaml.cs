using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using Color = System.Drawing.Color;
using MessageBox = System.Windows.MessageBox;
using System.Windows.Threading;

namespace Line_wpf
{
    // 可拖拽的竖线类
    public class DraggableVerticalLine : Window
    {
        private bool isDragging = false;
        private System.Drawing.Point lastCursor;
        private bool mouseClickThrough;

        // Windows API for mouse click-through
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int GWL_EXSTYLE = (-20);

        // 新增：窗口消息常量
        private const int WM_SETCURSOR = 0x0020;
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int MA_NOACTIVATE = 3;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_SHOWWINDOW = 0x0040;

        public DraggableVerticalLine(int width, int height, System.Drawing.Color color, double opacity, bool clickThrough)
        {
            this.WindowStyle = WindowStyle.None;
            this.ShowInTaskbar = false;
            this.Topmost = true;
            this.AllowsTransparency = true;
            this.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B));
            this.Opacity = opacity;
            this.Width = width;
            this.Height = height;
            this.ResizeMode = ResizeMode.NoResize;
            
            mouseClickThrough = clickThrough;
            
            // 如果不是鼠标穿透模式，添加拖拽事件和设置光标
            if (!mouseClickThrough)
            {
                this.MouseDown += OnMouseDown;
                this.MouseMove += OnMouseMove;
                this.MouseUp += OnMouseUp;
                this.Cursor = System.Windows.Input.Cursors.SizeWE; // 设置为水平调整大小光标
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            var hwnd = new WindowInteropHelper(this).Handle;
            var hwndSource = HwndSource.FromHwnd(hwnd);
            hwndSource.AddHook(HwndHook);
            
            // 设置鼠标穿透属性
            if (mouseClickThrough)
            {
                int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
                ex |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
                SetWindowLong(hwnd, GWL_EXSTYLE, ex);
            }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (mouseClickThrough)
            {
                if (msg == WM_MOUSEACTIVATE)
                {
                    handled = true;
                    return new IntPtr(MA_NOACTIVATE);
                }
                if (msg == WM_SETCURSOR)
                {
                    handled = true;
                    return IntPtr.Zero;
                }
            }
            return IntPtr.Zero;
        }

        public void SetClickThrough(bool enable)
        {
            mouseClickThrough = enable;
            var hwnd = new WindowInteropHelper(this).Handle;
            
            if (hwnd != IntPtr.Zero)
            {
                if (enable)
                {
                    // 启用穿透模式：移除拖拽事件，不设置光标
                    this.MouseDown -= OnMouseDown;
                    this.MouseMove -= OnMouseMove;
                    this.MouseUp -= OnMouseUp;
                    
                    int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                    SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT);
                }
                else
                {
                    // 禁用穿透模式：移除透明样式，添加拖拽事件
                    int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                    SetWindowLong(hwnd, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
                    this.Cursor = System.Windows.Input.Cursors.SizeWE;
                    // 添加拖拽事件
                    this.MouseDown -= OnMouseDown; // 先移除避免重复
                    this.MouseMove -= OnMouseMove;
                    this.MouseUp -= OnMouseUp;
                    this.MouseDown += OnMouseDown;
                    this.MouseMove += OnMouseMove;
                    this.MouseUp += OnMouseUp;
                }
            }
        }

        private void OnMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                isDragging = true;
                lastCursor = System.Windows.Forms.Cursor.Position;
                this.CaptureMouse();
            }
        }

        private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (isDragging)
            {
                var currentCursor = System.Windows.Forms.Cursor.Position;
                int deltaX = currentCursor.X - lastCursor.X;
                
                this.Left += deltaX;
                lastCursor = currentCursor;
            }
        }

        private void OnMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Released)
            {
                isDragging = false;
                this.ReleaseMouseCapture();
            }
        }

        public void SetPhysicalBounds(int x, int y, int width, int height)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                SetWindowPos(hwnd, HWND_TOPMOST, x, y, width, height, SWP_SHOWWINDOW);
            }
        }
    }

    public partial class VerticalLineWindow : Window
    {
        private const int WM_HOTKEY = 0x0312;
        private Dictionary<int, List<DraggableVerticalLine>> verticalLines = new Dictionary<int, List<DraggableVerticalLine>>();
        private Dictionary<int, bool> lineStates = new Dictionary<int, bool>();
        private NotifyIcon trayIcon;
        private MainWindow mainWindow;

        // 线条默认宽度为1像素
        private int lineWidth = 1;

        // 线条颜色，默认为蓝色
        private System.Drawing.Color lineColor = System.Drawing.Color.Blue;

        // 线条透明度，默认为100%
        private int lineOpacity = 100;

        // 鼠标穿透设置
        private bool mouseClickThrough = true;

        // 显示模式：false=仅当前屏幕，true=全部屏幕
        private bool showOnAllScreens = false;

        // 热键ID基础值（101-104用于开启，105-108用于关闭）
        private const int BASE_HOTKEY_ID_ON = 500;
        private const int BASE_HOTKEY_ID_OFF = 600;

        // 热键绑定状态
        private bool[] hotkeyEnabled = new bool[] { true, true, false, false }; // 默认启用前两组

        // 配置文件路径
        private readonly string configPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ScreenLine",
            "vertical_config.json"
        );

        // 配置类
        private class Config
        {
            public bool[] HotkeyEnabled { get; set; }
            public int LineWidth { get; set; }
            public string LineColor { get; set; }
            public int LineOpacity { get; set; }
            public bool MouseClickThrough { get; set; }
            public bool ShowOnAllScreens { get; set; }
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // 修饰键常量
        private const int MOD_CONTROL = 0x0002;
        private const int MOD_SHIFT = 0x0004;
        private const int MOD_ALT = 0x0001;

        // 增强的置顶Windows API
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        // 置顶相关常量
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_SHOWWINDOW = 0x0040;

        public VerticalLineWindow(NotifyIcon existingTrayIcon, MainWindow mainWindow)
        {
            this.trayIcon = existingTrayIcon;
            this.mainWindow = mainWindow;

            // 加载配置
            LoadConfig();

            // 窗体基本设置
            this.WindowStyle = WindowStyle.None;
            this.ShowInTaskbar = false;
            this.Topmost = true;
            this.Background = System.Windows.Media.Brushes.Transparent;
            this.AllowsTransparency = true;
            this.Opacity = 0;
            this.Width = 1;
            this.Height = 1;

            // 设置窗体为不可见状态
            this.Visibility = Visibility.Hidden;

            // 预先显示一次窗体来确保句柄创建，然后立即隐藏
            this.Show();
            this.Hide();

            // 预先创建所有可能需要的竖线窗体
            InitializeVerticalLines();

            // 注意：热键注册延迟到OnSourceInitialized中
            AddVerticalLineMenuItems();
        }

        private void ShowInitialLine()
        {
            // 移除这个方法，不再需要
        }

        /// <summary>
        /// 预先创建所有可能需要的竖线窗体
        /// </summary>
        private void InitializeVerticalLines()
        {
            Console.WriteLine("[竖线] 开始预创建竖线窗体");
            
            // 为每个可能的热键索引预创建线条
            for (int i = 0; i < 4; i++)
            {
                var lines = new List<DraggableVerticalLine>();
                
                // 如果是全屏模式，为每个屏幕创建一条线
                // 如果是单屏模式，只创建一条线（主屏幕）
                if (showOnAllScreens)
                {
                    foreach (Screen screen in Screen.AllScreens)
                    {
                        var line = CreateVerticalLine(screen.Bounds.Height);
                        line.SetPhysicalBounds(0, screen.Bounds.Y, lineWidth, screen.Bounds.Height);
                        line.Opacity = 0; // 初始隐藏
                        line.Show(); // 显示但透明
                        lines.Add(line);
                        Console.WriteLine($"[竖线] 为热键 {i + 1} 预创建屏幕线条: 屏幕{screen.Bounds}");
                    }
                }
                else
                {
                    // 单屏模式：只为主屏幕创建一条线
                    var primaryScreen = Screen.PrimaryScreen;
                    var line = CreateVerticalLine(primaryScreen.Bounds.Height);
                    line.SetPhysicalBounds(0, primaryScreen.Bounds.Y, lineWidth, primaryScreen.Bounds.Height);
                    line.Opacity = 0; // 初始隐藏
                    line.Show(); // 显示但透明
                    lines.Add(line);
                    Console.WriteLine($"[竖线] 为热键 {i + 1} 预创建单屏线条: 主屏幕{primaryScreen.Bounds}");
                }
                
                verticalLines[i] = lines;
                lineStates[i] = false;
            }
            
            Console.WriteLine("[竖线] 竖线窗体预创建完成");
        }

        /// <summary>
        /// 创建一个竖线窗体
        /// </summary>
        private DraggableVerticalLine CreateVerticalLine(int height)
        {
            var line = new DraggableVerticalLine(
                lineWidth,
                height,
                lineColor,
                lineOpacity / 100.0,
                mouseClickThrough
            )
            {
                Topmost = true
            };
            
            return line;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            var hwnd = new WindowInteropHelper(this).Handle;
            var hwndSource = HwndSource.FromHwnd(hwnd);
            hwndSource.AddHook(HwndHook);
            
            // 现在窗体句柄已创建，可以安全注册热键
            InitializeHotkeys();
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int hotkeyId = wParam.ToInt32();
                
                // 处理开启热键 (Ctrl+Shift+1-4)
                if (hotkeyId >= BASE_HOTKEY_ID_ON && hotkeyId < BASE_HOTKEY_ID_ON + 4)
                {
                    int lineIndex = hotkeyId - BASE_HOTKEY_ID_ON;
                    if (hotkeyEnabled[lineIndex])
                    {
                        ShowVerticalLine(lineIndex);
                    }
                }
                // 处理关闭热键 (Ctrl+Alt+1-4)
                else if (hotkeyId >= BASE_HOTKEY_ID_OFF && hotkeyId < BASE_HOTKEY_ID_OFF + 4)
                {
                    int lineIndex = hotkeyId - BASE_HOTKEY_ID_OFF;
                    if (hotkeyEnabled[lineIndex])
                    {
                        HideVerticalLine(lineIndex);
                    }
                }
                
                handled = true;
            }
            
            return IntPtr.Zero;
        }

        private void InitializeHotkeys()
        {
            // 根据配置注册所有启用的热键
            for (int i = 0; i < hotkeyEnabled.Length; i++)
            {
                if (hotkeyEnabled[i])
                {
                    RegisterHotkeyPair(i);
                }
            }
        }

        private void RegisterHotkeyPair(int index)
        {
            try
            {
                if (index >= 0 && index < 4)
                {
                    // 检查全局热键是否被禁用
                    if (mainWindow?.IsGlobalHotkeysDisabled == true)
                    {
                        System.Windows.MessageBox.Show("全局快捷键已被禁用，无法注册竖线热键。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    
                    Keys key = Keys.D1 + index; // 数字键1-4
                    var hwnd = new WindowInteropHelper(this).Handle;
                    
                    Console.WriteLine($"[竖线] RegisterHotkeyPair: 窗口句柄: {hwnd}");
                    
                    if (hwnd != IntPtr.Zero)
                    {
                        // 注册 Ctrl+Alt+1-4
                        bool onSuccess = RegisterHotKey(hwnd, BASE_HOTKEY_ID_ON + index, MOD_CONTROL | MOD_ALT, (int)key);
                        Console.WriteLine($"[竖线] RegisterHotKey ON (Ctrl+Alt+{index + 1}): {onSuccess}");
                        
                        // 注册 Ctrl+Shift+Alt+1-4
                        bool offSuccess = RegisterHotKey(hwnd, BASE_HOTKEY_ID_OFF + index, MOD_CONTROL | MOD_SHIFT | MOD_ALT, (int)key);
                        Console.WriteLine($"[竖线] RegisterHotKey OFF (Ctrl+Shift+Alt+{index + 1}): {offSuccess}");

                        if (onSuccess && offSuccess)
                        {
                            hotkeyEnabled[index] = true;
                            Console.WriteLine($"[竖线] 热键 {index + 1} 注册成功");
                        }
                        else
                        {
                            // 如果注册失败，注销已注册的热键
                            if (onSuccess) {
                                UnregisterHotKey(hwnd, BASE_HOTKEY_ID_ON + index);
                                Console.WriteLine($"[竖线] 回滚已注册的ON热键 {index + 1}");
                            }
                            if (offSuccess) {
                                UnregisterHotKey(hwnd, BASE_HOTKEY_ID_OFF + index);
                                Console.WriteLine($"[竖线] 回滚已注册的OFF热键 {index + 1}");
                            }
                            
                            hotkeyEnabled[index] = false;
                            Console.WriteLine($"[竖线] 热键 {index + 1} 注册失败");
                            System.Windows.MessageBox.Show($"竖线热键 Ctrl+Alt+{index + 1} 注册失败，可能已被其他程序占用。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[竖线] 窗口句柄为空，无法注册热键 {index + 1}");
                        hotkeyEnabled[index] = false;
                    }
                }
            }
            catch (Exception ex)
            {
                hotkeyEnabled[index] = false;
                UpdateHotkeyMenuCheckedState();
                SaveConfig();
                System.Windows.MessageBox.Show($"注册竖线热键时发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UnregisterHotkeyPair(int index)
        {
            if (index >= 0 && index < 4)
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                UnregisterHotKey(hwnd, BASE_HOTKEY_ID_ON + index);
                UnregisterHotKey(hwnd, BASE_HOTKEY_ID_OFF + index);
            }
        }

        private void SetClickThrough(bool enable)
        {
            mouseClickThrough = enable;
            foreach (var linesList in verticalLines.Values)
            {
                foreach (var line in linesList)
                {
                    line.SetClickThrough(enable);
                }
            }
            SaveConfig();
        }

        private void AddVerticalLineMenuItems()
        {
            if (trayIcon?.ContextMenuStrip == null) return;

            ToolStripMenuItem verticalLineMenu = new ToolStripMenuItem("持续竖线");

            // 热键绑定菜单
            ToolStripMenuItem hotkeyBindingMenu = new ToolStripMenuItem("热键绑定");
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                var item = new ToolStripMenuItem($"Ctrl+Alt+{i + 1}/Ctrl+Shift+Alt+{i + 1}", null, (s, e) => {
                    ToggleHotkeyBinding(index);
                });
                item.Checked = hotkeyEnabled[i];
                hotkeyBindingMenu.DropDownItems.Add(item);
            }

            // 显示模式菜单
            ToolStripMenuItem displayModeMenu = new ToolStripMenuItem("显示模式");
            var currentScreenItem = new ToolStripMenuItem("仅鼠标所在屏幕", null, (s, e) => {
                showOnAllScreens = false;
                if (s is ToolStripMenuItem menuItem)
                {
                    menuItem.Checked = true;
                    // 更新另一个选项的状态
                    foreach (ToolStripMenuItem item in displayModeMenu.DropDownItems)
                    {
                        if (item.Text == "所有屏幕")
                        {
                            item.Checked = false;
                            break;
                        }
                    }
                }
                SaveConfig();
            });
            currentScreenItem.Checked = !showOnAllScreens;

            var allScreensItem = new ToolStripMenuItem("所有屏幕", null, (s, e) => {
                showOnAllScreens = true;
                if (s is ToolStripMenuItem menuItem)
                {
                    menuItem.Checked = true;
                    // 更新另一个选项的状态
                    foreach (ToolStripMenuItem item in displayModeMenu.DropDownItems)
                    {
                        if (item.Text == "仅鼠标所在屏幕")
                        {
                            item.Checked = false;
                            break;
                        }
                    }
                }
                SaveConfig();
            });
            allScreensItem.Checked = showOnAllScreens;

            displayModeMenu.DropDownItems.Add(currentScreenItem);
            displayModeMenu.DropDownItems.Add(allScreensItem);

            // 鼠标穿透选项
            var mousePenetrationItem = new ToolStripMenuItem("鼠标穿透", null, (s, e) => {
                SetClickThrough(!mouseClickThrough);
                if (s is ToolStripMenuItem menuItem)
                {
                    menuItem.Checked = mouseClickThrough;
                }
            });
            mousePenetrationItem.Checked = mouseClickThrough;

            // 线条粗细菜单
            ToolStripMenuItem lineThicknessItem = new ToolStripMenuItem("竖线粗细");
            AddThicknessMenuItem(lineThicknessItem, "细线 (1像素)", 1);
            AddThicknessMenuItem(lineThicknessItem, "中等 (2像素)", 2);
            AddThicknessMenuItem(lineThicknessItem, "粗线 (3像素)", 3);
            AddThicknessMenuItem(lineThicknessItem, "很粗 (5像素)", 5);

            // 线条颜色菜单
            ToolStripMenuItem lineColorItem = new ToolStripMenuItem("竖线颜色");
            AddColorMenuItem(lineColorItem, "红色", System.Drawing.Color.Red);
            AddColorMenuItem(lineColorItem, "绿色", System.Drawing.Color.Green);
            AddColorMenuItem(lineColorItem, "蓝色", System.Drawing.Color.Blue);
            AddColorMenuItem(lineColorItem, "黄色", System.Drawing.Color.Yellow);
            AddColorMenuItem(lineColorItem, "橙色", System.Drawing.Color.Orange);
            AddColorMenuItem(lineColorItem, "紫色", System.Drawing.Color.Purple);
            AddColorMenuItem(lineColorItem, "青色", System.Drawing.Color.Cyan);
            AddColorMenuItem(lineColorItem, "黑色", System.Drawing.Color.FromArgb(1, 1, 1));
            AddColorMenuItem(lineColorItem, "白色", System.Drawing.Color.White);

            // 透明度菜单
            ToolStripMenuItem transparencyItem = new ToolStripMenuItem("竖线透明度");
            AddTransparencyMenuItem(transparencyItem, "100% (不透明)", 100);
            AddTransparencyMenuItem(transparencyItem, "75%", 75);
            AddTransparencyMenuItem(transparencyItem, "50%", 50);
            AddTransparencyMenuItem(transparencyItem, "25%", 25);

            // 添加所有子菜单
            verticalLineMenu.DropDownItems.Add(hotkeyBindingMenu);
            verticalLineMenu.DropDownItems.Add(displayModeMenu);
            verticalLineMenu.DropDownItems.Add(mousePenetrationItem);
            verticalLineMenu.DropDownItems.Add(lineThicknessItem);
            verticalLineMenu.DropDownItems.Add(lineColorItem);
            verticalLineMenu.DropDownItems.Add(transparencyItem);

            // 在瞬时横线菜单之后插入竖线菜单
            int insertIndex = -1;
            for (int i = 0; i < trayIcon.ContextMenuStrip.Items.Count; i++)
            {
                if (trayIcon.ContextMenuStrip.Items[i] is ToolStripMenuItem menuItem && 
                    menuItem.Text == "瞬时横线")
                {
                    insertIndex = i + 1;
                    break;
                }
            }

            if (insertIndex != -1)
            {
                trayIcon.ContextMenuStrip.Items.Insert(insertIndex, verticalLineMenu);
            }
            else
            {
                // 如果找不到瞬时横线菜单，就在分隔符前插入
                int separatorIndex = -1;
                for (int i = 0; i < trayIcon.ContextMenuStrip.Items.Count; i++)
                {
                    if (trayIcon.ContextMenuStrip.Items[i] is ToolStripSeparator)
                    {
                        separatorIndex = i;
                        break;
                    }
                }

                if (separatorIndex != -1)
                {
                    trayIcon.ContextMenuStrip.Items.Insert(separatorIndex, verticalLineMenu);
                }
                else
                {
                    trayIcon.ContextMenuStrip.Items.Add(verticalLineMenu);
                }
            }
        }

        private void AddThicknessMenuItem(ToolStripMenuItem parent, string text, int thickness)
        {
            var item = new ToolStripMenuItem(text, null, (s, e) => {
                ChangeLineThickness(thickness);
            });
            item.Checked = (thickness == lineWidth);
            parent.DropDownItems.Add(item);
        }

        private void AddColorMenuItem(ToolStripMenuItem parent, string name, System.Drawing.Color color)
        {
            var colorPreview = new System.Drawing.Bitmap(16, 16);
            using (var g = System.Drawing.Graphics.FromImage(colorPreview))
            {
                g.FillRectangle(new System.Drawing.SolidBrush(color), 0, 0, 16, 16);
                g.DrawRectangle(System.Drawing.Pens.Gray, 0, 0, 15, 15);
            }

            var item = new ToolStripMenuItem(name, colorPreview, (s, e) => {
                ChangeLineColor(color);
            });
            item.Checked = color.Equals(lineColor);
            parent.DropDownItems.Add(item);
        }

        private void AddTransparencyMenuItem(ToolStripMenuItem parent, string name, int value)
        {
            var item = new ToolStripMenuItem(name, null, (s, e) => {
                ChangeLineTransparency(value);
            });
            item.Checked = (value == lineOpacity);
            parent.DropDownItems.Add(item);
        }

        private void ToggleHotkeyBinding(int index)
        {
            Console.WriteLine($"[竖线] ToggleHotkeyBinding called: index={index}");
            
            if (index >= 0 && index < 4)
            {
                bool newState = !hotkeyEnabled[index];
                Console.WriteLine($"[竖线] 当前状态: {hotkeyEnabled[index]} -> 目标状态: {newState}");
                
                if (newState)
                {
                    // 检查全局热键是否被禁用
                    if (mainWindow?.IsGlobalHotkeysDisabled == true)
                    {
                        Console.WriteLine($"[竖线] 全局快捷键已被禁用，无法注册热键 {index + 1}");
                        System.Windows.MessageBox.Show("全局快捷键已被禁用，无法注册竖线热键。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    
                    // 尝试注册热键
                    Keys key = Keys.D1 + index;
                    var hwnd = new WindowInteropHelper(this).Handle;
                    Console.WriteLine($"[竖线] 尝试注册热键 {index + 1}: Ctrl+Alt+{index + 1} 和 Ctrl+Shift+Alt+{index + 1}");
                    Console.WriteLine($"[竖线] 窗口句柄: {hwnd}");
                    
                    if (hwnd != IntPtr.Zero)
                    {
                        // 注册 Ctrl+Alt+1-4
                        bool onSuccess = RegisterHotKey(hwnd, BASE_HOTKEY_ID_ON + index, MOD_CONTROL | MOD_ALT, (int)key);
                        Console.WriteLine($"[竖线] RegisterHotKey ON (Ctrl+Alt+{index + 1}): {onSuccess}");
                        
                        // 注册 Ctrl+Shift+Alt+1-4
                        bool offSuccess = RegisterHotKey(hwnd, BASE_HOTKEY_ID_OFF + index, MOD_CONTROL | MOD_SHIFT | MOD_ALT, (int)key);
                        Console.WriteLine($"[竖线] RegisterHotKey OFF (Ctrl+Shift+Alt+{index + 1}): {offSuccess}");

                        if (onSuccess && offSuccess)
                        {
                            hotkeyEnabled[index] = true;
                            Console.WriteLine($"[竖线] 热键 {index + 1} 注册成功");
                        }
                        else
                        {
                            // 如果注册失败，注销已注册的热键
                            if (onSuccess) {
                                UnregisterHotKey(hwnd, BASE_HOTKEY_ID_ON + index);
                                Console.WriteLine($"[竖线] 回滚已注册的ON热键 {index + 1}");
                            }
                            if (offSuccess) {
                                UnregisterHotKey(hwnd, BASE_HOTKEY_ID_OFF + index);
                                Console.WriteLine($"[竖线] 回滚已注册的OFF热键 {index + 1}");
                            }
                            
                            hotkeyEnabled[index] = false;
                            Console.WriteLine($"[竖线] 热键 {index + 1} 注册失败");
                            System.Windows.MessageBox.Show($"竖线热键 Ctrl+Alt+{index + 1} 注册失败，可能已被其他程序占用。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[竖线] 窗口句柄为空，无法注册热键 {index + 1}");
                        hotkeyEnabled[index] = false;
                        System.Windows.MessageBox.Show($"竖线热键注册失败：窗口句柄无效。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    Console.WriteLine($"[竖线] 注销热键 {index + 1}");
                    // 注销热键
                    UnregisterHotkeyPair(index);
                    hotkeyEnabled[index] = false;
                    
                    // 如果有对应的竖线，则移除它
                    if (verticalLines.ContainsKey(index))
                    {
                        foreach (var line in verticalLines[index])
                        {
                            line.Close();
                        }
                        verticalLines.Remove(index);
                        lineStates[index] = false;
                        Console.WriteLine($"[竖线] 移除了现有的竖线 {index + 1}");
                    }
                }
                
                Console.WriteLine($"[竖线] 更新菜单状态和保存配置");
                UpdateHotkeyMenuCheckedState();
                SaveConfig();
                Console.WriteLine($"[竖线] ToggleHotkeyBinding 完成: index={index}, 最终状态={hotkeyEnabled[index]}");
            }
        }

        private void UpdateHotkeyMenuCheckedState()
        {
            Console.WriteLine($"[竖线] UpdateHotkeyMenuCheckedState called");
            if (trayIcon?.ContextMenuStrip == null) 
            {
                Console.WriteLine($"[竖线] trayIcon?.ContextMenuStrip 为空");
                return;
            }

            foreach (ToolStripItem item in trayIcon.ContextMenuStrip.Items)
            {
                if (item is ToolStripMenuItem menuItem && menuItem.Text == "持续竖线")
                {
                    Console.WriteLine($"[竖线] 找到持续竖线菜单");
                    // 查找热键绑定子菜单
                    ToolStripMenuItem hotkeyMenu = null;
                    foreach (ToolStripItem subItem in menuItem.DropDownItems)
                    {
                        if (subItem is ToolStripMenuItem subMenuItem && subMenuItem.Text == "热键绑定")
                        {
                            hotkeyMenu = subMenuItem;
                            Console.WriteLine($"[竖线] 找到热键绑定子菜单");
                            break;
                        }
                    }
                    
                    if (hotkeyMenu != null)
                    {
                        Console.WriteLine($"[竖线] 开始更新热键菜单状态, 热键数量: {hotkeyMenu.DropDownItems.Count}");
                        // 更新热键绑定菜单项的选中状态
                        for (int i = 0; i < Math.Min(hotkeyMenu.DropDownItems.Count, hotkeyEnabled.Length); i++)
                        {
                            if (hotkeyMenu.DropDownItems[i] is ToolStripMenuItem subItem)
                            {
                                bool oldChecked = subItem.Checked;
                                subItem.Checked = hotkeyEnabled[i];
                                Console.WriteLine($"[竖线] 热键 {i + 1}: {oldChecked} -> {hotkeyEnabled[i]}");
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[竖线] 未找到热键绑定子菜单");
                    }
                    break;
                }
            }
        }

        private void ChangeLineThickness(int thickness)
        {
            lineWidth = thickness;
            
            // 更新所有竖线的宽度
            foreach (var linesList in verticalLines.Values)
            {
                foreach (var line in linesList)
                {
                    line.Width = lineWidth;
                }
            }
            
            // 更新菜单项选中状态
            UpdateThicknessMenuCheckedState();
            SaveConfig();
        }

        private void ChangeLineColor(System.Drawing.Color color)
        {
            lineColor = color;
            
            // 更新所有竖线的颜色
            foreach (var linesList in verticalLines.Values)
            {
                foreach (var line in linesList)
                {
                    line.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B));
                }
            }
            
            // 更新菜单项选中状态
            UpdateColorMenuCheckedState();
            SaveConfig();
        }

        private void ChangeLineTransparency(int value)
        {
            lineOpacity = value;
            
            // 更新所有竖线的透明度
            foreach (var linesList in verticalLines.Values)
            {
                foreach (var line in linesList)
                {
                    line.Opacity = lineOpacity / 100.0;
                }
            }
            
            // 更新菜单项选中状态
            UpdateTransparencyMenuCheckedState();
            SaveConfig();
        }

        private void UpdateThicknessMenuCheckedState()
        {
            UpdateMenuCheckedState("竖线粗细", item => {
                string thicknessStr = lineWidth.ToString();
                return item.Text.Contains(thicknessStr);
            });
        }

        private void UpdateColorMenuCheckedState()
        {
            UpdateMenuCheckedState("竖线颜色", item => {
                if (item.Image is System.Drawing.Bitmap bmp)
                {
                    try
                    {
                        var menuColor = bmp.GetPixel(8, 8);
                        return Math.Abs(menuColor.R - lineColor.R) < 5 &&
                               Math.Abs(menuColor.G - lineColor.G) < 5 &&
                               Math.Abs(menuColor.B - lineColor.B) < 5;
                    }
                    catch
                    {
                        return false;
                    }
                }
                return false;
            });
        }

        private void UpdateTransparencyMenuCheckedState()
        {
            UpdateMenuCheckedState("竖线透明度", item => item.Text.Contains(lineOpacity.ToString() + "%"));
        }

        private void UpdateMenuCheckedState(string menuName, Func<ToolStripMenuItem, bool> checkCondition)
        {
            if (trayIcon?.ContextMenuStrip == null) return;

            foreach (ToolStripItem item in trayIcon.ContextMenuStrip.Items)
            {
                if (item is ToolStripMenuItem verticalMenu && verticalMenu.Text == "持续竖线")
                {
                    foreach (ToolStripItem subItem in verticalMenu.DropDownItems)
                    {
                        if (subItem is ToolStripMenuItem targetMenu && targetMenu.Text == menuName)
                        {
                            foreach (ToolStripItem optionItem in targetMenu.DropDownItems)
                            {
                                if (optionItem is ToolStripMenuItem menuItem)
                                {
                                    menuItem.Checked = checkCondition(menuItem);
                                }
                            }
                            break;
                        }
                    }
                    break;
                }
            }
        }

        private void ShowVerticalLine(int index)
        {
            var mousePos = System.Windows.Forms.Cursor.Position;
            Console.WriteLine($"[竖线] ShowVerticalLine({index}) 开始");
            Console.WriteLine($"[竖线] 鼠标位置: ({mousePos.X}, {mousePos.Y})");
            Console.WriteLine($"[竖线] 显示模式: {(showOnAllScreens ? "所有屏幕" : "单屏幕")}");
            
            // 检查是否有预创建的线条
            if (!verticalLines.ContainsKey(index))
            {
                Console.WriteLine($"[竖线] 错误：没有找到预创建的竖线 {index}");
                return;
            }

            var lines = verticalLines[index];
            Console.WriteLine($"[竖线] 找到预创建的竖线 {index}，数量: {lines.Count}");
            
            if (showOnAllScreens)
            {
                // 在所有屏幕显示竖线
                var mouseScreen = Screen.FromPoint(mousePos);
                Console.WriteLine($"[竖线] 鼠标所在屏幕: {mouseScreen.Bounds}");
                int relativeX = mousePos.X - mouseScreen.Bounds.X;
                Console.WriteLine($"[竖线] 相对X坐标: {relativeX}");
                
                int lineIndex = 0;
                foreach (Screen screen in Screen.AllScreens)
                {
                    if (lineIndex < lines.Count)
                    {
                        var line = lines[lineIndex];
                        int lineX = screen.Bounds.X + relativeX;
                        // 确保线条不超出屏幕边界
                        if (lineX < screen.Bounds.X) lineX = screen.Bounds.X;
                        if (lineX > screen.Bounds.Right - lineWidth) lineX = screen.Bounds.Right - lineWidth;
                        
                        Console.WriteLine($"[竖线] 更新屏幕 {screen.Bounds} 线条位置: X={lineX}");
                        line.SetPhysicalBounds(lineX, screen.Bounds.Y, lineWidth, screen.Bounds.Height);
                        line.Opacity = lineOpacity / 100.0;
                        
                        // 确保置顶
                        var hwnd = new WindowInteropHelper(line).Handle;
                        if (hwnd != IntPtr.Zero)
                        {
                            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                        }
                        
                        lineIndex++;
                    }
                }
            }
            else
            {
                // 只在鼠标所在屏幕显示竖线
                var currentScreen = Screen.FromPoint(mousePos);
                Console.WriteLine($"[竖线] 单屏模式 - 鼠标所在屏幕: {currentScreen.Bounds}");
                
                if (lines.Count > 0)
                {
                    var line = lines[0]; // 单屏模式只使用第一条线
                    Console.WriteLine($"[竖线] 更新竖线位置: ({mousePos.X}, {currentScreen.Bounds.Y})");
                    line.SetPhysicalBounds(mousePos.X, currentScreen.Bounds.Y, lineWidth, currentScreen.Bounds.Height);
                    line.Opacity = lineOpacity / 100.0;
                    
                    // 确保置顶
                    var hwnd = new WindowInteropHelper(line).Handle;
                    if (hwnd != IntPtr.Zero)
                    {
                        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                    }
                }
            }

            lineStates[index] = true;
            Console.WriteLine($"[竖线] ShowVerticalLine({index}) 完成");
        }

        private void HideVerticalLine(int index)
        {
            Console.WriteLine($"[竖线] HideVerticalLine({index}) 开始");
            if (verticalLines.ContainsKey(index))
            {
                Console.WriteLine($"[竖线] 隐藏竖线 {index}，数量: {verticalLines[index].Count}");
                foreach (var line in verticalLines[index])
                {
                    line.Opacity = 0; // 只隐藏，不关闭
                }
                lineStates[index] = false;
                Console.WriteLine($"[竖线] HideVerticalLine({index}) 完成");
            }
            else
            {
                Console.WriteLine($"[竖线] 警告：没有找到要隐藏的竖线 {index}");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // 注销所有热键
            for (int i = 0; i < 4; i++)
            {
                if (hotkeyEnabled[i])
                {
                    UnregisterHotkeyPair(i);
                }
            }

            // 关闭所有竖线 - 创建副本避免集合修改异常
            var linesToClose = new List<List<DraggableVerticalLine>>(verticalLines.Values);
            foreach (var lines in linesToClose)
            {
                foreach (var line in lines)
                {
                    try
                    {
                        if (line != null)
                        {
                            line.Close();
                        }
                    }
                    catch (Exception)
                    {
                        // 忽略关闭时的异常
                    }
                }
            }
            verticalLines.Clear();
            lineStates.Clear();

            base.OnClosed(e);
        }

        /// <summary>
        /// 显示所有竖线
        /// </summary>
        public void ShowAllLines()
        {
            foreach (var lines in verticalLines.Values)
            {
                foreach (var line in lines)
                {
                    if (line.Visibility != Visibility.Visible)
                    {
                        line.Show();
                    }
                }
            }
        }

        /// <summary>
        /// 隐藏所有竖线
        /// </summary>
        public void HideAllLines()
        {
            foreach (var lines in verticalLines.Values)
            {
                foreach (var line in lines)
                {
                    if (line.Visibility == Visibility.Visible)
                    {
                        line.Hide();
                    }
                }
            }
        }

        /// <summary>
        /// 关闭所有竖线（彻底移除）
        /// </summary>
        public void CloseAllLines()
        {
            var linesToClose = new List<int>(verticalLines.Keys);
            foreach (int index in linesToClose)
            {
                if (verticalLines.ContainsKey(index))
                {
                    foreach (var line in verticalLines[index])
                    {
                        line.Close();
                    }
                    verticalLines.Remove(index);
                    lineStates[index] = false;
                }
            }
        }

        /// <summary>
        /// 重新置顶所有竖线
        /// </summary>
        public void BringAllLinesToTop()
        {
            foreach (var lines in verticalLines.Values)
            {
                foreach (var line in lines)
                {
                    if (line != null)
                    {
                        line.Topmost = false;
                        line.Topmost = true;
                        line.Activate();
                    }
                }
            }
        }

        /// <summary>
        /// 确保所有竖线保持置顶状态（用于持续置顶功能） - 与其他置顶程序抢夺置顶权
        /// </summary>
        public void EnsureTopmost()
        {
            foreach (var lines in verticalLines.Values)
            {
                foreach (var line in lines)
                {
                    if (line != null && line.Visibility == Visibility.Visible)
                    {
                        var hwnd = new WindowInteropHelper(line).Handle;
                        if (hwnd != IntPtr.Zero)
                        {
                            // 强制重新设置置顶状态，抢夺置顶权
                            line.Topmost = false;  // 先取消置顶
                            line.Topmost = true;   // 再重新置顶，抢夺置顶权
                            
                            // 使用Windows API强制置顶并显示
                            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                            BringWindowToTop(hwnd);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 启用所有热键
        /// </summary>
        public void EnableAllHotkeys()
        {
            EnableAllHotkeysWithRetry(0);
        }

        /// <summary>
        /// 带重试机制的启用所有热键
        /// </summary>
        private void EnableAllHotkeysWithRetry(int delaySeconds)
        {
            if (delaySeconds > 0)
            {
                // 延迟执行
                var delayTimer = new DispatcherTimer();
                delayTimer.Interval = TimeSpan.FromSeconds(delaySeconds);
                delayTimer.Tick += (s, e) => {
                    delayTimer.Stop();
                    PerformHotkeyRegistration();
                };
                delayTimer.Start();
            }
            else
            {
                PerformHotkeyRegistration();
            }
        }

        /// <summary>
        /// 执行热键注册
        /// </summary>
        private void PerformHotkeyRegistration()
        {
            int successCount = 0;
            int totalEnabled = 0;
            
            for (int i = 0; i < hotkeyEnabled.Length; i++)
            {
                if (hotkeyEnabled[i])
                {
                    totalEnabled++;
                    if (TryRegisterHotkeyPair(i))
                    {
                        successCount++;
                    }
                }
            }
            
            Console.WriteLine($"[竖线] 热键注册结果: {successCount}/{totalEnabled} 成功");
            
            // 如果部分失败，可以选择重试
            if (successCount < totalEnabled && successCount > 0)
            {
                Console.WriteLine("[竖线] 部分热键注册失败，但有部分成功，继续运行");
            }
        }

        /// <summary>
        /// 尝试注册热键对（不显示错误消息）
        /// </summary>
        private bool TryRegisterHotkeyPair(int index)
        {
            try
            {
                if (index >= 0 && index < 4)
                {
                    Keys key = Keys.D1 + index; // 数字键1-4
                    var hwnd = new WindowInteropHelper(this).Handle;
                    
                    if (hwnd != IntPtr.Zero)
                    {
                        // 注册 Ctrl+Alt+1-4
                        bool onSuccess = RegisterHotKey(hwnd, BASE_HOTKEY_ID_ON + index, MOD_CONTROL | MOD_ALT, (int)key);
                        // 注册 Ctrl+Shift+Alt+1-4
                        bool offSuccess = RegisterHotKey(hwnd, BASE_HOTKEY_ID_OFF + index, MOD_CONTROL | MOD_SHIFT | MOD_ALT, (int)key);

                        if (onSuccess && offSuccess)
                        {
                            return true;
                        }
                        else
                        {
                            // 如果注册失败，注销已注册的热键
                            if (onSuccess) {
                                UnregisterHotKey(hwnd, BASE_HOTKEY_ID_ON + index);
                            }
                            if (offSuccess) {
                                UnregisterHotKey(hwnd, BASE_HOTKEY_ID_OFF + index);
                            }
                            return false;
                        }
                    }
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 禁用所有热键
        /// </summary>
        public void DisableAllHotkeys()
        {
            for (int i = 0; i < hotkeyEnabled.Length; i++)
            {
                if (hotkeyEnabled[i])
                {
                    UnregisterHotkeyPair(i);
                }
            }
        }

        // 加载配置
        private void LoadConfig()
        {
            try
            {
                if (File.Exists(configPath))
                {
                    string jsonString = File.ReadAllText(configPath);
                    var config = System.Text.Json.JsonSerializer.Deserialize<Config>(jsonString);

                    // 确保热键数组长度正确
                    if (config.HotkeyEnabled != null && config.HotkeyEnabled.Length == 4)
                    {
                        hotkeyEnabled = config.HotkeyEnabled;
                    }
                    else
                    {
                        hotkeyEnabled = new bool[] { true, true, false, false };
                    }

                    lineWidth = config.LineWidth;
                    lineColor = System.Drawing.ColorTranslator.FromHtml(config.LineColor);
                    lineOpacity = config.LineOpacity;
                    mouseClickThrough = config.MouseClickThrough;
                    showOnAllScreens = config.ShowOnAllScreens;
                }
            }
            catch (Exception)
            {
                // 如果加载失败，使用默认值
                hotkeyEnabled = new bool[] { true, true, false, false };
                lineWidth = 1;
                lineColor = System.Drawing.Color.Blue;
                lineOpacity = 100;
                mouseClickThrough = true;
                showOnAllScreens = false;
            }
        }

        // 保存配置
        private void SaveConfig()
        {
            try
            {
                // 确保目录存在
                var configDir = Path.GetDirectoryName(configPath);
                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }
                
                var config = new Config
                {
                    HotkeyEnabled = hotkeyEnabled,
                    LineWidth = lineWidth,
                    LineColor = System.Drawing.ColorTranslator.ToHtml(lineColor),
                    LineOpacity = lineOpacity,
                    MouseClickThrough = mouseClickThrough,
                    ShowOnAllScreens = showOnAllScreens
                };

                string jsonString = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(configPath, jsonString);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"保存竖线配置时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
} 