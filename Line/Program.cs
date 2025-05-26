using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Threading;
using System.Reflection;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualBasic; // 新增：用于InputBox

namespace Line
{
    internal class Program
    {
        // 用于确保应用程序只运行一个实例的互斥体
        private static readonly Mutex SingleInstanceMutex = new Mutex(true, "LineAppSingleInstanceMutex");

        // 添加应用程序数据目录路径
        private static readonly string AppDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ScreenLine"
        );

        /// <summary>
        /// 释放单实例互斥体（用于重启功能）
        /// </summary>
        public static void ReleaseSingleInstanceMutex()
        {
            try
            {
                SingleInstanceMutex.ReleaseMutex();
            }
            catch (Exception)
            {
                // 忽略释放互斥体时的异常
            }
        }

        /// <summary>
        /// 检查是否有其他实例正在运行（备用方法）
        /// </summary>
        private static bool IsAnotherInstanceRunning()
        {
            try
            {
                string currentProcessName = Process.GetCurrentProcess().ProcessName;
                Process[] processes = Process.GetProcessesByName(currentProcessName);
                
                // 如果有多于一个同名进程，说明有其他实例在运行
                return processes.Length > 1;
            }
            catch (Exception)
            {
                // 如果检查失败，假设没有其他实例
                return false;
            }
        }

        [STAThread]
        static void Main(string[] args)
        {
            // 检查是否是重启启动（带特殊参数）
            bool isRestartMode = args.Length > 0 && args[0] == "--restart";
            
            // 只有在非重启模式下才检查互斥锁
            if (!isRestartMode)
            {
                // 首先尝试使用互斥锁检查
                bool mutexAcquired = SingleInstanceMutex.WaitOne(TimeSpan.Zero, true);
                
                if (!mutexAcquired)
                {
                    // 如果互斥锁获取失败，使用进程检查作为备用
                    if (IsAnotherInstanceRunning())
                    {
                        MessageBox.Show("程序已经在运行中！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return; // 退出本次运行
                    }
                }
            }

            try
            {
                // 确保应用程序数据目录存在
                if (!Directory.Exists(AppDataPath))
                {
                    Directory.CreateDirectory(AppDataPath);
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new LineForm());
            }
            finally
            {
                // 在应用程序结束时释放互斥体（仅在非重启模式下）
                if (!isRestartMode)
                {
                    try
                    {
                        SingleInstanceMutex.ReleaseMutex();
                    }
                    catch (Exception)
                    {
                        // 忽略释放异常
                    }
                }
            }
        }
    }

    public class LineForm : Form
    {
        private System.Windows.Forms.Timer fadeTimer;
        private System.Windows.Forms.Timer topmostTimer; // 持续置顶定时器
        private float opacity = 1.0f;
        private const int WM_HOTKEY = 0x0312;
        private int currentHotKeyId = 1;  // 添加热键ID
        private NotifyIcon trayIcon;
        private bool showOnAllScreens = false;
        private Dictionary<Screen, Form> screenLines = new Dictionary<Screen, Form>();
        private VerticalLineForm verticalLineForm;  // 添加竖线窗体实例
        private HorizontalLineForm horizontalLineForm;  // 添加横线窗体实例
        
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
        private readonly string configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ScreenLine",
            "config.json"
        );

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
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // 用于设置鼠标穿透的Windows API
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int GWL_EXSTYLE = (-20);

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

