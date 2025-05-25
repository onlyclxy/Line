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

        [STAThread]
        static void Main()
        {
            // 检查是否已经有一个实例在运行
            if (!SingleInstanceMutex.WaitOne(TimeSpan.Zero, true))
            {
                MessageBox.Show("程序已经在运行中！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return; // 退出本次运行
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
                // 在应用程序结束时释放互斥体
                SingleInstanceMutex.ReleaseMutex();
            }
        }
    }

    public class LineForm : Form
    {
        private System.Windows.Forms.Timer fadeTimer;
        private float opacity = 1.0f;
        private const int WM_HOTKEY = 0x0312;
        private int currentHotKeyId = 1;  // 添加热键ID
        private NotifyIcon trayIcon;
        private bool showOnAllScreens = false;
        private Dictionary<Screen, Form> screenLines = new Dictionary<Screen, Form>();
        private VerticalLineForm verticalLineForm;  // 添加竖线窗体实例
        
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
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

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

            // 设置定时器用于淡出效果
            fadeTimer = new System.Windows.Forms.Timer();
            fadeTimer.Interval = 50;  // 50毫秒更新一次
            fadeTimer.Tick += FadeTimer_Tick;

            // 注册默认热键(F5)
            RegisterCurrentHotKey();
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
                    HotKey = currentHotKey
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
                
                // ===== 显示模式菜单 =====
                ToolStripMenuItem displayModeItem = new ToolStripMenuItem("显示模式");
                var currentScreenItem = new ToolStripMenuItem("仅鼠标所在屏幕", null, (s, e) => {
                    showOnAllScreens = false;
                    SaveConfig();
                });
                currentScreenItem.Checked = true;
                
                var allScreensItem = new ToolStripMenuItem("所有屏幕", null, (s, e) => {
                    showOnAllScreens = true;
                    SaveConfig();
                });
                
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
                
                // ===== 线条粗细菜单 =====
                ToolStripMenuItem lineThicknessItem = new ToolStripMenuItem("线条粗细");
                
                // 添加不同粗细的选项
                var thinLine = new ToolStripMenuItem("细线 (1像素)", null, (s, e) => {
                    ChangeLineThickness(1);
                });
                thinLine.Checked = true;  // 默认选中细线
                
                var mediumLine = new ToolStripMenuItem("中等 (2像素)", null, (s, e) => {
                    ChangeLineThickness(2);
                });
                
                var thickLine = new ToolStripMenuItem("粗线 (3像素)", null, (s, e) => {
                    ChangeLineThickness(3);
                });
                
                var veryThickLine = new ToolStripMenuItem("很粗 (5像素)", null, (s, e) => {
                    ChangeLineThickness(5);
                });
                
                lineThicknessItem.DropDownItems.Add(thinLine);
                lineThicknessItem.DropDownItems.Add(mediumLine);
                lineThicknessItem.DropDownItems.Add(thickLine);
                lineThicknessItem.DropDownItems.Add(veryThickLine);
                
                // ===== 线条颜色菜单 =====
                ToolStripMenuItem lineColorItem = new ToolStripMenuItem("线条颜色");
                
                // 创建常用颜色选项
                AddColorMenuItem(lineColorItem, "红色", Color.Red);
                AddColorMenuItem(lineColorItem, "绿色", Color.Green);
                AddColorMenuItem(lineColorItem, "蓝色", Color.Blue);
                AddColorMenuItem(lineColorItem, "黄色", Color.Yellow);
                AddColorMenuItem(lineColorItem, "橙色", Color.Orange);
                AddColorMenuItem(lineColorItem, "紫色", Color.Purple);
                AddColorMenuItem(lineColorItem, "青色", Color.Cyan);
                AddColorMenuItem(lineColorItem, "黑色", Color.FromArgb(1, 1, 1)); // 接近黑色但不是完全黑色
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
                
                // ===== 透明度菜单 =====
                ToolStripMenuItem transparencyItem = new ToolStripMenuItem("线条透明度");
                
                // 创建不同透明度选项
                AddTransparencyMenuItem(transparencyItem, "100% (不透明)", 100);
                AddTransparencyMenuItem(transparencyItem, "75%", 75);
                AddTransparencyMenuItem(transparencyItem, "50%", 50);
                AddTransparencyMenuItem(transparencyItem, "25%", 25);
                
                // ===== 显示时长菜单 =====
                ToolStripMenuItem durationItem = new ToolStripMenuItem("显示时长");
                
                // 创建不同时长选项
                var durations = new[] { 0.1, 0.2, 0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 5.0 };
                foreach (double duration in durations)
                {
                    string text = duration.ToString("0.#") + "秒";
                    if (duration == 1.5) text += " (默认)";
                    
                    var item = new ToolStripMenuItem(text, null, (s, e) => {
                        ChangeDisplayDuration(duration);
                    });
                    item.Checked = Math.Abs(duration - displayDuration) < 0.01;
                    durationItem.DropDownItems.Add(item);
                }

                // ===== 热键设置菜单 =====
                ToolStripMenuItem hotKeyItem = new ToolStripMenuItem("热键设置");
                
                // 添加F3-F12热键选项
                for (int i = 3; i <= 12; i++)
                {
                    Keys key = Keys.F1 + (i - 1);
                    var keyItem = new ToolStripMenuItem($"F{i}", null, (s, e) => {
                        ChangeHotKey(key);
                    });
                    
                    // 设置默认热键F5的选中状态
                    keyItem.Checked = (key == currentHotKey);
                    
                    hotKeyItem.DropDownItems.Add(keyItem);
                }

                // 退出选项
                ToolStripMenuItem exitItem = new ToolStripMenuItem("退出", null, (s, e) => {
                    CleanupAndExit();
                });

                // 将所有项目添加到菜单
                contextMenu.Items.Add(displayModeItem);
                contextMenu.Items.Add(lineThicknessItem);
                contextMenu.Items.Add(lineColorItem);
                contextMenu.Items.Add(transparencyItem);
                contextMenu.Items.Add(durationItem);
                contextMenu.Items.Add(hotKeyItem);  // 使用新的变量名
                contextMenu.Items.Add(new ToolStripSeparator());
                contextMenu.Items.Add(exitItem);

                trayIcon.ContextMenuStrip = contextMenu;
                
                // 设置双击托盘图标的行为（显示线条）
                trayIcon.DoubleClick += (s, e) => ShowLine();
            }
            catch (Exception ex)
            {
                MessageBox.Show("初始化托盘图标失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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

                // 先隐藏托盘图标
                if (trayIcon != null)
                {
                    trayIcon.Visible = false;
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
            
            // 安全获取菜单项
            if (trayIcon?.ContextMenuStrip?.Items.Count > 1)
            {
                var thicknessMenu = trayIcon.ContextMenuStrip.Items[1] as ToolStripMenuItem;
                if (thicknessMenu != null)
                {
                    // 更新菜单项选中状态
                    foreach (var item in thicknessMenu.DropDownItems)
                    {
                        if (item is ToolStripMenuItem menuItem)
                        {
                            // 根据名称确定是否应该被选中
                            menuItem.Checked = menuItem.Text.Contains(thickness.ToString());
                        }
                    }
                }
            }
            SaveConfig();
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
            
            // 安全获取颜色菜单
            if (trayIcon?.ContextMenuStrip?.Items.Count > 2)
            {
                var colorMenu = trayIcon.ContextMenuStrip.Items[2] as ToolStripMenuItem;
                if (colorMenu != null)
                {
                    // 更新菜单项选中状态，安全地处理不同类型的菜单项
                    foreach (var item in colorMenu.DropDownItems)
                    {
                        // 跳过分隔符
                        if (item is ToolStripSeparator) continue;
                        
                        // 转换为菜单项
                        if (item is ToolStripMenuItem menuItem)
                        {
                            // 跳过自定义颜色选项
                            if (menuItem.Text == "自定义颜色...") continue;
                            
                            // 检查颜色菜单项的图像
                            if (menuItem.Image != null && menuItem.Image is Bitmap bmp)
                            {
                                try
                                {
                                    Color menuColor = bmp.GetPixel(8, 8); // 取中心点颜色
                                    menuItem.Checked = ColorEquals(menuColor, lineColor);
                                }
                                catch
                                {
                                    menuItem.Checked = false;
                                }
                            }
                            else
                            {
                                menuItem.Checked = false;
                            }
                        }
                    }
                }
            }
            SaveConfig();
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
            
            // 安全获取透明度菜单
            if (trayIcon?.ContextMenuStrip?.Items.Count > 3)
            {
                var transparencyMenu = trayIcon.ContextMenuStrip.Items[3] as ToolStripMenuItem;
                if (transparencyMenu != null)
                {
                    // 更新菜单项选中状态
                    foreach (var item in transparencyMenu.DropDownItems)
                    {
                        if (item is ToolStripMenuItem menuItem)
                        {
                            string itemText = menuItem.Text;
                            menuItem.Checked = itemText.Contains(lineOpacity.ToString() + "%");
                        }
                    }
                }
            }
            SaveConfig();
        }

        /// <summary>
        /// 更改显示时长
        /// </summary>
        private void ChangeDisplayDuration(double duration)
        {
            displayDuration = duration;
            
            // 更新菜单项选中状态
            if (trayIcon?.ContextMenuStrip?.Items.Count > 4)
            {
                var durationMenu = trayIcon.ContextMenuStrip.Items[4] as ToolStripMenuItem;
                if (durationMenu != null)
                {
                    foreach (ToolStripMenuItem item in durationMenu.DropDownItems)
                    {
                        // 从菜单项文本中提取秒数
                        string itemText = item.Text;
                        int secondsIndex = itemText.IndexOf("秒");
                        if (secondsIndex > 0)
                        {
                            string durationStr = itemText.Substring(0, secondsIndex);
                            if (double.TryParse(durationStr, out double itemDuration))
                            {
                                // 使用更精确的比较
                                item.Checked = Math.Abs(itemDuration - duration) < 0.01;
                            }
                        }
                    }
                }
            }
            SaveConfig();
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
            if (trayIcon?.ContextMenuStrip?.Items.Count > 5)
            {
                var hotKeyMenu = trayIcon.ContextMenuStrip.Items[5] as ToolStripMenuItem;
                if (hotKeyMenu != null)
                {
                    foreach (var item in hotKeyMenu.DropDownItems)
                    {
                        if (item is ToolStripMenuItem menuItem)
                        {
                            string keyName = $"F{(int)currentHotKey - (int)Keys.F1 + 1}";
                            menuItem.Checked = menuItem.Text.Contains(keyName);
                        }
                    }
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

        /// <summary>
        /// 窗体关闭时的清理工作
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 注销热键
            UnregisterHotKey(this.Handle, currentHotKeyId);
            
            // 使用更安全的清理方法
            CleanupAndExit();
            base.OnFormClosing(e);
        }
    }
}
