# 屏幕参考线工具 / Screen Reference Line Tool

[English](#english) | [中文](#中文)

---

## 中文

### 📖 简介

屏幕参考线工具是一款专业的屏幕辅助工具，帮助设计师、开发者和其他需要精确对齐的用户在屏幕上创建参考线。支持横线、竖线和包围框，具有丰富的自定义选项和智能穿透功能。

### ✨ 主要功能

#### 🔹 瞬时横线
- **热键触发**：默认 F5，可自定义
- **自动消失**：显示后自动淡出
- **多屏支持**：可在单屏或所有屏幕显示
- **跟随鼠标**：在鼠标位置显示横线

#### 🔹 持续竖线
- **热键控制**：Ctrl+F1~F4 开启，Ctrl+Shift+F1~F4 关闭
- **可拖拽**：非穿透模式下可拖拽调整位置
- **多条支持**：最多同时显示4条竖线

#### 🔹 持续横线
- **热键控制**：Ctrl+1~4 开启，Ctrl+Shift+1~4 关闭
- **可拖拽**：非穿透模式下可拖拽调整位置
- **多屏模式**：支持单屏或全屏显示

#### 🔹 包围框功能
- **智能框选**：创建矩形参考框
- **精确定位**：辅助元素对齐

### ⚙️ 自定义选项

#### 🎨 外观设置
- **线条粗细**：1-5像素可选
- **线条颜色**：9种预设颜色 + 自定义颜色
- **透明度**：25%、50%、75%、100% 可选

#### ⏱️ 显示设置
- **显示时长**：0.1-5秒可调（仅瞬时横线）
- **鼠标穿透**：可开启/关闭
- **显示模式**：单屏/全屏切换

#### 🎯 高级功能
- **持续置顶**：与其他程序抢夺置顶权
- **置顶策略**：暴力定时器/智能监听
- **监控程序**：自定义需要抢夺置顶的程序
- **快捷键管理**：可全局禁用所有快捷键

### 🚀 快速开始

1. **下载并运行**：解压后直接运行 `Line.exe`
2. **系统托盘**：程序将在系统托盘显示图标
3. **右键菜单**：右键托盘图标打开设置菜单
4. **开始使用**：按 F5 显示瞬时横线，或使用其他快捷键

### ⌨️ 快捷键列表

| 功能 | 快捷键 | 说明 |
|------|--------|------|
| 瞬时横线 | F5（默认） | 显示横线后自动消失 |
| 竖线 1-4 | Ctrl+F1~F4 | 开启持续竖线 |
| 关闭竖线 | Ctrl+Shift+F1~F4 | 关闭对应竖线 |
| 横线 1-4 | Ctrl+1~4 | 开启持续横线 |
| 关闭横线 | Ctrl+Shift+1~4 | 关闭对应横线 |

### 🔧 系统要求

- **操作系统**：Windows 10 或更高版本
- **框架**：.NET Framework 4.7.2 或更高版本
- **权限**：管理员权限（用于全局热键注册）

### 📁 配置文件

配置文件自动保存在：
```
%AppData%\ScreenLine\
├── config.json           # 主程序配置
├── vertical_config.json  # 竖线配置
└── horizontal_config.json # 横线配置
```

### 🔄 更新日志

#### v2.0.0
- ✅ 修复鼠标穿透问题
- ✅ 添加包围框功能
- ✅ 增强置顶策略
- ✅ 添加全局快捷键开关
- ✅ 优化用户界面

#### v1.0.0
- ✅ 基础横线、竖线功能
- ✅ 多屏支持
- ✅ 自定义外观
- ✅ 系统托盘集成

### 🐛 问题反馈

如果遇到问题或有功能建议，请通过以下方式反馈：
- 提交 GitHub Issue
- 发送邮件说明问题详情

---

## English

### 📖 Introduction

Screen Reference Line Tool is a professional screen assistance utility that helps designers, developers, and other users who need precise alignment to create reference lines on screen. It supports horizontal lines, vertical lines, and bounding boxes with rich customization options and intelligent click-through functionality.

### ✨ Key Features

#### 🔹 Temporary Horizontal Lines
- **Hotkey Triggered**: Default F5, customizable
- **Auto Fade**: Automatically disappears after display
- **Multi-Screen Support**: Display on single screen or all screens
- **Mouse Following**: Shows horizontal line at mouse position

#### 🔹 Persistent Vertical Lines
- **Hotkey Control**: Ctrl+F1~F4 to open, Ctrl+Shift+F1~F4 to close
- **Draggable**: Can be dragged to adjust position in non-click-through mode
- **Multi-Line Support**: Up to 4 vertical lines simultaneously

#### 🔹 Persistent Horizontal Lines
- **Hotkey Control**: Ctrl+1~4 to open, Ctrl+Shift+1~4 to close
- **Draggable**: Can be dragged to adjust position in non-click-through mode
- **Multi-Screen Mode**: Supports single screen or full screen display

#### 🔹 Bounding Box Feature
- **Smart Selection**: Create rectangular reference frames
- **Precise Positioning**: Assist with element alignment

### ⚙️ Customization Options

#### 🎨 Appearance Settings
- **Line Thickness**: 1-5 pixels selectable
- **Line Color**: 9 preset colors + custom colors
- **Transparency**: 25%, 50%, 75%, 100% options

#### ⏱️ Display Settings
- **Display Duration**: 0.1-5 seconds adjustable (temporary lines only)
- **Mouse Click-Through**: Can be enabled/disabled
- **Display Mode**: Single screen/full screen toggle

#### 🎯 Advanced Features
- **Persistent Topmost**: Compete for topmost status with other programs
- **Topmost Strategy**: Force timer/Smart monitoring
- **Monitor Programs**: Customize programs to compete for topmost status
- **Hotkey Management**: Can globally disable all hotkeys

### 🚀 Quick Start

1. **Download and Run**: Extract and run `Line.exe` directly
2. **System Tray**: Program will display icon in system tray
3. **Right-Click Menu**: Right-click tray icon to open settings menu
4. **Start Using**: Press F5 to show temporary horizontal line, or use other hotkeys

### ⌨️ Hotkey List

| Function | Hotkey | Description |
|----------|--------|-------------|
| Temporary Line | F5 (default) | Show horizontal line with auto fade |
| Vertical Line 1-4 | Ctrl+F1~F4 | Open persistent vertical lines |
| Close Vertical | Ctrl+Shift+F1~F4 | Close corresponding vertical line |
| Horizontal Line 1-4 | Ctrl+1~4 | Open persistent horizontal lines |
| Close Horizontal | Ctrl+Shift+1~4 | Close corresponding horizontal line |

### 🔧 System Requirements

- **Operating System**: Windows 10 or higher
- **Framework**: .NET Framework 4.7.2 or higher
- **Permissions**: Administrator privileges (for global hotkey registration)

### 📁 Configuration Files

Configuration files are automatically saved to:
```
%AppData%\ScreenLine\
├── config.json           # Main program configuration
├── vertical_config.json  # Vertical line configuration
└── horizontal_config.json # Horizontal line configuration
```

### 🔄 Changelog

#### v2.0.0
- ✅ Fixed mouse click-through issues
- ✅ Added bounding box functionality
- ✅ Enhanced topmost strategies
- ✅ Added global hotkey toggle
- ✅ Improved user interface

#### v1.0.0
- ✅ Basic horizontal and vertical line features
- ✅ Multi-screen support
- ✅ Custom appearance
- ✅ System tray integration

### 🐛 Feedback

If you encounter issues or have feature suggestions, please provide feedback through:
- Submit GitHub Issues
- Send email with detailed problem description

---

### 📄 License

This project is licensed under the MIT License - see the LICENSE file for details.

### 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

### ⭐ Star History

If this tool helps you, please consider giving it a star! ⭐