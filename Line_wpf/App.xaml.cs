using System;
using System.IO;
using System.Threading;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using System.Runtime.InteropServices;

namespace Line_wpf
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        // 设置DPI感知模式，确保1像素就是1物理像素
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
        
        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int value);
        
        // DPI感知模式
        private enum DPI_AWARENESS
        {
            DPI_AWARENESS_INVALID = -1,
            DPI_AWARENESS_UNAWARE = 0,      // DPI无关
            DPI_AWARENESS_SYSTEM_AWARE = 1, // 系统DPI感知
            DPI_AWARENESS_PER_MONITOR_AWARE = 2 // 每个显示器DPI感知
        }
        
        // 重启模式标志
        private bool isRestartMode = false;
        
        protected override void OnStartup(StartupEventArgs e)
        {
            // 设置为DPI无关模式，确保1像素就是1物理像素（模拟WinForms的AutoScaleMode.None）
            try
            {
                // 首先尝试使用Windows 8.1+的API
                SetProcessDpiAwareness((int)DPI_AWARENESS.DPI_AWARENESS_UNAWARE);
            }
            catch
            {
                try
                {
                    // 如果失败，使用Windows Vista+的API
                    SetProcessDPIAware();
                }
                catch
                {
                    // 如果都失败，继续运行但可能有DPI问题
                }
            }
            
            // 检查是否是重启启动（带特殊参数）
            isRestartMode = e.Args.Length > 0 && e.Args[0] == "--restart";
            
            if (isRestartMode)
            {
                Console.WriteLine("[启动] 检测到重启模式，将延迟注册快捷键");
            }
            
            // 只有在非重启模式下才检查单实例
            if (!isRestartMode)
            {
                // 使用更可靠的单实例检查
                var mutex = Program.GetSingleInstanceMutex();
                bool mutexAcquired = false;
                
                try
                {
                    // 尝试获取互斥锁，等待最多1秒
                    mutexAcquired = mutex.WaitOne(TimeSpan.FromSeconds(1), false);
                    
                    if (!mutexAcquired)
                    {
                        // 互斥锁获取失败，说明已有实例在运行
                        MessageBox.Show("程序已经在运行中！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        this.Shutdown();
                        return;
                    }
                    
                    // 额外的进程检查（双重保险）
                    if (Program.IsAnotherInstanceRunning())
                    {
                        MessageBox.Show("检测到其他实例正在运行！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        this.Shutdown();
                        return;
                    }
                }
                catch (AbandonedMutexException)
                {
                    // 处理被遗弃的互斥锁（正常情况）
                    mutexAcquired = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"单实例检查失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    this.Shutdown();
                    return;
                }
            }

            try
            {
                // 确保应用程序数据目录存在
                string appDataPath = Program.GetAppDataPath();
                if (!Directory.Exists(appDataPath))
                {
                    Directory.CreateDirectory(appDataPath);
                }

                base.OnStartup(e);
                
                // 创建主窗体并传递重启模式标志
                var mainWindow = new MainWindow(isRestartMode);
                
                // 显示主窗体以便能看到托盘图标
                mainWindow.Show();
                
                // 设置主窗体
                this.MainWindow = mainWindow;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动应用程序时发生错误: {ex.Message}\n\n堆栈跟踪:\n{ex.StackTrace}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                this.Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                Program.ReleaseSingleInstanceMutex();
            }
            catch (Exception)
            {
                // 忽略释放异常
            }
            
            base.OnExit(e);
        }
    }
} 