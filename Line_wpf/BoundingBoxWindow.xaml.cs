using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using Color = System.Drawing.Color;
using MessageBox = System.Windows.MessageBox;

namespace Line_wpf
{
    // 可拖拽的包围框类
    public class DraggableBoundingBox : Window
    {
        private bool isDragging = false;
        private System.Drawing.Point lastCursor;
        private bool mouseClickThrough;
        private System.Drawing.Drawing2D.DashStyle dashStyle;
        private System.Drawing.Color lineColor;
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

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_SHOWWINDOW = 0x0040;

        public DraggableBoundingBox(Rectangle bounds, System.Drawing.Color color, int thickness, double opacity, bool clickThrough, System.Drawing.Drawing2D.DashStyle dashStyle, Action onBoundsChanged = null)
        {
            this.WindowStyle = WindowStyle.None;
            this.ShowInTaskbar = false;
            this.Topmost = true;
            this.AllowsTransparency = true;
            this.Background = System.Windows.Media.Brushes.Transparent;
            this.Opacity = opacity;
            this.ResizeMode = ResizeMode.NoResize;
            
            // 设置窗体大小和位置
            this.Left = bounds.X;
            this.Top = bounds.Y;
            this.Width = bounds.Width;
            this.Height = bounds.Height;
            
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

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            var hwnd = new WindowInteropHelper(this).Handle;
            var hwndSource = HwndSource.FromHwnd(hwnd);
            hwndSource.AddHook(HwndHook);
            
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

        protected override void OnRender(System.Windows.Media.DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            
            // 将System.Drawing.Drawing2D.DashStyle转换为WPF DashStyle
            var wpfDashStyle = ConvertToWpfDashStyle(dashStyle);
            
            var pen = new System.Windows.Media.Pen(
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(
                    lineColor.A, lineColor.R, lineColor.G, lineColor.B)), 
                lineThickness);
            pen.DashStyle = wpfDashStyle;
            
            // 绘制矩形框，稍微向内缩进以确保线条完全可见
            double halfThickness = lineThickness / 2.0;
            var rect = new System.Windows.Rect(
                halfThickness,
                halfThickness,
                this.Width - lineThickness,
                this.Height - lineThickness
            );
            
            drawingContext.DrawRectangle(null, pen, rect);
        }

        // 转换System.Drawing.Drawing2D.DashStyle到WPF DashStyle
        private System.Windows.Media.DashStyle ConvertToWpfDashStyle(System.Drawing.Drawing2D.DashStyle drawingDashStyle)
        {
            switch (drawingDashStyle)
            {
                case System.Drawing.Drawing2D.DashStyle.Solid:
                    return System.Windows.Media.DashStyles.Solid;
                case System.Drawing.Drawing2D.DashStyle.Dash:
                    return System.Windows.Media.DashStyles.Dash;
                case System.Drawing.Drawing2D.DashStyle.Dot:
                    return System.Windows.Media.DashStyles.Dot;
                case System.Drawing.Drawing2D.DashStyle.DashDot:
                    return System.Windows.Media.DashStyles.DashDot;
                case System.Drawing.Drawing2D.DashStyle.DashDotDot:
                    return System.Windows.Media.DashStyles.DashDotDot;
                default:
                    return System.Windows.Media.DashStyles.Solid;
            }
        }

        // 根据鼠标位置确定拖拽模式和光标
        private DragMode GetDragModeFromPoint(System.Windows.Point point)
        {
            const int borderWidth = 20; // 增加边界检测宽度，让边缘更容易点击
            
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
                    this.Cursor = System.Windows.Input.Cursors.SizeAll;
                    break;
                case DragMode.ResizeTop:
                case DragMode.ResizeBottom:
                    this.Cursor = System.Windows.Input.Cursors.SizeNS;
                    break;
                case DragMode.ResizeLeft:
                case DragMode.ResizeRight:
                    this.Cursor = System.Windows.Input.Cursors.SizeWE;
                    break;
                case DragMode.ResizeTopLeft:
                case DragMode.ResizeBottomRight:
                    this.Cursor = System.Windows.Input.Cursors.SizeNWSE;
                    break;
                case DragMode.ResizeTopRight:
                case DragMode.ResizeBottomLeft:
                    this.Cursor = System.Windows.Input.Cursors.SizeNESW;
                    break;
                default:
                    this.Cursor = System.Windows.Input.Cursors.Arrow;
                    break;
            }
        }

        public void SetClickThrough(bool enable)
        {
            mouseClickThrough = enable;
            var hwnd = new WindowInteropHelper(this).Handle;
            
            if (hwnd != IntPtr.Zero)
            {
                if (enable)
                {
                    this.MouseDown -= OnMouseDown;
                    this.MouseMove -= OnMouseMove;
                    this.MouseUp -= OnMouseUp;
                    this.MouseLeave -= OnMouseLeave;
                    this.Cursor = System.Windows.Input.Cursors.Arrow;
                    
                    int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                    SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT);
                }
                else
                {
                    int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                    SetWindowLong(hwnd, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
                    
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

        public void SetDashStyle(System.Drawing.Drawing2D.DashStyle style)
        {
            dashStyle = style;
            this.InvalidateVisual();
        }

        public void SetLineColor(System.Drawing.Color color)
        {
            this.lineColor = color;
            this.InvalidateVisual();
        }

        public void SetLineThickness(int thickness)
        {
            this.lineThickness = thickness;
            this.InvalidateVisual();
        }

        private void OnMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                isDragging = true;
                lastCursor = System.Windows.Forms.Cursor.Position;
                currentDragMode = GetDragModeFromPoint(e.GetPosition(this));
                initialBounds = new Rectangle((int)this.Left, (int)this.Top, (int)this.Width, (int)this.Height);
                this.CaptureMouse();
            }
        }

        private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!isDragging)
            {
                // 更新光标样式
                DragMode mode = GetDragModeFromPoint(e.GetPosition(this));
                SetCursorForDragMode(mode);
                return;
            }

            var currentCursor = System.Windows.Forms.Cursor.Position;
            int deltaX = currentCursor.X - lastCursor.X;
            int deltaY = currentCursor.Y - lastCursor.Y;
            
            double newLeft = this.Left;
            double newTop = this.Top;
            double newWidth = this.Width;
            double newHeight = this.Height;
            
            switch (currentDragMode)
            {
                case DragMode.Move:
                    newLeft += deltaX;
                    newTop += deltaY;
                    break;
                    
                case DragMode.ResizeTop:
                    newTop += deltaY;
                    newHeight -= deltaY;
                    break;
                    
                case DragMode.ResizeBottom:
                    newHeight += deltaY;
                    break;
                    
                case DragMode.ResizeLeft:
                    newLeft += deltaX;
                    newWidth -= deltaX;
                    break;
                    
                case DragMode.ResizeRight:
                    newWidth += deltaX;
                    break;
                    
                case DragMode.ResizeTopLeft:
                    newLeft += deltaX;
                    newTop += deltaY;
                    newWidth -= deltaX;
                    newHeight -= deltaY;
                    break;
                    
                case DragMode.ResizeTopRight:
                    newTop += deltaY;
                    newWidth += deltaX;
                    newHeight -= deltaY;
                    break;
                    
                case DragMode.ResizeBottomLeft:
                    newLeft += deltaX;
                    newWidth -= deltaX;
                    newHeight += deltaY;
                    break;
                    
                case DragMode.ResizeBottomRight:
                    newWidth += deltaX;
                    newHeight += deltaY;
                    break;
            }
            
            // 确保最小尺寸
            if (newWidth < 50) newWidth = 50;
            if (newHeight < 50) newHeight = 50;
            
            this.Left = newLeft;
            this.Top = newTop;
            this.Width = newWidth;
            this.Height = newHeight;
            
            lastCursor = currentCursor;
            