        public LineForm()
        {
            // 2) 关闭 WinForms 自动 DPI 缩放，保证 Height=1 就是真正的 1 物理像素
            this.AutoScaleMode = AutoScaleMode.None;

            // 加载配置
            LoadConfig();

            // 设置窗体属性
            this.FormBorderStyle = FormBorderStyle.None;  // 无边框
            this.ShowInTaskbar = false;  // 不在任务栏显示
            this.TopMost = true;  // 置顶显示
            this.BackColor = lineColor;  // 线条颜色
            this.TransparencyKey = Color.Black;  // 透明色
            this.Opacity = lineOpacity / 100.0;  // 初始透明度
            this.Width = Screen.PrimaryScreen.Bounds.Width;  // 宽度等于屏幕宽度
            this.Height = lineHeight;  // 高度为设定的线条高度

            // 初始化系统托盘图标
            InitializeTrayIcon();

            // 初始化竖线窗体
            verticalLineForm = new VerticalLineForm(trayIcon);

            // 初始化横线窗体
            horizontalLineForm = new HorizontalLineForm(trayIcon);

            // 设置定时器用于淡出效果
            fadeTimer = new System.Windows.Forms.Timer();
            fadeTimer.Interval = 50;  // 50毫秒更新一次
            fadeTimer.Tick += FadeTimer_Tick;

            // 初始化持续置顶定时器
            topmostTimer = new System.Windows.Forms.Timer();
            
            // 验证定时器间隔，确保不为0或负数
            if (currentTimerInterval <= 0)
            {
                currentTimerInterval = 100; // 使用默认值
            }
            
            topmostTimer.Interval = currentTimerInterval;  // 使用验证后的间隔
            topmostTimer.Tick += TopmostTimer_Tick;
            
            // 初始化Windows事件监听委托
            winEventDelegate = new WinEventDelegate(WinEventProc);
            
            // 如果配置中启用了持续置顶，立即启动相应的监听机制
            if (persistentTopmost)
            {
                StartTopmostMonitoring();
            }

            // 注册默认热键(F5)
            RegisterCurrentHotKey();
        }

