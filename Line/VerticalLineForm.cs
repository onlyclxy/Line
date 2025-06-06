using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Reflection;

namespace Line
{
    // 可拖拽的竖线类
    public class DraggableVerticalLine : Form
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

        public DraggableVerticalLine(int width, int height, Color color, double opacity, bool clickThrough)
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
                this.Cursor = Cursors.SizeWE; // 设置为水平调整大小光标
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
                    this.Cursor = Cursors.SizeWE;
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
                int deltaX = currentCursor.X - lastCursor.X;
                
                this.Location = new Point(this.Location.X + deltaX, this.Location.Y);
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

    public class VerticalLineForm : Form
    {
        private const int WM_HOTKEY = 0x0312;
        private Dictionary<int, DraggableVerticalLine> verticalLines = new Dictionary<int, DraggableVerticalLine>();
        private Dictionary<int, bool> lineStates = new Dictionary<int, bool>();
        private NotifyIcon trayIcon;

        // 线条默认宽度为1像素
        private int lineWidth = 1;

        // 线条颜色，默认为蓝色
        private Color lineColor = Color.Blue;

        // 线条透明度，默认为100%
        private int lineOpacity = 100;

        // 鼠标穿透设置
        private bool mouseClickThrough = true;

        // 热键ID基础值（1-4用于开启，5-8用于关闭）
        private const int BASE_HOTKEY_ID_ON = 100;
        private const int BASE_HOTKEY_ID_OFF = 200;

        // 热键绑定状态
        private bool[] hotkeyEnabled = new bool[] { true, false, false, false }; // 默认只启用第一组

        // 用于处理初始显示的标志
        private bool isFirstShow = true;

