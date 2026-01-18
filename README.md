# ViveTool WinUI

**ViveTool WinUI** 是一款基于 **Windows WinUI** 的系统功能管理与工具软件，提供内核管理、系统功能启用/禁用、设置备份与还原、主题切换等功能，旨在为高级用户提供高效、直观的系统优化体验。  

Microsoft Store 页面：[安装或了解更多](https://apps.microsoft.com/detail/9n4pvjnwrnjz)

---

## ✨ 主要功能

### MainWindow 功能
- **窗口初始化和标题栏设置**  
- **导航菜单和路由**  
- **内核自动安装**（从 Assets）  
- **内核版本查询**  
- **内核备份**（ZIP 到桌面）  
- **内核还原**（从 ZIP）  
- **打开内核文件夹**  
- **检查更新**（静默/手动）  
- **下载并安装更新**  
- **主题切换**（浅色/深色/系统）  
- **背景材质切换**（Mica / Acrylic）  
- **Toast 通知**（成功/警告/错误/信息）  
- **设置读写**

### ViveToolPage 功能
- **12 个快速命令按钮**  
- **功能启用/禁用/重置**  
- **自定义命令执行**  
- **命令历史记录**  
- **自动重启选项**  
- **PowerShell 执行**

### FeatureIDFinderPage 功能
- **查询系统所有功能 ID**  
- **显示系统信息**  
- **按状态过滤**（启用/禁用/默认）  
- **按 ID/状态排序**  
- **搜索功能 ID**  
- **快速启用/禁用（⚡按钮）**  
- **详情面板**  
- **导出 JSON / CSV**  
- **复制 ID**

### SettingsPage 功能
- **主题切换**  
- **背景材质切换**  
- **显示内核版本**  
- **自动更新开关**  
- **检查更新**  
- **打开内核文件夹**  
- **备份内核**  
- **还原内核**  
- **导出设置**  
- **导入设置**  
- **重置设置**  
- **关于信息**  
- **GitHub 链接**  
- **反馈链接**

### OutputPage 功能
- **显示命令输出**  
- **复制到剪贴板**  
- **保存到桌面**

---

## 🖥 系统要求

- **适用平台**：Windows 10 / Windows 11  
- **体系架构**：x86 / x64 / ARM（根据微软商店页面）  
- **UWP / WinUI 平台**，适配触控与键盘操作

---

## 📥 下载 & 安装

ViveTool WinUI 已在 **Microsoft Store** 发布，直接点击以下链接即可安装：

[Microsoft Store 页面](https://apps.microsoft.com/detail/9n4pvjnwrnjz)

---

## 🧪 使用指南

1. 启动 ViveTool WinUI  
2. 使用 **MainWindow** 管理内核、主题、更新与设置  
3. 切换到 **ViveToolPage** 执行快速命令或自定义命令  
4. 切换到 **FeatureIDFinderPage** 查询系统功能 ID 并批量管理  
5. 切换到 **SettingsPage** 修改设置、导入导出、备份还原  
6. 切换到 **OutputPage** 查看命令输出并保存或复制结果

---

## 💻 构建 & 开发

如需从源代码编译：

```bash
git clone https://github.com/Furry-Xiyi/ViveToolWinUI.git
cd ViveToolWinUI
