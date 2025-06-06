using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Reflection;
using System.Linq;

namespace Line
{
    // 可拖拽的横线类
    public class DraggableHorizontalLine : Form
    {
        private bool isDragging = false;
        private Point lastCursor;
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

        public DraggableHorizontalLine(int width, int height, Color color, double opacity, bool clickThrough)
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.BackColor = color;
            this.TransparencyKey = Color.Black;
            this.Opacity = opacity;
            this.Width = width;
            this.Height = height;
            this.AutoScaleMode = AutoScaleMode.None;
            this.StartPosition = FormStartPosition.Manual;
            
            mouseClickThrough = clickThrough;
            
            // 如果不是鼠标穿透模式，添加拖拽事件和设置光标
            if (!mouseClickThrough)
            {
                this.MouseDown += OnMouseDown;
                this.MouseMove += OnMouseMove;
                this.MouseUp += OnMouseUp;
                this.Cursor = Cursors.SizeNS; // 设置为垂直调整大小光标
            }
        }

        // 重写CreateParams，在创建窗口时就设置所有必要的扩展样式
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // 分层 + 点透 + 不激活
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // 强制保证都加上去
            int ex = GetWindowLong(this.Handle, GWL_EXSTYLE);
            ex |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
            SetWindowLong(this.Handle, GWL_EXSTYLE, ex);
        }

        public void SetClickThrough(bool enable)
        {
            mouseClickThrough = enable;
            if (this.Handle != IntPtr.Zero)
            {
                if (enable)
                {
                    // 启用穿透模式：移除拖拽事件，不设置光标
                    this.MouseDown -= OnMouseDown;
                    this.MouseMove -= OnMouseMove;
                    this.MouseUp -= OnMouseUp;
                    // 不再设置 this.Cursor，让系统决定光标形状
                }
                else
                {
                    // 禁用穿透模式：移除透明样式，添加拖拽事件
                    int exStyle = GetWindowLong(this.Handle, GWL_EXSTYLE);
                    SetWindowLong(this.Handle, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
                    this.Cursor = Cursors.SizeNS;
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

        // 拦截窗口消息，解决光标和激活问题
        protected override void WndProc(ref Message m)
        {
            if (mouseClickThrough)
            {
                if (m.Msg == WM_MOUSEACTIVATE)
                {
                    // 不激活自己，直接交给下面窗口
                    m.Result = new IntPtr(MA_NOACTIVATE);
                    return;
                }
                if (m.Msg == WM_SETCURSOR)
                {
                    // 不处理，让系统去给下面窗口设光标
                    return;
                }
            }
            base.WndProc(ref m);
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isDragging = true;
                lastCursor = Cursor.Position;
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (isDragging)
            {
                Point currentCursor = Cursor.Position;
                int deltaY = currentCursor.Y - lastCursor.Y;
                
                this.Location = new Point(this.Location.X, this.Location.Y + deltaY);
                lastCursor = currentCursor;
            }
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isDragging = false;
            }
        }
    }

    public class HorizontalLineForm : Form
    {
        private const int WM_HOTKEY = 0x0312;
        private Dictionary<int, List<DraggableHorizontalLine>> horizontalLines = new Dictionary<int, List<DraggableHorizontalLine>>();
        private Dictionary<int, bool> lineStates = new Dictionary<int, bool>();
        private NotifyIcon trayIcon;

        // 线条默认高度为1像素
        private int lineHeight = 1;

        // 线条颜色，默认为绿色
        private Color lineColor = Color.Green;

        // 线条透明度，默认为100%
        private int lineOpacity = 100;

        // 鼠标穿透设置
        private bool mouseClickThrough = true;

        // 显示模式：false=仅当前屏幕，true=全部屏幕
        private bool showOnAllScreens = false;

        // 热键ID基础值（1-4用于开启，5-8用于关闭）
        private const int BASE_HOTKEY_ID_ON = 300;
        private const int BASE_HOTKEY_ID_OFF = 400;

        // 热键绑定状态
        private bool[] hotkeyEnabled = new bool[] { true, true, false, false }; // 默认启用前两组

        // 用于处理初始显示的标志
        private bool isFirstShow = true;

        // 修改配置文件路径
        private readonly string configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ScreenLine",
            "horizontal_config.json"
        );

        // 配置类
        private class Config
        {
            public bool[] HotkeyEnabled { get; set; }
            public int LineHeight { get; set; }
            public string LineColor { get; set; }
            public int LineOpacity { get; set; }
            public bool MouseClickThrough { get; set; }
            public bool ShowOnAllScreens { get; set; }
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

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

        // 修饰键常量
        private const int MOD_CONTROL = 0x0002;
        private const int MOD_SHIFT = 0x0004;

        public HorizontalLineForm(NotifyIcon existingTrayIcon)
        {
            this.AutoScaleMode = AutoScaleMode.None;
            this.trayIcon = existingTrayIcon;

            // 加载配置
            LoadConfig();

            InitializeComponent();
            InitializeHotkeys();
            AddHorizontalLineMenuItems();
            
            // 设置初始鼠标穿透状态
            SetClickThrough(mouseClickThrough);

            // 显示一次初始横线，然后立即隐藏
            ShowInitialLine();
        }

        private void ShowInitialLine()
        {
            if (isFirstShow)
            {
                // 创建并显示一个临时的横线
                DraggableHorizontalLine tempLine = new DraggableHorizontalLine(
                    Screen.PrimaryScreen.Bounds.Width,
                    lineHeight,
                    lineColor,
                    lineOpacity / 100.0,
                    mouseClickThrough
                );

                tempLine.Show();
                tempLine.Close();
                isFirstShow = false;
            }

            // 隐藏主窗体
            this.Opacity = 0;
        }

        private void InitializeComponent()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.BackColor = lineColor;
            this.TransparencyKey = Color.Black;
            this.AutoScaleMode = AutoScaleMode.None;
            this.Width = Screen.PrimaryScreen.Bounds.Width;
            this.Height = lineHeight;
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
                    Keys key = Keys.D1 + index; // 数字键1-4
                    // 注册 Ctrl+1-4
                    bool onSuccess = RegisterHotKey(this.Handle, BASE_HOTKEY_ID_ON + index, MOD_CONTROL, (int)key);
                    // 注册 Ctrl+Shift+1-4
                    bool offSuccess = RegisterHotKey(this.Handle, BASE_HOTKEY_ID_OFF + index, MOD_CONTROL | MOD_SHIFT, (int)key);

                    // 如果注册失败，更新状态并保存配置
                    if (!onSuccess || !offSuccess)
                    {
                        hotkeyEnabled[index] = false;
                        UpdateHotkeyMenuCheckedState();
                        SaveConfig();
                        MessageBox.Show($"热键 Ctrl+{index + 1} 注册失败，可能已被其他程序占用。", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                hotkeyEnabled[index] = false;
                UpdateHotkeyMenuCheckedState();
                SaveConfig();
                MessageBox.Show($"注册热键时发生错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UnregisterHotkeyPair(int index)
        {
            if (index >= 0 && index < 4)
            {
                UnregisterHotKey(this.Handle, BASE_HOTKEY_ID_ON + index);
                UnregisterHotKey(this.Handle, BASE_HOTKEY_ID_OFF + index);
            }
        }

        private void SetClickThrough(bool enable)
        {
            mouseClickThrough = enable;
            foreach (var linesList in horizontalLines.Values)
            {
                foreach (var line in linesList)
                {
                    line.SetClickThrough(enable);
                }
            }
            SaveConfig();
        }

        private void AddHorizontalLineMenuItems()
        {
            if (trayIcon?.ContextMenuStrip == null) return;

            ToolStripMenuItem horizontalLineMenu = new ToolStripMenuItem("持续横线");

            // 热键绑定菜单
            ToolStripMenuItem hotkeyBindingMenu = new ToolStripMenuItem("热键绑定");
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                var item = new ToolStripMenuItem($"Ctrl+{i + 1}/Ctrl+Shift+{i + 1}", null, (s, e) => {
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
            ToolStripMenuItem lineThicknessItem = new ToolStripMenuItem("横线粗细");
            AddThicknessMenuItem(lineThicknessItem, "细线 (1像素)", 1);
            AddThicknessMenuItem(lineThicknessItem, "中等 (2像素)", 2);
            AddThicknessMenuItem(lineThicknessItem, "粗线 (3像素)", 3);
            AddThicknessMenuItem(lineThicknessItem, "很粗 (5像素)", 5);

            // 线条颜色菜单
            ToolStripMenuItem lineColorItem = new ToolStripMenuItem("横线颜色");
            AddColorMenuItem(lineColorItem, "红色", Color.Red);
            AddColorMenuItem(lineColorItem, "绿色", Color.Green);
            AddColorMenuItem(lineColorItem, "蓝色", Color.Blue);
            AddColorMenuItem(lineColorItem, "黄色", Color.Yellow);
            AddColorMenuItem(lineColorItem, "橙色", Color.Orange);
            AddColorMenuItem(lineColorItem, "紫色", Color.Purple);
            AddColorMenuItem(lineColorItem, "青色", Color.Cyan);
            AddColorMenuItem(lineColorItem, "黑色", Color.FromArgb(1, 1, 1));
            AddColorMenuItem(lineColorItem, "白色", Color.White);

            // 透明度菜单
            ToolStripMenuItem transparencyItem = new ToolStripMenuItem("横线透明度");
            AddTransparencyMenuItem(transparencyItem, "100% (不透明)", 100);
            AddTransparencyMenuItem(transparencyItem, "75%", 75);
            AddTransparencyMenuItem(transparencyItem, "50%", 50);
            AddTransparencyMenuItem(transparencyItem, "25%", 25);

            // 添加所有子菜单
            horizontalLineMenu.DropDownItems.Add(hotkeyBindingMenu);
            horizontalLineMenu.DropDownItems.Add(displayModeMenu);
            horizontalLineMenu.DropDownItems.Add(mousePenetrationItem);
            horizontalLineMenu.DropDownItems.Add(lineThicknessItem);
            horizontalLineMenu.DropDownItems.Add(lineColorItem);
            horizontalLineMenu.DropDownItems.Add(transparencyItem);

            // 在竖线菜单之后插入横线菜单
            int insertIndex = -1;
            for (int i = 0; i < trayIcon.ContextMenuStrip.Items.Count; i++)
            {
                if (trayIcon.ContextMenuStrip.Items[i] is ToolStripMenuItem menuItem && 
                    menuItem.Text == "竖线设置")
                {
                    insertIndex = i + 1;
                    break;
                }
            }

            if (insertIndex != -1)
            {
                trayIcon.ContextMenuStrip.Items.Insert(insertIndex, horizontalLineMenu);
            }
            else
            {
                // 如果找不到竖线菜单，就在分隔符前插入
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
                    trayIcon.ContextMenuStrip.Items.Insert(separatorIndex, horizontalLineMenu);
                }
                else
                {
                    trayIcon.ContextMenuStrip.Items.Add(horizontalLineMenu);
                }
            }
        }

        private void AddThicknessMenuItem(ToolStripMenuItem parent, string text, int thickness)
        {
            var item = new ToolStripMenuItem(text, null, (s, e) => {
                ChangeLineThickness(thickness);
            });
            item.Checked = (thickness == lineHeight);
            parent.DropDownItems.Add(item);
        }

        private void AddColorMenuItem(ToolStripMenuItem parent, string name, Color color)
        {
            Bitmap colorPreview = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(colorPreview))
            {
                g.FillRectangle(new SolidBrush(color), 0, 0, 16, 16);
                g.DrawRectangle(Pens.Gray, 0, 0, 15, 15);
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
            if (index >= 0 && index < 4)
            {
                bool newState = !hotkeyEnabled[index];
                
                if (newState)
                {
                    // 尝试注册热键
                    try
                    {
                        Keys key = Keys.D1 + index;
                        bool onSuccess = RegisterHotKey(this.Handle, BASE_HOTKEY_ID_ON + index, MOD_CONTROL, (int)key);
                        bool offSuccess = RegisterHotKey(this.Handle, BASE_HOTKEY_ID_OFF + index, MOD_CONTROL | MOD_SHIFT, (int)key);

                        if (onSuccess && offSuccess)
                        {
                            hotkeyEnabled[index] = true;
                        }
                        else
                        {
                            MessageBox.Show($"热键 Ctrl+{index + 1} 注册失败，可能已被其他程序占用。", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"注册热键时发生错误: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else
                {
                    // 注销热键
                    UnregisterHotkeyPair(index);
                    hotkeyEnabled[index] = false;
                    
                    // 如果有对应的横线，则移除它
                    if (horizontalLines.ContainsKey(index))
                    {
                        foreach (var line in horizontalLines[index])
                        {
                            line.Close();
                        }
                        horizontalLines.Remove(index);
                        lineStates[index] = false;
                    }
                }
                
                UpdateHotkeyMenuCheckedState();
                SaveConfig();
            }
        }

        private void UpdateHotkeyMenuCheckedState()
        {
            if (trayIcon?.ContextMenuStrip == null) return;

            foreach (ToolStripItem item in trayIcon.ContextMenuStrip.Items)
            {
                if (item is ToolStripMenuItem menuItem && menuItem.Text == "持续横线")
                {
                    var hotkeyMenu = menuItem.DropDownItems[0] as ToolStripMenuItem;
                    if (hotkeyMenu != null)
                    {
                        for (int i = 0; i < hotkeyMenu.DropDownItems.Count; i++)
                        {
                            if (hotkeyMenu.DropDownItems[i] is ToolStripMenuItem subItem)
                            {
                                subItem.Checked = hotkeyEnabled[i];
                            }
                        }
                    }
                    break;
                }
            }
        }

        private void ChangeLineThickness(int thickness)
        {
            lineHeight = thickness;
            
            // 更新所有横线的高度
            foreach (var linesList in horizontalLines.Values)
            {
                foreach (var line in linesList)
                {
                    line.Height = lineHeight;
                }
            }
            
            // 更新菜单项选中状态
            UpdateThicknessMenuCheckedState();
            SaveConfig();
        }

        private void ChangeLineColor(Color color)
        {
            lineColor = color;
            
            // 更新所有横线的颜色
            foreach (var linesList in horizontalLines.Values)
            {
                foreach (var line in linesList)
                {
                    line.BackColor = lineColor;
                }
            }
            
            // 更新菜单项选中状态
            UpdateColorMenuCheckedState();
            SaveConfig();
        }

        private void ChangeLineTransparency(int value)
        {
            lineOpacity = value;
            
            // 更新所有横线的透明度
            foreach (var linesList in horizontalLines.Values)
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
            UpdateMenuCheckedState("横线粗细", item => {
                string thicknessStr = lineHeight.ToString();
                return item.Text.Contains(thicknessStr);
            });
        }

        private void UpdateColorMenuCheckedState()
        {
            UpdateMenuCheckedState("横线颜色", item => {
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
            UpdateMenuCheckedState("横线透明度", item => item.Text.Contains(lineOpacity.ToString() + "%"));
        }

        private void UpdateMenuCheckedState(string menuName, Func<ToolStripMenuItem, bool> checkCondition)
        {
            if (trayIcon?.ContextMenuStrip == null) return;

            foreach (ToolStripItem item in trayIcon.ContextMenuStrip.Items)
            {
                if (item is ToolStripMenuItem horizontalMenu && horizontalMenu.Text == "持续横线")
                {
                    foreach (ToolStripItem subItem in horizontalMenu.DropDownItems)
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

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int hotkeyId = m.WParam.ToInt32();
                
                // 处理开启热键 (Ctrl+1-4)
                if (hotkeyId >= BASE_HOTKEY_ID_ON && hotkeyId < BASE_HOTKEY_ID_ON + 4)
                {
                    int lineIndex = hotkeyId - BASE_HOTKEY_ID_ON;
                    if (hotkeyEnabled[lineIndex])
                    {
                        ShowHorizontalLine(lineIndex);
                    }
                }
                // 处理关闭热键 (Ctrl+Shift+1-4)
                else if (hotkeyId >= BASE_HOTKEY_ID_OFF && hotkeyId < BASE_HOTKEY_ID_OFF + 4)
                {
                    int lineIndex = hotkeyId - BASE_HOTKEY_ID_OFF;
                    if (hotkeyEnabled[lineIndex])
                    {
                        HideHorizontalLine(lineIndex);
                    }
                }
            }
            base.WndProc(ref m);
        }

        private void ShowHorizontalLine(int index)
        {
            Point mousePos = Cursor.Position;
            
            if (showOnAllScreens)
            {
                // 在所有屏幕显示横线
                Screen mouseScreen = Screen.FromPoint(mousePos);
                int relativeY = mousePos.Y - mouseScreen.Bounds.Y;
                
                // 如果这条线已经存在，先关闭所有相关的线
                if (horizontalLines.ContainsKey(index))
                {
                    foreach (var line in horizontalLines[index])
                    {
                        line.Close();
                    }
                    horizontalLines.Remove(index);
                }
                
                List<DraggableHorizontalLine> firstLines = new List<DraggableHorizontalLine>();
                // 为每个屏幕创建横线
                foreach (Screen screen in Screen.AllScreens)
                {
                    int lineY = screen.Bounds.Y + relativeY;
                    // 确保线条不超出屏幕边界
                    if (lineY < screen.Bounds.Y) lineY = screen.Bounds.Y;
                    if (lineY > screen.Bounds.Bottom - lineHeight) lineY = screen.Bounds.Bottom - lineHeight;
                    
                    DraggableHorizontalLine line = new DraggableHorizontalLine(
                        screen.Bounds.Width,
                        lineHeight,
                        lineColor,
                        lineOpacity / 100.0,
                        mouseClickThrough
                    )
                    {
                        TopMost = true  // 创建时就设置为置顶
                    };

                    line.Location = new Point(screen.Bounds.X, lineY);
                    line.Show();
                    line.Height = lineHeight; // 重置高度以绕过DPI缩放
                    
                    // 强制使用Windows API置顶
                    if (line.Handle != IntPtr.Zero)
                    {
                        SetWindowPos(line.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                        BringWindowToTop(line.Handle);
                    }
                    
                    firstLines.Add(line);
                }
                
                // 保存第一个屏幕的线条引用（用于管理）
                horizontalLines[index] = firstLines;
            }
            else
            {
                // 只在鼠标所在屏幕显示横线
                Screen currentScreen = Screen.FromPoint(mousePos);
                
                // 如果这条线已经存在，就更新它的位置
                if (horizontalLines.ContainsKey(index))
                {
                    foreach (var line in horizontalLines[index])
                    {
                        line.Location = new Point(currentScreen.Bounds.X, mousePos.Y);
                        
                        // 确保现有线条保持置顶
                        if (line.Handle != IntPtr.Zero)
                        {
                            line.TopMost = true;
                            SetWindowPos(line.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                        }
                    }
                }
                else
                {
                    // 创建新的横线 - 单屏幕模式只创建一条线
                    List<DraggableHorizontalLine> newLines = new List<DraggableHorizontalLine>();
                    
                    DraggableHorizontalLine line = new DraggableHorizontalLine(
                        currentScreen.Bounds.Width,
                        lineHeight,
                        lineColor,
                        lineOpacity / 100.0,
                        mouseClickThrough
                    )
                    {
                        TopMost = true  // 创建时就设置为置顶
                    };

                    line.Location = new Point(currentScreen.Bounds.X, mousePos.Y);
                    line.Show();
                    line.Height = lineHeight; // 重置高度以绕过DPI缩放
                    
                    // 强制使用Windows API置顶
                    if (line.Handle != IntPtr.Zero)
                    {
                        SetWindowPos(line.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                        BringWindowToTop(line.Handle);
                    }
                    
                    newLines.Add(line);
                    horizontalLines[index] = newLines;
                }
            }

            lineStates[index] = true;
        }

        private void HideHorizontalLine(int index)
        {
            if (horizontalLines.ContainsKey(index))
            {
                foreach (var line in horizontalLines[index])
                {
                    line.Close();
                }
                horizontalLines.Remove(index);
                lineStates[index] = false;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // 注销所有热键
            for (int i = 0; i < 4; i++)
            {
                if (hotkeyEnabled[i])
                {
                    UnregisterHotkeyPair(i);
                }
            }

            // 关闭所有横线 - 创建副本避免集合修改异常
            var linesToClose = new List<List<DraggableHorizontalLine>>(horizontalLines.Values);
            foreach (var lines in linesToClose)
            {
                foreach (var line in lines)
                {
                    try
                    {
                        if (line != null && !line.IsDisposed)
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
            horizontalLines.Clear();
            lineStates.Clear();

            base.OnFormClosing(e);
        }

        /// <summary>
        /// 显示所有横线
        /// </summary>
        public void ShowAllLines()
        {
            foreach (var lines in horizontalLines.Values)
            {
                foreach (var line in lines)
                {
                    if (!line.Visible)
                    {
                        line.Show();
                    }
                }
            }
        }

        /// <summary>
        /// 隐藏所有横线
        /// </summary>
        public void HideAllLines()
        {
            foreach (var lines in horizontalLines.Values)
            {
                foreach (var line in lines)
                {
                    if (line.Visible)
                    {
                        line.Hide();
                    }
                }
            }
        }

        /// <summary>
        /// 关闭所有横线（彻底移除）
        /// </summary>
        public void CloseAllLines()
        {
            var linesToClose = new List<int>(horizontalLines.Keys);
            foreach (int index in linesToClose)
            {
                if (horizontalLines.ContainsKey(index))
                {
                    foreach (var line in horizontalLines[index])
                    {
                        line.Close();
                    }
                    horizontalLines.Remove(index);
                    lineStates[index] = false;
                }
            }
        }

        /// <summary>
        /// 重新置顶所有横线
        /// </summary>
        public void BringAllLinesToTop()
        {
            foreach (var lines in horizontalLines.Values)
            {
                foreach (var line in lines)
                {
                    if (line != null && !line.IsDisposed)
                    {
                        line.TopMost = false;
                        line.TopMost = true;
                        line.BringToFront();
                    }
                }
            }
        }

        /// <summary>
        /// 确保所有横线保持置顶状态（用于持续置顶功能） - 与其他置顶程序抢夺置顶权
        /// </summary>
        public void EnsureTopmost()
        {
            foreach (var lines in horizontalLines.Values)
            {
                foreach (var line in lines)
                {
                    if (line != null && !line.IsDisposed && line.Visible && line.Handle != IntPtr.Zero)
                    {
                        // 强制重新设置置顶状态，抢夺置顶权
                        line.TopMost = false;  // 先取消置顶
                        line.TopMost = true;   // 再重新置顶，抢夺置顶权
                        
                        // 使用Windows API强制置顶并显示
                        SetWindowPos(line.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                        BringWindowToTop(line.Handle);
                    }
                }
            }
        }

        /// <summary>
        /// 启用所有热键
        /// </summary>
        public void EnableAllHotkeys()
        {
            for (int i = 0; i < hotkeyEnabled.Length; i++)
            {
                if (hotkeyEnabled[i])
                {
                    RegisterHotkeyPair(i);
                }
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
                    var config = JsonSerializer.Deserialize<Config>(jsonString);

                    // 确保热键数组长度正确
                    if (config.HotkeyEnabled != null && config.HotkeyEnabled.Length == 4)
                    {
                        hotkeyEnabled = config.HotkeyEnabled;
                    }
                    else
                    {
                        hotkeyEnabled = new bool[] { true, true, false, false };
                    }

                    lineHeight = config.LineHeight;
                    lineColor = ColorTranslator.FromHtml(config.LineColor);
                    lineOpacity = config.LineOpacity;
                    mouseClickThrough = config.MouseClickThrough;
                    showOnAllScreens = config.ShowOnAllScreens;
                }
            }
            catch (Exception)
            {
                // 如果加载失败，使用默认值
                hotkeyEnabled = new bool[] { true, true, false, false };
                lineHeight = 1;
                lineColor = Color.Green;
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
                var config = new Config
                {
                    HotkeyEnabled = hotkeyEnabled,
                    LineHeight = lineHeight,
                    LineColor = ColorTranslator.ToHtml(lineColor),
                    LineOpacity = lineOpacity,
                    MouseClickThrough = mouseClickThrough,
                    ShowOnAllScreens = showOnAllScreens
                };

                string jsonString = JsonSerializer.Serialize(config, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(configPath, jsonString);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存横线配置时出错: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
} 