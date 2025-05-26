using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Line
{
    public partial class MonitoredAppsWindow : Form
    {
        private DataGridView dataGridView;
        private Button addButton;
        private Button deleteButton;
        private Button windowPickerButton;
        private Button saveButton;
        private Button cancelButton;
        
        public List<LineForm.MonitoredApp> MonitoredApps { get; private set; }
        public bool DialogResultOK { get; private set; }
        
        // Windows API for window detection and global mouse hooks
        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(Point point);

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
        private static extern bool GetCursorPos(out Point lpPoint);

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
        private Cursor originalCursor;
        private IntPtr hookID = IntPtr.Zero;
        private LowLevelMouseProc hookProc;

        public MonitoredAppsWindow(List<LineForm.MonitoredApp> currentApps)
        {
            InitializeComponent();
            
            // 深拷贝当前应用程序列表
            MonitoredApps = new List<LineForm.MonitoredApp>();
            foreach (var app in currentApps)
            {
                MonitoredApps.Add(new LineForm.MonitoredApp(app.Name, app.IsEnabled));
            }
            
            SetupDataGridView();
            RefreshDataGridView();
            
            // 初始化鼠标钩子回调
            hookProc = HookCallback;
        }

        private void InitializeComponent()
        {
            this.Text = "管理监控程序";
            this.Size = new Size(600, 450);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            
            // 创建DataGridView
            dataGridView = new DataGridView
            {
                Location = new Point(12, 12),
                Size = new Size(560, 320),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                ReadOnly = false
            };
            
            // 创建按钮
            addButton = new Button
            {
                Text = "添加程序",
                Location = new Point(12, 340),
                Size = new Size(80, 30)
            };
            addButton.Click += AddButton_Click;
            
            deleteButton = new Button
            {
                Text = "删除选中",
                Location = new Point(100, 340),
                Size = new Size(80, 30)
            };
            deleteButton.Click += DeleteButton_Click;
            
            windowPickerButton = new Button
            {
                Text = "🎯 拖拽拾取",
                Location = new Point(188, 340),
                Size = new Size(90, 30),
            };
            windowPickerButton.MouseDown += WindowPickerButton_MouseDown;
            
            saveButton = new Button
            {
                Text = "保存",
                Location = new Point(412, 380),
                Size = new Size(75, 30)
            };
            saveButton.Click += SaveButton_Click;
            
            cancelButton = new Button
            {
                Text = "取消",
                Location = new Point(497, 380),
                Size = new Size(75, 30)
            };
            cancelButton.Click += CancelButton_Click;
            
            // 添加控件到窗体
            this.Controls.Add(dataGridView);
            this.Controls.Add(addButton);
            this.Controls.Add(deleteButton);
            this.Controls.Add(windowPickerButton);
            this.Controls.Add(saveButton);
            this.Controls.Add(cancelButton);
        }
        
        private void SetupDataGridView()
        {
            // 添加列
            var nameColumn = new DataGridViewTextBoxColumn
            {
                Name = "Name",
                HeaderText = "程序标题",
                DataPropertyName = "Name",
                Width = 460,
                ReadOnly = true
            };
            
            var enabledColumn = new DataGridViewCheckBoxColumn
            {
                Name = "IsEnabled",
                HeaderText = "启用",
                DataPropertyName = "IsEnabled",
                Width = 80
            };
            
            dataGridView.Columns.Add(nameColumn);
            dataGridView.Columns.Add(enabledColumn);
            
            // 禁用自动调整列宽
            dataGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            
            // 添加单元格值变化事件处理
            dataGridView.CellValueChanged += DataGridView_CellValueChanged;
            dataGridView.CurrentCellDirtyStateChanged += DataGridView_CurrentCellDirtyStateChanged;
        }
        
        private void DataGridView_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            // 当复选框状态改变时立即提交更改
            if (dataGridView.IsCurrentCellDirty)
            {
                dataGridView.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }
        
        private void DataGridView_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            // 当单元格值发生变化时，同步到我们的数据列表
            if (e.RowIndex >= 0 && e.RowIndex < MonitoredApps.Count && e.ColumnIndex == 1) // IsEnabled 列
            {
                var cell = dataGridView.Rows[e.RowIndex].Cells[e.ColumnIndex];
                if (cell.Value is bool enabled)
                {
                    MonitoredApps[e.RowIndex].IsEnabled = enabled;
                }
            }
        }
        
        private void RefreshDataGridView()
        {
            dataGridView.DataSource = null;
            dataGridView.DataSource = MonitoredApps;
            
            // 重新设置列宽，确保不会被重置
            if (dataGridView.Columns.Count > 0)
            {
                dataGridView.Columns[0].Width = 460; // 程序标题列
                if (dataGridView.Columns.Count > 1)
                {
                    dataGridView.Columns[1].Width = 80; // 启用列
                }
            }
            
            // 确保禁用自动调整列宽
            dataGridView.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        }

        private void AddButton_Click(object sender, EventArgs e)
        {
            using (var inputForm = new InputForm("添加监控程序", "请输入要监控的程序窗口标题："))
            {
                if (inputForm.ShowDialog() == DialogResult.OK)
                {
                    string appName = inputForm.InputText.Trim();
                    if (!string.IsNullOrWhiteSpace(appName))
                    {
                        // 检查是否已存在
                        if (!MonitoredApps.Any(a => a.Name.Equals(appName, StringComparison.OrdinalIgnoreCase)))
                        {
                            MonitoredApps.Add(new LineForm.MonitoredApp(appName, true));
                            RefreshDataGridView();
                        }
                        else
                        {
                            MessageBox.Show("该程序已在列表中！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    }
                }
            }
        }

        private void DeleteButton_Click(object sender, EventArgs e)
        {
            if (dataGridView.SelectedRows.Count > 0)
            {
                int selectedIndex = dataGridView.SelectedRows[0].Index;
                if (selectedIndex >= 0 && selectedIndex < MonitoredApps.Count)
                {
                    var selectedApp = MonitoredApps[selectedIndex];
                    var result = MessageBox.Show($"确定要删除 \"{selectedApp.Name}\" 吗？", 
                        "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    
                    if (result == DialogResult.Yes)
                    {
                        MonitoredApps.RemoveAt(selectedIndex);
                        RefreshDataGridView();
                    }
                }
            }
            else
            {
                MessageBox.Show("请先选择要删除的程序！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void WindowPickerButton_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                StartWindowPicking();
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
            windowPickerButton.Text = "松开获取";
            windowPickerButton.BackColor = Color.LightCoral;
            
            // 设置十字光标
            Cursor.Current = Cursors.Cross;
            
            // 直接开始拖拽，无需弹窗提示
            isDragging = true;
        }

        private void StopWindowPicking()
        {
            isPickingWindow = false;
            isDragging = false;
            
            // 移除全局鼠标钩子
            if (hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hookID);
                hookID = IntPtr.Zero;
            }
            
            // 恢复按钮状态
            windowPickerButton.Text = "🎯 拖拽拾取";
            windowPickerButton.BackColor = SystemColors.Control;
            
            // 恢复光标
            Cursor.Current = Cursors.Default;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && isPickingWindow)
            {
                int msgType = wParam.ToInt32();
                
                if (msgType == WM_LBUTTONDOWN)
                {
                    // 开始拖拽
                    isDragging = true;
                    Cursor.Current = Cursors.Cross;
                }
                else if (msgType == WM_LBUTTONUP && isDragging)
                {
                    // 结束拖拽，获取窗口信息
                    isDragging = false;
                    
                    MSLLHOOKSTRUCT hookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                    Point mousePoint = new Point(hookStruct.pt.x, hookStruct.pt.y);
                    
                    // 获取鼠标位置的窗口
                    IntPtr targetWindow = WindowFromPoint(mousePoint);
                    
                    if (targetWindow != IntPtr.Zero && targetWindow != this.Handle)
                    {
                        // 获取窗口标题
                        StringBuilder windowTitle = new StringBuilder(256);
                        GetWindowText(targetWindow, windowTitle, windowTitle.Capacity);
                        string title = windowTitle.ToString();
                        
                        // 停止拾取模式
                        this.Invoke(new Action(() => {
                            StopWindowPicking();
                            ProcessCapturedWindow(title);
                        }));
                    }
                    else
                    {
                        this.Invoke(new Action(() => {
                            StopWindowPicking();
                        }));
                    }
                }
                else if (msgType == WM_MOUSEMOVE && isDragging)
                {
                    // 在拖拽过程中保持十字光标
                    Cursor.Current = Cursors.Cross;
                }
            }
            
            return CallNextHookEx(hookID, nCode, wParam, lParam);
        }

        private void ProcessCapturedWindow(string title)
        {
            if (!string.IsNullOrWhiteSpace(title))
            {
                // 询问用户是否添加
                var result = MessageBox.Show($"检测到窗口标题：\n\n\"{title}\"\n\n是否添加到监控列表？", 
                    "确认添加", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                
                if (result == DialogResult.Yes)
                {
                    // 检查是否已存在
                    if (!MonitoredApps.Any(a => a.Name.Equals(title, StringComparison.OrdinalIgnoreCase)))
                    {
                        MonitoredApps.Add(new LineForm.MonitoredApp(title, true));
                        RefreshDataGridView();
                        MessageBox.Show("已成功添加到监控列表！", "添加成功", 
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        MessageBox.Show("该程序已在列表中！", "提示", 
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            else
            {
                MessageBox.Show("无法获取窗口标题，请重试。", "获取失败", 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SaveButton_Click(object sender, EventArgs e)
        {
            // 确保所有编辑都被提交
            dataGridView.EndEdit();
            
            // 手动同步所有数据，确保没有遗漏
            for (int i = 0; i < dataGridView.Rows.Count && i < MonitoredApps.Count; i++)
            {
                var enabledCell = dataGridView.Rows[i].Cells["IsEnabled"];
                if (enabledCell.Value is bool enabled)
                {
                    MonitoredApps[i].IsEnabled = enabled;
                }
            }
            
            DialogResultOK = true;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            DialogResultOK = false;
            this.Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (isPickingWindow)
            {
                StopWindowPicking();
            }
            base.OnFormClosing(e);
        }
    }

    // 简单的输入对话框
    public partial class InputForm : Form
    {
        private Label label;
        private TextBox textBox;
        private Button okButton;
        private Button cancelButton;
        
        public string InputText { get; private set; } = "";

        public InputForm(string title, string prompt)
        {
            InitializeComponent(title, prompt);
        }

        private void InitializeComponent(string title, string prompt)
        {
            this.Text = title;
            this.Size = new Size(400, 150);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            label = new Label
            {
                Text = prompt,
                Location = new Point(12, 15),
                Size = new Size(360, 20)
            };

            textBox = new TextBox
            {
                Location = new Point(12, 40),
                Size = new Size(360, 25)
            };

            okButton = new Button
            {
                Text = "确定",
                Location = new Point(217, 75),
                Size = new Size(75, 25),
                DialogResult = DialogResult.OK
            };
            okButton.Click += (s, e) => { InputText = textBox.Text; };

            cancelButton = new Button
            {
                Text = "取消",
                Location = new Point(297, 75),
                Size = new Size(75, 25),
                DialogResult = DialogResult.Cancel
            };

            this.Controls.Add(label);
            this.Controls.Add(textBox);
            this.Controls.Add(okButton);
            this.Controls.Add(cancelButton);
            
            this.AcceptButton = okButton;
            this.CancelButton = cancelButton;
            
            // 设置焦点到文本框
            this.Load += (s, e) => textBox.Focus();
        }
    }
} 