using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Line
{
    // 可拖拽的包围框窗体
    public class DraggableBoundingBox : Form
    {
        private bool isDragging = false;
        private Point lastCursor;
        private bool mouseClickThrough;
        private DashStyle dashStyle;
        private Color lineColor;
        private int lineThickness;
        private Rectangle boundingRect;
        private Action onBoundsChanged; // 添加边界变化回调
        
        // 拖拽模式枚举
        private enum DragMode
        {
            None,
            Move,           // 移动整个框
            ResizeTop,      // 调整顶边
            ResizeBottom,   // 调整底边
            ResizeLeft,     // 调整左边
            ResizeRight,    // 调整右边
            ResizeTopLeft,      // 调整左上角
            ResizeTopRight,     // 调整右上角
            ResizeBottomLeft,   // 调整左下角
            ResizeBottomRight   // 调整右下角
        }
        
        private DragMode currentDragMode = DragMode.None;
        private Rectangle initialBounds;

        // Windows API for mouse click-through
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int GWL_EXSTYLE = (-20);

        // 窗口消息常量
        private const int WM_SETCURSOR = 0x0020;
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int MA_NOACTIVATE = 3;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        public DraggableBoundingBox(Rectangle bounds, Color color, int thickness, double opacity, bool clickThrough, DashStyle dashStyle, Action onBoundsChanged = null)
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.BackColor = Color.Black;
            this.TransparencyKey = Color.Black;
            this.Opacity = opacity;
            this.AutoScaleMode = AutoScaleMode.None;
            this.StartPosition = FormStartPosition.Manual;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer | ControlStyles.ResizeRedraw, true);
            
            // 设置窗体大小和位置
            this.Location = bounds.Location;
            this.Size = bounds.Size;
            
            mouseClickThrough = clickThrough;
            this.dashStyle = dashStyle;
            this.lineColor = color;
            this.lineThickness = thickness;
            this.boundingRect = new Rectangle(0, 0, bounds.Width, bounds.Height);
            this.onBoundsChanged = onBoundsChanged;
            
            // 如果不是鼠标穿透模式，添加拖拽事件
            if (!mouseClickThrough)
            {
                this.MouseDown += OnMouseDown;
                this.MouseMove += OnMouseMove;
                this.MouseUp += OnMouseUp;
                this.MouseLeave += OnMouseLeave;
            }
        }

        // 重写CreateParams，在创建窗口时就设置所有必要的扩展样式
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                if (mouseClickThrough)
                {
                    cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
                }
                else
                {
                    cp.ExStyle |= WS_EX_LAYERED | WS_EX_NOACTIVATE;
                }
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (mouseClickThrough)
            {
                int ex = GetWindowLong(this.Handle, GWL_EXSTYLE);
                ex |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
                SetWindowLong(this.Handle, GWL_EXSTYLE, ex);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            
            using (Pen pen = new Pen(lineColor, lineThickness))
            {
                pen.DashStyle = dashStyle;
                
                // 绘制矩形框，稍微向内缩进以确保线条完全可见
                int halfThickness = lineThickness / 2;
                Rectangle drawRect = new Rectangle(
                    halfThickness,
                    halfThickness,
                    this.Width - lineThickness,
                    this.Height - lineThickness
                );
                
                e.Graphics.DrawRectangle(pen, drawRect);
            }
        }

        // 根据鼠标位置确定拖拽模式和光标
        private DragMode GetDragModeFromPoint(Point point)
        {
            const int borderWidth = 10; // 边界检测宽度
            
            bool nearLeft = point.X <= borderWidth;
            bool nearRight = point.X >= this.Width - borderWidth;
            bool nearTop = point.Y <= borderWidth;
            bool nearBottom = point.Y >= this.Height - borderWidth;
            
            // 边角优先
            if (nearTop && nearLeft) return DragMode.ResizeTopLeft;
            if (nearTop && nearRight) return DragMode.ResizeTopRight;
            if (nearBottom && nearLeft) return DragMode.ResizeBottomLeft;
            if (nearBottom && nearRight) return DragMode.ResizeBottomRight;
            
            // 边线
            if (nearTop) return DragMode.ResizeTop;
            if (nearBottom) return DragMode.ResizeBottom;
            if (nearLeft) return DragMode.ResizeLeft;
            if (nearRight) return DragMode.ResizeRight;
            
            // 内部区域
            return DragMode.Move;
        }

        // 根据拖拽模式设置光标
        private void SetCursorForDragMode(DragMode mode)
        {
            switch (mode)
            {
                case DragMode.Move:
                    this.Cursor = Cursors.SizeAll;
                    break;
                case DragMode.ResizeTop:
                case DragMode.ResizeBottom:
                    this.Cursor = Cursors.SizeNS;
                    break;
                case DragMode.ResizeLeft:
                case DragMode.ResizeRight:
                    this.Cursor = Cursors.SizeWE;
                    break;
                case DragMode.ResizeTopLeft:
                case DragMode.ResizeBottomRight:
                    this.Cursor = Cursors.SizeNWSE;
                    break;
                case DragMode.ResizeTopRight:
                case DragMode.ResizeBottomLeft:
                    this.Cursor = Cursors.SizeNESW;
                    break;
                default:
                    this.Cursor = Cursors.Default;
                    break;
            }
        }

        public void SetClickThrough(bool enable)
        {
            mouseClickThrough = enable;
            if (this.Handle != IntPtr.Zero)
            {
                if (enable)
                {
                    this.MouseDown -= OnMouseDown;
                    this.MouseMove -= OnMouseMove;
                    this.MouseUp -= OnMouseUp;
                    this.MouseLeave -= OnMouseLeave;
                    this.Cursor = Cursors.Default;
                    
                    int exStyle = GetWindowLong(this.Handle, GWL_EXSTYLE);
                    SetWindowLong(this.Handle, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT);
                }
                else
                {
                    int exStyle = GetWindowLong(this.Handle, GWL_EXSTYLE);
                    SetWindowLong(this.Handle, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
                    
                    this.MouseDown -= OnMouseDown;
                    this.MouseMove -= OnMouseMove;
                    this.MouseUp -= OnMouseUp;
                    this.MouseLeave -= OnMouseLeave;
                    this.MouseDown += OnMouseDown;
                    this.MouseMove += OnMouseMove;
                    this.MouseUp += OnMouseUp;
                    this.MouseLeave += OnMouseLeave;
                }
            }
        }

        public void SetDashStyle(DashStyle style)
        {
            dashStyle = style;
            this.Invalidate();
        }

        public void SetLineColor(Color color)
        {
            this.lineColor = color;
            this.Invalidate();
        }

        public void SetLineThickness(int thickness)
        {
            this.lineThickness = thickness;
            this.Invalidate();
        }

        // 拦截窗口消息，解决光标和激活问题
        protected override void WndProc(ref Message m)
        {
            if (mouseClickThrough)
            {
                if (m.Msg == WM_MOUSEACTIVATE)
                {
                    m.Result = new IntPtr(MA_NOACTIVATE);
                    return;
                }
                if (m.Msg == WM_SETCURSOR)
                {
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
                currentDragMode = GetDragModeFromPoint(e.Location);
                initialBounds = this.Bounds;
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!isDragging)
            {
                // 更新光标样式
                DragMode mode = GetDragModeFromPoint(e.Location);
                SetCursorForDragMode(mode);
                return;
            }

            Point currentCursor = Cursor.Position;
            int deltaX = currentCursor.X - lastCursor.X;
            int deltaY = currentCursor.Y - lastCursor.Y;
            
            Rectangle newBounds = this.Bounds;
            
            switch (currentDragMode)
            {
                case DragMode.Move:
                    newBounds.X += deltaX;
                    newBounds.Y += deltaY;
                    break;
                    
                case DragMode.ResizeTop:
                    newBounds.Y += deltaY;
                    newBounds.Height -= deltaY;
                    break;
                    
                case DragMode.ResizeBottom:
                    newBounds.Height += deltaY;
                    break;
                    
                case DragMode.ResizeLeft:
                    newBounds.X += deltaX;
                    newBounds.Width -= deltaX;
                    break;
                    
                case DragMode.ResizeRight:
                    newBounds.Width += deltaX;
                    break;
                    
                case DragMode.ResizeTopLeft:
                    newBounds.X += deltaX;
                    newBounds.Y += deltaY;
                    newBounds.Width -= deltaX;
                    newBounds.Height -= deltaY;
                    break;
                    
                case DragMode.ResizeTopRight:
                    newBounds.Y += deltaY;
                    newBounds.Width += deltaX;
                    newBounds.Height -= deltaY;
                    break;
                    
                case DragMode.ResizeBottomLeft:
                    newBounds.X += deltaX;
                    newBounds.Width -= deltaX;
                    newBounds.Height += deltaY;
                    break;
                    
                case DragMode.ResizeBottomRight:
                    newBounds.Width += deltaX;
                    newBounds.Height += deltaY;
                    break;
            }
            
            // 确保最小尺寸
            if (newBounds.Width < 50) newBounds.Width = 50;
            if (newBounds.Height < 50) newBounds.Height = 50;
            
            this.Bounds = newBounds;
            lastCursor = currentCursor;
            
            // 触发边界变化回调
            onBoundsChanged?.Invoke();
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isDragging = false;
                currentDragMode = DragMode.None;
            }
        }

        private void OnMouseLeave(object sender, EventArgs e)
        {
            if (!isDragging)
            {
                this.Cursor = Cursors.Default;
            }
        }
    }

    public class BoundingBoxForm : Form
    {
        private NotifyIcon trayIcon;
        private DraggableBoundingBox boundingBox;
        private bool isVisible = false;

        // 辅助线
        private Form topGuide, bottomGuide, leftGuide, rightGuide;
        private bool guideLinesEnabled = true; // 默认开启辅助线

        // 线条默认粗细为2像素
        private int lineThickness = 2;

        // 线条颜色，默认为红色
        private Color lineColor = Color.Red;

        // 线条透明度，默认为100%
        private int lineOpacity = 100;

        // 鼠标穿透设置，默认不穿透
        private bool mouseClickThrough = false;

        // 虚线样式，默认为实线
        private DashStyle dashStyle = DashStyle.Solid;

        // 默认矩形大小和位置 - 改为屏幕大约一半的大小
        private Rectangle defaultBounds;

        // 配置文件路径
        private readonly string configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ScreenLine",
            "boundingbox_config.json"
        );

        // 配置类
        private class Config
        {
            public int LineThickness { get; set; }
            public string LineColor { get; set; }
            public int LineOpacity { get; set; }
            public bool MouseClickThrough { get; set; }
            public int DashStyle { get; set; }
            public Rectangle DefaultBounds { get; set; }
            public bool GuideLinesEnabled { get; set; } // 添加辅助线开关配置
        }

        // 置顶相关API
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_SHOWWINDOW = 0x0040;

        public BoundingBoxForm(NotifyIcon existingTrayIcon)
        {
            this.AutoScaleMode = AutoScaleMode.None;
            this.trayIcon = existingTrayIcon;

            // 首先设置默认边界
            SetDefaultBounds();

            // 加载配置
            LoadConfig();

            InitializeComponent();
            AddBoundingBoxMenuItems();
        }

        private void InitializeComponent()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.AutoScaleMode = AutoScaleMode.None;
            this.Opacity = 0;
            this.Width = 1;
            this.Height = 1;
        }

        private void AddBoundingBoxMenuItems()
        {
            if (trayIcon?.ContextMenuStrip == null) return;

            ToolStripMenuItem boundingBoxMenu = new ToolStripMenuItem("包围框");

            // 显示/隐藏包围框
            ToolStripMenuItem toggleVisibilityItem = new ToolStripMenuItem("显示包围框", null, (s, e) => {
                ToggleBoundingBox();
            });
            toggleVisibilityItem.Checked = isVisible;

            // 辅助线开关
            var guideLineItem = new ToolStripMenuItem("显示辅助线", null, (s, e) => {
                ToggleGuideLines();
                if (s is ToolStripMenuItem menuItem)
                {
                    menuItem.Checked = guideLinesEnabled;
                }
            });
            guideLineItem.Checked = guideLinesEnabled;

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

            // 透明度菜单
            ToolStripMenuItem transparencyItem = new ToolStripMenuItem("线条透明度");
            AddTransparencyMenuItem(transparencyItem, "100% (不透明)", 100);
            AddTransparencyMenuItem(transparencyItem, "75%", 75);
            AddTransparencyMenuItem(transparencyItem, "50%", 50);
            AddTransparencyMenuItem(transparencyItem, "25%", 25);

            // 虚线样式菜单
            ToolStripMenuItem dashStyleItem = new ToolStripMenuItem("线条样式");
            AddDashStyleMenuItem(dashStyleItem, "实线", DashStyle.Solid);
            AddDashStyleMenuItem(dashStyleItem, "虚线", DashStyle.Dash);
            AddDashStyleMenuItem(dashStyleItem, "点线", DashStyle.Dot);
            AddDashStyleMenuItem(dashStyleItem, "点划线", DashStyle.DashDot);
            AddDashStyleMenuItem(dashStyleItem, "双点划线", DashStyle.DashDotDot);

            // 重置位置菜单
            ToolStripMenuItem resetPositionItem = new ToolStripMenuItem("重置位置", null, (s, e) => {
                ResetBoundingBox();
            });

            // 添加所有子菜单
            boundingBoxMenu.DropDownItems.Add(toggleVisibilityItem);
            boundingBoxMenu.DropDownItems.Add(guideLineItem);
            boundingBoxMenu.DropDownItems.Add(new ToolStripSeparator());
            boundingBoxMenu.DropDownItems.Add(mousePenetrationItem);
            boundingBoxMenu.DropDownItems.Add(lineThicknessItem);
            boundingBoxMenu.DropDownItems.Add(lineColorItem);
            boundingBoxMenu.DropDownItems.Add(transparencyItem);
            boundingBoxMenu.DropDownItems.Add(dashStyleItem);
            boundingBoxMenu.DropDownItems.Add(new ToolStripSeparator());
            boundingBoxMenu.DropDownItems.Add(resetPositionItem);

            // 在持续横线菜单之后插入包围框菜单
            int insertIndex = -1;
            for (int i = 0; i < trayIcon.ContextMenuStrip.Items.Count; i++)
            {
                if (trayIcon.ContextMenuStrip.Items[i] is ToolStripMenuItem menuItem && 
                    menuItem.Text == "持续横线")
                {
                    insertIndex = i + 1;
                    break;
                }
            }

            if (insertIndex != -1)
            {
                trayIcon.ContextMenuStrip.Items.Insert(insertIndex, boundingBoxMenu);
            }
            else
            {
                // 如果找不到持续横线菜单，就在分隔符前插入
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
                    trayIcon.ContextMenuStrip.Items.Insert(separatorIndex, boundingBoxMenu);
                }
                else
                {
                    trayIcon.ContextMenuStrip.Items.Add(boundingBoxMenu);
                }
            }
        }

        private void AddThicknessMenuItem(ToolStripMenuItem parent, string text, int thickness)
        {
            var item = new ToolStripMenuItem(text, null, (s, e) => {
                ChangeLineThickness(thickness);
            });
            item.Checked = (thickness == lineThickness);
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

        private void AddDashStyleMenuItem(ToolStripMenuItem parent, string name, DashStyle style)
        {
            var item = new ToolStripMenuItem(name, null, (s, e) => {
                ChangeDashStyle(style);
            });
            item.Checked = (style == dashStyle);
            parent.DropDownItems.Add(item);
        }

        private void ToggleBoundingBox()
        {
            if (isVisible)
            {
                HideBoundingBox();
            }
            else
            {
                ShowBoundingBox();
            }
            UpdateVisibilityMenuText();
        }

        private void ShowBoundingBox()
        {
            Screen currentScreen = Screen.FromPoint(Cursor.Position);
            
            // 如果已存在，先隐藏
            if (boundingBox != null) HideBoundingBox();

            // 计算包围框位置，确保在当前屏幕内
            Rectangle bounds = defaultBounds;
            
            // 如果默认位置超出当前屏幕，调整位置
            if (bounds.Right > currentScreen.Bounds.Right)
                bounds.X = currentScreen.Bounds.Right - bounds.Width;
            if (bounds.Bottom > currentScreen.Bounds.Bottom)
                bounds.Y = currentScreen.Bounds.Bottom - bounds.Height;
            if (bounds.X < currentScreen.Bounds.X)
                bounds.X = currentScreen.Bounds.X;
            if (bounds.Y < currentScreen.Bounds.Y)
                bounds.Y = currentScreen.Bounds.Y;

            // 确保最小大小
            if (bounds.Width < 100) bounds.Width = 100;
            if (bounds.Height < 100) bounds.Height = 100;

            // 创建包围框
            boundingBox = new DraggableBoundingBox(bounds, lineColor, lineThickness, lineOpacity / 100.0, mouseClickThrough, dashStyle, () => {
                UpdateGuideLines();
            });
            boundingBox.Show();
            
            // **关键：给操作系统一点时间把窗体"真正"摆到屏幕上**
            Application.DoEvents();
            
            // 创建辅助线 - 使用包围框的实际位置
            if (guideLinesEnabled)
            {
                CreateGuideLines(currentScreen, boundingBox.Bounds);
            }
            
            isVisible = true;
        }

        private void CreateGuideLines(Screen screen, Rectangle boundingRect)
        {
            // 清理现有辅助线
            HideGuideLines();

            // 计算辅助线的透明度（比主线条透明50%）
            double guideOpacity = (lineOpacity / 100.0) * 0.5;
            Color guideColor = lineColor;

            // 顶部辅助线 - 从包围框顶边延伸到屏幕两侧
            topGuide = CreateGuideLine(
                new Rectangle(screen.Bounds.X, boundingRect.Y, screen.Bounds.Width, lineThickness),
                guideColor, guideOpacity, DashStyle.Dash
            );

            // 底部辅助线 - 从包围框底边延伸到屏幕两侧
            bottomGuide = CreateGuideLine(
                new Rectangle(screen.Bounds.X, boundingRect.Bottom - lineThickness, screen.Bounds.Width, lineThickness),
                guideColor, guideOpacity, DashStyle.Dash
            );

            // 左侧辅助线 - 从包围框左边延伸到屏幕上下
            leftGuide = CreateGuideLine(
                new Rectangle(boundingRect.X, screen.Bounds.Y, lineThickness, screen.Bounds.Height),
                guideColor, guideOpacity, DashStyle.Dash
            );

            // 右侧辅助线 - 从包围框右边延伸到屏幕上下
            rightGuide = CreateGuideLine(
                new Rectangle(boundingRect.Right - lineThickness, screen.Bounds.Y, lineThickness, screen.Bounds.Height),
                guideColor, guideOpacity, DashStyle.Dash
            );
        }

        private Form CreateGuideLine(Rectangle bounds, Color color, double opacity, DashStyle style)
        {
            var guideLine = new GuideLineForm(bounds, color, opacity, style, lineThickness);
            guideLine.Show();
            return guideLine;
        }

        // 定义专门的辅助线窗体类
        private class GuideLineForm : Form
        {
            private Color lineColor;
            private DashStyle lineStyle;
            private int thickness;

            public GuideLineForm(Rectangle bounds, Color color, double opacity, DashStyle style, int thickness)
            {
                this.FormBorderStyle = FormBorderStyle.None;
                this.ShowInTaskbar = false;
                this.TopMost = true;
                this.BackColor = Color.Black;
                this.TransparencyKey = Color.Black;
                this.Opacity = opacity;
                this.Bounds = bounds;
                this.AutoScaleMode = AutoScaleMode.None;
                this.StartPosition = FormStartPosition.Manual;
                
                this.lineColor = color;
                this.lineStyle = style;
                this.thickness = thickness;

                this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer, true);
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    const int WS_EX_TRANSPARENT = 0x20;
                    const int WS_EX_LAYERED = 0x80000;
                    const int WS_EX_NOACTIVATE = 0x08000000;
                    cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
                    return cp;
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                
                using (Pen pen = new Pen(lineColor, thickness))
                {
                    pen.DashStyle = lineStyle;
                    
                    if (this.Width > this.Height)
                    {
                        // 水平线
                        int y = this.Height / 2;
                        e.Graphics.DrawLine(pen, 0, y, this.Width, y);
                    }
                    else
                    {
                        // 垂直线
                        int x = this.Width / 2;
                        e.Graphics.DrawLine(pen, x, 0, x, this.Height);
                    }
                }
            }
        }

        private void UpdateGuideLines()
        {
            if (!guideLinesEnabled || !isVisible || boundingBox == null)
                return;

            Screen currentScreen = Screen.FromPoint(boundingBox.Location);
            Rectangle boundingRect = boundingBox.Bounds;
            
            // 更新辅助线位置
            if (topGuide != null)
            {
                topGuide.Bounds = new Rectangle(currentScreen.Bounds.X, boundingRect.Y, currentScreen.Bounds.Width, lineThickness);
                topGuide.Invalidate();
            }

            if (bottomGuide != null)
            {
                bottomGuide.Bounds = new Rectangle(currentScreen.Bounds.X, boundingRect.Bottom - lineThickness, currentScreen.Bounds.Width, lineThickness);
                bottomGuide.Invalidate();
            }

            if (leftGuide != null)
            {
                leftGuide.Bounds = new Rectangle(boundingRect.X, currentScreen.Bounds.Y, lineThickness, currentScreen.Bounds.Height);
                leftGuide.Invalidate();
            }

            if (rightGuide != null)
            {
                rightGuide.Bounds = new Rectangle(boundingRect.Right - lineThickness, currentScreen.Bounds.Y, lineThickness, currentScreen.Bounds.Height);
                rightGuide.Invalidate();
            }
        }

        private void HideGuideLines()
        {
            if (topGuide != null) { topGuide.Close(); topGuide = null; }
            if (bottomGuide != null) { bottomGuide.Close(); bottomGuide = null; }
            if (leftGuide != null) { leftGuide.Close(); leftGuide = null; }
            if (rightGuide != null) { rightGuide.Close(); rightGuide = null; }
        }

        private void HideBoundingBox()
        {
            if (boundingBox != null) { boundingBox.Close(); boundingBox = null; }
            HideGuideLines();
            
            isVisible = false;
        }

        private void ResetBoundingBox()
        {
            Screen currentScreen = Screen.FromPoint(Cursor.Position);
            
            // 重置到当前屏幕中央，大约屏幕大小的一半
            int width = currentScreen.Bounds.Width / 2;
            int height = currentScreen.Bounds.Height / 2;
            
            defaultBounds = new Rectangle(
                currentScreen.Bounds.X + (currentScreen.Bounds.Width - width) / 2,
                currentScreen.Bounds.Y + (currentScreen.Bounds.Height - height) / 2,
                width,
                height
            );

            if (isVisible)
            {
                HideBoundingBox();
                ShowBoundingBox();
            }
            
            SaveConfig();
        }

        private void SetClickThrough(bool enable)
        {
            mouseClickThrough = enable;
            
            if (boundingBox != null) boundingBox.SetClickThrough(enable);
            
            SaveConfig();
        }

        private void ChangeLineThickness(int thickness)
        {
            lineThickness = thickness;
            
            if (boundingBox != null)
            {
                boundingBox.SetLineThickness(thickness);
            }
            
            UpdateThicknessMenuCheckedState();
            SaveConfig();
        }

        private void ChangeLineColor(Color color)
        {
            lineColor = color;
            
            if (boundingBox != null) boundingBox.SetLineColor(color);
            
            UpdateColorMenuCheckedState();
            SaveConfig();
        }

        private void ChangeLineTransparency(int value)
        {
            lineOpacity = value;
            double opacity = value / 100.0;
            
            if (boundingBox != null) boundingBox.Opacity = opacity;
            
            UpdateTransparencyMenuCheckedState();
            SaveConfig();
        }

        private void ChangeDashStyle(DashStyle style)
        {
            dashStyle = style;
            
            if (boundingBox != null) boundingBox.SetDashStyle(style);
            
            UpdateDashStyleMenuCheckedState();
            SaveConfig();
        }

        private Rectangle GetCurrentBounds()
        {
            if (boundingBox != null)
            {
                return boundingBox.Bounds;
            }
            return defaultBounds;
        }

        private void UpdateVisibilityMenuText()
        {
            UpdateMenuCheckedState("显示包围框", "隐藏包围框", isVisible);
        }

        private void UpdateThicknessMenuCheckedState()
        {
            UpdateMenuCheckedState("线条粗细", item => {
                string thicknessStr = lineThickness.ToString();
                return item.Text.Contains(thicknessStr);
            });
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

        private void UpdateDashStyleMenuCheckedState()
        {
            UpdateMenuCheckedState("线条样式", item => {
                return (item.Text == "实线" && dashStyle == DashStyle.Solid) ||
                       (item.Text == "虚线" && dashStyle == DashStyle.Dash) ||
                       (item.Text == "点线" && dashStyle == DashStyle.Dot) ||
                       (item.Text == "点划线" && dashStyle == DashStyle.DashDot) ||
                       (item.Text == "双点划线" && dashStyle == DashStyle.DashDotDot);
            });
        }

        private void UpdateMenuCheckedState(string menuName, Func<ToolStripMenuItem, bool> checkCondition)
        {
            if (trayIcon?.ContextMenuStrip == null) return;

            foreach (ToolStripItem item in trayIcon.ContextMenuStrip.Items)
            {
                if (item is ToolStripMenuItem boundingBoxMenu && boundingBoxMenu.Text == "包围框")
                {
                    foreach (ToolStripItem subItem in boundingBoxMenu.DropDownItems)
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

        private void UpdateMenuCheckedState(string showText, string hideText, bool isChecked)
        {
            if (trayIcon?.ContextMenuStrip == null) return;

            foreach (ToolStripItem item in trayIcon.ContextMenuStrip.Items)
            {
                if (item is ToolStripMenuItem boundingBoxMenu && boundingBoxMenu.Text == "包围框")
                {
                    foreach (ToolStripItem subItem in boundingBoxMenu.DropDownItems)
                    {
                        if (subItem is ToolStripMenuItem menuItem && 
                            (menuItem.Text == showText || menuItem.Text == hideText))
                        {
                            menuItem.Text = isChecked ? hideText : showText;
                            menuItem.Checked = isChecked;
                            break;
                        }
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// 确保所有包围线保持置顶状态
        /// </summary>
        public void EnsureTopmost()
        {
            if (boundingBox != null && !boundingBox.IsDisposed && boundingBox.Visible && boundingBox.Handle != IntPtr.Zero)
            {
                boundingBox.TopMost = false;
                boundingBox.TopMost = true;
                
                SetWindowPos(boundingBox.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                BringWindowToTop(boundingBox.Handle);
            }

            // 确保辅助线也保持置顶
            var guides = new[] { topGuide, bottomGuide, leftGuide, rightGuide };
            foreach (var guide in guides)
            {
                if (guide != null && !guide.IsDisposed && guide.Visible && guide.Handle != IntPtr.Zero)
                {
                    guide.TopMost = false;
                    guide.TopMost = true;
                    
                    SetWindowPos(guide.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                    BringWindowToTop(guide.Handle);
                }
            }
        }

        /// <summary>
        /// 显示所有包围线
        /// </summary>
        public void ShowAllLines()
        {
            if (isVisible && boundingBox != null && !boundingBox.Visible)
            {
                boundingBox.Show();
            }

            if (guideLinesEnabled)
            {
                var guides = new[] { topGuide, bottomGuide, leftGuide, rightGuide };
                foreach (var guide in guides)
                {
                    if (guide != null && !guide.Visible)
                    {
                        guide.Show();
                    }
                }
            }
        }

        /// <summary>
        /// 隐藏所有包围线
        /// </summary>
        public void HideAllLines()
        {
            if (isVisible && boundingBox != null && boundingBox.Visible)
            {
                boundingBox.Hide();
            }

            var guides = new[] { topGuide, bottomGuide, leftGuide, rightGuide };
            foreach (var guide in guides)
            {
                if (guide != null && guide.Visible)
                {
                    guide.Hide();
                }
            }
        }

        /// <summary>
        /// 关闭所有包围线
        /// </summary>
        public void CloseAllLines()
        {
            HideBoundingBox();
            UpdateVisibilityMenuText();
        }

        /// <summary>
        /// 重新置顶所有包围线
        /// </summary>
        public void BringAllLinesToTop()
        {
            EnsureTopmost();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            HideBoundingBox();
            base.OnFormClosing(e);
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

                    lineThickness = config.LineThickness;
                    lineColor = ColorTranslator.FromHtml(config.LineColor);
                    lineOpacity = config.LineOpacity;
                    mouseClickThrough = config.MouseClickThrough;
                    dashStyle = (DashStyle)config.DashStyle;
                    defaultBounds = config.DefaultBounds;
                    guideLinesEnabled = config.GuideLinesEnabled;
                }
                else
                {
                    // 第一次运行，设置默认值
                    guideLinesEnabled = true; // 默认开启辅助线
                    SetDefaultBounds();
                }
            }
            catch
            {
                // 如果加载失败，使用默认值
                lineThickness = 2;
                lineColor = Color.Red;
                lineOpacity = 100;
                mouseClickThrough = false;
                dashStyle = DashStyle.Solid;
                guideLinesEnabled = true; // 默认开启辅助线
                
                SetDefaultBounds();
            }
        }

        private void SetDefaultBounds()
        {
            Screen primaryScreen = Screen.PrimaryScreen;
            int width = primaryScreen.Bounds.Width / 2;  // 屏幕宽度的一半
            int height = primaryScreen.Bounds.Height / 2; // 屏幕高度的一半
            
            defaultBounds = new Rectangle(
                primaryScreen.Bounds.X + (primaryScreen.Bounds.Width - width) / 2,
                primaryScreen.Bounds.Y + (primaryScreen.Bounds.Height - height) / 2,
                width,
                height
            );
        }

        // 保存配置
        private void SaveConfig()
        {
            try
            {
                // 如果包围框可见，更新默认位置
                if (isVisible)
                {
                    defaultBounds = GetCurrentBounds();
                }

                var config = new Config
                {
                    LineThickness = lineThickness,
                    LineColor = ColorTranslator.ToHtml(lineColor),
                    LineOpacity = lineOpacity,
                    MouseClickThrough = mouseClickThrough,
                    DashStyle = (int)dashStyle,
                    DefaultBounds = defaultBounds,
                    GuideLinesEnabled = guideLinesEnabled
                };

                string jsonString = JsonSerializer.Serialize(config, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                var configDir = Path.GetDirectoryName(configPath);
                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }

                File.WriteAllText(configPath, jsonString);
            }
            catch
            {
                // 忽略保存错误
            }
        }

        private void ToggleGuideLines()
        {
            guideLinesEnabled = !guideLinesEnabled;
            
            if (isVisible)
            {
                if (guideLinesEnabled)
                {
                    // 重新创建辅助线
                    if (boundingBox != null)
                    {
                        Screen currentScreen = Screen.FromPoint(boundingBox.Location);
                        CreateGuideLines(currentScreen, boundingBox.Bounds);
                    }
                }
                else
                {
                    // 隐藏辅助线
                    HideGuideLines();
                }
            }
            
            UpdateGuideLinesMenuCheckedState();
            SaveConfig();
        }

        private void UpdateGuideLinesMenuCheckedState()
        {
            if (trayIcon?.ContextMenuStrip == null) return;

            foreach (ToolStripItem item in trayIcon.ContextMenuStrip.Items)
            {
                if (item is ToolStripMenuItem boundingBoxMenu && boundingBoxMenu.Text == "包围框")
                {
                    foreach (ToolStripItem subItem in boundingBoxMenu.DropDownItems)
                    {
                        if (subItem is ToolStripMenuItem menuItem && menuItem.Text == "显示辅助线")
                        {
                            menuItem.Checked = guideLinesEnabled;
                            break;
                        }
                    }
                    break;
                }
            }
        }
    }
} 