        // 修改配置文件路径
        private readonly string configPath = Path.Combine(
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
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // 修饰键常量
        private const int MOD_CONTROL = 0x0002;
        private const int MOD_SHIFT = 0x0004;

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

        public VerticalLineForm(NotifyIcon existingTrayIcon)
        {
            this.AutoScaleMode = AutoScaleMode.None;
            this.trayIcon = existingTrayIcon;

            // 加载配置
            LoadConfig();

            InitializeComponent();
            InitializeHotkeys();
            AddVerticalLineMenuItems();
            
            // 设置初始鼠标穿透状态
            SetClickThrough(mouseClickThrough);

            // 显示一次初始竖线，然后立即隐藏
            ShowInitialLine();
        }

        private void ShowInitialLine()
        {
            if (isFirstShow)
            {
                // 创建并显示一个临时的竖线
                Form tempLine = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    ShowInTaskbar = false,
                    TopMost = true,
                    BackColor = lineColor,
                    TransparencyKey = Color.Black,
                    Opacity = lineOpacity / 100.0,
                    Width = lineWidth,
                    Height = Screen.PrimaryScreen.Bounds.Height,

                     // ③ 临时线也要关掉缩放
                    AutoScaleMode = AutoScaleMode.None,
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(
                      Screen.PrimaryScreen.Bounds.Width / 2,
                      Screen.PrimaryScreen.Bounds.Y)
                };

                // 放在屏幕中央
                tempLine.Location = new Point(
                    Screen.PrimaryScreen.Bounds.Width / 2,
                    Screen.PrimaryScreen.Bounds.Y
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
            this.Width = lineWidth;
            this.Height = Screen.PrimaryScreen.Bounds.Height;
            // 不在这里设置 Opacity，而是在 ShowInitialLine 中处理
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
                    Keys key = Keys.F1 + index;
                    // 注册 Ctrl+F1-F4
                    bool onSuccess = RegisterHotKey(this.Handle, BASE_HOTKEY_ID_ON + index, MOD_CONTROL, (int)key);
                    // 注册 Ctrl+Shift+F1-F4
                    bool offSuccess = RegisterHotKey(this.Handle, BASE_HOTKEY_ID_OFF + index, MOD_CONTROL | MOD_SHIFT, (int)key);

                    // 如果注册失败，更新状态并保存配置
                    if (!onSuccess || !offSuccess)
                    {
                        hotkeyEnabled[index] = false;
                        UpdateHotkeyMenuCheckedState();
                        SaveConfig();
                        MessageBox.Show($"热键 Ctrl+F{index + 1} 注册失败，可能已被其他程序占用。", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            
            // 更新所有现有的竖线
            foreach (var line in verticalLines.Values)
            {
                line.SetClickThrough(enable);
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
                var item = new ToolStripMenuItem($"Ctrl+F{i + 1}/Ctrl+Shift+F{i + 1}", null, (s, e) => {
                    ToggleHotkeyBinding(index);
                });
                item.Checked = hotkeyEnabled[i];
                hotkeyBindingMenu.DropDownItems.Add(item);
            }

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
            ToolStripMenuItem transparencyItem = new ToolStripMenuItem("竖线透明度");
            AddTransparencyMenuItem(transparencyItem, "100% (不透明)", 100);
            AddTransparencyMenuItem(transparencyItem, "75%", 75);
            AddTransparencyMenuItem(transparencyItem, "50%", 50);
            AddTransparencyMenuItem(transparencyItem, "25%", 25);

            // 添加所有子菜单
            verticalLineMenu.DropDownItems.Add(hotkeyBindingMenu);
            verticalLineMenu.DropDownItems.Add(mousePenetrationItem);
            verticalLineMenu.DropDownItems.Add(lineThicknessItem);
            verticalLineMenu.DropDownItems.Add(lineColorItem);
            verticalLineMenu.DropDownItems.Add(transparencyItem);

            // 在分隔符之前插入竖线菜单
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

        private void AddThicknessMenuItem(ToolStripMenuItem parent, string text, int thickness)
        {
            var item = new ToolStripMenuItem(text, null, (s, e) => {
                ChangeLineThickness(thickness);
            });
            item.Checked = (thickness == lineWidth);
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
                        Keys key = Keys.F1 + index;
                        bool onSuccess = RegisterHotKey(this.Handle, BASE_HOTKEY_ID_ON + index, MOD_CONTROL, (int)key);
                        bool offSuccess = RegisterHotKey(this.Handle, BASE_HOTKEY_ID_OFF + index, MOD_CONTROL | MOD_SHIFT, (int)key);

                        if (onSuccess && offSuccess)
                        {
                            hotkeyEnabled[index] = true;
                        }
                        else
                        {
                            MessageBox.Show($"热键 Ctrl+F{index + 1} 注册失败，可能已被其他程序占用。", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                    
                    // 如果有对应的竖线，则移除它
                    if (verticalLines.ContainsKey(index))
                    {
                        verticalLines[index].Close();
                        verticalLines.Remove(index);
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
                if (item is ToolStripMenuItem menuItem && menuItem.Text == "持续竖线")
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
            lineWidth = thickness;
            foreach (var line in verticalLines.Values)
            {
                line.Width = thickness;
            }
            UpdateThicknessMenuCheckedState();
            SaveConfig();
        }

        private void ChangeLineColor(Color color)
        {
            lineColor = color;
            foreach (var line in verticalLines.Values)
            {
                line.BackColor = color;
            }
            UpdateColorMenuCheckedState();
            SaveConfig();
        }

        private void ChangeLineTransparency(int value)
        {
            lineOpacity = value;
            double opacity = value / 100.0;
            foreach (var line in verticalLines.Values)
            {
                line.Opacity = opacity;
            }
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

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int hotkeyId = m.WParam.ToInt32();
                
                // 处理开启热键 (Ctrl+F1-F4)
                if (hotkeyId >= BASE_HOTKEY_ID_ON && hotkeyId < BASE_HOTKEY_ID_ON + 4)
                {
                    int lineIndex = hotkeyId - BASE_HOTKEY_ID_ON;
                    if (hotkeyEnabled[lineIndex])
                    {
                        ShowVerticalLine(lineIndex);
                    }
                }
                // 处理关闭热键 (Ctrl+Shift+F1-F4)
                else if (hotkeyId >= BASE_HOTKEY_ID_OFF && hotkeyId < BASE_HOTKEY_ID_OFF + 4)
                {
                    int lineIndex = hotkeyId - BASE_HOTKEY_ID_OFF;
                    if (hotkeyEnabled[lineIndex])
                    {
                        HideVerticalLine(lineIndex);
                    }
                }
            }
            base.WndProc(ref m);
        }

        private void ShowVerticalLine(int index)
        {
            Screen currentScreen = Screen.FromPoint(Cursor.Position);
            
            // 如果这条线已经存在，就更新它的位置
            if (verticalLines.ContainsKey(index))
            {
                DraggableVerticalLine line = verticalLines[index];
                line.Location = new Point(Cursor.Position.X - (lineWidth / 2), currentScreen.Bounds.Y);
                
                // 确保现有线条保持置顶
                if (line.Handle != IntPtr.Zero)
                {
                    line.TopMost = true;
                    SetWindowPos(line.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                }
            }
            else
            {
                // 创建新的竖线
                DraggableVerticalLine line = new DraggableVerticalLine(lineWidth, currentScreen.Bounds.Height, lineColor, lineOpacity / 100.0, mouseClickThrough)
                {
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(
                        Cursor.Position.X - (lineWidth / 2),
                        currentScreen.Bounds.Y
                    ),
                    TopMost = true  // 创建时就设置为置顶
                };

                line.Location = new Point(Cursor.Position.X - (lineWidth / 2), currentScreen.Bounds.Y);
                line.Show();

                // → 关键：Show() 后立刻重置 Width，绕过 DPI 放大
                line.Width = lineWidth;
                
                // 强制使用Windows API置顶
                if (line.Handle != IntPtr.Zero)
                {
                    SetWindowPos(line.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                    BringWindowToTop(line.Handle);
                }

                verticalLines[index] = line;
            }

            lineStates[index] = true;
        }

        private void HideVerticalLine(int index)
        {
            if (verticalLines.ContainsKey(index))
            {
                verticalLines[index].Close();
                verticalLines.Remove(index);
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

            // 关闭所有竖线 - 创建副本避免集合修改异常
            var linesToClose = new List<DraggableVerticalLine>(verticalLines.Values);
            foreach (var line in linesToClose)
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
            verticalLines.Clear();
            lineStates.Clear();

            base.OnFormClosing(e);
        }

        /// <summary>
        /// 显示所有竖线
        /// </summary>
        public void ShowAllLines()
        {
            foreach (var line in verticalLines.Values)
            {
                if (!line.Visible)
                {
                    line.Show();
                }
            }
        }

        /// <summary>
        /// 隐藏所有竖线
        /// </summary>
        public void HideAllLines()
        {
            foreach (var line in verticalLines.Values)
            {
                if (line.Visible)
                {
                    line.Hide();
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
                    verticalLines[index].Close();
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
            foreach (var line in verticalLines.Values)
            {
                if (line != null && !line.IsDisposed)
                {
                    line.TopMost = false;
                    line.TopMost = true;
                    line.BringToFront();
                }
            }
        }

        /// <summary>
        /// 确保所有竖线保持置顶状态（用于持续置顶功能） - 与其他置顶程序抢夺置顶权
        /// </summary>
        public void EnsureTopmost()
        {
            foreach (var line in verticalLines.Values)
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
                        hotkeyEnabled = new bool[] { true, false, false, false };
                    }

                    lineWidth = config.LineWidth;
                    lineColor = ColorTranslator.FromHtml(config.LineColor);
                    lineOpacity = config.LineOpacity;
                    mouseClickThrough = config.MouseClickThrough;
                }
            }
            catch (Exception)
            {
                // 如果加载失败，使用默认值
                hotkeyEnabled = new bool[] { true, false, false, false };
                lineWidth = 1;
                lineColor = Color.Blue;
                lineOpacity = 100;
                mouseClickThrough = true;
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
                    LineWidth = lineWidth,
                    LineColor = ColorTranslator.ToHtml(lineColor),
                    LineOpacity = lineOpacity,
                    MouseClickThrough = mouseClickThrough
                };

                string jsonString = JsonSerializer.Serialize(config, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(configPath, jsonString);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存竖线配置时出错: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
} 