            // 触发边界变化回调
            onBoundsChanged?.Invoke();
        }

        private void OnMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Released)
            {
                isDragging = false;
                currentDragMode = DragMode.None;
                this.ReleaseMouseCapture();
            }
        }

        private void OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!isDragging)
            {
                this.Cursor = System.Windows.Input.Cursors.Arrow;
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

    public partial class BoundingBoxWindow : Window
    {
        private NotifyIcon trayIcon;
        private DraggableBoundingBox boundingBox;
        private bool isVisible = false;
        private bool isGuideLinesOnlyMode = false; // 添加仅辅助线模式标志

        // 辅助线1 - 第一套辅助线系统（与包围框完全独立）
        private DraggableGuideLine topGuide, bottomGuide, leftGuide, rightGuide;
        private bool guideLinesEnabled = true; // 默认开启辅助线1
        private bool guideLinesCanDrag = true; // 默认辅助线1可拖动
        // 辅助线1独立配置
        private System.Drawing.Color lineColor1 = System.Drawing.Color.Blue; // 辅助线1独立颜色
        private int lineOpacity1 = 100; // 辅助线1独立透明度
        private System.Drawing.Drawing2D.DashStyle guideLineDashStyle1 = System.Drawing.Drawing2D.DashStyle.Dash; // 辅助线1独立样式
        private Rectangle defaultBounds1; // 辅助线1的默认位置

        // 辅助线2 - 第二套独立的辅助线系统
        private DraggableGuideLine topGuide2, bottomGuide2, leftGuide2, rightGuide2;
        private bool guideLinesEnabled2 = true; // 默认开启辅助线2
        private bool guideLinesCanDrag2 = true; // 默认辅助线2可拖动
        private System.Drawing.Color lineColor2 = System.Drawing.Color.LimeGreen; // 辅助线2颜色
        private int lineOpacity2 = 100; // 辅助线2透明度
        private System.Drawing.Drawing2D.DashStyle guideLineDashStyle2 = System.Drawing.Drawing2D.DashStyle.Dot; // 辅助线2样式
        private bool isGuideLinesOnlyMode2 = false; // 辅助线2仅显示模式标志
        private Rectangle defaultBounds2; // 辅助线2的默认位置

        // 线条默认粗细为2像素（所有系统共用）
        private int lineThickness = 2;

        // 包围框独立配置
        private System.Drawing.Color lineColor = System.Drawing.Color.Red; // 包围框颜色
        private int lineOpacity = 100; // 包围框透明度
        private bool mouseClickThrough = false; // 包围框鼠标穿透设置
        private System.Drawing.Drawing2D.DashStyle dashStyle = System.Drawing.Drawing2D.DashStyle.Solid; // 包围框样式

        // 移除原来的guideLineDashStyle，因为现在有guideLineDashStyle1
        // private System.Drawing.Drawing2D.DashStyle guideLineDashStyle = System.Drawing.Drawing2D.DashStyle.Dash;

        // 包围框默认边界
        private Rectangle defaultBounds;

        // 配置文件路径
        private readonly string configPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ScreenLine",
            "boundingbox_config.json"
        );

        // 配置类
        private class Config
        {
            // 包围框配置
            public int LineThickness { get; set; }
            public string LineColor { get; set; } // 包围框颜色
            public int LineOpacity { get; set; } // 包围框透明度
            public bool MouseClickThrough { get; set; }
            public int DashStyle { get; set; } // 包围框样式
            public Rectangle DefaultBounds { get; set; }
            
            // 辅助线1独立配置
            public bool GuideLinesEnabled { get; set; } // 辅助线1开关配置
            public bool GuideLinesCanDrag { get; set; } // 辅助线1拖动配置
            public string LineColor1 { get; set; } // 辅助线1颜色配置
            public int LineOpacity1 { get; set; } // 辅助线1透明度配置
            public int GuideLineDashStyle1 { get; set; } // 辅助线1样式配置
            public Rectangle DefaultBounds1 { get; set; } // 辅助线1默认位置配置
            
            // 辅助线2配置
            public bool GuideLinesEnabled2 { get; set; } // 辅助线2开关配置
            public bool GuideLinesCanDrag2 { get; set; } // 辅助线2拖动配置
            public string LineColor2 { get; set; } // 辅助线2颜色配置
            public int LineOpacity2 { get; set; } // 辅助线2透明度配置
            public int GuideLineDashStyle2 { get; set; } // 辅助线2样式配置
            public Rectangle DefaultBounds2 { get; set; } // 辅助线2默认位置配置
        }

        // Windows API for mouse click-through and topmost
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int GWL_EXSTYLE = (-20);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        // 置顶相关API
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_SHOWWINDOW = 0x0040;

        public BoundingBoxWindow(NotifyIcon existingTrayIcon)
        {
            this.trayIcon = existingTrayIcon;

            // 首先设置默认边界
            SetDefaultBounds();

            // 加载配置
            LoadConfig();

            // 初始化组件 - 不要重复定义InitializeComponent
            this.WindowStyle = WindowStyle.None;
            this.ShowInTaskbar = false;
            this.Topmost = true;
            this.AllowsTransparency = true;
            this.Opacity = 0;
            this.Width = 1;
            this.Height = 1;
            
            AddBoundingBoxMenuItems();
            AddGuideLinesMenuItems(); // 添加辅助线1菜单
            AddGuideLines2MenuItems(); // 添加辅助线2菜单
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
            AddColor1MenuItem(lineColorItem, "红色", System.Drawing.Color.Red);
            AddColor1MenuItem(lineColorItem, "绿色", System.Drawing.Color.Green);
            AddColor1MenuItem(lineColorItem, "蓝色", System.Drawing.Color.Blue);
            AddColor1MenuItem(lineColorItem, "黄色", System.Drawing.Color.Yellow);
            AddColor1MenuItem(lineColorItem, "橙色", System.Drawing.Color.Orange);
            AddColor1MenuItem(lineColorItem, "紫色", System.Drawing.Color.Purple);
            AddColor1MenuItem(lineColorItem, "青色", System.Drawing.Color.Cyan);
            AddColor1MenuItem(lineColorItem, "黑色", System.Drawing.Color.FromArgb(1, 1, 1));
            AddColor1MenuItem(lineColorItem, "白色", System.Drawing.Color.White);

            // 透明度菜单
            ToolStripMenuItem transparencyItem = new ToolStripMenuItem("线条透明度");
            AddTransparency1MenuItem(transparencyItem, "100% (不透明)", 100);
            AddTransparency1MenuItem(transparencyItem, "75%", 75);
            AddTransparency1MenuItem(transparencyItem, "50%", 50);
            AddTransparency1MenuItem(transparencyItem, "25%", 25);

            // 虚线样式菜单
            ToolStripMenuItem dashStyleItem = new ToolStripMenuItem("线条样式");
            AddDashStyleMenuItem(dashStyleItem, "实线", System.Drawing.Drawing2D.DashStyle.Solid);
            AddDashStyleMenuItem(dashStyleItem, "虚线", System.Drawing.Drawing2D.DashStyle.Dash);
            AddDashStyleMenuItem(dashStyleItem, "点线", System.Drawing.Drawing2D.DashStyle.Dot);
            AddDashStyleMenuItem(dashStyleItem, "点划线", System.Drawing.Drawing2D.DashStyle.DashDot);
            AddDashStyleMenuItem(dashStyleItem, "双点划线", System.Drawing.Drawing2D.DashStyle.DashDotDot);

            // 重置位置菜单
            ToolStripMenuItem resetPositionItem = new ToolStripMenuItem("重置位置", null, (s, e) => {
                ResetBoundingBox();
            });

            // 添加所有子菜单 - 移除辅助线选项
            boundingBoxMenu.DropDownItems.Add(toggleVisibilityItem);
            boundingBoxMenu.DropDownItems.Add(mousePenetrationItem);
            boundingBoxMenu.DropDownItems.Add(lineThicknessItem);
            boundingBoxMenu.DropDownItems.Add(lineColorItem);
            boundingBoxMenu.DropDownItems.Add(transparencyItem);
            boundingBoxMenu.DropDownItems.Add(dashStyleItem);
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

        // ===================
        // 辅助线1的完整方法系统
        // ===================

        private void AddGuideLinesMenuItems()
        {
            if (trayIcon?.ContextMenuStrip == null) return;

            ToolStripMenuItem guideLinesMenu = new ToolStripMenuItem("辅助线1");

            // 仅显示辅助线
            ToolStripMenuItem guideLinesOnlyItem = new ToolStripMenuItem("显示辅助线", null, (s, e) => {
                ToggleGuideLinesOnly();
            });
            guideLinesOnlyItem.Checked = isGuideLinesOnlyMode;

            // 辅助线拖动开关
            var guideLineDragItem = new ToolStripMenuItem("辅助线可拖动", null, (s, e) => {
                ToggleGuideLinesDrag();
                if (s is ToolStripMenuItem menuItem)
                {
                    menuItem.Checked = guideLinesCanDrag;
                }
            });
            guideLineDragItem.Checked = guideLinesCanDrag;

            // 线条粗细菜单（共用lineThickness）
            ToolStripMenuItem lineThicknessItem = new ToolStripMenuItem("线条粗细");
            AddThicknessMenuItem(lineThicknessItem, "细线 (1像素)", 1);
            AddThicknessMenuItem(lineThicknessItem, "中等 (2像素)", 2);
            AddThicknessMenuItem(lineThicknessItem, "粗线 (3像素)", 3);
            AddThicknessMenuItem(lineThicknessItem, "很粗 (5像素)", 5);

            // 线条颜色菜单
            ToolStripMenuItem lineColorItem = new ToolStripMenuItem("线条颜色");
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
            ToolStripMenuItem transparencyItem = new ToolStripMenuItem("线条透明度");
            AddTransparencyMenuItem(transparencyItem, "100% (不透明)", 100);
            AddTransparencyMenuItem(transparencyItem, "75%", 75);
            AddTransparencyMenuItem(transparencyItem, "50%", 50);
            AddTransparencyMenuItem(transparencyItem, "25%", 25);

            // 辅助线样式菜单
            ToolStripMenuItem guideLineDashStyleItem = new ToolStripMenuItem("线条样式");
            AddGuideLineDashStyleMenuItem(guideLineDashStyleItem, "实线", System.Drawing.Drawing2D.DashStyle.Solid);
            AddGuideLineDashStyleMenuItem(guideLineDashStyleItem, "虚线", System.Drawing.Drawing2D.DashStyle.Dash);
            AddGuideLineDashStyleMenuItem(guideLineDashStyleItem, "点线", System.Drawing.Drawing2D.DashStyle.Dot);
            AddGuideLineDashStyleMenuItem(guideLineDashStyleItem, "点划线", System.Drawing.Drawing2D.DashStyle.DashDot);
            AddGuideLineDashStyleMenuItem(guideLineDashStyleItem, "双点划线", System.Drawing.Drawing2D.DashStyle.DashDotDot);

            // 重置位置菜单
            ToolStripMenuItem resetPositionItem = new ToolStripMenuItem("重置位置", null, (s, e) => {
                ResetGuideLines();
            });

            // 添加所有子菜单
            guideLinesMenu.DropDownItems.Add(guideLinesOnlyItem);
            guideLinesMenu.DropDownItems.Add(new ToolStripSeparator());
            guideLinesMenu.DropDownItems.Add(guideLineDragItem);
            guideLinesMenu.DropDownItems.Add(lineThicknessItem);
            guideLinesMenu.DropDownItems.Add(lineColorItem);
            guideLinesMenu.DropDownItems.Add(transparencyItem);
            guideLinesMenu.DropDownItems.Add(guideLineDashStyleItem);
            guideLinesMenu.DropDownItems.Add(new ToolStripSeparator());
            guideLinesMenu.DropDownItems.Add(resetPositionItem);

            // 在包围框菜单之后插入辅助线菜单
            int insertIndex = -1;
            for (int i = 0; i < trayIcon.ContextMenuStrip.Items.Count; i++)
            {
                if (trayIcon.ContextMenuStrip.Items[i] is ToolStripMenuItem menuItem && 
                    menuItem.Text == "包围框")
                {
                    insertIndex = i + 1;
                    break;
                }
            }

            if (insertIndex != -1)
            {
                trayIcon.ContextMenuStrip.Items.Insert(insertIndex, guideLinesMenu);
            }
            else
            {
                // 如果找不到包围框菜单，就在分隔符前插入
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
                    trayIcon.ContextMenuStrip.Items.Insert(separatorIndex, guideLinesMenu);
                }
                else
                {
                    trayIcon.ContextMenuStrip.Items.Add(guideLinesMenu);
                }
            }
        }

        // ===================
        // 辅助线2的完整方法系统（复制辅助线1并完全替换名称）
        // ===================

        private void AddGuideLines2MenuItems()
        {
            if (trayIcon?.ContextMenuStrip == null) return;

            ToolStripMenuItem guideLinesMenu2 = new ToolStripMenuItem("辅助线2");

            // 仅显示辅助线2
            ToolStripMenuItem guideLinesOnlyItem2 = new ToolStripMenuItem("显示辅助线", null, (s, e) => {
                ToggleGuideLinesOnly2();
            });
            guideLinesOnlyItem2.Checked = isGuideLinesOnlyMode2;

            // 辅助线2拖动开关
            var guideLineDragItem2 = new ToolStripMenuItem("辅助线可拖动", null, (s, e) => {
                ToggleGuideLinesDrag2();
                if (s is ToolStripMenuItem menuItem)
                {
                    menuItem.Checked = guideLinesCanDrag2;
                }
            });
            guideLineDragItem2.Checked = guideLinesCanDrag2;

            // 线条粗细菜单（共用lineThickness）
            ToolStripMenuItem lineThicknessItem2 = new ToolStripMenuItem("线条粗细");
            AddThickness2MenuItem(lineThicknessItem2, "细线 (1像素)", 1);
            AddThickness2MenuItem(lineThicknessItem2, "中等 (2像素)", 2);
            AddThickness2MenuItem(lineThicknessItem2, "粗线 (3像素)", 3);
            AddThickness2MenuItem(lineThicknessItem2, "很粗 (5像素)", 5);

            // 线条颜色菜单
            ToolStripMenuItem lineColorItem2 = new ToolStripMenuItem("线条颜色");
            AddColor2MenuItem(lineColorItem2, "红色", System.Drawing.Color.Red);
            AddColor2MenuItem(lineColorItem2, "绿色", System.Drawing.Color.Green);
            AddColor2MenuItem(lineColorItem2, "蓝色", System.Drawing.Color.Blue);
            AddColor2MenuItem(lineColorItem2, "黄色", System.Drawing.Color.Yellow);
            AddColor2MenuItem(lineColorItem2, "橙色", System.Drawing.Color.Orange);
            AddColor2MenuItem(lineColorItem2, "紫色", System.Drawing.Color.Purple);
            AddColor2MenuItem(lineColorItem2, "青色", System.Drawing.Color.Cyan);
            AddColor2MenuItem(lineColorItem2, "鲜绿色", System.Drawing.Color.LimeGreen);
            AddColor2MenuItem(lineColorItem2, "黑色", System.Drawing.Color.FromArgb(1, 1, 1));
            AddColor2MenuItem(lineColorItem2, "白色", System.Drawing.Color.White);

            // 透明度菜单
            ToolStripMenuItem transparencyItem2 = new ToolStripMenuItem("线条透明度");
            AddTransparency2MenuItem(transparencyItem2, "100% (不透明)", 100);
            AddTransparency2MenuItem(transparencyItem2, "75%", 75);
            AddTransparency2MenuItem(transparencyItem2, "50%", 50);
            AddTransparency2MenuItem(transparencyItem2, "25%", 25);

            // 辅助线样式菜单
            ToolStripMenuItem guideLineDashStyleItem2 = new ToolStripMenuItem("线条样式");
            AddGuideLineDashStyle2MenuItem(guideLineDashStyleItem2, "实线", System.Drawing.Drawing2D.DashStyle.Solid);
            AddGuideLineDashStyle2MenuItem(guideLineDashStyleItem2, "虚线", System.Drawing.Drawing2D.DashStyle.Dash);
            AddGuideLineDashStyle2MenuItem(guideLineDashStyleItem2, "点线", System.Drawing.Drawing2D.DashStyle.Dot);
            AddGuideLineDashStyle2MenuItem(guideLineDashStyleItem2, "点划线", System.Drawing.Drawing2D.DashStyle.DashDot);
            AddGuideLineDashStyle2MenuItem(guideLineDashStyleItem2, "双点划线", System.Drawing.Drawing2D.DashStyle.DashDotDot);

            // 重置位置菜单
            ToolStripMenuItem resetPositionItem2 = new ToolStripMenuItem("重置位置", null, (s, e) => {
                ResetGuideLines2();
            });

            // 添加所有子菜单
            guideLinesMenu2.DropDownItems.Add(guideLinesOnlyItem2);
            guideLinesMenu2.DropDownItems.Add(new ToolStripSeparator());
            guideLinesMenu2.DropDownItems.Add(guideLineDragItem2);
            guideLinesMenu2.DropDownItems.Add(lineThicknessItem2);
            guideLinesMenu2.DropDownItems.Add(lineColorItem2);
            guideLinesMenu2.DropDownItems.Add(transparencyItem2);
            guideLinesMenu2.DropDownItems.Add(guideLineDashStyleItem2);
            guideLinesMenu2.DropDownItems.Add(new ToolStripSeparator());
            guideLinesMenu2.DropDownItems.Add(resetPositionItem2);

            // 在辅助线1菜单之后插入辅助线2菜单
            int insertIndex = -1;
            for (int i = 0; i < trayIcon.ContextMenuStrip.Items.Count; i++)
            {
                if (trayIcon.ContextMenuStrip.Items[i] is ToolStripMenuItem menuItem && 
                    menuItem.Text == "辅助线1")
                {
                    insertIndex = i + 1;
                    break;
                }
            }

            if (insertIndex != -1)
            {
                trayIcon.ContextMenuStrip.Items.Insert(insertIndex, guideLinesMenu2);
            }
            else
            {
                // 如果找不到辅助线1菜单，就在分隔符前插入
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
                    trayIcon.ContextMenuStrip.Items.Insert(separatorIndex, guideLinesMenu2);
                }
                else
                {
                    trayIcon.ContextMenuStrip.Items.Add(guideLinesMenu2);
                }
            }
        }

        // 继续在下一个编辑中完成所有方法...

        // ===================
        // 通用菜单项添加方法
        // ===================

        private void AddThicknessMenuItem(ToolStripMenuItem parent, string text, int thickness)
        {
            var item = new ToolStripMenuItem(text, null, (s, e) => {
                ChangeLineThickness(thickness);
            });
            item.Checked = (thickness == lineThickness);
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

        private void AddDashStyleMenuItem(ToolStripMenuItem parent, string name, System.Drawing.Drawing2D.DashStyle style)
        {
            var item = new ToolStripMenuItem(name, null, (s, e) => {
                ChangeDashStyle(style);
            });
            item.Checked = style.Equals(dashStyle);
            parent.DropDownItems.Add(item);
        }

        private void AddGuideLineDashStyleMenuItem(ToolStripMenuItem parent, string name, System.Drawing.Drawing2D.DashStyle style)
        {
            var item = new ToolStripMenuItem(name, null, (s, e) => {
                ChangeGuideLineDashStyle(style);
            });
            item.Checked = style.Equals(guideLineDashStyle1);
            parent.DropDownItems.Add(item);
        }

        // ===================
        // 辅助线2的菜单项添加方法
        // ===================

        private void AddThickness2MenuItem(ToolStripMenuItem parent, string text, int thickness)
        {
            var item = new ToolStripMenuItem(text, null, (s, e) => {
                ChangeLineThickness2(thickness);
            });
            item.Checked = (thickness == lineThickness);
            parent.DropDownItems.Add(item);
        }

        private void AddColor2MenuItem(ToolStripMenuItem parent, string name, System.Drawing.Color color)
        {
            var colorPreview = new System.Drawing.Bitmap(16, 16);
            using (var g = System.Drawing.Graphics.FromImage(colorPreview))
            {
                g.FillRectangle(new System.Drawing.SolidBrush(color), 0, 0, 16, 16);
                g.DrawRectangle(System.Drawing.Pens.Gray, 0, 0, 15, 15);
            }

            var item = new ToolStripMenuItem(name, colorPreview, (s, e) => {
                ChangeLineColor2(color);
            });
            item.Checked = color.Equals(lineColor2);
            parent.DropDownItems.Add(item);
        }

        private void AddTransparency2MenuItem(ToolStripMenuItem parent, string name, int value)
        {
            var item = new ToolStripMenuItem(name, null, (s, e) => {
                ChangeLineTransparency2(value);
            });
            item.Checked = (value == lineOpacity2);
            parent.DropDownItems.Add(item);
        }

        private void AddGuideLineDashStyle2MenuItem(ToolStripMenuItem parent, string name, System.Drawing.Drawing2D.DashStyle style)
        {
            var item = new ToolStripMenuItem(name, null, (s, e) => {
                ChangeGuideLineDashStyle2(style);
            });
            item.Checked = style.Equals(guideLineDashStyle2);
            parent.DropDownItems.Add(item);
        }

        // ===================
        // 包围框相关方法
        // ===================

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
            var currentScreen = Screen.FromPoint(System.Windows.Forms.Cursor.Position);
            
            // 移除对辅助线1的干扰
            // 如果当前是仅辅助线模式，先关闭它
            // if (isGuideLinesOnlyMode)
            // {
            //     HideGuideLinesOnly();
            // }
            
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

            // 创建包围框 - 移除回调，包围框不再控制辅助线1
            boundingBox = new DraggableBoundingBox(bounds, lineColor, lineThickness, lineOpacity / 100.0, mouseClickThrough, dashStyle);
            boundingBox.Show();
            
            // 移除自动创建辅助线的逻辑
            // 创建辅助线 - 使用包围框的实际位置
            // if (guideLinesEnabled)
            // {
            //     CreateGuideLines(currentScreen, new Rectangle((int)boundingBox.Left, (int)boundingBox.Top, (int)boundingBox.Width, (int)boundingBox.Height));
            // }
            
            isVisible = true;
        }

        private void HideBoundingBox()
        {
            if (boundingBox != null) 
            { 
                boundingBox.Close(); 
                boundingBox = null; 
            }
            
            // 移除对辅助线1的控制
            // 同时隐藏辅助线
            // HideGuideLines();
            
            isVisible = false;
            // 移除对辅助线1菜单的更新
            // UpdateGuideLinesOnlyMenuText(); // 更新仅辅助线菜单状态
        }

        private void ResetBoundingBox()
        {
            var currentScreen = Screen.FromPoint(System.Windows.Forms.Cursor.Position);
            
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
            
            // 移除对辅助线1的影响
            // 如果在仅辅助线模式下，需要刷新辅助线
            // if (isGuideLinesOnlyMode || (isVisible && guideLinesEnabled))
            // {
            //     UpdateGuideLineColors();
            // }
            
            UpdateThicknessMenuCheckedState();
            SaveConfig();
        }

        private void ChangeLineColor(System.Drawing.Color color)
        {
            lineColor = color;
            
            if (boundingBox != null) boundingBox.SetLineColor(color);
            
            // 移除对辅助线1的影响
            // 如果在仅辅助线模式下，或者包围框模式下启用了辅助线，需要刷新辅助线
            // if (isGuideLinesOnlyMode || (isVisible && guideLinesEnabled))
            // {
            //     UpdateGuideLineColors();
            // }
            
            UpdateColorMenuCheckedState();
            SaveConfig();
        }

        private void ChangeLineTransparency(int value)
        {
            lineOpacity = value;
            
            if (boundingBox != null)
            {
                boundingBox.Opacity = value / 100.0;
            }
            
            UpdateTransparencyMenuCheckedState();
            SaveConfig();
        }

        private void ChangeDashStyle(System.Drawing.Drawing2D.DashStyle style)
        {
            dashStyle = style;
            
            if (boundingBox != null)
            {
                boundingBox.SetDashStyle(style);
            }
            
            UpdateDashStyleMenuCheckedState();
            SaveConfig();
        }

        /// <summary>
        /// 创建辅助线1 - 使用独立的配置
        /// </summary>
        private void CreateGuideLines(Screen screen, Rectangle boundingRect)
        {
            // 清理现有辅助线
            HideGuideLines();

            // 辅助线1使用独立的颜色、透明度和样式
            double guideOpacity = Math.Max(0.3, (lineOpacity1 / 100.0) * 0.7); // 至少30%透明度，最多70%原透明度
            var guideColor = lineColor1; // 使用辅助线1独立颜色

            // 顶部辅助线 - 从包围框顶边延伸到屏幕两侧
            topGuide = new DraggableGuideLine(
                screen.Bounds.X, boundingRect.Y, screen.Bounds.Width, lineThickness,
                guideColor, guideOpacity, true, guideLinesCanDrag, guideLineDashStyle1, OnTopGuidePositionChanged
            );

            // 底部辅助线 - 从包围框底边延伸到屏幕两侧
            bottomGuide = new DraggableGuideLine(
                screen.Bounds.X, boundingRect.Bottom - lineThickness, screen.Bounds.Width, lineThickness,
                guideColor, guideOpacity, true, guideLinesCanDrag, guideLineDashStyle1, OnBottomGuidePositionChanged
            );

            // 左侧辅助线 - 从包围框左边延伸到屏幕上下
            leftGuide = new DraggableGuideLine(
                boundingRect.X, screen.Bounds.Y, lineThickness, screen.Bounds.Height,
                guideColor, guideOpacity, false, guideLinesCanDrag, guideLineDashStyle1, OnLeftGuidePositionChanged
            );

            // 右侧辅助线 - 从包围框右边延伸到屏幕上下
            rightGuide = new DraggableGuideLine(
                boundingRect.Right - lineThickness, screen.Bounds.Y, lineThickness, screen.Bounds.Height,
                guideColor, guideOpacity, false, guideLinesCanDrag, guideLineDashStyle1, OnRightGuidePositionChanged
            );

            // 显示所有辅助线
            topGuide?.Show();
            bottomGuide?.Show();
            leftGuide?.Show();
            rightGuide?.Show();

            // 移除创建交叉点
            // 创建交叉点
            // CreateIntersections(boundingRect, guideColor, guideOpacity);
        }

        /// <summary>
        /// 隐藏所有辅助线
        /// </summary>
        private void HideGuideLines()
        {
            var guides = new[] { topGuide, bottomGuide, leftGuide, rightGuide };
            foreach (var guide in guides)
            {
                if (guide != null)
                {
                    guide.Close();
                }
            }
            topGuide = bottomGuide = leftGuide = rightGuide = null;
            
            // 移除隐藏交叉点
            // 同时隐藏交叉点
            // HideIntersections();
        }

        /// <summary>
        /// 隐藏所有交叉点
        /// </summary>
        // private void HideIntersections()
        // {
        //     var intersections = new[] { topLeftIntersection, topRightIntersection, bottomLeftIntersection, bottomRightIntersection };
        //     foreach (var intersection in intersections)
        //     {
        //         if (intersection != null)
        //         {
        //             intersection.Close();
        //         }
        //     }
        //     topLeftIntersection = topRightIntersection = bottomLeftIntersection = bottomRightIntersection = null;
        // }

        /// <summary>
        /// 更新辅助线位置（当包围框移动或调整大小时调用）
        /// </summary>
        private void UpdateGuideLines()
        {
            if (!guideLinesEnabled || !isVisible || boundingBox == null) return;

            var currentScreen = Screen.FromPoint(System.Windows.Forms.Cursor.Position);
            CreateGuideLines(currentScreen, new Rectangle((int)boundingBox.Left, (int)boundingBox.Top, (int)boundingBox.Width, (int)boundingBox.Height));
        }

        private void ResetGuideLines()
        {
            var currentScreen = Screen.FromPoint(System.Windows.Forms.Cursor.Position);
            
            // 重置到当前屏幕中央，大约屏幕大小的一半
            int width = currentScreen.Bounds.Width / 2;
            int height = currentScreen.Bounds.Height / 2;
            
            defaultBounds = new Rectangle(
                currentScreen.Bounds.X + (currentScreen.Bounds.Width - width) / 2,
                currentScreen.Bounds.Y + (currentScreen.Bounds.Height - height) / 2,
                width,
                height
            );

            if (isGuideLinesOnlyMode)
            {
                HideGuideLinesOnly();
                ShowGuideLinesOnly();
            }
            
            SaveConfig();
        }

        /// <summary>
        /// 切换辅助线拖动功能
        /// </summary>
        private void ToggleGuideLinesDrag()
        {
            guideLinesCanDrag = !guideLinesCanDrag;
            
            // 更新现有辅助线的拖动状态
            var guides = new[] { topGuide, bottomGuide, leftGuide, rightGuide };
            foreach (var guide in guides)
            {
                guide?.SetDraggable(guideLinesCanDrag);
            }
            
            // 移除更新交叉点拖动状态
            // 更新交叉点的拖动状态
            // var intersections = new[] { topLeftIntersection, topRightIntersection, bottomLeftIntersection, bottomRightIntersection };
            // foreach (var intersection in intersections)
            // {
            //     intersection?.SetDraggable(guideLinesCanDrag);
            // }
            
            SaveConfig();
        }

        private void ChangeGuideLineDashStyle(System.Drawing.Drawing2D.DashStyle style)
        {
            guideLineDashStyle1 = style;
            
            if ((isVisible && boundingBox != null) || isGuideLinesOnlyMode)
            {
                UpdateGuideLineColors();
            }
            
            SaveConfig();
        }

        private void UpdateGuideLineColors()
        {
            if (!isGuideLinesOnlyMode) return; // 简化条件，只在辅助线1独立模式下才需要

            // 重新创建辅助线以应用新颜色或样式
            var currentScreen = Screen.FromPoint(System.Windows.Forms.Cursor.Position);
            
            Rectangle bounds;
            if (topGuide != null && leftGuide != null)
            {
                // 基于当前辅助线位置计算边界
                bounds = new Rectangle(
                    (int)leftGuide.Left,
                    (int)topGuide.Top,
                    (int)(rightGuide != null ? rightGuide.Left - leftGuide.Left + lineThickness : defaultBounds1.Width),
                    (int)(bottomGuide != null ? bottomGuide.Top - topGuide.Top + lineThickness : defaultBounds1.Height)
                );
            }
            else
            {
                bounds = defaultBounds1; // 使用辅助线1的默认边界
            }
            
            CreateGuideLines(currentScreen, bounds);
        }

        // ===================
        // 辅助线1位置变化回调方法
        // ===================

        /// <summary>
        /// 顶部辅助线位置变化回调 - 辅助线1独立移动，不影响包围框
        /// </summary>
        private void OnTopGuidePositionChanged(double left, double top)
        {
            // 辅助线1独立移动，不影响包围框
            // 只需要更新其他辅助线1的位置
            UpdateOtherGuideLines("top");
        }

        /// <summary>
        /// 底部辅助线位置变化回调 - 辅助线1独立移动，不影响包围框
        /// </summary>
        private void OnBottomGuidePositionChanged(double left, double top)
        {
            // 辅助线1独立移动，不影响包围框
            UpdateOtherGuideLines("bottom");
        }

        /// <summary>
        /// 左侧辅助线位置变化回调 - 辅助线1独立移动，不影响包围框
        /// </summary>
        private void OnLeftGuidePositionChanged(double left, double top)
        {
            // 辅助线1独立移动，不影响包围框
            UpdateOtherGuideLines("left");
        }

        /// <summary>
        /// 右侧辅助线位置变化回调 - 辅助线1独立移动，不影响包围框
        /// </summary>
        private void OnRightGuidePositionChanged(double left, double top)
        {
            // 辅助线1独立移动，不影响包围框
            UpdateOtherGuideLines("right");
        }

        /// <summary>
        /// 更新其他辅助线1位置（当某条辅助线1被拖动时）- 完全独立于包围框
        /// </summary>
        private void UpdateOtherGuideLines(string excludeGuide)
        {
            // 辅助线1独立移动，不依赖包围框
            var currentScreen = Screen.FromPoint(System.Windows.Forms.Cursor.Position);
            
            // 计算当前辅助线1的矩形边界
            if (topGuide != null && leftGuide != null && bottomGuide != null && rightGuide != null)
            {
                int left = (int)leftGuide.Left;
                int top = (int)topGuide.Top;
                int right = (int)(rightGuide.Left + lineThickness);
                int bottom = (int)(bottomGuide.Top + lineThickness);
                
                if (excludeGuide != "top" && topGuide != null)
                {
                    topGuide.UpdatePosition(currentScreen.Bounds.X, top, 
                        currentScreen.Bounds.Width, lineThickness);
                }
                
                if (excludeGuide != "bottom" && bottomGuide != null)
                {
                    bottomGuide.UpdatePosition(currentScreen.Bounds.X, bottom - lineThickness, 
                        currentScreen.Bounds.Width, lineThickness);
                }
                
                if (excludeGuide != "left" && leftGuide != null)
                {
                    leftGuide.UpdatePosition(left, currentScreen.Bounds.Y, 
                        lineThickness, currentScreen.Bounds.Height);
                }
                
                if (excludeGuide != "right" && rightGuide != null)
                {
                    rightGuide.UpdatePosition(right - lineThickness, currentScreen.Bounds.Y, 
                        lineThickness, currentScreen.Bounds.Height);
                }
            }
        }

        // ===================
        // 辅助线2的完整方法系统（复制辅助线1并完全替换名称）
        // ===================

        private void ToggleGuideLinesOnly2()
        {
            if (isGuideLinesOnlyMode2)
            {
                HideGuideLinesOnly2();
            }
            else
            {
                ShowGuideLinesOnly2();
            }
            UpdateGuideLinesOnlyMenuText2();
        }

        private void ShowGuideLinesOnly2()
        {
            // 辅助线2完全独立显示，不再影响包围框
            // 移除：if (isVisible) { HideBoundingBox(); }

            // 如果已经有辅助线2存在但被隐藏了，直接显示它们
            if (topGuide2 != null && leftGuide2 != null && 
                topGuide2.Visibility == Visibility.Hidden)
            {
                var guides2 = new[] { topGuide2, bottomGuide2, leftGuide2, rightGuide2 };
                foreach (var guide in guides2)
                {
                    if (guide != null)
                    {
                        guide.Show();
                    }
                }
            }
            else
            {
                // 否则创建新的辅助线2
                var currentScreen = Screen.FromPoint(System.Windows.Forms.Cursor.Position);
                
                Rectangle bounds2;
                
                // 如果当前有辅助线2存在，基于当前辅助线的位置计算边界
                if (topGuide2 != null && leftGuide2 != null)
                {
                    // 基于当前辅助线位置计算边界
                    bounds2 = new Rectangle(
                        (int)leftGuide2.Left,
                        (int)topGuide2.Top,
                        (int)(rightGuide2 != null ? rightGuide2.Left - leftGuide2.Left + lineThickness : defaultBounds2.Width),
                        (int)(bottomGuide2 != null ? bottomGuide2.Top - topGuide2.Top + lineThickness : defaultBounds2.Height)
                    );
                }
                else
                {
                    // 使用默认边界2
                    bounds2 = defaultBounds2;
                    
                    // 如果默认位置超出当前屏幕，调整位置
                    if (bounds2.Right > currentScreen.Bounds.Right)
                        bounds2.X = currentScreen.Bounds.Right - bounds2.Width;
                    if (bounds2.Bottom > currentScreen.Bounds.Bottom)
                        bounds2.Y = currentScreen.Bounds.Bottom - bounds2.Height;
                    if (bounds2.X < currentScreen.Bounds.X)
                        bounds2.X = currentScreen.Bounds.X;
                    if (bounds2.Y < currentScreen.Bounds.Y)
                        bounds2.Y = currentScreen.Bounds.Y;

                    // 确保最小大小
                    if (bounds2.Width < 100) bounds2.Width = 100;
                    if (bounds2.Height < 100) bounds2.Height = 100;
                }

                // 只创建辅助线2，不创建包围框
                CreateGuideLines2(currentScreen, bounds2);
            }
            
            isGuideLinesOnlyMode2 = true;
        }

        private void HideGuideLinesOnly2()
        {
            // 只隐藏辅助线2，不销毁它们，这样重新显示时可以保持位置
            HideGuideLinesTemporary2();
            isGuideLinesOnlyMode2 = false;
            // 只更新辅助线2自己的菜单状态，不影响包围框
            UpdateGuideLinesOnlyMenuText2();
        }

        /// <summary>
        /// 临时隐藏所有辅助线2（不销毁对象）
        /// </summary>
        private void HideGuideLinesTemporary2()
        {
            var guides2 = new[] { topGuide2, bottomGuide2, leftGuide2, rightGuide2 };
            foreach (var guide in guides2)
            {
                if (guide != null)
                {
                    guide.Hide();
                }
            }
            
            // 移除交叉点2隐藏代码
            // 同时隐藏交叉点2
            // var intersections2 = new[] { topLeftIntersection2, topRightIntersection2, bottomLeftIntersection2, bottomRightIntersection2 };
            // foreach (var intersection in intersections2)
            // {
            //     if (intersection != null)
            //     {
            //         intersection.Hide();
            //     }
            // }
        }

        /// <summary>
        /// 创建辅助线2
        /// </summary>
        private void CreateGuideLines2(Screen screen, Rectangle boundingRect)
        {
            // 清理现有辅助线2
            HideGuideLines2();

            // 辅助线2使用独立的颜色和透明度
            double guideOpacity2 = Math.Max(0.3, (lineOpacity2 / 100.0) * 0.7); // 至少30%透明度，最多70%原透明度
            var guideColor2 = lineColor2;

            // 顶部辅助线2 - 从包围框顶边延伸到屏幕两侧
            topGuide2 = new DraggableGuideLine(
                screen.Bounds.X, boundingRect.Y, screen.Bounds.Width, lineThickness,
                guideColor2, guideOpacity2, true, guideLinesCanDrag2, guideLineDashStyle2, OnTopGuide2PositionChanged
            );

            // 底部辅助线2 - 从包围框底边延伸到屏幕两侧
            bottomGuide2 = new DraggableGuideLine(
                screen.Bounds.X, boundingRect.Bottom - lineThickness, screen.Bounds.Width, lineThickness,
                guideColor2, guideOpacity2, true, guideLinesCanDrag2, guideLineDashStyle2, OnBottomGuide2PositionChanged
            );

            // 左侧辅助线2 - 从包围框左边延伸到屏幕上下
            leftGuide2 = new DraggableGuideLine(
                boundingRect.X, screen.Bounds.Y, lineThickness, screen.Bounds.Height,
                guideColor2, guideOpacity2, false, guideLinesCanDrag2, guideLineDashStyle2, OnLeftGuide2PositionChanged
            );

            // 右侧辅助线2 - 从包围框右边延伸到屏幕上下
            rightGuide2 = new DraggableGuideLine(
                boundingRect.Right - lineThickness, screen.Bounds.Y, lineThickness, screen.Bounds.Height,
                guideColor2, guideOpacity2, false, guideLinesCanDrag2, guideLineDashStyle2, OnRightGuide2PositionChanged
            );

            // 显示所有辅助线2
            topGuide2?.Show();
            bottomGuide2?.Show();
            leftGuide2?.Show();
            rightGuide2?.Show();

            // 移除创建交叉点2
            // 创建交叉点2
            // CreateIntersections2(boundingRect, guideColor2, guideOpacity2);
        }

        /// <summary>
        /// 隐藏所有辅助线2
        /// </summary>
        private void HideGuideLines2()
        {
            var guides2 = new[] { topGuide2, bottomGuide2, leftGuide2, rightGuide2 };
            foreach (var guide in guides2)
            {
                if (guide != null)
                {
                    guide.Close();
                }
            }
            topGuide2 = bottomGuide2 = leftGuide2 = rightGuide2 = null;
            
            // 移除隐藏交叉点2
            // 同时隐藏交叉点2
            // HideIntersections2();
        }

        /// <summary>
        /// 隐藏所有交叉点2
        /// </summary>
        // private void HideIntersections2()
        // {
        //     var intersections2 = new[] { topLeftIntersection2, topRightIntersection2, bottomLeftIntersection2, bottomRightIntersection2 };
        //     foreach (var intersection in intersections2)
        //     {
        //         if (intersection != null)
        //         {
        //             intersection.Close();
        //         }
        //     }
        //     topLeftIntersection2 = topRightIntersection2 = bottomLeftIntersection2 = bottomRightIntersection2 = null;
        // }

        private void ResetGuideLines2()
        {
            var currentScreen = Screen.FromPoint(System.Windows.Forms.Cursor.Position);
            
            // 重置到当前屏幕中央，比辅助线1有偏移
            int width = currentScreen.Bounds.Width / 2;
            int height = currentScreen.Bounds.Height / 2;
            int offset = Math.Min(100, Math.Min(width / 6, height / 6)); // 偏移量
            
            defaultBounds2 = new Rectangle(
                currentScreen.Bounds.X + (currentScreen.Bounds.Width - width) / 2 + offset, // 向右偏移
                currentScreen.Bounds.Y + (currentScreen.Bounds.Height - height) / 2 + offset, // 向下偏移
                width - offset, // 稍微缩小宽度
                height - offset // 稍微缩小高度
            );

            if (isGuideLinesOnlyMode2)
            {
                HideGuideLinesOnly2();
                ShowGuideLinesOnly2();
            }
            
            SaveConfig();
        }

        /// <summary>
        /// 切换辅助线2拖动功能
        /// </summary>
        private void ToggleGuideLinesDrag2()
        {
            guideLinesCanDrag2 = !guideLinesCanDrag2;
            
            // 更新现有辅助线2的拖动状态
            var guides2 = new[] { topGuide2, bottomGuide2, leftGuide2, rightGuide2 };
            foreach (var guide in guides2)
            {
                guide?.SetDraggable(guideLinesCanDrag2);
            }
            
            // 移除更新交叉点2拖动状态
            // 更新交叉点2的拖动状态
            // var intersections2 = new[] { topLeftIntersection2, topRightIntersection2, bottomLeftIntersection2, bottomRightIntersection2 };
            // foreach (var intersection in intersections2)
            // {
            //     intersection?.SetDraggable(guideLinesCanDrag2);
            // }
            
            SaveConfig();
        }

        private void ChangeLineThickness2(int thickness)
        {
            lineThickness = thickness; // 共用lineThickness
            
            // 如果在仅辅助线2模式下，需要刷新辅助线2
            if (isGuideLinesOnlyMode2)
            {
                UpdateGuideLineColors2();
            }
            
            UpdateThicknessMenuCheckedState2();
            SaveConfig();
        }

        private void ChangeLineColor2(System.Drawing.Color color)
        {
            lineColor2 = color;
            
            // 如果在仅辅助线2模式下，需要刷新辅助线2
            if (isGuideLinesOnlyMode2)
            {
                UpdateGuideLineColors2();
            }
            
            UpdateColorMenuCheckedState2();
            SaveConfig();
        }

        private void ChangeLineTransparency2(int value)
        {
            lineOpacity2 = value;
            
            // 如果在仅辅助线2模式下，需要刷新辅助线2
            if (isGuideLinesOnlyMode2)
            {
                UpdateGuideLineColors2();
            }
            
            UpdateTransparencyMenuCheckedState2();
            SaveConfig();
        }

        private void ChangeGuideLineDashStyle2(System.Drawing.Drawing2D.DashStyle style)
        {
            guideLineDashStyle2 = style;
            
            if (isGuideLinesOnlyMode2)
            {
                UpdateGuideLineColors2();
            }
            
            SaveConfig();
        }

        private void UpdateGuideLineColors2()
        {
            if (!isGuideLinesOnlyMode2) return;

            // 重新创建辅助线2以应用新颜色或样式
            var currentScreen = Screen.FromPoint(System.Windows.Forms.Cursor.Position);
            
            Rectangle bounds2;
            if (topGuide2 != null && leftGuide2 != null)
            {
                // 基于当前辅助线位置计算边界
                bounds2 = new Rectangle(
                    (int)leftGuide2.Left,
                    (int)topGuide2.Top,
                    (int)(rightGuide2 != null ? rightGuide2.Left - leftGuide2.Left + lineThickness : defaultBounds2.Width),
                    (int)(bottomGuide2 != null ? bottomGuide2.Top - topGuide2.Top + lineThickness : defaultBounds2.Height)
                );
            }
            else
            {
                bounds2 = defaultBounds2;
            }
            
            CreateGuideLines2(currentScreen, bounds2);
        }

        // ===================
        // 辅助线2位置变化回调方法（独立于包围框）
        // ===================

        /// <summary>
        /// 顶部辅助线2位置变化回调（独立移动，不影响包围框）
        /// </summary>
        private void OnTopGuide2PositionChanged(double left, double top)
        {
            // 辅助线2独立移动，不影响包围框
            // 只需要更新其他辅助线2的位置
            UpdateOtherGuideLines2("top");
        }

        /// <summary>
        /// 底部辅助线2位置变化回调
        /// </summary>
        private void OnBottomGuide2PositionChanged(double left, double top)
        {
            // 辅助线2独立移动，不影响包围框
            UpdateOtherGuideLines2("bottom");
        }

        /// <summary>
        /// 左侧辅助线2位置变化回调
        /// </summary>
        private void OnLeftGuide2PositionChanged(double left, double top)
        {
            // 辅助线2独立移动，不影响包围框
            UpdateOtherGuideLines2("left");
        }

        /// <summary>
        /// 右侧辅助线2位置变化回调
        /// </summary>
        private void OnRightGuide2PositionChanged(double left, double top)
        {
            // 辅助线2独立移动，不影响包围框
            UpdateOtherGuideLines2("right");
        }

        /// <summary>
        /// 更新其他辅助线2位置（当某条辅助线2被拖动时）
        /// </summary>
        private void UpdateOtherGuideLines2(string excludeGuide)
        {
            // 辅助线2独立移动，不依赖包围框
            var currentScreen = Screen.FromPoint(System.Windows.Forms.Cursor.Position);
            
            // 计算当前辅助线2的矩形边界
            if (topGuide2 != null && leftGuide2 != null && bottomGuide2 != null && rightGuide2 != null)
            {
                int left = (int)leftGuide2.Left;
                int top = (int)topGuide2.Top;
                int right = (int)(rightGuide2.Left + lineThickness);
                int bottom = (int)(bottomGuide2.Top + lineThickness);
                
                if (excludeGuide != "top" && topGuide2 != null)
                {
                    topGuide2.UpdatePosition(currentScreen.Bounds.X, top, 
                        currentScreen.Bounds.Width, lineThickness);
                }
                
                if (excludeGuide != "bottom" && bottomGuide2 != null)
                {
                    bottomGuide2.UpdatePosition(currentScreen.Bounds.X, bottom - lineThickness, 
                        currentScreen.Bounds.Width, lineThickness);
                }
                
                if (excludeGuide != "left" && leftGuide2 != null)
                {
                    leftGuide2.UpdatePosition(left, currentScreen.Bounds.Y, 
                        lineThickness, currentScreen.Bounds.Height);
                }
                
                if (excludeGuide != "right" && rightGuide2 != null)
                {
                    rightGuide2.UpdatePosition(right - lineThickness, currentScreen.Bounds.Y, 
                        lineThickness, currentScreen.Bounds.Height);
                }
            }
        }

        // 继续在下一个编辑中添加配置、菜单状态更新等其他方法...

        // ===================
        // 配置管理方法
        // ===================

        private void SetDefaultBounds()
        {
            var currentScreen = Screen.PrimaryScreen;
            
            // 设置包围框默认位置为屏幕中央，大约屏幕大小的一半
            int width = currentScreen.Bounds.Width / 2;
            int height = currentScreen.Bounds.Height / 2;
            
            defaultBounds = new Rectangle(
                currentScreen.Bounds.X + (currentScreen.Bounds.Width - width) / 2,
                currentScreen.Bounds.Y + (currentScreen.Bounds.Height - height) / 2,
                width,
                height
            );

            // 设置辅助线1的默认位置（稍微偏左上）
            int offset1 = Math.Min(50, Math.Min(width / 8, height / 8)); // 较小的偏移量
            defaultBounds1 = new Rectangle(
                defaultBounds.X - offset1, // 向左偏移
                defaultBounds.Y - offset1, // 向上偏移
                width,
                height
            );

            // 设置辅助线2的默认位置（稍微偏右下，与辅助线1区分）
            int offset2 = Math.Min(100, Math.Min(width / 6, height / 6)); // 较大的偏移量
            defaultBounds2 = new Rectangle(
                defaultBounds.X + offset2, // 向右偏移
                defaultBounds.Y + offset2, // 向下偏移
                width - offset2, // 稍微缩小宽度
                height - offset2 // 稍微缩小高度
            );
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    var config = JsonSerializer.Deserialize<Config>(json);
                    
                    // 包围框配置
                    lineThickness = config.LineThickness;
                    lineColor = ColorTranslator.FromHtml(config.LineColor);
                    lineOpacity = config.LineOpacity;
                    mouseClickThrough = config.MouseClickThrough;
                    dashStyle = (System.Drawing.Drawing2D.DashStyle)config.DashStyle;
                    
                    // 辅助线1独立配置
                    guideLinesEnabled = config.GuideLinesEnabled;
                    guideLinesCanDrag = config.GuideLinesCanDrag;
                    if (!string.IsNullOrEmpty(config.LineColor1))
                        lineColor1 = ColorTranslator.FromHtml(config.LineColor1);
                    lineOpacity1 = config.LineOpacity1;
                    guideLineDashStyle1 = (System.Drawing.Drawing2D.DashStyle)config.GuideLineDashStyle1;
                    
                    // 辅助线2配置
                    guideLinesEnabled2 = config.GuideLinesEnabled2;
                    guideLinesCanDrag2 = config.GuideLinesCanDrag2;
                    if (!string.IsNullOrEmpty(config.LineColor2))
                        lineColor2 = ColorTranslator.FromHtml(config.LineColor2);
                    lineOpacity2 = config.LineOpacity2;
                    guideLineDashStyle2 = (System.Drawing.Drawing2D.DashStyle)config.GuideLineDashStyle2;
                    
                    // 位置配置
                    if (config.DefaultBounds.Width > 0 && config.DefaultBounds.Height > 0)
                    {
                        defaultBounds = config.DefaultBounds;
                    }
                    
                    if (config.DefaultBounds1.Width > 0 && config.DefaultBounds1.Height > 0)
                    {
                        defaultBounds1 = config.DefaultBounds1;
                    }
                    
                    if (config.DefaultBounds2.Width > 0 && config.DefaultBounds2.Height > 0)
                    {
                        defaultBounds2 = config.DefaultBounds2;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载配置失败: {ex.Message}", "配置错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SaveConfig()
        {
            try
            {
                // 确保配置目录存在
                Directory.CreateDirectory(Path.GetDirectoryName(configPath));
                
                var config = new Config
                {
                    // 包围框配置
                    LineThickness = lineThickness,
                    LineColor = ColorTranslator.ToHtml(lineColor),
                    LineOpacity = lineOpacity,
                    MouseClickThrough = mouseClickThrough,
                    DashStyle = (int)dashStyle,
                    DefaultBounds = defaultBounds,
                    
                    // 辅助线1独立配置
                    GuideLinesEnabled = guideLinesEnabled,
                    GuideLinesCanDrag = guideLinesCanDrag,
                    LineColor1 = ColorTranslator.ToHtml(lineColor1),
                    LineOpacity1 = lineOpacity1,
                    GuideLineDashStyle1 = (int)guideLineDashStyle1,
                    DefaultBounds1 = defaultBounds1,
                    
                    // 辅助线2配置
                    GuideLinesEnabled2 = guideLinesEnabled2,
                    GuideLinesCanDrag2 = guideLinesCanDrag2,
                    LineColor2 = ColorTranslator.ToHtml(lineColor2),
                    LineOpacity2 = lineOpacity2,
                    GuideLineDashStyle2 = (int)guideLineDashStyle2,
                    DefaultBounds2 = defaultBounds2
                };
                
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存配置失败: {ex.Message}", "配置错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ===================
        // 菜单状态更新方法
        // ===================

        private void UpdateVisibilityMenuText()
        {
            if (trayIcon?.ContextMenuStrip == null) return;

            foreach (ToolStripItem item in trayIcon.ContextMenuStrip.Items)
            {
                if (item is ToolStripMenuItem menuItem && menuItem.Text == "包围框")
                {
                    foreach (ToolStripItem subItem in menuItem.DropDownItems)
                    {
                        if (subItem is ToolStripMenuItem subMenuItem && subMenuItem.Text.Contains("显示包围框"))
                        {
                            subMenuItem.Checked = isVisible;
                            break;
                        }
                    }
                    break;
                }
            }
        }

        private void UpdateGuideLinesOnlyMenuText()
        {
            if (trayIcon?.ContextMenuStrip == null) return;

            foreach (ToolStripItem item in trayIcon.ContextMenuStrip.Items)
            {
                if (item is ToolStripMenuItem menuItem && menuItem.Text == "辅助线1")
                {
                    foreach (ToolStripItem subItem in menuItem.DropDownItems)
                    {
                        if (subItem is ToolStripMenuItem subMenuItem && subMenuItem.Text.Contains("显示辅助线"))
                        {
                            subMenuItem.Checked = isGuideLinesOnlyMode;
                            break;
                        }
                    }
                    break;
                }
            }
        }

        private void UpdateGuideLinesOnlyMenuText2()
        {
            if (trayIcon?.ContextMenuStrip == null) return;

            foreach (ToolStripItem item in trayIcon.ContextMenuStrip.Items)
            {
                if (item is ToolStripMenuItem menuItem && menuItem.Text == "辅助线2")
                {
                    foreach (ToolStripItem subItem in menuItem.DropDownItems)
                    {
                        if (subItem is ToolStripMenuItem subMenuItem && subMenuItem.Text.Contains("显示辅助线"))
                        {
                            subMenuItem.Checked = isGuideLinesOnlyMode2;
                            break;
                        }
                    }
                    break;
                }
            }
        }

        private void UpdateThicknessMenuCheckedState()
        {
            if (trayIcon?.ContextMenuStrip == null) return;

            // 更新包围框菜单
            UpdateMenuCheckedState("包围框", "线条粗细", (item) => {
                return item.Text.Contains($"({lineThickness}像素)");
            });

            // 更新辅助线1菜单
            UpdateMenuCheckedState("辅助线1", "线条粗细", (item) => {
                return item.Text.Contains($"({lineThickness}像素)");
            });
        }

        private void UpdateThicknessMenuCheckedState2()
        {
            if (trayIcon?.ContextMenuStrip == null) return;

            // 更新辅助线2菜单
            UpdateMenuCheckedState("辅助线2", "线条粗细", (item) => {
                return item.Text.Contains($"({lineThickness}像素)");
            });
        }

        private void UpdateColorMenuCheckedState()
        {
            if (trayIcon?.ContextMenuStrip == null) return;

            // 更新包围框菜单
            UpdateMenuCheckedState("包围框", "线条颜色", (item) => {
                return IsColorMatch(item.Text, lineColor);
            });

            // 更新辅助线1菜单
            UpdateMenuCheckedState("辅助线1", "线条颜色", (item) => {
                return IsColorMatch(item.Text, lineColor);
            });
        }

        private void UpdateColorMenuCheckedState2()
        {
            if (trayIcon?.ContextMenuStrip == null) return;

            // 更新辅助线2菜单
            UpdateMenuCheckedState("辅助线2", "线条颜色", (item) => {
                return IsColorMatch(item.Text, lineColor2);
            });
        }

        private void UpdateTransparencyMenuCheckedState()
        {
            if (trayIcon?.ContextMenuStrip == null) return;

            // 更新包围框菜单
            UpdateMenuCheckedState("包围框", "线条透明度", (item) => {
                return item.Text.Contains($"{lineOpacity}%");
            });

            // 更新辅助线1菜单
            UpdateMenuCheckedState("辅助线1", "线条透明度", (item) => {
                return item.Text.Contains($"{lineOpacity}%");
            });
        }

        private void UpdateTransparencyMenuCheckedState2()
        {
            if (trayIcon?.ContextMenuStrip == null) return;

            // 更新辅助线2菜单
            UpdateMenuCheckedState("辅助线2", "线条透明度", (item) => {
                return item.Text.Contains($"{lineOpacity2}%");
            });
        }

        private void UpdateDashStyleMenuCheckedState()
        {
            if (trayIcon?.ContextMenuStrip == null) return;

            // 更新包围框菜单
            UpdateMenuCheckedState("包围框", "线条样式", (item) => {
                return IsDashStyleMatch(item.Text, dashStyle);
            });
        }

        // ===================
        // 工具方法
        // ===================

        private void UpdateMenuCheckedState(string mainMenuText, string subMenuText, Func<ToolStripMenuItem, bool> checkCondition)
        {
            foreach (ToolStripItem item in trayIcon.ContextMenuStrip.Items)
            {
                if (item is ToolStripMenuItem menuItem && menuItem.Text == mainMenuText)
                {
                    foreach (ToolStripItem subItem in menuItem.DropDownItems)
                    {
                        if (subItem is ToolStripMenuItem subMenuItem && subMenuItem.Text == subMenuText)
                        {
                            foreach (ToolStripItem thirdItem in subMenuItem.DropDownItems)
                            {
                                if (thirdItem is ToolStripMenuItem thirdMenuItem)
                                {
                                    thirdMenuItem.Checked = checkCondition(thirdMenuItem);
                                }
                            }
                            break;
                        }
                    }
                    break;
                }
            }
        }

        private bool IsColorMatch(string itemText, System.Drawing.Color color)
        {
            var colorMap = new Dictionary<string, System.Drawing.Color>
            {
                { "红色", System.Drawing.Color.Red },
                { "绿色", System.Drawing.Color.Green },
                { "蓝色", System.Drawing.Color.Blue },
                { "黄色", System.Drawing.Color.Yellow },
                { "橙色", System.Drawing.Color.Orange },
                { "紫色", System.Drawing.Color.Purple },
                { "青色", System.Drawing.Color.Cyan },
                { "鲜绿色", System.Drawing.Color.LimeGreen },
                { "黑色", System.Drawing.Color.FromArgb(1, 1, 1) },
                { "白色", System.Drawing.Color.White }
            };

            return colorMap.ContainsKey(itemText) && colorMap[itemText].ToArgb() == color.ToArgb();
        }

        private bool IsDashStyleMatch(string itemText, System.Drawing.Drawing2D.DashStyle style)
        {
            var styleMap = new Dictionary<string, System.Drawing.Drawing2D.DashStyle>
            {
                { "实线", System.Drawing.Drawing2D.DashStyle.Solid },
                { "虚线", System.Drawing.Drawing2D.DashStyle.Dash },
                { "点线", System.Drawing.Drawing2D.DashStyle.Dot },
                { "点划线", System.Drawing.Drawing2D.DashStyle.DashDot },
                { "双点划线", System.Drawing.Drawing2D.DashStyle.DashDotDot }
            };

            return styleMap.ContainsKey(itemText) && styleMap[itemText] == style;
        }

        // ===================
        // MainWindow调用的兼容性方法
        // ===================

        /// <summary>
        /// 确保所有线条窗口置顶
        /// </summary>
        public void EnsureTopmost()
        {
            // 确保包围框置顶
            if (boundingBox != null)
            {
                boundingBox.Topmost = true;
            }

            // 确保辅助线1置顶
            var guides = new[] { topGuide, bottomGuide, leftGuide, rightGuide };
            foreach (var guide in guides)
            {
                if (guide != null)
                {
                    guide.Topmost = true;
                }
            }

            // 移除辅助线1交叉点置顶代码
            // 确保辅助线1交叉点置顶
            // var intersections = new[] { topLeftIntersection, topRightIntersection, bottomLeftIntersection, bottomRightIntersection };
            // foreach (var intersection in intersections)
            // {
            //     if (intersection != null)
            //     {
            //         intersection.Topmost = true;
            //     }
            // }

            // 确保辅助线2置顶
            var guides2 = new[] { topGuide2, bottomGuide2, leftGuide2, rightGuide2 };
            foreach (var guide in guides2)
            {
                if (guide != null)
                {
                    guide.Topmost = true;
                }
            }

            // 移除辅助线2交叉点置顶代码
            // 确保辅助线2交叉点置顶
            // var intersections2 = new[] { topLeftIntersection2, topRightIntersection2, bottomLeftIntersection2, bottomRightIntersection2 };
            // foreach (var intersection in intersections2)
            // {
            //     if (intersection != null)
            //     {
            //         intersection.Topmost = true;
            //     }
            // }
        }

        /// <summary>
        /// 显示所有线条
        /// </summary>
        public void ShowAllLines()
        {
            // 显示包围框和辅助线1
            if (!isVisible && !isGuideLinesOnlyMode)
            {
                ShowBoundingBox();
            }
            else if (!isGuideLinesOnlyMode)
            {
                ShowGuideLinesOnly();
            }

            // 显示辅助线2
            if (!isGuideLinesOnlyMode2)
            {
                ShowGuideLinesOnly2();
            }
        }

        /// <summary>
        /// 隐藏所有线条
        /// </summary>
        public void HideAllLines()
        {
            // 隐藏包围框
            if (isVisible)
            {
                HideBoundingBox();
            }

            // 隐藏辅助线1
            if (isGuideLinesOnlyMode)
            {
                HideGuideLinesOnly();
            }

            // 隐藏辅助线2
            if (isGuideLinesOnlyMode2)
            {
                HideGuideLinesOnly2();
            }
        }

        /// <summary>
        /// 关闭所有线条
        /// </summary>
        public void CloseAllLines()
        {
            // 关闭包围框
            if (boundingBox != null)
            {
                boundingBox.Close();
                boundingBox = null;
            }

            // 关闭辅助线1
            HideGuideLines();

            // 关闭辅助线2
            HideGuideLines2();

            // 重置状态
            isVisible = false;
            isGuideLinesOnlyMode = false;
            isGuideLinesOnlyMode2 = false;
        }

        /// <summary>
        /// 将所有线条窗口带到最前面
        /// </summary>
        public void BringAllLinesToTop()
        {
            // 包围框窗口
            if (boundingBox != null)
            {
                var hwnd = new WindowInteropHelper(boundingBox).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    BringWindowToTop(hwnd);
                    SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                }
            }

            // 辅助线窗口（移除交叉点）
            var allGuides = new List<Window>();
            if (topGuide != null) allGuides.Add(topGuide);
            if (bottomGuide != null) allGuides.Add(bottomGuide);
            if (leftGuide != null) allGuides.Add(leftGuide);
            if (rightGuide != null) allGuides.Add(rightGuide);
            // 移除辅助线1交叉点
            // if (topLeftIntersection != null) allGuides.Add(topLeftIntersection);
            // if (topRightIntersection != null) allGuides.Add(topRightIntersection);
            // if (bottomLeftIntersection != null) allGuides.Add(bottomLeftIntersection);
            // if (bottomRightIntersection != null) allGuides.Add(bottomRightIntersection);

            // 辅助线2窗口（移除交叉点2）
            if (topGuide2 != null) allGuides.Add(topGuide2);
            if (bottomGuide2 != null) allGuides.Add(bottomGuide2);
            if (leftGuide2 != null) allGuides.Add(leftGuide2);
            if (rightGuide2 != null) allGuides.Add(rightGuide2);
            // 移除辅助线2交叉点
            // if (topLeftIntersection2 != null) allGuides.Add(topLeftIntersection2);
            // if (topRightIntersection2 != null) allGuides.Add(topRightIntersection2);
            // if (bottomLeftIntersection2 != null) allGuides.Add(bottomLeftIntersection2);
            // if (bottomRightIntersection2 != null) allGuides.Add(bottomRightIntersection2);

            foreach (var guide in allGuides)
            {
                var hwnd = new WindowInteropHelper(guide).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    BringWindowToTop(hwnd);
                    SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                }
            }
        }

        // ===================
        // 辅助线1的独立菜单项添加方法
        // ===================

        private void AddColor1MenuItem(ToolStripMenuItem parent, string name, System.Drawing.Color color)
        {
            var colorPreview = new System.Drawing.Bitmap(16, 16);
            using (var g = System.Drawing.Graphics.FromImage(colorPreview))
            {
                g.FillRectangle(new System.Drawing.SolidBrush(color), 0, 0, 16, 16);
                g.DrawRectangle(System.Drawing.Pens.Gray, 0, 0, 15, 15);
            }

            var item = new ToolStripMenuItem(name, colorPreview, (s, e) => {
                ChangeLineColor1(color);
            });
            item.Checked = color.Equals(lineColor1);
            parent.DropDownItems.Add(item);
        }

        private void AddTransparency1MenuItem(ToolStripMenuItem parent, string name, int value)
        {
            var item = new ToolStripMenuItem(name, null, (s, e) => {
                ChangeLineTransparency1(value);
            });
            item.Checked = (value == lineOpacity1);
            parent.DropDownItems.Add(item);
        }

        // ===================
        // 辅助类定义
        // ===================

        /// <summary>
        /// 可拖拽的辅助线类
        /// </summary>
        public class DraggableGuideLine : Window
        {
            private bool isDragging = false;
            private System.Drawing.Point lastCursor;
            private bool isHorizontal;
            private bool canDrag;
            private System.Drawing.Color lineColor;
            private System.Drawing.Drawing2D.DashStyle dashStyle;
            private int lineThickness;
            private Action<double, double> onPositionChanged;

            // Windows API for mouse click-through and topmost
            private const int WS_EX_TRANSPARENT = 0x20;
            private const int WS_EX_LAYERED = 0x80000;
            private const int WS_EX_NOACTIVATE = 0x08000000;
            private const int GWL_EXSTYLE = (-20);

            [DllImport("user32.dll")]
            private static extern int GetWindowLong(IntPtr hwnd, int index);

            [DllImport("user32.dll")]
            private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

            public DraggableGuideLine(int x, int y, int width, int height, System.Drawing.Color color, double opacity, bool horizontal, bool draggable, System.Drawing.Drawing2D.DashStyle dashStyle, Action<double, double> positionChangedCallback)
            {
                this.WindowStyle = WindowStyle.None;
                this.ShowInTaskbar = false;
                this.Topmost = true;
                this.AllowsTransparency = true;
                this.Background = System.Windows.Media.Brushes.Transparent;
                this.Opacity = opacity;
                this.ResizeMode = ResizeMode.NoResize;
                
                this.Left = x;
                this.Top = y;
                this.Width = width;
                this.Height = height;
                
                this.isHorizontal = horizontal;
                this.canDrag = draggable;
                this.lineColor = color;
                this.dashStyle = dashStyle;
                this.lineThickness = Math.Max(1, horizontal ? height : width);
                this.onPositionChanged = positionChangedCallback;
                
                if (draggable)
                {
                    this.MouseDown += OnMouseDown;
                    this.MouseMove += OnMouseMove;
                    this.MouseUp += OnMouseUp;
                    this.Cursor = isHorizontal ? System.Windows.Input.Cursors.SizeNS : System.Windows.Input.Cursors.SizeWE;
                }
            }

            protected override void OnSourceInitialized(EventArgs e)
            {
                base.OnSourceInitialized(e);
                
                var hwnd = new WindowInteropHelper(this).Handle;
                
                if (!canDrag)
                {
                    int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
                    ex |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
                    SetWindowLong(hwnd, GWL_EXSTYLE, ex);
                }
            }

            protected override void OnRender(System.Windows.Media.DrawingContext drawingContext)
            {
                base.OnRender(drawingContext);
                
                var wpfDashStyle = ConvertToWpfDashStyle(dashStyle);
                var pen = new System.Windows.Media.Pen(
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(
                        lineColor.A, lineColor.R, lineColor.G, lineColor.B)), 
                    lineThickness);
                pen.DashStyle = wpfDashStyle;
                
                if (isHorizontal)
                {
                    // 水平线
                    drawingContext.DrawLine(pen, new System.Windows.Point(0, this.Height / 2), new System.Windows.Point(this.Width, this.Height / 2));
                }
                else
                {
                    // 垂直线
                    drawingContext.DrawLine(pen, new System.Windows.Point(this.Width / 2, 0), new System.Windows.Point(this.Width / 2, this.Height));
                }
            }

            private System.Windows.Media.DashStyle ConvertToWpfDashStyle(System.Drawing.Drawing2D.DashStyle drawingDashStyle)
            {
                switch (drawingDashStyle)
                {
                    case System.Drawing.Drawing2D.DashStyle.Solid:
                        return System.Windows.Media.DashStyles.Solid;
                    case System.Drawing.Drawing2D.DashStyle.Dash:
                        return System.Windows.Media.DashStyles.Dash;
                    case System.Drawing.Drawing2D.DashStyle.Dot:
                        return System.Windows.Media.DashStyles.Dot;
                    case System.Drawing.Drawing2D.DashStyle.DashDot:
                        return System.Windows.Media.DashStyles.DashDot;
                    case System.Drawing.Drawing2D.DashStyle.DashDotDot:
                        return System.Windows.Media.DashStyles.DashDotDot;
                    default:
                        return System.Windows.Media.DashStyles.Solid;
                }
            }

            private void OnMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
            {
                if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed && canDrag)
                {
                    isDragging = true;
                    lastCursor = System.Windows.Forms.Cursor.Position;
                    this.CaptureMouse();
                }
            }

            private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
            {
                if (!isDragging || !canDrag) return;

                var currentCursor = System.Windows.Forms.Cursor.Position;
                int deltaX = currentCursor.X - lastCursor.X;
                int deltaY = currentCursor.Y - lastCursor.Y;
                
                if (isHorizontal)
                {
                    // 水平线只能上下移动
                    this.Top += deltaY;
                }
                else
                {
                    // 垂直线只能左右移动
                    this.Left += deltaX;
                }
                
                lastCursor = currentCursor;
                
                // 触发位置变化回调
                onPositionChanged?.Invoke(this.Left, this.Top);
            }

            private void OnMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
            {
                if (e.LeftButton == System.Windows.Input.MouseButtonState.Released)
                {
                    isDragging = false;
                    this.ReleaseMouseCapture();
                }
            }

            public void SetDraggable(bool draggable)
            {
                canDrag = draggable;
                
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    if (draggable)
                    {
                        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
                        this.Cursor = isHorizontal ? System.Windows.Input.Cursors.SizeNS : System.Windows.Input.Cursors.SizeWE;
                    }
                    else
                    {
                        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT);
                        this.Cursor = System.Windows.Input.Cursors.Arrow;
                    }
                }
            }

            public void UpdatePosition(int x, int y, int width, int height)
            {
                this.Left = x;
                this.Top = y;
                this.Width = width;
                this.Height = height;
            }
        }

        /// <summary>
        /// 可拖拽的交叉点类 - 使用8x8像素的小尺寸便于观察位置
        /// </summary>
        public class DraggableIntersection : Window
        {
            private bool isDragging = false;
            private System.Drawing.Point lastCursor;
            private bool canDrag;
            private System.Drawing.Color fillColor;
            private Action<double, double> onPositionChanged;

            // Windows API
            private const int WS_EX_TRANSPARENT = 0x20;
            private const int WS_EX_LAYERED = 0x80000;
            private const int WS_EX_NOACTIVATE = 0x08000000;
            private const int GWL_EXSTYLE = (-20);

            [DllImport("user32.dll")]
            private static extern int GetWindowLong(IntPtr hwnd, int index);

            [DllImport("user32.dll")]
            private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

            public DraggableIntersection(int x, int y, System.Drawing.Color color, double opacity, bool draggable, Action<double, double> positionChangedCallback)
            {
                this.WindowStyle = WindowStyle.None;
                this.ShowInTaskbar = false;
                this.Topmost = true;
                this.AllowsTransparency = true;
                this.Background = System.Windows.Media.Brushes.Transparent;
                this.Opacity = opacity;
                this.ResizeMode = ResizeMode.NoResize;
                
                // 使用8x8像素的小尺寸，便于观察位置
                this.Width = 8;
                this.Height = 8;
                this.Left = x - 4; // 居中显示
                this.Top = y - 4;
                
                this.canDrag = draggable;
                this.fillColor = color;
                this.onPositionChanged = positionChangedCallback;
                
                if (draggable)
                {
                    this.MouseDown += OnMouseDown;
                    this.MouseMove += OnMouseMove;
                    this.MouseUp += OnMouseUp;
                    this.Cursor = System.Windows.Input.Cursors.SizeAll;
                }
            }

            protected override void OnSourceInitialized(EventArgs e)
            {
                base.OnSourceInitialized(e);
                
                var hwnd = new WindowInteropHelper(this).Handle;
                
                if (!canDrag)
                {
                    int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
                    ex |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
                    SetWindowLong(hwnd, GWL_EXSTYLE, ex);
                }
            }

            protected override void OnRender(System.Windows.Media.DrawingContext drawingContext)
            {
                base.OnRender(drawingContext);
                
                var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(
                    fillColor.A, fillColor.R, fillColor.G, fillColor.B));
                
                // 绘制小正方形
                var rect = new System.Windows.Rect(0, 0, this.Width, this.Height);
                drawingContext.DrawRectangle(brush, null, rect);
            }

            private void OnMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
            {
                if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed && canDrag)
                {
                    isDragging = true;
                    lastCursor = System.Windows.Forms.Cursor.Position;
                    this.CaptureMouse();
                }
            }

            private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
            {
                if (!isDragging || !canDrag) return;

                var currentCursor = System.Windows.Forms.Cursor.Position;
                int deltaX = currentCursor.X - lastCursor.X;
                int deltaY = currentCursor.Y - lastCursor.Y;
                
                this.Left += deltaX;
                this.Top += deltaY;
                
                lastCursor = currentCursor;
                
                // 触发位置变化回调 - 传递中心点坐标
                onPositionChanged?.Invoke(this.Left + 4, this.Top + 4);
            }

            private void OnMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
            {
                if (e.LeftButton == System.Windows.Input.MouseButtonState.Released)
                {
                    isDragging = false;
                    this.ReleaseMouseCapture();
                }
            }

            public void SetDraggable(bool draggable)
            {
                canDrag = draggable;
                
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    if (draggable)
                    {
                        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
                        this.Cursor = System.Windows.Input.Cursors.SizeAll;
                    }
                    else
                    {
                        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT);
                        this.Cursor = System.Windows.Input.Cursors.Arrow;
                    }
                }
            }

            public void UpdatePosition(int x, int y)
            {
                this.Left = x - 4; // 居中显示
                this.Top = y - 4;
            }
        }

        // ===================
        // 辅助线1独立控制方法
        // ===================

        private void ToggleGuideLinesOnly()
        {
            if (isGuideLinesOnlyMode)
            {
                HideGuideLinesOnly();
            }
            else
            {
                ShowGuideLinesOnly();
            }
            UpdateGuideLinesOnlyMenuText();
        }

        private void ShowGuideLinesOnly()
        {
            // 辅助线1完全独立显示，不再影响包围框
            // 移除：if (isVisible) { HideBoundingBox(); }

            var currentScreen = Screen.FromPoint(System.Windows.Forms.Cursor.Position);
            Rectangle bounds = defaultBounds1;
            
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

            // 只创建辅助线1，不创建包围框
            CreateGuideLines(currentScreen, bounds);
            isGuideLinesOnlyMode = true;
        }

        private void HideGuideLinesOnly()
        {
            HideGuideLines();
            isGuideLinesOnlyMode = false;
            // 只更新辅助线1自己的菜单状态，不影响包围框
            UpdateGuideLinesOnlyMenuText();
        }

        private void ChangeLineColor1(System.Drawing.Color color)
        {
            lineColor1 = color;
            
            // 如果在仅辅助线1模式下，需要刷新辅助线1
            if (isGuideLinesOnlyMode)
            {
                UpdateGuideLineColors();
            }
            
            UpdateColorMenuCheckedState();
            SaveConfig();
        }

        private void ChangeLineTransparency1(int value)
        {
            lineOpacity1 = value;
            
            // 如果在仅辅助线1模式下，需要刷新辅助线1
            if (isGuideLinesOnlyMode)
            {
                UpdateGuideLineColors();
            }
            
            UpdateTransparencyMenuCheckedState();
            SaveConfig();
        }
    }
} 