        /// <summary>
        /// 设置窗体的鼠标穿透属性
        /// </summary>
        private void SetClickThrough(Form form)
        {
            if (form.Handle != IntPtr.Zero)
            {
                int exStyle = GetWindowLong(form.Handle, GWL_EXSTYLE);
                SetWindowLong(form.Handle, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // 主窗体创建句柄后立即设置鼠标穿透
            SetClickThrough(this);
        }

        // 加载配置
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
                }
            }
            catch (Exception ex)
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
            }
        }

        // 保存配置
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
                    TopmostStrategy = (int)currentTopmostStrategy, // 新增
                    TimerInterval = currentTimerInterval, // 新增
                    MonitoredApplications = monitoredApplications // 新增
                };

                string jsonString = JsonSerializer.Serialize(config, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(configPath, jsonString);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存配置时出错: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            // 第一次启动时闪一下横线
            ShowLine();

            // 立刻把自己隐藏
            this.Opacity = 0;
        }

        /// <summary>
        /// 初始化系统托盘图标和右键菜单
        /// </summary>
        private void InitializeTrayIcon()
        {
            try
            {
                trayIcon = new NotifyIcon();
                // 加载图标
                string iconPath = AppDomain.CurrentDomain.BaseDirectory + "LineIco.ico";
                if (System.IO.File.Exists(iconPath))
                {
                    trayIcon.Icon = new Icon(iconPath);
                }
                else
                {
                    // 使用嵌入资源中的图标或默认应用图标
                    trayIcon.Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
                }
                trayIcon.Text = "屏幕参考线";
                trayIcon.Visible = true;

                // 创建右键菜单
                ContextMenuStrip contextMenu = new ContextMenuStrip();
                
                // ===== 瞬时横线菜单组 =====
                ToolStripMenuItem temporaryHorizontalLineMenu = new ToolStripMenuItem("瞬时横线");
                
                // 显示模式菜单
                ToolStripMenuItem displayModeItem = new ToolStripMenuItem("显示模式");
                var currentScreenItem = new ToolStripMenuItem("仅鼠标所在屏幕", null, (s, e) => {
                    showOnAllScreens = false;
                    SaveConfig();
                });
                currentScreenItem.Checked = !showOnAllScreens;
                
                var allScreensItem = new ToolStripMenuItem("所有屏幕", null, (s, e) => {
                    showOnAllScreens = true;
                    SaveConfig();
                });
                allScreensItem.Checked = showOnAllScreens;
                
                displayModeItem.DropDownItems.Add(currentScreenItem);
                displayModeItem.DropDownItems.Add(allScreensItem);
                
                // 添加事件处理程序来更新选中状态
                currentScreenItem.Click += (s, e) => {
                    currentScreenItem.Checked = true;
                    allScreensItem.Checked = false;
                };
                
                allScreensItem.Click += (s, e) => {
                    allScreensItem.Checked = true;
                    currentScreenItem.Checked = false;
                };
                
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
                
                var customColorItem = new ToolStripMenuItem("自定义颜色...", null, (s, e) => {
                    ColorDialog colorDialog = new ColorDialog();
                    colorDialog.Color = lineColor;
                    if (colorDialog.ShowDialog() == DialogResult.OK)
                    {
                        ChangeLineColor(colorDialog.Color);
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

                // 退出选项
                ToolStripMenuItem exitItem = new ToolStripMenuItem("退出", null, (s, e) => {
                    CleanupAndExit();
                });

                // 将所有主菜单项添加到上下文菜单
                contextMenu.Items.Add(temporaryHorizontalLineMenu);
                // 竖线菜单会在 verticalLineForm 中自动添加
                contextMenu.Items.Add(new ToolStripSeparator());
                
                // 添加显示/隐藏全部持续线条的菜单项
                ToolStripMenuItem toggleAllLinesItem = new ToolStripMenuItem("隐藏全部持续线条", null, (s, e) => {
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
                
                // 新增：持续置顶策略配置菜单
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
                
                // 监控应用程序管理 - 简化为单个菜单项
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
                
                contextMenu.Items.Add(new ToolStripSeparator());
                
                contextMenu.Items.Add(exitItem);

                trayIcon.ContextMenuStrip = contextMenu;
                
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
                            Point mousePos = Control.MousePosition;
                            // 显示菜单在鼠标位置
                            trayIcon.ContextMenuStrip.Show(mousePos);
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                MessageBox.Show("初始化托盘图标失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 添加线条粗细菜单项
        /// </summary>
        private void AddThicknessMenuItem(ToolStripMenuItem parent, string text, int thickness)
        {
            var item = new ToolStripMenuItem(text, null, (s, e) => {
                ChangeLineThickness(thickness);
            });
            item.Checked = (thickness == lineHeight);
            parent.DropDownItems.Add(item);
        }

        /// <summary>
        /// 安全地清理资源并退出应用程序
        /// </summary>
        private void CleanupAndExit()
        {
            try
            {
                // 关闭竖线窗体
                if (verticalLineForm != null)
                {
                    verticalLineForm.Close();
                }

                // 关闭横线窗体
                if (horizontalLineForm != null)
                {
                    horizontalLineForm.Close();
                }

                // 彻底清理托盘图标
                if (trayIcon != null)
                {
                    trayIcon.Visible = false;
                    trayIcon.Dispose();
                    trayIcon = null;
                }

                // 注销热键
                UnregisterHotKey(this.Handle, currentHotKeyId);

                // 安全地关闭其他窗体
                var formsToClose = new List<Form>();
                foreach (var entry in screenLines)
                {
                    if (!entry.Key.Primary && entry.Value != this)
                    {
                        formsToClose.Add(entry.Value);
                    }
                }

                // 在不遍历集合的情况下关闭窗体
                foreach (var form in formsToClose)
                {
                    form.Close();
                }

                // 退出应用程序
                Application.Exit();
            }
            catch (Exception ex)
            {
                MessageBox.Show("退出程序时发生错误: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(1); // 强制退出
            }
        }

        /// <summary>
        /// 向菜单添加颜色选项
        /// </summary>
        private void AddColorMenuItem(ToolStripMenuItem parentMenu, string name, Color color)
        {
            // 创建颜色预览图像
            Bitmap colorPreview = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(colorPreview))
            {
                g.FillRectangle(new SolidBrush(color), 0, 0, 16, 16);
                g.DrawRectangle(Pens.Gray, 0, 0, 15, 15);
            }

            // 创建菜单项
            var colorItem = new ToolStripMenuItem(name, colorPreview, (s, e) => {
                ChangeLineColor(color);
            });
            
            // 如果是当前颜色则标记为选中
            colorItem.Checked = color.Equals(lineColor);
            
            // 添加到父菜单
            parentMenu.DropDownItems.Add(colorItem);
        }

        /// <summary>
        /// 向菜单添加透明度选项
        /// </summary>
        private void AddTransparencyMenuItem(ToolStripMenuItem parentMenu, string name, int value)
        {
            var item = new ToolStripMenuItem(name, null, (s, e) => {
                ChangeLineTransparency(value);
            });
            
            // 如果是当前透明度则标记为选中
            item.Checked = (value == lineOpacity);
            
            // 添加到父菜单
            parentMenu.DropDownItems.Add(item);
        }

        /// <summary>
        /// 更改所有线条的粗细
        /// </summary>
        /// <param name="thickness">线条粗细（像素）</param>
        private void ChangeLineThickness(int thickness)
        {
            // 更新线条高度
            lineHeight = thickness;
            
            // 更新主窗体高度
            this.Height = lineHeight;
            
            // 更新所有其他屏幕上的线条高度
            foreach (var form in screenLines.Values)
            {
                if (form != this)
                {
                    form.Height = lineHeight;
                }
            }
            
            // 更新菜单项选中状态
            UpdateThicknessMenuCheckedState();
            SaveConfig();
        }

        /// <summary>
        /// 更新线条粗细菜单的选中状态
        /// </summary>
        private void UpdateThicknessMenuCheckedState()
        {
            UpdateTemporaryHorizontalMenuCheckedState("线条粗细", item => {
                string thicknessStr = lineHeight.ToString();
                return item.Text.Contains(thicknessStr);
            });
        }
        
        /// <summary>
        /// 更改线条颜色
        /// </summary>
        /// <param name="color">新的颜色</param>
        private void ChangeLineColor(Color color)
        {
            // 更新线条颜色
            lineColor = color;
            
            // 更新所有窗体的颜色
            this.BackColor = lineColor;
            foreach (var form in screenLines.Values)
            {
                if (form != this)
                {
                    form.BackColor = lineColor;
                }
            }
            
            // 更新菜单项选中状态
            UpdateColorMenuCheckedState();
            SaveConfig();
        }

        /// <summary>
        /// 更新线条颜色菜单的选中状态
        /// </summary>
        private void UpdateColorMenuCheckedState()
        {
            UpdateTemporaryHorizontalMenuCheckedState("线条颜色", item => {
                // 跳过自定义颜色选项
                if (item.Text == "自定义颜色...") return false;
                
                // 检查颜色菜单项的图像
                if (item.Image != null && item.Image is Bitmap bmp)
                {
                    try
                    {
                        Color menuColor = bmp.GetPixel(8, 8); // 取中心点颜色
                        return ColorEquals(menuColor, lineColor);
                    }
                    catch
                    {
                        return false;
                    }
                }
                return false;
            });
        }
        
        /// <summary>
        /// 比较两个颜色是否相等（忽略轻微差异）
        /// </summary>
        private bool ColorEquals(Color c1, Color c2)
        {
            return Math.Abs(c1.R - c2.R) < 5 && 
                   Math.Abs(c1.G - c2.G) < 5 && 
                   Math.Abs(c1.B - c2.B) < 5;
        }

        /// <summary>
        /// 更改线条透明度
        /// </summary>
        /// <param name="opacityValue">透明度值（0-100）</param>
        private void ChangeLineTransparency(int opacityValue)
        {
            // 更新透明度值
            lineOpacity = opacityValue;
            
            // 更新菜单项选中状态
            UpdateTransparencyMenuCheckedState();
            SaveConfig();
        }

        /// <summary>
        /// 更新线条透明度菜单的选中状态
        /// </summary>
        private void UpdateTransparencyMenuCheckedState()
        {
            UpdateTemporaryHorizontalMenuCheckedState("线条透明度", item => {
                return item.Text.Contains(lineOpacity.ToString() + "%");
            });
        }

        /// <summary>
        /// 更改显示时长
        /// </summary>
        private void ChangeDisplayDuration(double duration)
        {
            displayDuration = duration;
            
            // 更新菜单项选中状态
            UpdateDurationMenuCheckedState();
            SaveConfig();
        }

        /// <summary>
        /// 更新显示时长菜单的选中状态
        /// </summary>
        private void UpdateDurationMenuCheckedState()
        {
            UpdateTemporaryHorizontalMenuCheckedState("显示时长", item => {
                // 从菜单项文本中提取秒数
                string itemText = item.Text;
                int secondsIndex = itemText.IndexOf("秒");
                if (secondsIndex > 0)
                {
                    string durationStr = itemText.Substring(0, secondsIndex);
                    if (double.TryParse(durationStr, out double itemDuration))
                    {
                        return Math.Abs(itemDuration - displayDuration) < 0.01;
                    }
                }
                return false;
            });
        }

        /// <summary>
        /// 注册当前设置的热键
        /// </summary>
        private void RegisterCurrentHotKey()
        {
            try
            {
                // 注册新热键
                if (!RegisterHotKey(this.Handle, currentHotKeyId, 0, (int)currentHotKey))
                {
                    MessageBox.Show($"热键 {currentHotKey} 注册失败，可能已被其他程序占用。", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"注册热键时发生错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 更改热键
        /// </summary>
        private void ChangeHotKey(Keys newHotKey)
        {
            try
            {
                // 先注销旧热键
                UnregisterHotKey(this.Handle, currentHotKeyId);
                
                // 更改热键并重新注册
                currentHotKey = newHotKey;
                RegisterCurrentHotKey();
                
                // 更新菜单项选中状态
                UpdateHotKeyMenuCheckedState();
                SaveConfig();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"更改热键时发生错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 更新热键菜单的选中状态
        /// </summary>
        private void UpdateHotKeyMenuCheckedState()
        {
            UpdateTemporaryHorizontalMenuCheckedState("热键设置", item => {
                string keyName = $"F{(int)currentHotKey - (int)Keys.F1 + 1}";
                return item.Text.Contains(keyName);
            });
        }

        /// <summary>
        /// 通用的瞬时横线菜单更新方法
        /// </summary>
        private void UpdateTemporaryHorizontalMenuCheckedState(string menuName, Func<ToolStripMenuItem, bool> checkCondition)
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

        /// <summary>
        /// 窗体加载时初始化其他屏幕上的线条
        /// </summary>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            
            // 为主屏幕添加自己到字典
            screenLines[Screen.PrimaryScreen] = this;
            
            // 为其他屏幕创建额外的线条窗体
            foreach (Screen screen in Screen.AllScreens)
            {
                if (screen.Primary) continue;

                Form lineForm = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    ShowInTaskbar = false,
                    TopMost = true,
                    BackColor = lineColor,
                    TransparencyKey = Color.Black,
                    Opacity = 0,
                    Width = screen.Bounds.Width,
                    Height = lineHeight  // 使用设定的线条高度
                };
                
                // 为每个线条窗体设置鼠标穿透
                lineForm.HandleCreated += (s, args) => {
                    if (s is Form form)
                    {
                        SetClickThrough(form);
                    }
                };
                
                screenLines[screen] = lineForm;
                lineForm.Show();
            }
            // 3) 立刻把当前配置应用到所有窗体，确保首次 ShowLine() 就正常
            ApplyAllSettings();
        }

        /// <summary>
        /// 将当前 lineHeight、lineColor、lineOpacity 应用到所有窗体
        /// </summary>
        private void ApplyAllSettings()
        {
            ChangeLineThickness(lineHeight);
            ChangeLineColor(lineColor);
            ChangeLineTransparency(lineOpacity);
        }

        /// <summary>
        /// 显示线条
        /// </summary>
        private void ShowLine()
        {
            // 停止任何正在进行的淡出效果
            fadeTimer.Stop();

            // 获取鼠标当前位置和所在屏幕
            Point mousePos = Cursor.Position;
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
                    
                    // 计算线条应该出现的位置 (保持Y坐标相对一致)
                    int lineY = screen.Bounds.Y + relativeY;
                    // 确保线条不超出屏幕边界
                    if (lineY < screen.Bounds.Y) lineY = screen.Bounds.Y;
                    if (lineY > screen.Bounds.Bottom - lineHeight) lineY = screen.Bounds.Bottom - lineHeight;
                    
                    // 设置线条位置和可见性
                    form.Location = new Point(screen.Bounds.X, lineY);
                    form.Opacity = currentOpacity;
                    
                    // 确保窗体是可见的
                    if (!form.Visible) form.Show();
                    form.BringToFront();
                }
            }
            else
            {
                // 只在鼠标所在的屏幕显示线条
                if (screenLines.ContainsKey(currentScreen))
                {
                    var form = screenLines[currentScreen];
                    form.Location = new Point(currentScreen.Bounds.X, mousePos.Y);
                    form.Opacity = currentOpacity;
                    
                    // 确保窗体是可见的
                    if (!form.Visible) form.Show();
                    form.BringToFront();
                }
            }

            // 重置opacity状态并启动淡出计时器
            this.opacity = (float)currentOpacity;
            fadeTimer.Start();
        }

        /// <summary>
        /// 淡出效果定时器回调
        /// </summary>
        private void FadeTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                // 根据显示时长计算每次减少的不透明度
                float fadeStep = (float)(0.02 * (2.0 / displayDuration));
                opacity -= fadeStep;

                if (opacity <= 0)
                {
                    // 淡出完成，停止定时器并隐藏所有线条
                    fadeTimer.Stop();
                    foreach (var form in screenLines.Values)
                    {
                        form.Opacity = 0;
                    }
                    opacity = 0; // 确保重置opacity
                }
                else
                {
                    // 更新所有可见线条的不透明度
                    foreach (var form in screenLines.Values)
                    {
                        if (form.Opacity > 0)
                        {
                            form.Opacity = opacity;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 如果发生异常，重置状态
                fadeTimer.Stop();
                foreach (var form in screenLines.Values)
                {
                    form.Opacity = 0;
                }
                opacity = 0;
            }
        }

        /// <summary>
        /// 切换所有持续线条的可见性
        /// </summary>
        private void ToggleAllPersistentLines()
        {
            allPersistentLinesVisible = !allPersistentLinesVisible;
            
            // 控制竖线的显示/隐藏
            if (verticalLineForm != null)
            {
                if (allPersistentLinesVisible)
                {
                    verticalLineForm.ShowAllLines();
                }
                else
                {
                    verticalLineForm.HideAllLines();
                }
            }
            
            // 控制横线的显示/隐藏
            if (horizontalLineForm != null)
            {
                if (allPersistentLinesVisible)
                {
                    horizontalLineForm.ShowAllLines();
                }
                else
                {
                    horizontalLineForm.HideAllLines();
                }
            }
            
            // 更新菜单项文本
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
                    (menuItem.Text == "隐藏全部持续线条" || menuItem.Text == "显示全部持续线条"))
                {
                    menuItem.Text = allPersistentLinesVisible ? "隐藏全部持续线条" : "显示全部持续线条";
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
            DialogResult result = MessageBox.Show(
                "确定要关闭所有持续线条吗？\n\n这将关闭所有竖线和横线。",
                "确认关闭",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2  // 默认选择"否"
            );
            
            if (result == DialogResult.Yes)
            {
                try
                {
                    // 关闭所有竖线
                    if (verticalLineForm != null)
                    {
                        verticalLineForm.CloseAllLines();
                    }
                    
                    // 关闭所有横线
                    if (horizontalLineForm != null)
                    {
                        horizontalLineForm.CloseAllLines();
                    }
                    
                    // 更新全局显示状态
                    allPersistentLinesVisible = true;
                    UpdateToggleMenuText();
                    
                    MessageBox.Show("所有线条已关闭。", "操作完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"关闭线条时发生错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>
        /// 重新置顶所有线条
        /// </summary>
        private void BringAllLinesToTop()
        {
            try
            {
                // 重新置顶所有竖线
                if (verticalLineForm != null)
                {
                    verticalLineForm.BringAllLinesToTop();
                }
                
                // 重新置顶所有横线
                if (horizontalLineForm != null)
                {
                    horizontalLineForm.BringAllLinesToTop();
                }
                
                // 重新置顶瞬时横线窗体
                foreach (var form in screenLines.Values)
                {
                    if (form != null && !form.IsDisposed)
                    {
                        form.TopMost = false;
                        form.TopMost = true;
                        form.BringToFront();
                    }
                }
                
                MessageBox.Show("所有线条已重新置顶。", "操作完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"置顶线条时发生错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 重启应用程序
        /// </summary>
        private void RestartApplication()
        {
            try
            {
                // 获取当前应用程序的路径
                string currentPath = Application.ExecutablePath;
                
                // 创建新进程的启动信息，传递重启参数
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = currentPath,
                    Arguments = "--restart",  // 传递重启参数
                    UseShellExecute = true
                };
                
                // 启动新的应用程序实例
                System.Diagnostics.Process.Start(startInfo);
                
                // 释放互斥体以允许新实例运行
                Program.ReleaseSingleInstanceMutex();
                
                // 清理托盘图标
                if (trayIcon != null)
                {
                    trayIcon.Visible = false;
                    trayIcon.Dispose();
                }
                
                // 直接强制退出，避免触发复杂的清理过程
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"重启程序时发生错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 切换持续置顶功能
        /// </summary>
        private void TogglePersistentTopmost()
        {
            persistentTopmost = !persistentTopmost;
            
            // 启动或停止持续置顶监听机制
            if (persistentTopmost)
            {
                // 立即执行一次置顶操作，确保当前线条处于置顶状态
                PerformTopmostOperation();
                
                // 然后启动监听机制
                StartTopmostMonitoring();
            }
            else
            {
                StopTopmostMonitoring();
            }
            
            // 更新菜单项选中状态
            UpdatePersistentTopmostMenuCheckedState();
            SaveConfig();
        }
        
        /// <summary>
        /// 启动置顶监听机制
        /// </summary>
        private void StartTopmostMonitoring()
        {
            StopTopmostMonitoring(); // 先停止之前的监听
            
            if (currentTopmostStrategy == TopmostStrategy.ForceTimer)
            {
                // 验证定时器间隔，确保大于0
                if (currentTimerInterval <= 0)
                {
                    currentTimerInterval = 100; // 使用默认值
                }
                
                // 启动暴力定时器
                topmostTimer.Interval = currentTimerInterval;
                topmostTimer.Start();
            }
            else if (currentTopmostStrategy == TopmostStrategy.SmartMonitor)
            {
                // 启动智能监听
                hWinEventHook = SetWinEventHook(
                    EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero, winEventDelegate,
                    0, 0, WINEVENT_OUTOFCONTEXT);
            }
        }
        
        /// <summary>
        /// 停止置顶监听机制
        /// </summary>
        private void StopTopmostMonitoring()
        {
            // 停止定时器
            topmostTimer.Stop();
            
            // 停止Windows事件监听
            if (hWinEventHook != IntPtr.Zero)
            {
                UnhookWinEvent(hWinEventHook);
                hWinEventHook = IntPtr.Zero;
            }
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
            
            UpdateTopmostStrategyMenuCheckedState();
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
                topmostTimer.Stop();
                topmostTimer.Interval = interval;
                topmostTimer.Start();
            }
            
            UpdateTimerIntervalMenuCheckedState();
            SaveConfig();
        }
        
        /// <summary>
        /// 管理自定义监控程序
        /// </summary>
        private void ManageCustomApps()
        {
            try
            {
                using (var managementWindow = new MonitoredAppsWindow(monitoredApplications))
                {
                    if (managementWindow.ShowDialog() == DialogResult.OK && managementWindow.DialogResultOK)
                    {
                        // 更新监控应用程序列表
                        monitoredApplications.Clear();
                        monitoredApplications.AddRange(managementWindow.MonitoredApps);
                        
                        // 更新菜单状态并保存配置
                        SaveConfig();
                        
                        MessageBox.Show($"监控程序配置已更新。当前监控 {monitoredApplications.Count} 个程序。", 
                            "配置已保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开管理窗口时发生错误：{ex.Message}", "错误", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        /// <summary>
        /// Windows事件处理程序
        /// </summary>
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
                        // 检测到监控的应用程序置顶，立即重新置顶我们的线条
                        Task.Run(() =>
                        {
                            // 稍微延迟一下再执行置顶操作，确保目标程序完全置顶后再抢夺
                            Thread.Sleep(50);
                            
                            this.BeginInvoke(new Action(() =>
                            {
                                PerformTopmostOperation();
                            }));
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
                // 检查是否有菜单正在显示，如果有则跳过这次置顶
                if (IsMenuVisible()) return;

                // 强力抢夺所有瞬时横线窗体的置顶权
                foreach (var form in screenLines.Values)
                {
                    if (form != null && !form.IsDisposed && form.Visible && form.Opacity > 0 && form.Handle != IntPtr.Zero)
                    {
                        // 强制重新设置置顶状态，抢夺置顶权
                        form.TopMost = false;  // 先取消置顶
                        form.TopMost = true;   // 再重新置顶，抢夺置顶权
                        
                        // 使用Windows API强制置顶并显示
                        SetWindowPos(form.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                        BringWindowToTop(form.Handle);
                        SetForegroundWindow(form.Handle);
                    }
                }

                // 强力抢夺竖线的置顶权
                if (verticalLineForm != null)
                {
                    verticalLineForm.EnsureTopmost();
                }

                // 强力抢夺横线的置顶权
                if (horizontalLineForm != null)
                {
                    horizontalLineForm.EnsureTopmost();
                }
            }
            catch (Exception)
            {
                // 忽略置顶过程中的异常
            }
        }

        /// <summary>
        /// 持续置顶定时器回调 - 用于与其他置顶程序抢夺置顶权
        /// </summary>
        private void TopmostTimer_Tick(object sender, EventArgs e)
        {
            if (!persistentTopmost || currentTopmostStrategy != TopmostStrategy.ForceTimer) return;
            
            PerformTopmostOperation();
        }
        
        /// <summary>
        /// 更新置顶策略菜单的选中状态
        /// </summary>
        private void UpdateTopmostStrategyMenuCheckedState()
        {
            if (trayIcon?.ContextMenuStrip == null) return;

            foreach (ToolStripItem item in trayIcon.ContextMenuStrip.Items)
            {
                if (item is ToolStripMenuItem strategyMenu && strategyMenu.Text == "置顶策略配置")
                {
                    var strategyTypeMenu = strategyMenu.DropDownItems[0] as ToolStripMenuItem;
                    if (strategyTypeMenu != null)
                    {
                        foreach (ToolStripItem subItem in strategyTypeMenu.DropDownItems)
                        {
                            if (subItem is ToolStripMenuItem menuItem)
                            {
                                if (menuItem.Text == "暴力定时器")
                                    menuItem.Checked = (currentTopmostStrategy == TopmostStrategy.ForceTimer);
                                else if (menuItem.Text == "智能监听")
                                    menuItem.Checked = (currentTopmostStrategy == TopmostStrategy.SmartMonitor);
                            }
                        }
                    }
                    break;
                }
            }
        }
        
        /// <summary>
        /// 更新定时器间隔菜单的选中状态
        /// </summary>
        private void UpdateTimerIntervalMenuCheckedState()
        {
            if (trayIcon?.ContextMenuStrip == null) return;

            foreach (ToolStripItem item in trayIcon.ContextMenuStrip.Items)
            {
                if (item is ToolStripMenuItem strategyMenu && strategyMenu.Text == "置顶策略配置")
                {
                    var timerIntervalMenu = strategyMenu.DropDownItems[1] as ToolStripMenuItem;
                    if (timerIntervalMenu != null)
                    {
                        foreach (ToolStripItem subItem in timerIntervalMenu.DropDownItems)
                        {
                            if (subItem is ToolStripMenuItem menuItem)
                            {
                                // 从菜单文本中提取间隔值进行匹配
                                string text = menuItem.Text;
                                bool isMatch = false;
                                
                                if (text.Contains("毫秒"))
                                {
                                    string numStr = text.Split('毫')[0];
                                    if (int.TryParse(numStr, out int interval))
                                    {
                                        isMatch = (interval == currentTimerInterval);
                                    }
                                }
                                else if (text.Contains("秒"))
                                {
                                    string numStr = text.Split('秒')[0];
                                    if (int.TryParse(numStr, out int seconds))
                                    {
                                        isMatch = (seconds * 1000 == currentTimerInterval);
                                    }
                                }
                                
                                menuItem.Checked = isMatch;
                            }
                        }
                    }
                    break;
                }
            }
        }
        
        /// <summary>
        /// 更新持续置顶功能菜单的选中状态
        /// </summary>
        private void UpdatePersistentTopmostMenuCheckedState()
        {
            if (trayIcon?.ContextMenuStrip == null) return;

            foreach (ToolStripItem item in trayIcon.ContextMenuStrip.Items)
            {
                if (item is ToolStripMenuItem menuItem && menuItem.Text == "持续保证置顶")
                {
                    menuItem.Checked = persistentTopmost;
                    break;
                }
            }
        }

        /// <summary>
        /// 检查是否有菜单正在显示
        /// </summary>
        private bool IsMenuVisible()
        {
            try
            {
                // 检查托盘菜单是否可见
                if (trayIcon?.ContextMenuStrip != null && trayIcon.ContextMenuStrip.Visible)
                {
                    return true;
                }
                
                // 检查是否有其他下拉菜单打开
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 窗体关闭时的清理工作
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 停止置顶监听机制
            StopTopmostMonitoring();
            
            // 注销热键
            UnregisterHotKey(this.Handle, currentHotKeyId);
            
            // 使用更安全的清理方法
            CleanupAndExit();
            base.OnFormClosing(e);
        }

        /// <summary>
        /// 添加一个重置方法
        /// </summary>
        private void ResetLineState()
        {
            fadeTimer.Stop();
            opacity = 0;
            foreach (var form in screenLines.Values)
            {
                form.Opacity = 0;
                if (!form.IsDisposed && !form.Visible)
                {
                    form.Show();
                }
            }
        }

        /// <summary>
        /// 处理窗口消息
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            // 处理热键消息
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == currentHotKeyId)
            {
                // 在显示线条前先重置状态
                ResetLineState();
                ShowLine();
            }
            base.WndProc(ref m);
        }
    }
}
