using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace Line_wpf
{
    internal class Program
    {
        // 用于确保应用程序只运行一个实例的互斥体
        private static readonly Mutex SingleInstanceMutex = new Mutex(true, "LineAppSingleInstanceMutex_WPF");

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
        public static bool IsAnotherInstanceRunning()
        {
            try
            {
                var currentProcess = Process.GetCurrentProcess();
                string currentProcessName = currentProcess.ProcessName;
                Process[] processes = Process.GetProcessesByName(currentProcessName);
                
                // 检查是否有其他进程具有相同的可执行文件路径
                string currentPath = currentProcess.MainModule.FileName;
                
                foreach (var process in processes)
                {
                    if (process.Id != currentProcess.Id && 
                        !process.HasExited &&
                        process.MainModule.FileName.Equals(currentPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                
                return false;
            }
            catch (Exception)
            {
                // 如果检查失败，返回false（假设没有其他实例）
                return false;
            }
        }

        public static Mutex GetSingleInstanceMutex()
        {
            return SingleInstanceMutex;
        }

        public static string GetAppDataPath()
        {
            return AppDataPath;
        }
    }
}
