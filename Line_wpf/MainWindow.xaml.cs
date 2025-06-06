using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Forms;
using System.Windows.Interop;
using Color = System.Drawing.Color;
using MessageBox = System.Windows.MessageBox;
using Screen = System.Windows.Forms.Screen;

namespace Line_wpf
{
    /// <summary>
    /// 临时线条窗口类 - 完全模拟WinForms的方式
    /// </summary>
    public class TemporaryLineWindow : Window
    {
        // Windows API常量
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int GWL_EXSTYLE = (-20);
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

        public TemporaryLineWindow()
        {
            // 完全模拟WinForms的设置
            this.WindowStyle = WindowStyle.None;
            this.ShowInTaskbar = false;
            this.Topmost = true;
            this.AllowsTransparency = true;
            this.ResizeMode = ResizeMode.NoResize;
            this.Background = System.Windows.Media.Brushes.Transparent;
            
            // 强制使用物理像素
            this.UseLayoutRounding = true;
            this.SnapsToDevicePixels = true;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            // 获取窗口句柄并设置扩展样式
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                // 设置窗口样式：分层 + 点透 + 不激活
                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
                
                // 强制置顶
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
            }
        }
        
        /// <summary>
        /// 设置窗口为精确的物理像素大小和位置
        /// </summary>
        public void SetPhysicalBounds(int x, int y, int width, int height)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                // 直接使用Windows API设置精确的物理像素位置和大小
                SetWindowPos(hwnd, HWND_TOPMOST, x, y, width, height, SWP_SHOWWINDOW);
            }
            else
            {
                // 如果句柄还没创建，使用WPF属性
                this.Left = x;
                this.Top = y;
                this.Width = width;
                this.Height = height;
            }
        }
    }

    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private DispatcherTimer fadeTimer;
        private DispatcherTimer topmostTimer; // 持续置顶定时器
        private double opacity = 1.0;
        private const int WM_HOTKEY = 0x0312;
        private int currentHotKeyId = 1;  // 添加热键ID
        private NotifyIcon trayIcon;
        private bool showOnAllScreens = false;
        private Dictionary<Screen, TemporaryLineWindow> screenLines = new Dictionary<Screen, TemporaryLineWindow>();
        
        // 添加新功能窗体实例变量
        private VerticalLineWindow verticalLineWindow;
        private HorizontalLineWindow horizontalLineWindow;
        private BoundingBoxWindow boundingBoxWindow;
        
        // 全局显示/隐藏状态（不保存到配置文件，每次启动都是显示状态）
        private bool allPersistentLinesVisible = true;
        
        // 持续置顶功能开关
        private bool persistentTopmost = false;
        
        // 新增：持续置顶实现方案类型
        private enum TopmostStrategy
        {
            ForceTimer,     // 暴力定时器方案
            SmartMonitor    // 智能监听方案
        }
        
        // 新增：当前置顶策略
        private TopmostStrategy currentTopmostStrategy = TopmostStrategy.ForceTimer;
        
        // 新增：暴力定时器间隔选项（毫秒）
        private int[] timerIntervals = { 50, 100, 200, 500, 1000, 2000, 3000 };
        private int currentTimerInterval = 100; // 默认100毫秒
        
        // 新增：智能监听相关变量
        private IntPtr hWinEventHook = IntPtr.Zero;
        private List<MonitoredApp> monitoredApplications = new List<MonitoredApp> 
        { 
            new MonitoredApp("Paster - Snipaste", true), 
            new MonitoredApp("PixPin", true) 
        };
        
        // 新增：监控应用程序类
        public class MonitoredApp
        {
            public string Name { get; set; }
            public bool IsEnabled { get; set; }
            
            public MonitoredApp(string name, bool isEnabled = true)
            {
                Name = name;
                IsEnabled = isEnabled;
            }
        }
        
        // 新增：Windows API for SetWinEventHook
        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
        
        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);
        
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        
        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
        
        // Windows事件常量
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        
        // 委托类型
        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);
        
        // 监听事件处理器
        private WinEventDelegate winEventDelegate;
        
        // 线条默认高度为1像素，可以调整为更粗的线条
        private int lineHeight = 1;

        // 线条颜色，默认为红色
        private Color lineColor = Color.Red;

        // 线条透明度，默认为100%
        private int lineOpacity = 100;

        // 显示时长（秒），默认为1.5秒
        private double displayDuration = 1;

        // 当前热键
        private Keys currentHotKey = Keys.F5;

        // 修改配置文件路径
        private readonly string configPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ScreenLine",
            "wpf_config.json"
        );

        // 添加全局快捷键控制开关
        private bool globalHotkeysDisabled = false;

        // 公共属性，允许子窗体检查全局热键状态
        public bool IsGlobalHotkeysDisabled => globalHotkeysDisabled;

        // 配置类
        private class Config
        {
            public bool ShowOnAllScreens { get; set; }
            public int LineHeight { get; set; }
            public string LineColor { get; set; }
            public int LineOpacity { get; set; }
            public double DisplayDuration { get; set; }
            public Keys HotKey { get; set; }
            public bool PersistentTopmost { get; set; } // 持续置顶配置
            public int TopmostStrategy { get; set; } // 新增：置顶策略
            public int TimerInterval { get; set; } // 新增：定时器间隔
            public List<MonitoredApp> MonitoredApplications { get; set; } // 新增：监控的应用程序列表
            public bool GlobalHotkeysDisabled { get; set; }
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // 用于设置鼠标穿透的Windows API
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

        // 增强的置顶Windows API
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        // 置顶相关常量
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_SHOWWINDOW = 0x0040;

        // 防止重复初始化的标志
        private bool isTrayIconInitialized = false;
        private bool isHotKeyRegistered = false;
        private bool isTrayMenuVisible = false; // 添加托盘菜单状态跟踪

        // 新增：重启模式标志
        private bool isRestartMode = false;

        public MainWindow() : this(false)
        {
            // 默认构造函数调用有参构造函数，非重启模式
        }

        public MainWindow(bool restartMode)
        {
            isRestartMode = restartMode;
            
            if (isRestartMode)
            {
                Console.WriteLine("[启动] MainWindow构造函数：重启模式");
            }
            
            // 首先加载配置，获取必要的属性值
            LoadConfigBeforeInitialization();
            
            // 在 InitializeComponent 之前设置关键属性
            this.AllowsTransparency = true;
            this.WindowStyle = WindowStyle.None;
            this.ShowInTaskbar = false;
            this.Topmost = true;
            this.ResizeMode = ResizeMode.NoResize;
            
            InitializeComponent();

            // 直接继续初始化，不管是否重启
            ContinueInitialization();
        }
        
        private void ContinueInitialization()
        {
            Console.WriteLine("[启动] 开始ContinueInitialization");
            
            // 完整加载配置
            Console.WriteLine("[启动] 开始加载配置");
            LoadConfig();
            Console.WriteLine("[启动] 配置加载完成");

            // 设置剩余的窗体属性
            Console.WriteLine("[启动] 开始设置窗体属性");
            this.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(lineOpacity * 255 / 100), lineColor.R, lineColor.G, lineColor.B));
            this.Opacity = lineOpacity / 100.0;  // 初始透明度
            this.Width = SystemParameters.PrimaryScreenWidth;  // 宽度等于屏幕宽度
            this.Height = lineHeight;  // 高度为设定的线条高度
            Console.WriteLine("[启动] 窗体属性设置完成");

            // 设置定时器用于淡出效果
            Console.WriteLine("[启动] 开始初始化淡出定时器");
            fadeTimer = new DispatcherTimer();
            fadeTimer.Interval = TimeSpan.FromMilliseconds(50);  // 50毫秒更新一次
            fadeTimer.Tick += FadeTimer_Tick;
            Console.WriteLine("[启动] 淡出定时器初始化完成");

            // 初始化持续置顶定时器
            Console.WriteLine("[启动] 开始初始化置顶定时器");
            topmostTimer = new DispatcherTimer();
            
            // 验证定时器间隔，确保不为0或负数
            if (currentTimerInterval <= 0)
            {
                currentTimerInterval = 100; // 使用默认值
            }
            Console.WriteLine($"[启动] 定时器间隔设置为: {currentTimerInterval}ms");
            
            topmostTimer.Interval = TimeSpan.FromMilliseconds(currentTimerInterval);  // 使用验证后的间隔
            topmostTimer.Tick += TopmostTimer_Tick;
            Console.WriteLine("[启动] 置顶定时器初始化完成");
            
            // 初始化Windows事件监听委托
            Console.WriteLine("[启动] 开始初始化Windows事件监听");
            winEventDelegate = new WinEventDelegate(WinEventProc);
            Console.WriteLine("[启动] Windows事件监听初始化完成");

            // 注册事件处理
            Console.WriteLine("[启动] 开始注册事件处理");
            this.SourceInitialized += MainWindow_SourceInitialized;
            this.Loaded += MainWindow_Loaded;
            Console.WriteLine("[启动] 事件处理注册完成");
            
            Console.WriteLine("[启动] ContinueInitialization完成");
        }

        /// <summary>
        /// 在InitializeComponent之前加载基本配置
        /// </summary>
        private void LoadConfigBeforeInitialization()
        {
            try
            {
                if (File.Exists(configPath))
                {
                    string jsonString = File.ReadAllText(configPath);
                    var config = JsonSerializer.Deserialize<Config>(jsonString);
                    
                    // 只加载必要的属性
                    lineHeight = config.LineHeight;
                    lineColor = ColorTranslator.FromHtml(config.LineColor);
                    lineOpacity = config.LineOpacity;
                }
            }
            catch (Exception)
            {
                // 如果加载失败，使用默认值
                lineHeight = 1;
                lineColor = Color.Red;
                lineOpacity = 100;
            }
        }

        private void MainWindow_SourceInitialized(object sender, EventArgs e)
        {
            // 设置窗口为鼠标穿透
            WindowInteropHelper helper = new WindowInteropHelper(this);
            int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
            
            // 根据模式决定是否立即注册热键
            if (isRestartMode)
            {
                Console.WriteLine("[启动] 重启模式：延迟注册热键");
                // 重启模式下延迟注册热键
                DelayedRegisterHotKey();
            }
            else
            {
                Console.WriteLine("[启动] 正常模式：立即注册热键");
                // 正常模式立即注册热键
                RegisterCurrentHotKey();
            }
            
            // 添加消息钩子
            HwndSource source = HwndSource.FromHwnd(helper.Handle);
            source.AddHook(HwndHook);
        }

        /// <summary>
        /// 延迟注册热键（重启模式使用）
        /// </summary>
        private void DelayedRegisterHotKey()
        {
            var delayTimer = new DispatcherTimer();
            delayTimer.Interval = TimeSpan.FromSeconds(3); // 延迟3秒
            
            int retryCount = 0;
            const int maxRetries = 5;
            
            delayTimer.Tick += (s, e) => {
                delayTimer.Stop();
                
                Console.WriteLine($"[启动] 尝试注册热键，第 {retryCount + 1} 次");
                
                // 尝试注册热键
                bool success = TryRegisterCurrentHotKey();
                
                if (success)
                {
                    Console.WriteLine("[启动] 热键注册成功");
                    // 注册成功后，延迟注册子窗体热键
                    DelayedRegisterChildWindowsHotKeys();
                }
                else
                {
                    retryCount++;
                    if (retryCount < maxRetries)
                    {
                        Console.WriteLine($"[启动] 热键注册失败，将在2秒后重试 ({retryCount}/{maxRetries})");
                        // 重试，间隔2秒
                        delayTimer.Interval = TimeSpan.FromSeconds(2);
                        delayTimer.Start();
                    }
                    else
                    {
                        Console.WriteLine("[启动] 热键注册失败，已达到最大重试次数");
                        // 最终失败，使用默认逻辑
                        RegisterCurrentHotKey();
                        DelayedRegisterChildWindowsHotKeys();
                    }
                }
            };
            
            delayTimer.Start();
        }

        /// <summary>
        /// 尝试注册热键（不显示错误消息）
        /// </summary>
        private bool TryRegisterCurrentHotKey()
        {
            if (isHotKeyRegistered || currentHotKey == Keys.None)
            {
                return true;
            }
            
            try
            {
                WindowInteropHelper helper = new WindowInteropHelper(this);
                if (helper.Handle != IntPtr.Zero)
                {
                    bool success = RegisterHotKey(helper.Handle, currentHotKeyId, 0, (int)currentHotKey);
                    if (success)
                    {
                        isHotKeyRegistered = true;
                        return true;
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
        /// 延迟注册子窗体热键
        /// </summary>
        private void DelayedRegisterChildWindowsHotKeys()
        {
            // 延迟1秒注册子窗体热键，确保主窗体热键先注册成功
            var childDelayTimer = new DispatcherTimer();
            childDelayTimer.Interval = TimeSpan.FromSeconds(1);
            childDelayTimer.Tick += (s, e) => {
                childDelayTimer.Stop();
                
                Console.WriteLine("[启动] 开始注册子窗体热键");
                
                // 重新启用子窗体热键（如果全局热键未被禁用）
                if (!globalHotkeysDisabled)
                {
                    verticalLineWindow?.EnableAllHotkeys();
                    horizontalLineWindow?.EnableAllHotkeys();
                }
                
                Console.WriteLine("[启动] 子窗体热键注册完成");
            };
            childDelayTimer.Start();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("[启动] 开始MainWindow_Loaded");
            try
            {
                // 隐藏主窗口
                Console.WriteLine("[启动] 隐藏主窗口");
                this.WindowState = WindowState.Minimized;
                this.ShowInTaskbar = false;
                this.Visibility = Visibility.Hidden;
                Console.WriteLine("[启动] 主窗口隐藏完成");
                
                // 初始化系统托盘图标（只初始化一次）
                Console.WriteLine("[启动] 开始初始化托盘图标");
                InitializeTrayIcon();
                Console.WriteLine("[启动] 托盘图标初始化完成");
                
                // 初始化子窗体（只初始化一次）
                Console.WriteLine("[启动] 开始初始化子窗体");
                verticalLineWindow = new VerticalLineWindow(trayIcon, this);
                Console.WriteLine("[启动] 竖线窗体初始化完成");
                horizontalLineWindow = new HorizontalLineWindow(trayIcon, this);
                Console.WriteLine("[启动] 横线窗体初始化完成");
                boundingBoxWindow = new BoundingBoxWindow(trayIcon);
                Console.WriteLine("[启动] 包围框窗体初始化完成");
                
                // 预先创建所有屏幕的线条窗体
                Console.WriteLine("[启动] 开始初始化屏幕线条");
                InitializeScreenLines();
                Console.WriteLine("[启动] 屏幕线条初始化完成");
                
                // 应用设置
                Console.WriteLine("[启动] 开始应用设置");
                ApplyAllSettings();
                Console.WriteLine("[启动] 设置应用完成");
                
                // 显示一次初始线条然后隐藏
                Console.WriteLine("[启动] 显示初始线条");
                ShowInitialLine();
                Console.WriteLine("[启动] 初始线条显示完成");
                
                // 如果启用了持续置顶，启动监控机制
                if (persistentTopmost)
                {
                    Console.WriteLine("[启动] 恢复持续置顶监控");
                    StartTopmostMonitoring();
                    Console.WriteLine($"[置顶] 启动时恢复持续置顶，策略: {currentTopmostStrategy}");
                }
                
                Console.WriteLine("[启动] MainWindow_Loaded完成！程序启动成功");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[启动] MainWindow_Loaded发生异常: {ex.Message}");
                Console.WriteLine($"[启动] 异常堆栈: {ex.StackTrace}");
                // 如果初始化失败，使用默认值
                showOnAllScreens = false;
                lineHeight = 1;
                lineColor = Color.Red;
                lineOpacity = 100;
                displayDuration = 1.5;
                currentHotKey = Keys.F5;
                persistentTopmost = false;
                currentTopmostStrategy = TopmostStrategy.ForceTimer;
                currentTimerInterval = 100;
                monitoredApplications = new List<MonitoredApp> { new MonitoredApp("Paster - Snipaste", true), new MonitoredApp("PixPin", true) };
                globalHotkeysDisabled = false;
            }
        }

        /// <summary>
        /// 初始化所有屏幕的线条窗体 - 模拟WinForms的方式
        /// </summary>
        private void InitializeScreenLines()
        {
            // 清理现有的线条窗体
            foreach (var line in screenLines.Values)
            {
                if (line != null && line.IsLoaded)
                {
                    line.Close();
                }
            }
            screenLines.Clear();

            // 为每个屏幕创建一个持久的线条窗体
            foreach (Screen screen in Screen.AllScreens)
            {
                var lineWindow = new TemporaryLineWindow();
                
                // 初始化窗体属性
                lineWindow.SetPhysicalBounds(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, lineHeight);
                lineWindow.Opacity = 0; // 初始隐藏
                lineWindow.Show(); // 显示但透明
                
                screenLines[screen] = lineWindow;
            }
        }

        private void ShowInitialLine()
        {
            // 简单显示一次然后立即隐藏，确保窗体正常工作
            var primaryScreen = Screen.PrimaryScreen;
            if (screenLines.ContainsKey(primaryScreen))
            {
                var lineWindow = screenLines[primaryScreen];
                lineWindow.Opacity = lineOpacity / 100.0;
                
                var hideTimer = new DispatcherTimer();
                hideTimer.Interval = TimeSpan.FromMilliseconds(100);
                hideTimer.Tick += (s, e) => {
                    hideTimer.Stop();
                    lineWindow.Opacity = 0;
                };
                hideTimer.Start();
            }
        }

        private TemporaryLineWindow CreateTemporaryLineWindow()
        {
            var window = new TemporaryLineWindow();
            window.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(lineOpacity * 255 / 100), lineColor.R, lineColor.G, lineColor.B));
            window.Width = this.Width;
            window.Height = this.Height;
            return window;
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // 拦截窗口消息，解决光标和激活问题
            if (msg == WM_MOUSEACTIVATE)
            {
                // 不激活自己，直接交给下面窗口
                handled = true;
                return new IntPtr(MA_NOACTIVATE);
            }
            if (msg == WM_SETCURSOR)
            {
                // 不处理，让系统去给下面窗口设光标
                handled = true;
                return IntPtr.Zero;
            }
            
            // 处理热键消息
            if (msg == WM_HOTKEY && wParam.ToInt32() == currentHotKeyId)
            {
                // 在显示线条前先重置状态
                ResetLineState();
                ShowLine();
                handled = true;
            }
            
            return IntPtr.Zero;
        }

        // 其余方法将在后续添加...
        // 这里先添加一些基本方法

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(configPath))
                {
                    string jsonString = File.ReadAllText(configPath);
                    var config = JsonSerializer.Deserialize<Config>(jsonString);

                    showOnAllScreens = config.ShowOnAllScreens;
                    lineHeight = config.LineHeight;
                    lineColor = ColorTranslator.FromHtml(config.LineColor);
                    lineOpacity = config.LineOpacity;
                    displayDuration = config.DisplayDuration;
                    currentHotKey = config.HotKey;
                    persistentTopmost = config.PersistentTopmost;
                    
                    // 新增：加载置顶策略配置
                    currentTopmostStrategy = (TopmostStrategy)config.TopmostStrategy;
                    currentTimerInterval = config.TimerInterval;
                    
                    // 验证定时器间隔，确保不为0或负数
                    if (currentTimerInterval <= 0)
                    {
                        currentTimerInterval = 100; // 默认值
                    }
                    
                    if (config.MonitoredApplications != null && config.MonitoredApplications.Count > 0)
                    {
                        monitoredApplications = config.MonitoredApplications;
                    }

                    globalHotkeysDisabled = config.GlobalHotkeysDisabled;
                }
            }
            catch (Exception)
            {
                // 如果加载失败，使用默认值
                showOnAllScreens = false;
                lineHeight = 1;
                lineColor = Color.Red;
                lineOpacity = 100;
                displayDuration = 1.5;
                currentHotKey = Keys.F5;
                persistentTopmost = false;
                currentTopmostStrategy = TopmostStrategy.ForceTimer;
                currentTimerInterval = 100;
                monitoredApplications = new List<MonitoredApp> { new MonitoredApp("Paster - Snipaste", true), new MonitoredApp("PixPin", true) };
                globalHotkeysDisabled = false;
            }
        }

        private void SaveConfig()
        {
            try
            {
                var config = new Config
                {
                    ShowOnAllScreens = showOnAllScreens,
                    LineHeight = lineHeight,
                    LineColor = ColorTranslator.ToHtml(lineColor),
                    LineOpacity = lineOpacity,
                    DisplayDuration = displayDuration,
                    HotKey = currentHotKey,
                    PersistentTopmost = persistentTopmost,
                    TopmostStrategy = (int)currentTopmostStrategy,
                    TimerInterval = currentTimerInterval,
                    MonitoredApplications = monitoredApplications,
                    GlobalHotkeysDisabled = globalHotkeysDisabled,
                };

                string jsonString = JsonSerializer.Serialize(config, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(configPath, jsonString);
            }
            catch
            {
                MessageBox.Show("保存配置时出错", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RegisterCurrentHotKey()
        {
            // 防止重复注册热键
            if (isHotKeyRegistered)
            {
                return;
            }
            
            try
            {
                WindowInteropHelper helper = new WindowInteropHelper(this);
                // 注册新热键
                if (RegisterHotKey(helper.Handle, currentHotKeyId, 0, (int)currentHotKey))
                {
                    isHotKeyRegistered = true;
                }
                else
                {
                    MessageBox.Show($"热键 {currentHotKey} 注册失败，可能已被其他程序占用。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"注册热键时发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 初始化系统托盘图标和右键菜单 - 完整移植WinForms版本
        /// </summary>
        private void InitializeTrayIcon()
        {
            if (isTrayIconInitialized) return;

            try
            {
                trayIcon = new NotifyIcon();
                
                // 加载图标
                string iconPath = AppDomain.CurrentDomain.BaseDirectory + "LineIco.ico";
                if (File.Exists(iconPath))
                {
                    trayIcon.Icon = new Icon(iconPath);
                }
                else
                {
                    // 使用默认应用图标
                    trayIcon.Icon = SystemIcons.Application;
                }
                
                trayIcon.Text = "屏幕参考线";
                trayIcon.Visible = true;

                // 创建右键菜单
                ContextMenuStrip contextMenu = new ContextMenuStrip();
                
                // 添加瞬时横线菜单组（改回原来的名字）
                ToolStripMenuItem temporaryHorizontalLineMenu = new ToolStripMenuItem("瞬时横线");
                
                // 显示模式菜单
                ToolStripMenuItem displayModeItem = new ToolStripMenuItem("显示模式");
                var currentScreenItem = new ToolStripMenuItem("仅鼠标所在屏幕", null, (s, e) => {
                    showOnAllScreens = false;
                    UpdateDisplayModeMenu();
                    SaveConfig();
                });
                currentScreenItem.Checked = !showOnAllScreens;
                
                var allScreensItem = new ToolStripMenuItem("所有屏幕", null, (s, e) => {
                    showOnAllScreens = true;
                    UpdateDisplayModeMenu();
                    SaveConfig();
                });
                allScreensItem.Checked = showOnAllScreens;
                
                displayModeItem.DropDownItems.Add(currentScreenItem);
                displayModeItem.DropDownItems.Add(allScreensItem);
                
                // 线条粗细菜单
                ToolStripMenuItem lineThicknessItem = new ToolStripMenuItem("线条粗细");
                AddThicknessMenuItem(lineThicknessItem, "细线 (1像素)", 1);
                AddThicknessMenuItem(lineThicknessItem, "中等 (2像素)", 2);
                AddThicknessMenuItem(lineThicknessItem, "粗线 (3像素)", 3);
                AddThicknessMenuItem(lineThicknessItem, "很粗 (5像素)", 5);
                
                // 线条颜色菜单
                ToolStripMenuItem lineColorItem = new ToolStripMenuItem("线条颜色");
                AddColorMenuItem(lineColorItem, "红色", Color.Red);
                AddColorMenuItem(lineColorItem, "绿色", Color.Green);
                AddColorMenuItem(lineColorItem, "蓝色", Color.Blue);
                AddColorMenuItem(lineColorItem, "黄色", Color.Yellow);
                AddColorMenuItem(lineColorItem, "橙色", Color.Orange);
                AddColorMenuItem(lineColorItem, "紫色", Color.Purple);
                AddColorMenuItem(lineColorItem, "青色", Color.Cyan);
                AddColorMenuItem(lineColorItem, "黑色", Color.FromArgb(1, 1, 1));
                AddColorMenuItem(lineColorItem, "白色", Color.White);
                
                // 添加自定义颜色选项
                var customColorItem = new ToolStripMenuItem("自定义颜色...", null, (s, e) => {
                    using (var colorDialog = new System.Windows.Forms.ColorDialog())
                    {
                        colorDialog.Color = lineColor;
                        if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        {
                            ChangeLineColor(colorDialog.Color);
                        }
                    }
                });
                lineColorItem.DropDownItems.Add(new ToolStripSeparator());
                lineColorItem.DropDownItems.Add(customColorItem);
                
                // 透明度菜单
                ToolStripMenuItem transparencyItem = new ToolStripMenuItem("线条透明度");
                AddTransparencyMenuItem(transparencyItem, "100% (不透明)", 100);
                AddTransparencyMenuItem(transparencyItem, "75%", 75);
                AddTransparencyMenuItem(transparencyItem, "50%", 50);
                AddTransparencyMenuItem(transparencyItem, "25%", 25);
                
                // 显示时长菜单
                ToolStripMenuItem durationItem = new ToolStripMenuItem("显示时长");
                var durations = new[] { 0.1, 0.2, 0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 5.0 };
                foreach (double duration in durations)
                {
                    string text = duration.ToString("0.#") + "秒";
                    if (duration == 1.0) text += " (默认)";
                    
                    var item = new ToolStripMenuItem(text, null, (s, e) => {
                        ChangeDisplayDuration(duration);
                    });
                    item.Checked = Math.Abs(duration - displayDuration) < 0.01;
                    durationItem.DropDownItems.Add(item);
                }

                // 热键设置菜单
                ToolStripMenuItem hotKeyItem = new ToolStripMenuItem("热键设置");
                for (int i = 3; i <= 12; i++)
                {
                    Keys key = Keys.F1 + (i - 1);
                    var keyItem = new ToolStripMenuItem($"F{i}", null, (s, e) => {
                        ChangeHotKey(key);
                    });
                    keyItem.Checked = (key == currentHotKey);
                    hotKeyItem.DropDownItems.Add(keyItem);
                }

                // 将所有子菜单添加到瞬时横线菜单
                temporaryHorizontalLineMenu.DropDownItems.Add(displayModeItem);
                temporaryHorizontalLineMenu.DropDownItems.Add(lineThicknessItem);
                temporaryHorizontalLineMenu.DropDownItems.Add(lineColorItem);
                temporaryHorizontalLineMenu.DropDownItems.Add(transparencyItem);
                temporaryHorizontalLineMenu.DropDownItems.Add(durationItem);
                temporaryHorizontalLineMenu.DropDownItems.Add(hotKeyItem);

                // 将所有主菜单项添加到上下文菜单
                contextMenu.Items.Add(temporaryHorizontalLineMenu);
                // 竖线菜单和其他菜单会在相应的窗体中自动添加
                contextMenu.Items.Add(new ToolStripSeparator());
                
                // 添加禁用快捷键菜单项到主菜单
                var disableHotkeysItem = new ToolStripMenuItem("禁用快捷键", null, (s, e) => {
                    ToggleGlobalHotkeys();
                });
                disableHotkeysItem.Checked = globalHotkeysDisabled;
                contextMenu.Items.Add(disableHotkeysItem);
                
                // 添加显示/隐藏全部持续线条的菜单项
                ToolStripMenuItem toggleAllLinesItem = new ToolStripMenuItem("隐藏全部线条", null, (s, e) => {
                    ToggleAllPersistentLines();
                });
                contextMenu.Items.Add(toggleAllLinesItem);
                
                // 添加关闭全部线条的菜单项（带确认框）
                ToolStripMenuItem closeAllLinesItem = new ToolStripMenuItem("关闭全部线条", null, (s, e) => {
                    CloseAllLines();
                });
                contextMenu.Items.Add(closeAllLinesItem);
                
                // 添加重新置顶全部线条的菜单项
                ToolStripMenuItem bringToTopItem = new ToolStripMenuItem("重新置顶全部线条", null, (s, e) => {
                    BringAllLinesToTop();
                });
                contextMenu.Items.Add(bringToTopItem);
                
                // 添加持续置顶功能菜单项
                ToolStripMenuItem persistentTopmostItem = new ToolStripMenuItem("持续保证置顶", null, (s, e) => {
                    TogglePersistentTopmost();
                });
                persistentTopmostItem.Checked = persistentTopmost;
                contextMenu.Items.Add(persistentTopmostItem);
                
                // 持续置顶策略配置菜单
                ToolStripMenuItem topmostStrategyMenu = new ToolStripMenuItem("置顶策略配置");
                
                // 策略选择子菜单
                ToolStripMenuItem strategyTypeMenu = new ToolStripMenuItem("实现方案");
                var forceTimerItem = new ToolStripMenuItem("暴力定时器", null, (s, e) => {
                    ChangeTopmostStrategy(TopmostStrategy.ForceTimer);
                });
                forceTimerItem.Checked = (currentTopmostStrategy == TopmostStrategy.ForceTimer);
                
                var smartMonitorItem = new ToolStripMenuItem("智能监听", null, (s, e) => {
                    ChangeTopmostStrategy(TopmostStrategy.SmartMonitor);
                });
                smartMonitorItem.Checked = (currentTopmostStrategy == TopmostStrategy.SmartMonitor);
                
                strategyTypeMenu.DropDownItems.Add(forceTimerItem);
                strategyTypeMenu.DropDownItems.Add(smartMonitorItem);
                
                // 定时器间隔配置子菜单
                ToolStripMenuItem timerIntervalMenu = new ToolStripMenuItem("定时器间隔");
                foreach (int interval in timerIntervals)
                {
                    string text = interval < 1000 ? $"{interval}毫秒" : $"{interval / 1000}秒";
                    if (interval == 100) text += " (推荐)";
                    
                    var intervalItem = new ToolStripMenuItem(text, null, (s, e) => {
                        ChangeTimerInterval(interval);
                    });
                    intervalItem.Checked = (interval == currentTimerInterval);
                    timerIntervalMenu.DropDownItems.Add(intervalItem);
                }
                
                // 监控应用程序管理
                var manageMonitoredAppsItem = new ToolStripMenuItem("管理监控程序", null, (s, e) => {
                    ManageCustomApps();
                });
                
                // 将所有子菜单添加到策略配置菜单
                topmostStrategyMenu.DropDownItems.Add(strategyTypeMenu);
                topmostStrategyMenu.DropDownItems.Add(timerIntervalMenu);
                topmostStrategyMenu.DropDownItems.Add(manageMonitoredAppsItem);
                
                contextMenu.Items.Add(topmostStrategyMenu);
                
                // 添加重启程序的菜单项
                ToolStripMenuItem restartItem = new ToolStripMenuItem("重启程序", null, (s, e) => {
                    RestartApplication();
                });
                contextMenu.Items.Add(restartItem);
                
                // 添加退出选项
                ToolStripMenuItem exitItem = new ToolStripMenuItem("退出", null, (s, e) => {
                    ExitApplication();
                });
                contextMenu.Items.Add(exitItem);

                trayIcon.ContextMenuStrip = contextMenu;
                
                // 添加菜单打开和关闭事件监听器
                contextMenu.Opening += (s, e) => {
                    isTrayMenuVisible = true;
                };
                contextMenu.Closed += (s, e) => {
                    isTrayMenuVisible = false;
                };
                
                // 设置双击托盘图标的行为（显示线条）
                trayIcon.DoubleClick += (s, e) => ShowLine();
                
                // 添加鼠标点击事件处理，左键单击显示菜单
                trayIcon.MouseClick += (s, e) => {
                    if (e.Button == MouseButtons.Left)
                    {
                        // 左键单击显示上下文菜单
                        if (trayIcon.ContextMenuStrip != null)
                        {
                            // 获取鼠标位置
                            System.Drawing.Point mousePos = Control.MousePosition;
                            // 显示菜单在鼠标位置
                            trayIcon.ContextMenuStrip.Show(mousePos);
                        }
                    }
                };
                
                isTrayIconInitialized = true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"初始化托盘图标失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddThicknessMenuItem(ToolStripMenuItem parent, string text, int thickness)
        {
            var item = new ToolStripMenuItem(text, null, (s, e) => {
                lineHeight = thickness;
                SaveConfig();
                UpdateThicknessMenuCheckedState();
            });
            item.Checked = (thickness == lineHeight);
            parent.DropDownItems.Add(item);
        }

        private void AddColorMenuItem(ToolStripMenuItem parent, string name, Color color)
        {
            var bitmap = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.FillRectangle(new SolidBrush(color), 0, 0, 16, 16);
                g.DrawRectangle(Pens.Gray, 0, 0, 15, 15);
            }

            var item = new ToolStripMenuItem(name, bitmap, (s, e) => {
                lineColor = color;
                SaveConfig();
                UpdateColorMenuCheckedState();
            });
            item.Checked = color.Equals(lineColor);
            parent.DropDownItems.Add(item);
        }

        private void AddTransparencyMenuItem(ToolStripMenuItem parent, string name, int value)
        {
            var item = new ToolStripMenuItem(name, null, (s, e) => {
                lineOpacity = value;
                SaveConfig();
                UpdateTransparencyMenuCheckedState();
            });
            item.Checked = (value == lineOpacity);
            parent.DropDownItems.Add(item);
        }

        private void UpdateDisplayModeMenu()
        {
            if (trayIcon?.ContextMenuStrip == null) return;
            
            foreach (ToolStripItem item in trayIcon.ContextMenuStrip.Items)
            {
                if (item is ToolStripMenuItem tempLineMenu && tempLineMenu.Text == "瞬时横线")
                {
                    foreach (ToolStripItem subItem in tempLineMenu.DropDownItems)
                    {
                        if (subItem is ToolStripMenuItem displayModeMenu && displayModeMenu.Text == "显示模式")
                        {
                            foreach (ToolStripItem modeItem in displayModeMenu.DropDownItems)
                            {
                                if (modeItem is ToolStripMenuItem menuItem)
                                {
                                    menuItem.Checked = (menuItem.Text == "仅鼠标所在屏幕" && !showOnAllScreens) ||
                                                      (menuItem.Text == "所有屏幕" && showOnAllScreens);
                                }
                            }
                            break;
                        }
                    }
                    break;
                }
            }
        }

        private void UpdateThicknessMenuCheckedState()
        {
            UpdateMenuCheckedState("线条粗细", item => item.Text.Contains(lineHeight.ToString()));
        }

        private void UpdateColorMenuCheckedState()
        {
            UpdateMenuCheckedState("线条颜色", item => {
                if (item.Image is Bitmap bmp)
                {
                    try
                    {
                        Color menuColor = bmp.GetPixel(8, 8);
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
            UpdateMenuCheckedState("线条透明度", item => item.Text.Contains(lineOpacity.ToString() + "%"));
        }

        private void UpdateDurationMenuCheckedState()
        {
            UpdateMenuCheckedState("显示时长", item => {
                if (item.Text.Contains("0.5秒") && Math.Abs(displayDuration - 0.5) < 0.01) return true;
                if (item.Text.Contains("1秒") && Math.Abs(displayDuration - 1.0) < 0.01) return true;
                if (item.Text.Contains("1.5秒") && Math.Abs(displayDuration - 1.5) < 0.01) return true;
                if (item.Text.Contains("2秒") && Math.Abs(displayDuration - 2.0) < 0.01) return true;
                if (item.Text.Contains("3秒") && Math.Abs(displayDuration - 3.0) < 0.01) return true;
                if (item.Text.Contains("5秒") && Math.Abs(displayDuration - 5.0) < 0.01) return true;
                return false;
            });
        }

        private void UpdateHotKeyMenuCheckedState()
        {
            UpdateMenuCheckedState("热键设置", item => {
                return (item.Text == "F5" && currentHotKey == Keys.F5) ||
                       (item.Text == "F6" && currentHotKey == Keys.F6) ||
                       (item.Text == "F7" && currentHotKey == Keys.F7) ||
                       (item.Text == "F8" && currentHotKey == Keys.F8) ||
                       (item.Text == "F9" && currentHotKey == Keys.F9) ||
                       (item.Text == "F10" && currentHotKey == Keys.F10) ||
                       (item.Text == "F11" && currentHotKey == Keys.F11) ||
                       (item.Text == "F12" && currentHotKey == Keys.F12);
            });
        }

        /// <summary>
        /// 通用的瞬时横线菜单更新方法
        /// </summary>
        private void UpdateMenuCheckedState(string menuName, Func<ToolStripMenuItem, bool> checkCondition)
        {
            if (trayIcon?.ContextMenuStrip == null) return;

            // 查找瞬时横线菜单
            foreach (ToolStripItem item in trayIcon.ContextMenuStrip.Items)
            {
                if (item is ToolStripMenuItem temporaryMenu && temporaryMenu.Text == "瞬时横线")
                {
                    // 在瞬时横线菜单中查找指定的子菜单
                    foreach (ToolStripItem subItem in temporaryMenu.DropDownItems)
                    {
                        if (subItem is ToolStripMenuItem targetMenu && targetMenu.Text == menuName)
                        {
                            // 更新目标菜单中的选项
                            foreach (ToolStripItem optionItem in targetMenu.DropDownItems)
                            {
                                // 跳过分隔符
                                if (optionItem is ToolStripSeparator) continue;
                                
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

        private void ApplyAllSettings()
        {
            // 设置窗口状态
            this.WindowState = WindowState.Minimized;
            this.ShowInTaskbar = false;
            this.Visibility = Visibility.Hidden;
        }

        /// <summary>
        /// 显示线条 - 完全模拟WinForms的方式
        /// </summary>
        private void ShowLine()
        {
            try
            {
                // 停止任何正在进行的淡出效果
                ResetLineState();

                // 直接获取鼠标的物理像素位置（完全模拟WinForms的Cursor.Position）
                var mousePos = System.Windows.Forms.Cursor.Position;
                Screen currentScreen = Screen.FromPoint(mousePos);

                // 计算鼠标在屏幕上的相对Y坐标
                int relativeY = mousePos.Y - currentScreen.Bounds.Y;

                // 重置所有线条的不透明度为0
                foreach (var form in screenLines.Values)
                {
                    form.Opacity = 0;
                }

                // 计算当前应用的不透明度
                double currentOpacity = lineOpacity / 100.0;

                if (showOnAllScreens)
                {
                    // 显示所有屏幕上的线条
                    foreach (var screenEntry in screenLines)
                    {
                        var screen = screenEntry.Key;
                        var form = screenEntry.Value;
                        
                        // 计算线条应该出现的位置 (保持Y坐标相对一致) - 使用物理像素
                        int lineY = screen.Bounds.Y + relativeY;
                        // 确保线条不超出屏幕边界
                        if (lineY < screen.Bounds.Y) lineY = screen.Bounds.Y;
                        if (lineY > screen.Bounds.Bottom - lineHeight) lineY = screen.Bounds.Bottom - lineHeight;
                        
                        // 使用物理像素直接设置位置和大小
                        form.SetPhysicalBounds(screen.Bounds.X, lineY, screen.Bounds.Width, lineHeight);
                        
                        // 设置背景颜色
                        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(
                            (byte)(currentOpacity * 255),
                            lineColor.R, lineColor.G, lineColor.B));
                        form.Background = brush;
                        
                        form.Opacity = currentOpacity;
                        
                        // 确保窗体是可见的
                        if (!form.IsVisible) form.Show();
                    }
                }
                else
                {
                    // 只在鼠标所在的屏幕显示线条
                    if (screenLines.ContainsKey(currentScreen))
                    {
                        var form = screenLines[currentScreen];
                        
                        // 使用物理像素直接设置位置 - 直接使用鼠标的Y坐标
                        form.SetPhysicalBounds(currentScreen.Bounds.X, mousePos.Y, currentScreen.Bounds.Width, lineHeight);
                        
                        // 设置背景颜色
                        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(
                            (byte)(currentOpacity * 255),
                            lineColor.R, lineColor.G, lineColor.B));
                        form.Background = brush;
                        
                        form.Opacity = currentOpacity;
                        
                        // 确保窗体是可见的
                        if (!form.IsVisible) form.Show();
                    }
                }

                // 重置opacity状态并启动淡出计时器
                this.opacity = currentOpacity;
                StartWinFormsStyleFade(currentOpacity);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"显示线条时发生错误: {ex.Message}", "错误");
            }
        }

        private void StartWinFormsStyleFade(double startOpacity)
        {
            // 使用类似WinForms的淡出逻辑
            fadeTimer = new DispatcherTimer();
            fadeTimer.Interval = TimeSpan.FromMilliseconds(50); // 和WinForms保持一致
            
            double currentOpacity = startOpacity;
            
            fadeTimer.Tick += (s, e) => {
                // 根据显示时长计算每次减少的不透明度（和WinForms完全一致）
                double fadeStep = 0.02 * (2.0 / displayDuration);
                currentOpacity -= fadeStep;

                if (currentOpacity <= 0)
                {
                    // 淡出完成，停止定时器并隐藏所有线条
                    fadeTimer.Stop();
                    fadeTimer = null;
                    foreach (var form in screenLines.Values)
                    {
                        form.Opacity = 0;
                    }
                }
                else
                {
                    // 更新所有可见线条的不透明度
                    foreach (var form in screenLines.Values)
                    {
                        if (form.Opacity > 0)
                        {
                            form.Opacity = currentOpacity;
                        }
                    }
                }
            };
            
            fadeTimer.Start();
        }

        private void ResetLineState()
        {
            // 停止旧的淡出定时器（如果存在）
            if (fadeTimer != null)
            {
                fadeTimer.Stop();
                fadeTimer = null;
            }
            
            // 隐藏所有现有线条但不关闭窗体（模拟WinForms逻辑）
            foreach (var line in screenLines.Values)
            {
                if (line != null && line.IsLoaded)
                {
                    line.Opacity = 0; // 只是隐藏，不关闭
                }
            }
        }

        private void FadeTimer_Tick(object sender, EventArgs e)
        {
            // 已被新的动画机制替代，此方法不再使用
        }

        private void TopmostTimer_Tick(object sender, EventArgs e)
        {
            // 如果托盘菜单正在显示，跳过置顶操作以避免干扰菜单
            if (isTrayMenuVisible)
            {
                return;
            }

            // 强制置顶所有线条窗口
            try
            {
                // 置顶持续竖线
                verticalLineWindow?.EnsureTopmost();
                
                // 置顶持续横线
                horizontalLineWindow?.EnsureTopmost();
                
                // 置顶包围框
                boundingBoxWindow?.EnsureTopmost();
                
                // 置顶瞬时横线
                foreach (var line in screenLines.Values)
                {
                    if (line != null && line.IsLoaded && line.Visibility == Visibility.Visible)
                    {
                        line.Topmost = false;
                        line.Topmost = true;
                    }
                }
            }
            catch (Exception)
            {
                // 忽略置顶过程中的异常
            }
        }

        private void StartTopmostMonitoring()
        {
            // 停止旧的监听
            StopTopmostMonitoring();
            
            if (currentTopmostStrategy == TopmostStrategy.ForceTimer)
            {
                // 暴力定时器方案
                if (topmostTimer == null)
                {
                    topmostTimer = new DispatcherTimer();
                    topmostTimer.Tick += TopmostTimer_Tick;
                }
                topmostTimer.Interval = TimeSpan.FromMilliseconds(currentTimerInterval);
                topmostTimer.Start();
                
                Console.WriteLine($"[置顶] 启动暴力定时器方案，间隔: {currentTimerInterval}ms");
            }
            else if (currentTopmostStrategy == TopmostStrategy.SmartMonitor)
            {
                // 智能监听方案
                if (winEventDelegate == null)
                {
                    winEventDelegate = new WinEventDelegate(WinEventProc);
                }
                
                hWinEventHook = SetWinEventHook(
                    EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero, winEventDelegate,
                    0, 0, WINEVENT_OUTOFCONTEXT
                );
                
                if (hWinEventHook != IntPtr.Zero)
                {
                    Console.WriteLine("[置顶] 启动智能监听方案");
                }
                else
                {
                    Console.WriteLine("[置顶] 智能监听方案启动失败，切换到定时器方案");
                    // 如果智能监听失败，回退到定时器方案
                    currentTopmostStrategy = TopmostStrategy.ForceTimer;
                    StartTopmostMonitoring();
                }
            }
        }

        private void StopTopmostMonitoring()
        {
            // 停止定时器
            topmostTimer?.Stop();
            
            // 停止Windows事件监听
            if (hWinEventHook != IntPtr.Zero)
            {
                UnhookWinEvent(hWinEventHook);
                hWinEventHook = IntPtr.Zero;
                Console.WriteLine("[置顶] 停止智能监听");
            }
        }

        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (eventType == EVENT_SYSTEM_FOREGROUND && persistentTopmost && currentTopmostStrategy == TopmostStrategy.SmartMonitor)
            {
                try
                {
                    // 获取前台窗口标题
                    StringBuilder windowTitle = new StringBuilder(256);
                    GetWindowText(hwnd, windowTitle, windowTitle.Capacity);
                    string title = windowTitle.ToString();
                    
                    // 检查是否是监控的应用程序
                    bool isMonitoredApp = monitoredApplications.Any(app => app.IsEnabled && title.Contains(app.Name));
                    
                    if (isMonitoredApp)
                    {
                        Console.WriteLine($"[置顶] 检测到监控程序置顶: {title}");
                        
                        // 检测到监控的应用程序置顶，立即重新置顶我们的线条
                        Task.Run(() =>
                        {
                            // 稍微延迟一下再执行置顶操作，确保目标程序完全置顶后再抢夺
                            System.Threading.Thread.Sleep(50);
                            
                            this.Dispatcher.Invoke(() =>
                            {
                                PerformTopmostOperation();
                            });
                        });
                    }
                }
                catch (Exception)
                {
                    // 忽略监听过程中的异常
                }
            }
        }

        /// <summary>
        /// 执行置顶操作（提取公共代码）
        /// </summary>
        private void PerformTopmostOperation()
        {
            try
            {
                // 置顶持续竖线
                verticalLineWindow?.EnsureTopmost();
                
                // 置顶持续横线
                horizontalLineWindow?.EnsureTopmost();
                
                // 置顶包围框
                boundingBoxWindow?.EnsureTopmost();
                
                // 置顶瞬时横线
                foreach (var line in screenLines.Values)
                {
                    if (line != null && line.IsLoaded && line.Visibility == Visibility.Visible)
                    {
                        line.Topmost = false;
                        line.Topmost = true;
                    }
                }
                
                Console.WriteLine("[置顶] 执行了一次置顶操作");
            }
            catch (Exception)
            {
                // 忽略置顶过程中的异常
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // 停止置顶监控
            StopTopmostMonitoring();
            
            // 清理托盘图标
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }

            // 注销热键
            if (currentHotKey != Keys.None && isHotKeyRegistered)
            {
                WindowInteropHelper helper = new WindowInteropHelper(this);
                UnregisterHotKey(helper.Handle, currentHotKeyId);
                isHotKeyRegistered = false;
            }

            base.OnClosed(e);
        }

        private void ExitApplication()
        {
            // 清理托盘图标
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
                isTrayIconInitialized = false;
            }
            
            // 注销热键
            if (currentHotKey != Keys.None && isHotKeyRegistered)
            {
                WindowInteropHelper helper = new WindowInteropHelper(this);
                UnregisterHotKey(helper.Handle, currentHotKeyId);
                isHotKeyRegistered = false;
            }
            
            // 清理线条
            ResetLineState();
            
            // 退出应用程序
            System.Windows.Application.Current.Shutdown();
        }

        /// <summary>
        /// 更改显示时长
        /// </summary>
        private void ChangeDisplayDuration(double duration)
        {
            displayDuration = duration;
            UpdateDurationMenuCheckedState();
            SaveConfig();
        }

        /// <summary>
        /// 更改热键
        /// </summary>
        private void ChangeHotKey(Keys newHotKey)
        {
            try
            {
                WindowInteropHelper helper = new WindowInteropHelper(this);
                
                // 如果点击当前已绑定的热键，则解除绑定
                if (newHotKey == currentHotKey)
                {
                    if (isHotKeyRegistered)
                    {
                        UnregisterHotKey(helper.Handle, currentHotKeyId);
                        isHotKeyRegistered = false;
                    }
                    currentHotKey = Keys.None;
                }
                else
                {
                    // 注销旧热键
                    if (currentHotKey != Keys.None && isHotKeyRegistered)
                    {
                        UnregisterHotKey(helper.Handle, currentHotKeyId);
                        isHotKeyRegistered = false;
                    }
                    
                    // 注册新热键
                    currentHotKey = newHotKey;
                    if (!globalHotkeysDisabled)
                    {
                        RegisterCurrentHotKey();
                    }
                }
                
                UpdateHotKeyMenuCheckedState();
                SaveConfig();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"更改热键时发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 更改线条粗细
        /// </summary>
        private void ChangeLineThickness(int thickness)
        {
            lineHeight = thickness;
            UpdateThicknessMenuCheckedState();
            SaveConfig();
        }

        /// <summary>
        /// 更改线条颜色
        /// </summary>
        private void ChangeLineColor(Color color)
        {
            lineColor = color;
            UpdateColorMenuCheckedState();
            SaveConfig();
        }

        /// <summary>
        /// 更改线条透明度
        /// </summary>
        private void ChangeLineTransparency(int opacityValue)
        {
            lineOpacity = opacityValue;
            UpdateTransparencyMenuCheckedState();
            SaveConfig();
        }

        /// <summary>
        /// 更改置顶策略
        /// </summary>
        private void ChangeTopmostStrategy(TopmostStrategy strategy)
        {
            currentTopmostStrategy = strategy;
            
            // 如果当前启用了持续置顶，重新启动监听机制
            if (persistentTopmost)
            {
                StartTopmostMonitoring();
            }
            
            SaveConfig();
        }

        /// <summary>
        /// 更改定时器间隔
        /// </summary>
        private void ChangeTimerInterval(int interval)
        {
            // 验证间隔值，确保大于0
            if (interval <= 0)
            {
                interval = 100; // 使用默认值
            }
            
            currentTimerInterval = interval;
            
            // 如果当前使用定时器策略且已启用，重新启动定时器
            if (persistentTopmost && currentTopmostStrategy == TopmostStrategy.ForceTimer)
            {
                if (topmostTimer != null)
                {
                    topmostTimer.Stop();
                    topmostTimer.Interval = TimeSpan.FromMilliseconds(interval);
                    topmostTimer.Start();
                }
            }
            
            SaveConfig();
        }

        /// <summary>
        /// 切换全局快捷键开关
        /// </summary>
        private void ToggleGlobalHotkeys()
        {
            globalHotkeysDisabled = !globalHotkeysDisabled;

            if (!globalHotkeysDisabled)
            {
                // 重新注册之前启用的热键
                if (currentHotKey != Keys.None)
                {
                    RegisterCurrentHotKey();
                }
                
                // 启用其他窗口的热键
                verticalLineWindow?.EnableAllHotkeys();
                horizontalLineWindow?.EnableAllHotkeys();
            }
            else
            {
                // 注销所有热键
                if (currentHotKey != Keys.None && isHotKeyRegistered)
                {
                    WindowInteropHelper helper = new WindowInteropHelper(this);
                    UnregisterHotKey(helper.Handle, currentHotKeyId);
                    isHotKeyRegistered = false;
                }
                
                // 禁用其他窗口的热键
                verticalLineWindow?.DisableAllHotkeys();
                horizontalLineWindow?.DisableAllHotkeys();
            }

            UpdateGlobalHotkeyMenuCheckedState();
            SaveConfig();
        }

        /// <summary>
        /// 更新全局快捷键菜单的选中状态
        /// </summary>
        private void UpdateGlobalHotkeyMenuCheckedState()
        {
            if (trayIcon?.ContextMenuStrip == null) return;

            foreach (ToolStripItem item in trayIcon.ContextMenuStrip.Items)
            {
                if (item is ToolStripMenuItem menuItem && menuItem.Text == "禁用快捷键")
                {
                    menuItem.Checked = globalHotkeysDisabled;
                    break;
                }
            }
        }

        /// <summary>
        /// 切换所有线条的可见性
        /// </summary>
        private void ToggleAllPersistentLines()
        {
            allPersistentLinesVisible = !allPersistentLinesVisible;
            
            if (allPersistentLinesVisible)
            {
                // 显示所有线条
                verticalLineWindow?.ShowAllLines();
                horizontalLineWindow?.ShowAllLines();
                boundingBoxWindow?.ShowAllLines();
                
                // 显示瞬时横线
                foreach (var line in screenLines.Values)
                {
                    line?.Show();
                }
            }
            else
            {
                // 隐藏所有线条
                verticalLineWindow?.HideAllLines();
                horizontalLineWindow?.HideAllLines();
                boundingBoxWindow?.HideAllLines();
                
                // 隐藏瞬时横线
                foreach (var line in screenLines.Values)
                {
                    line?.Hide();
                }
            }
            
            UpdateToggleMenuText();
        }

        /// <summary>
        /// 更新显示/隐藏菜单项的文本
        /// </summary>
        private void UpdateToggleMenuText()
        {
            if (trayIcon?.ContextMenuStrip == null) return;
            
            foreach (ToolStripItem item in trayIcon.ContextMenuStrip.Items)
            {
                if (item is ToolStripMenuItem menuItem && 
                    (menuItem.Text == "隐藏全部线条" || menuItem.Text == "显示全部线条"))
                {
                    menuItem.Text = allPersistentLinesVisible ? "隐藏全部线条" : "显示全部线条";
                    break;
                }
            }
        }

        /// <summary>
        /// 关闭所有线条（带确认对话框）
        /// </summary>
        private void CloseAllLines()
        {
            // 显示确认对话框
            MessageBoxResult result = System.Windows.MessageBox.Show(
                "确定要关闭所有线条吗？\n\n这将关闭所有瞬时横线、持续横线、持续竖线和包围框。",
                "确认关闭",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No  // 默认选择"否"
            );
            
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // 关闭持续竖线
                    verticalLineWindow?.CloseAllLines();
                    
                    // 关闭持续横线
                    horizontalLineWindow?.CloseAllLines();
                    
                    // 关闭包围框
                    boundingBoxWindow?.CloseAllLines();
                    
                    // 关闭瞬时横线
                    foreach (var line in screenLines.Values.ToList())
                    {
                        line?.Close();
                    }
                    screenLines.Clear();
                    
                    // 重新初始化瞬时横线
                    InitializeScreenLines();
                    
                    // 更新全局显示状态
                    allPersistentLinesVisible = true;
                    UpdateToggleMenuText();
                    
                    System.Windows.MessageBox.Show("所有线条已关闭。", "操作完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"关闭线条时发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// 重新置顶所有线条
        /// </summary>
        public void BringAllLinesToTop()
        {
            try
            {
                // 重新置顶持续竖线
                verticalLineWindow?.BringAllLinesToTop();
                
                // 重新置顶持续横线
                horizontalLineWindow?.BringAllLinesToTop();
                
                // 重新置顶包围框
                boundingBoxWindow?.BringAllLinesToTop();
                
                // 重新置顶瞬时横线窗体
                foreach (var form in screenLines.Values)
                {
                    if (form != null && form.IsLoaded)
                    {
                        form.Topmost = false;
                        form.Topmost = true;
                    }
                }
                
                System.Windows.MessageBox.Show("所有线条已重新置顶。", "操作完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"置顶线条时发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 切换持续置顶功能
        /// </summary>
        private void TogglePersistentTopmost()
        {
            persistentTopmost = !persistentTopmost;
            
            if (persistentTopmost)
            {
                // 启动置顶监控
                StartTopmostMonitoring();
                Console.WriteLine($"[置顶] 启动持续置顶，策略: {currentTopmostStrategy}");
            }
            else
            {
                // 停止置顶监控
                StopTopmostMonitoring();
                Console.WriteLine("[置顶] 停止持续置顶");
            }
            
            SaveConfig();
        }

        /// <summary>
        /// 管理自定义监控程序
        /// </summary>
        private void ManageCustomApps()
        {
            try
            {
                var managementWindow = new MonitoredAppsWindow(monitoredApplications)
                {
                    Owner = this
                };
                
                if (managementWindow.ShowDialog() == true && managementWindow.DialogResultOK)
                {
                    // 更新监控应用程序列表
                    monitoredApplications.Clear();
                    monitoredApplications.AddRange(managementWindow.MonitoredApps);
                    
                    // 更新菜单状态并保存配置
                    SaveConfig();
                    
                    System.Windows.MessageBox.Show($"监控程序配置已更新。当前监控 {monitoredApplications.Count} 个程序。", 
                        "配置已保存", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"打开管理窗口时发生错误：{ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 重启应用程序
        /// </summary>
        private void RestartApplication()
        {
            try
            {
                // **立即注销所有热键，防止与新进程冲突**
                UnregisterAllHotKeys();
                
                // 获取当前应用程序的可执行文件路径
                string currentPath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                
                if (string.IsNullOrEmpty(currentPath))
                {
                    // 备用方法：使用应用程序域基目录 + 可执行文件名
                    currentPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Line_wpf.exe");
                }
                
                Console.WriteLine($"[重启] 尝试启动: {currentPath}");
                
                if (!File.Exists(currentPath))
                {
                    System.Windows.MessageBox.Show($"找不到可执行文件: {currentPath}", "重启失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                
                // 创建新进程的启动信息
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = currentPath,
                    Arguments = "--restart",  // 传递重启参数
                    UseShellExecute = true,
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                };
                
                Console.WriteLine($"[重启] 立即启动新进程...");
                
                // 立即启动新的应用程序实例
                var process = System.Diagnostics.Process.Start(startInfo);
                
                if (process != null)
                {
                    Console.WriteLine($"[重启] 新进程已启动，PID: {process.Id}，延迟退出当前进程");
                    
                    // 创建延迟退出定时器 - 稍微延长到5秒确保新进程完全启动
                    var exitTimer = new DispatcherTimer();
                    exitTimer.Interval = TimeSpan.FromMilliseconds(5000); // 延迟5秒退出
                    exitTimer.Tick += (s, e) => {
                        exitTimer.Stop();
                        
                        Console.WriteLine($"[重启] 准备退出当前进程...");
                        
                        // 清理托盘图标
                        if (trayIcon != null)
                        {
                            trayIcon.Visible = false;
                            trayIcon.Dispose();
                        }
                        
                        // 强制退出当前进程
                        Environment.Exit(0);
                    };
                    exitTimer.Start();
                }
                else
                {
                    System.Windows.MessageBox.Show("启动新进程失败", "重启失败", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[重启] 发生错误: {ex.Message}");
                System.Windows.MessageBox.Show($"重启程序时发生错误: {ex.Message}\n\n详细信息: {ex}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 注销所有热键（重启时使用）
        /// </summary>
        private void UnregisterAllHotKeys()
        {
            try
            {
                Console.WriteLine("[重启] 开始注销所有热键...");
                
                // 注销主窗体热键
                if (currentHotKey != Keys.None && isHotKeyRegistered)
                {
                    WindowInteropHelper helper = new WindowInteropHelper(this);
                    if (helper.Handle != IntPtr.Zero)
                    {
                        UnregisterHotKey(helper.Handle, currentHotKeyId);
                        isHotKeyRegistered = false;
                        Console.WriteLine($"[重启] 已注销主窗体热键: {currentHotKey}");
                    }
                }
                
                // 注销子窗体热键
                verticalLineWindow?.DisableAllHotkeys();
                horizontalLineWindow?.DisableAllHotkeys();
                Console.WriteLine("[重启] 已注销所有子窗体热键");
                
                // 小延迟确保热键完全释放
                System.Threading.Thread.Sleep(200);
                Console.WriteLine("[重启] 热键注销完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[重启] 注销热键时发生错误: {ex.Message}");
            }
        }
    }
} 