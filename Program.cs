using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace Snipaste
{
    public partial class Program : Form
    {
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
                monitoredApplications = new List<string> { "Paster - Snipaste", "PixPin" };
            }
        }
    }
} 