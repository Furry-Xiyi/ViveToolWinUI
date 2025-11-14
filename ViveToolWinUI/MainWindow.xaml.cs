using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Graphics;
using Windows.Storage;
using Windows.System;

namespace ViveToolWinUI
{
    public sealed partial class MainWindow : Window
    {
        private AppWindow _appWindow = null!;
        private readonly ApplicationDataContainer _settings = ApplicationData.Current.LocalSettings;

        private const string ViveToolFolderName = "ViveTool";
        private const string ViveToolZipName = "ViveTool.zip";
        private const string ViveToolZipUrl = "https://github.com/thebookisclosed/ViVe/releases/latest/download/ViveTool.zip";

        private const int MinWidth = 800;
        private const int MinHeight = 600;
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        // Win32 回调
        private delegate IntPtr WndProcDelegate(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        private WndProcDelegate? _newWndProc;
        private IntPtr _oldWndProc;
        private const int GWLP_WNDPROC = -4;
        private const int WM_GETMINMAXINFO = 0x0024;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        private bool _pendingDragUpdate = false;

        // InfoBar / Toast fields (保留原有逻辑)
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _infoTimer;
        public enum InfoBarSeverity { Informational = 0, Success = 1, Warning = 2, Error = 3 }

        // 保留原有 PendingInfoBarRequest 和 _pendingInfoBarRequest
        private volatile PendingInfoBarRequest? _pendingInfoBarRequest = null;
        private class PendingInfoBarRequest
        {
            public string Title { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public InfoBarSeverity Severity { get; set; } = InfoBarSeverity.Informational;
        }

        // 保留原有 _runningStoryboard
        private Storyboard? _runningStoryboard = null;

        public MainWindow()
        {
            InitializeComponent();

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId)!;

            // 设置窗口大小
            try { _appWindow.Resize(new SizeInt32(1100, 720)); }
            catch { _appWindow.Resize(new SizeInt32(1100, 720)); }

            // 设置任务栏和预览窗口图标（必须传完整路径）
            string iconPath = Path.Combine(
                Windows.ApplicationModel.Package.Current.InstalledLocation.Path,
                "Assets",
                "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                _appWindow.SetIcon(iconPath);
            }

            // 自定义标题栏
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            var tb = _appWindow.TitleBar;
            tb.ExtendsContentIntoTitleBar = true;
            tb.PreferredHeightOption = TitleBarHeightOption.Tall;
            tb.BackgroundColor = Colors.Transparent;
            tb.InactiveBackgroundColor = Colors.Transparent;
            tb.ButtonBackgroundColor = Colors.Transparent;
            tb.ButtonInactiveBackgroundColor = Colors.Transparent;
            tb.ButtonHoverBackgroundColor = ColorHelper.FromArgb(32, 255, 255, 255);
            tb.ButtonPressedBackgroundColor = ColorHelper.FromArgb(64, 255, 255, 255);

            AppTitleBar.Loaded += (_, __) => EnsureDragRectsOnce();
            RootLayout.Loaded += (_, __) => UpdateTitleBarDragRegions();
            _appWindow.Changed += (_, __) => DispatcherQueue.TryEnqueue(UpdateTitleBarDragRegions);

            // 限制最小窗口大小
            SubclassWindowForMinSize(hwnd);

            // 应用首次启动时，强制复制一次
            EnsureViveToolInstalled();

            // 自动更新逻辑
            var auto = (bool?)(ApplicationData.Current.LocalSettings.Values["AutoUpdateViveTool"]) ?? true;
            if (auto)
            {
                DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        await CheckAndPromptViveToolUpdateAsync(silent: true);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Auto-update silent check failed: " + ex);
                    }
                });
            }

            // 默认选中导航菜单第一项
            NavView.SelectedItem = NavView.MenuItems[0];

            // 应用背景和主题
            ApplyBackdropFromSettings();
            UpdateTitleBarButtonsForTheme((Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default);

            // RootLayout 加载后：更新拖拽区域 + 启动定时器隐藏 Toast
            RootLayout.Loaded += (_, __) =>
            {
                UpdateTitleBarDragRegions();

                _infoTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
                _infoTimer.Interval = TimeSpan.FromSeconds(2);
                _infoTimer.Tick += (s, ev) =>
                {
                    HideToast();
                    _infoTimer?.Stop();
                };
            };
        }

        #region WM_GETMINMAXINFO 最小尺寸回调
        private void SubclassWindowForMinSize(IntPtr hwnd)
        {
            _newWndProc = WndProc;
            _oldWndProc = SetWindowLongPtr(hwnd, GWLP_WNDPROC,
                Marshal.GetFunctionPointerForDelegate(_newWndProc));
        }

        private IntPtr WndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_GETMINMAXINFO)
            {
                var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                mmi.ptMinTrackSize.x = MinWidth;
                mmi.ptMinTrackSize.y = MinHeight;
                Marshal.StructureToPtr(mmi, lParam, true);
            }
            return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        #endregion

        #region 导航 / Frame 功能
        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                ContentFrame.Navigate(typeof(ViveToolWinUI.Pages.SettingsPage), null, new EntranceNavigationTransitionInfo());
                return;
            }

            var selected = (args.SelectedItem as NavigationViewItem)?.Tag?.ToString();
            if (string.IsNullOrEmpty(selected)) return;

            Type targetPage = selected switch
            {
                "ViveTool" => typeof(ViveToolWinUI.Pages.ViveToolPage),
                "Output" => typeof(ViveToolWinUI.Pages.OutputPage),
                _ => typeof(ViveToolWinUI.Pages.ViveToolPage)
            };

            if (ContentFrame.CurrentSourcePageType != targetPage)
                ContentFrame.Navigate(targetPage, null, new EntranceNavigationTransitionInfo());
        }

        private void NavView_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        {
            if (ContentFrame.CanGoBack) ContentFrame.GoBack();
        }

        private void ContentFrame_Navigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            NavView.IsBackEnabled = ContentFrame.CanGoBack;

            if (e.SourcePageType == typeof(ViveToolWinUI.Pages.SettingsPage))
            {
                NavView.SelectedItem = NavView.SettingsItem;
                return;
            }

            if (e.SourcePageType == typeof(ViveToolWinUI.Pages.ViveToolPage))
            {
                SelectNavItemByTag("ViveTool");
            }
            else if (e.SourcePageType == typeof(ViveToolWinUI.Pages.OutputPage))
            {
                // 不要高亮 Output，直接清掉
                NavView.SelectedItem = null;
            }
        }

        private void SelectNavItemByTag(string tag)
        {
            foreach (var mi in NavView.MenuItems)
            {
                if (mi is NavigationViewItem nvi && nvi.Tag is string t && t == tag)
                {
                    NavView.SelectedItem = nvi;
                    return;
                }
            }
        }
        #endregion

        #region ViveTool 路径与安装管理

        // ===== 核心路径方法 =====

        /// <summary>
        /// 唯一的 ViveTool 可写目录：LocalState\ViveTool
        /// </summary>
        private string GetViveToolFolder()
        {
            return Path.Combine(ApplicationData.Current.LocalFolder.Path, "ViveTool");
        }

        /// <summary>
        /// ViveTool.exe 的完整路径
        /// </summary>
        private string GetViveToolExePath()
        {
            return Path.Combine(GetViveToolFolder(), "ViveTool.exe");
        }

        /// <summary>
        /// Assets 中的 ViveTool 源目录（只读）
        /// </summary>
        private string GetAssetsViveToolFolder()
        {
            return Path.Combine(AppContext.BaseDirectory, "Assets", "ViveTool");
        }

        // ===== 核心安装方法 =====

        /// <summary>
        /// 确保 ViveTool 已从 Assets 复制到 LocalState
        /// 如果目标目录为空或缺少 exe，强制重新复制
        /// </summary>
        public void EnsureViveToolInstalled()
        {
            var destFolder = GetViveToolFolder();
            var destExe = GetViveToolExePath();
            var assetsFolder = GetAssetsViveToolFolder();

            try
            {
                // 检查目标是否完整
                if (Directory.Exists(destFolder) && File.Exists(destExe))
                {
                    var fileCount = Directory.GetFiles(destFolder, "*", SearchOption.AllDirectories).Length;
                    if (fileCount > 2) // 至少有 exe + dll
                    {
                        Debug.WriteLine($"[ViveTool] 已存在完整安装，跳过复制（{fileCount} 个文件）");
                        return;
                    }
                }

                Debug.WriteLine("[ViveTool] 目标目录不完整，开始从 Assets 复制");

                // 检查 Assets 源目录
                if (!Directory.Exists(assetsFolder))
                {
                    ShowError($"未找到 Assets\\ViveTool 文件夹，请确认打包时包含了该目录");
                    Debug.WriteLine($"[ViveTool] Assets 路径不存在: {assetsFolder}");
                    return;
                }

                var assetsExe = Path.Combine(assetsFolder, "ViveTool.exe");
                if (!File.Exists(assetsExe))
                {
                    ShowError($"Assets\\ViveTool 中缺少 ViveTool.exe");
                    Debug.WriteLine($"[ViveTool] Assets exe 不存在: {assetsExe}");
                    return;
                }

                // 创建目标目录
                Directory.CreateDirectory(destFolder);

                // 清理旧文件
                try
                {
                    foreach (var file in Directory.EnumerateFiles(destFolder))
                    {
                        try { File.Delete(file); } catch { }
                    }
                    foreach (var dir in Directory.EnumerateDirectories(destFolder))
                    {
                        try { Directory.Delete(dir, true); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ViveTool] 清理旧文件失败: {ex.Message}");
                }

                // 递归复制所有文件
                CopyDirectoryRecursive(assetsFolder, destFolder);

                // 验证复制结果
                if (!File.Exists(destExe))
                {
                    ShowError("复制失败：目标文件夹中缺少 ViveTool.exe");
                    Debug.WriteLine($"[ViveTool] 复制后仍缺少 exe: {destExe}");
                    return;
                }

                var copiedCount = Directory.GetFiles(destFolder, "*", SearchOption.AllDirectories).Length;
                Debug.WriteLine($"[ViveTool] 复制完成，共 {copiedCount} 个文件");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ViveTool] 安装异常: {ex}");
            }
        }

        /// <summary>
        /// 递归复制目录（简化版）
        /// </summary>
        private void CopyDirectoryRecursive(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            // 复制所有文件
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var fileName = Path.GetFileName(file);
                var destFile = Path.Combine(destDir, fileName);
                try
                {
                    File.Copy(file, destFile, overwrite: true);
                    Debug.WriteLine($"[ViveTool] 复制文件: {fileName}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ViveTool] 复制文件失败 {fileName}: {ex.Message}");
                }
            }

            // 递归复制子目录
            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                var dirName = Path.GetFileName(subDir);
                var destSubDir = Path.Combine(destDir, dirName);
                CopyDirectoryRecursive(subDir, destSubDir);
            }
        }

        #endregion
        #region ViveTool 更新与执行

        /// <summary>
        /// 下载并导入 ViveTool（从网络更新）
        /// </summary>
        public async Task DownloadAndImportViveToolAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                ShowError("下载链接为空");
                return;
            }

            var tempFolder = Path.Combine(Path.GetTempPath(), $"ViveTool_Download_{Guid.NewGuid():N}");
            var tempZip = Path.Combine(tempFolder, "ViveTool.zip");

            try
            {
                Directory.CreateDirectory(tempFolder);

                // 下载
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                var response = await http.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    ShowError($"下载失败: HTTP {response.StatusCode}");
                    return;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync();
                await File.WriteAllBytesAsync(tempZip, bytes);

                // 备份当前版本到桌面
                var destFolder = GetViveToolFolder();
                if (Directory.Exists(destFolder) && File.Exists(GetViveToolExePath()))
                {
                    try
                    {
                        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                        var backupDir = Path.Combine(desktop, "ViveTool_Backups");
                        Directory.CreateDirectory(backupDir);
                        var backupPath = Path.Combine(backupDir, $"ViveTool_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
                        ZipFile.CreateFromDirectory(destFolder, backupPath, CompressionLevel.Optimal, false);
                        Debug.WriteLine($"[ViveTool] 已备份到: {backupPath}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ViveTool] 备份失败: {ex.Message}");
                    }
                }

                // 清理目标目录
                Directory.CreateDirectory(destFolder);
                foreach (var file in Directory.EnumerateFiles(destFolder))
                {
                    try { File.Delete(file); } catch { }
                }
                foreach (var dir in Directory.EnumerateDirectories(destFolder))
                {
                    try { Directory.Delete(dir, true); } catch { }
                }

                // 解压到目标目录
                ZipFile.ExtractToDirectory(tempZip, destFolder, overwriteFiles: true);

                // 验证
                if (File.Exists(GetViveToolExePath()))
                {
                    ShowSuccess("ViveTool 更新成功");
                    Debug.WriteLine("[ViveTool] 更新完成");
                }
                else
                {
                    ShowError("更新失败：解压后未找到 ViveTool.exe");
                }
            }
            catch (Exception ex)
            {
                ShowError($"更新失败: {ex.Message}");
                Debug.WriteLine($"[ViveTool] 更新异常: {ex}");
            }
            finally
            {
                // 清理临时文件
                try
                {
                    if (File.Exists(tempZip)) File.Delete(tempZip);
                    if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
                }
                catch { }
            }
        }

        /// <summary>
        /// 检查并提示更新（调用 /appupdate 和 /dictupdate）
        /// </summary>
        public async Task CheckAndPromptViveToolUpdateAsync(bool silent = false)
        {
            try
            {
                // 保证已安装并能找到 exe
                EnsureViveToolInstalled();
                var exePath = GetViveToolExePath();
                var workingDir = GetViveToolFolder();

                if (!File.Exists(exePath))
                {
                    if (!silent)
                        ShowError("ViveTool 未安装，无法检查更新");
                    return;
                }

                // 获取当前版本（可能是输出包含换行或额外信息）
                var currentVersionOutput = await GetViveToolVersionAsync();
                var currentMatch = System.Text.RegularExpressions.Regex.Match(currentVersionOutput, @"v?(\d+\.\d+\.\d+)");
                string currentVer = currentMatch.Success ? currentMatch.Groups[1].Value : "0.0.0";

                // 如果不是 silent 模式，显示初始“检测中”对话框；silent 模式下不创建任何 UI，直接后台检测
                ContentDialog initialDialog = null;
                Task<ContentDialogResult>? showTask = null;
                if (!silent)
                {
                    var progressRing = new ProgressRing { IsActive = true, Width = 32, Height = 32 };
                    var progressPanel = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };
                    progressPanel.Children.Add(new TextBlock { Text = "正在检测更新...", HorizontalAlignment = HorizontalAlignment.Center });
                    progressPanel.Children.Add(progressRing);

                    initialDialog = new ContentDialog
                    {
                        Title = "检测更新",
                        Content = progressPanel,
                        CloseButtonText = "取消",
                        XamlRoot = ContentFrame.XamlRoot
                    };

                    // 启动显示（但我们不 await 直到检测结果准备好更新内容）
                    showTask = initialDialog.ShowAsync().AsTask();
                }

                // 后台执行检测（不阻塞 UI），复用你的 RunProcessCaptureAsync 调用
                string combinedOutput = "";
                try
                {
                    combinedOutput = await Task.Run(async () =>
                    {
                        var r = await RunProcessCaptureAsync(exePath, "/appupdate", workingDir, TimeSpan.FromSeconds(30), CancellationToken.None);
                        return $"{r.Output}\n{r.Error}".Trim();
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Update check process failed: " + ex);
                    if (!silent)
                    {
                        // 关闭初始对话框并报错
                        try { initialDialog?.Hide(); } catch { }
                        ShowError("检测更新失败");
                    }
                    return;
                }

                // 解析最新版本号
                var latestMatch = System.Text.RegularExpressions.Regex.Match(combinedOutput, @"v?(\d+\.\d+\.\d+)");
                string latestVer = latestMatch.Success ? latestMatch.Groups[1].Value : "";

                bool hasUpdate = !string.IsNullOrEmpty(latestVer) &&
                                 Version.TryParse(latestVer, out var latest) &&
                                 Version.TryParse(currentVer, out var current) &&
                                 latest > current;

                if (silent)
                {
                    // 静默模式：只有在发现更新时才弹窗提示
                    if (hasUpdate)
                    {
                        try
                        {
                            // 在 UI 线程显示提示并允许用户选择下载
                            DispatcherQueue.TryEnqueue(async () =>
                            {
                                try
                                {
                                    var panel = new StackPanel { Spacing = 8 };
                                    panel.Children.Add(new TextBlock
                                    {
                                        Text = $"当前版本：{currentVer}\n最新版本：{latestVer}\n检测到新版本可用！",
                                        TextWrapping = TextWrapping.Wrap
                                    });

                                    var dialog = new ContentDialog
                                    {
                                        Title = "检测到可用更新",
                                        Content = panel,
                                        PrimaryButtonText = "下载并更新",
                                        CloseButtonText = "稍后",
                                        XamlRoot = ContentFrame.XamlRoot
                                    };

                                    var result = await dialog.ShowAsync();
                                    if (result == ContentDialogResult.Primary)
                                    {
                                        await DownloadAndInstallUpdateAsync(dialog);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine("Failed to prompt update found UI: " + ex);
                                }
                            });
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("Failed to prompt update found UI: " + ex);
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"Auto-update silent check: no update (current={currentVer}, latest={latestVer})");
                    }

                    return;
                }

                // 非 silent（手动触发或原行为）：更新初始对话框内容并等待用户交互
                // 如果初始对话框不存在（理论上不会），就创建一个新的
                if (initialDialog == null)
                {
                    initialDialog = new ContentDialog
                    {
                        Title = "检测更新",
                        Content = new TextBlock { Text = "正在检测更新...", HorizontalAlignment = HorizontalAlignment.Center },
                        CloseButtonText = "取消",
                        XamlRoot = ContentFrame.XamlRoot
                    };
                    showTask = initialDialog.ShowAsync().AsTask();
                }

                // 构建结果面板
                var resultPanel = new StackPanel { Spacing = 8 };
                resultPanel.Children.Add(new TextBlock
                {
                    Text = hasUpdate
                        ? $"当前版本：{currentVer}\n最新版本：{latestVer}\n检测到新版本可用！"
                        : $"当前版本：{currentVer}\n已是最新版本。",
                    TextWrapping = TextWrapping.Wrap
                });

                // 更新对话框并设置按钮
                try
                {
                    initialDialog.Content = resultPanel;
                    initialDialog.PrimaryButtonText = hasUpdate ? "下载并更新" : null;
                    initialDialog.CloseButtonText = hasUpdate ? "稍后" : "关闭";
                }
                catch { /* 忽略可能的 UI 更新异常 */ }

                // 等待用户关闭初始对话框（如果用户在检测进行中就已关闭 it'll return）
                if (showTask != null)
                {
                    var result = await showTask;
                    if (result == ContentDialogResult.Primary && hasUpdate)
                    {
                        await DownloadAndInstallUpdateAsync(initialDialog);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CheckAndPromptViveToolUpdateAsync failed: {ex}");
                if (!silent) ShowError($"检查更新失败: {ex.Message}");
            }
        }
        // 新增: 带进度的下载安装
        private async Task DownloadAndInstallUpdateAsync(ContentDialog existingDialog)
        {
            var progressBar = new ProgressBar { IsIndeterminate = false, Value = 0, Width = 400 };
            var statusText = new TextBlock { Text = "准备下载...", HorizontalAlignment = HorizontalAlignment.Center };
            var progressPanel = new StackPanel { Spacing = 12 };
            progressPanel.Children.Add(statusText);
            progressPanel.Children.Add(progressBar);

            existingDialog.Content = progressPanel;
            existingDialog.Title = "正在下载更新";
            existingDialog.PrimaryButtonText = null;
            existingDialog.CloseButtonText = null;
            existingDialog.IsPrimaryButtonEnabled = false;

            try
            {
                var url = (ApplicationData.Current.LocalSettings.Values["CustomViveToolUpdateUrl"] as string)
                          ?? "https://github.com/thebookisclosed/ViVe/releases/latest/download/ViveTool.zip";

                var tempFolder = Path.Combine(Path.GetTempPath(), $"ViveTool_Download_{Guid.NewGuid():N}");
                var tempZip = Path.Combine(tempFolder, "ViveTool.zip");
                Directory.CreateDirectory(tempFolder);

                // 下载并报告进度
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"HTTP {response.StatusCode}");
                }

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;

                    if (totalBytes > 0)
                    {
                        var progress = (double)totalRead / totalBytes * 100;
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            progressBar.Value = progress;
                            statusText.Text = $"正在下载... {progress:F1}% ({totalRead / 1024}KB / {totalBytes / 1024}KB)";
                        });
                    }
                }

                DispatcherQueue.TryEnqueue(() =>
                {
                    statusText.Text = "正在安装...";
                    progressBar.IsIndeterminate = true;
                });

                // 备份并替换
                var destFolder = GetViveToolFolder();
                if (Directory.Exists(destFolder) && File.Exists(GetViveToolExePath()))
                {
                    try
                    {
                        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                        var backupDir = Path.Combine(desktop, "ViveTool_Backups");
                        Directory.CreateDirectory(backupDir);
                        var backupPath = Path.Combine(backupDir, $"ViveTool_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
                        ZipFile.CreateFromDirectory(destFolder, backupPath, CompressionLevel.Optimal, false);
                    }
                    catch { }
                }

                // 清理并解压
                foreach (var file in Directory.EnumerateFiles(destFolder))
                    try { File.Delete(file); } catch { }
                foreach (var dir in Directory.EnumerateDirectories(destFolder))
                    try { Directory.Delete(dir, true); } catch { }

                ZipFile.ExtractToDirectory(tempZip, destFolder, overwriteFiles: true);

                // 清理临时文件
                try
                {
                    File.Delete(tempZip);
                    Directory.Delete(tempFolder, true);
                }
                catch { }

                DispatcherQueue.TryEnqueue(() =>
                {
                    existingDialog.Title = "更新完成";
                    existingDialog.Content = new TextBlock
                    {
                        Text = "ViveTool 已成功更新到最新版本！",
                        TextWrapping = TextWrapping.Wrap,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    existingDialog.CloseButtonText = "完成";
                });

                ShowSuccess("ViveTool 已更新到最新版本");
            }
            catch (Exception ex)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    existingDialog.Title = "更新失败";
                    existingDialog.Content = new TextBlock
                    {
                        Text = $"更新失败：{ex.Message}",
                        TextWrapping = TextWrapping.Wrap
                    };
                    existingDialog.CloseButtonText = "关闭";
                });
                ShowError($"更新失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取 ViveTool 版本信息
        /// </summary>
        public async Task<string> GetViveToolVersionAsync()
        {
            try
            {
                EnsureViveToolInstalled();
                var exePath = GetViveToolExePath();
                var workingDir = GetViveToolFolder();

                if (!File.Exists(exePath))
                    return "未找到 ViveTool.exe";

                var result = await RunProcessCaptureAsync(exePath, "/version", workingDir, TimeSpan.FromSeconds(20), CancellationToken.None);
                return !string.IsNullOrEmpty(result.Output) ? result.Output : result.Error;
            }
            catch (Exception ex)
            {
                return $"获取版本失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 打开 ViveTool 文件夹
        /// </summary>
        public async Task OpenViveToolFolderAsync()
        {
            try
            {
                EnsureViveToolInstalled();
                var folder = GetViveToolFolder();

                if (!Directory.Exists(folder))
                {
                    ShowWarning($"文件夹不存在: {folder}");
                    return;
                }

                var success = await Launcher.LaunchFolderPathAsync(folder);
                if (!success)
                {
                    // 降级：使用 explorer
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{folder}\"",
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                ShowError($"打开文件夹失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 运行 ViveTool 并捕获输出（无提升权限）
        /// </summary>
        private Task<(string Output, string Error, int ExitCode)> RunProcessCaptureAsync(
    string exePath, string arguments, string workingDir, TimeSpan timeout, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arguments,
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using var proc = Process.Start(psi) ?? throw new InvalidOperationException("无法启动进程");

                // 修复：分开处理 timeout
                bool exited;
                if (timeout > TimeSpan.Zero)
                {
                    exited = proc.WaitForExit((int)timeout.TotalMilliseconds);
                }
                else
                {
                    proc.WaitForExit();
                    exited = true;
                }

                if (!exited)
                {
                    try { proc.Kill(true); } catch { }
                }

                if (ct.IsCancellationRequested)
                {
                    try { if (!proc.HasExited) proc.Kill(true); } catch { }
                    throw new OperationCanceledException();
                }

                return (proc.StandardOutput.ReadToEnd().Trim(),
                        proc.StandardError.ReadToEnd().Trim(),
                        proc.ExitCode);
            }, ct);
        }

        #endregion

        // 运行 ViveTool 带提升（保留原有）
        public Process? RunViveToolWithElevation(string arguments, bool waitForExit = true, int timeoutMs = 120000)
        {
            try
            {
                EnsureViveToolInstalled();

                var exePath = GetViveToolExePath(); // 修复：使用正确的方法
                if (!File.Exists(exePath))
                {
                    ShowError("ViveTool 内核缺失，无法执行");
                    return null;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arguments ?? string.Empty,
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = Path.GetDirectoryName(exePath)
                };

                var proc = Process.Start(psi);
                if (proc == null)
                {
                    ShowError("无法启动 ViveTool");
                    return null;
                }

                if (waitForExit)
                {
                    if (!proc.WaitForExit(timeoutMs))
                    {
                        try { proc.Kill(entireProcessTree: true); } catch { }
                        ShowWarning("ViveTool 执行超时，已终止");
                    }
                }

                return proc;
            }
            catch (System.ComponentModel.Win32Exception ex) when ((uint)ex.NativeErrorCode == 1223)
            {
                ShowWarning("用户取消提升权限");
                return null;
            }
            catch (Exception ex)
            {
                ShowError($"启动 ViveTool 失败: {ex.Message}");
                return null;
            }
        }

        // 运行 ViveTool 带提升并读取输出
        public async Task<string?> RunViveToolElevatedAndReadOutputAsync(string args, int timeoutMs = 120000)
        {
            try
            {
                EnsureViveToolInstalled();
                var exePath = GetViveToolExePath(); // 修复：使用正确的方法
                if (!File.Exists(exePath))
                {
                    ShowError("ViveTool 内核缺失，无法执行");
                    return null;
                }

                var tmpOut = Path.Combine(Path.GetTempPath(), $"vivetool_out_{Guid.NewGuid():N}.txt");

                var cmdArgs = $"/c \"{exePath} {args} > \"{tmpOut}\" 2>&1\"";

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = cmdArgs,
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                var proc = Process.Start(psi);
                if (proc == null) return null;

                if (!proc.WaitForExit(timeoutMs))
                {
                    try { proc.Kill(); } catch { }
                    ShowWarning("ViveTool 执行超时");
                }

                if (File.Exists(tmpOut))
                {
                    var txt = await File.ReadAllTextAsync(tmpOut, Encoding.UTF8);
                    try { File.Delete(tmpOut); } catch { }
                    return txt;
                }

                return null;
            }
            catch (System.ComponentModel.Win32Exception ex) when ((uint)ex.NativeErrorCode == 1223)
            {
                ShowWarning("用户取消提升权限");
                return null;
            }
            catch (Exception ex)
            {
                ShowError($"执行 ViveTool 失败: {ex.Message}");
                return null;
            }
        }

        #region 背景与主题
        public void ApplyBackdropFromSettings()
        {
            var s = ApplicationData.Current.LocalSettings.Values;
            var material = (s["BackgroundMaterial"] as string) ?? "Mica";

            // First apply theme so controls update colors before backdrop changes
            var theme = (s["AppTheme"] as string) ?? "System";
            if (theme == "Light") (Content as FrameworkElement)!.RequestedTheme = ElementTheme.Light;
            else if (theme == "Dark") (Content as FrameworkElement)!.RequestedTheme = ElementTheme.Dark;
            else (Content as FrameworkElement)!.RequestedTheme = ElementTheme.Default;

            UpdateTitleBarButtonsForTheme((Content as FrameworkElement)!.RequestedTheme);

            // Only change SystemBackdrop if needed to avoid unnecessary recreations/flicker
            try
            {
                var current = this.SystemBackdrop;

                bool needsNewBackdrop = false;
                dynamic? newBackdrop = null;

                // Check if we need a new backdrop type
                if (material == "Mica")
                {
                    if (current is not MicaBackdrop mb || mb.Kind != MicaKind.Base)
                    {
                        needsNewBackdrop = true;
                        newBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
                    }
                }
                else if (material == "MicaAlt")
                {
                    if (current is not MicaBackdrop mb || mb.Kind != MicaKind.BaseAlt)
                    {
                        needsNewBackdrop = true;
                        newBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
                    }
                }
                else if (material == "Acrylic")
                {
                    if (current is not DesktopAcrylicBackdrop)
                    {
                        needsNewBackdrop = true;
                        newBackdrop = new DesktopAcrylicBackdrop();
                    }
                }
                else if (current != null)
                {
                    // clear backdrop if not recognized
                    this.SystemBackdrop = null;
                }

                // Apply new backdrop if needed
                if (needsNewBackdrop && newBackdrop != null)
                {
                    // Detach old backdrop first
                    this.SystemBackdrop = null;

                    // Small delay to let the old backdrop detach, then apply new backdrop on UI thread
                    DispatcherQueue.TryEnqueue(async () =>
                    {
                        await Task.Delay(10);
                        try { this.SystemBackdrop = newBackdrop; } catch { }
                    });
                }
            }
            catch { }
        }

        public void UpdateTitleBarButtonsForTheme(ElementTheme theme)
        {
            var tb = _appWindow.TitleBar;
            tb.ExtendsContentIntoTitleBar = true;

            Windows.UI.Color fg = theme switch
            {
                ElementTheme.Light => Microsoft.UI.Colors.Black,
                ElementTheme.Dark => Microsoft.UI.Colors.White,
                _ => ((SolidColorBrush)Application.Current.Resources["TextFillColorPrimaryBrush"]).Color
            };

            tb.ButtonForegroundColor = fg;
            tb.ButtonInactiveForegroundColor = fg;

            if (theme == ElementTheme.Light)
            {
                tb.ButtonHoverBackgroundColor = ColorHelper.FromArgb(24, 0, 0, 0);
                tb.ButtonPressedBackgroundColor = ColorHelper.FromArgb(48, 0, 0, 0);
            }
            else
            {
                tb.ButtonHoverBackgroundColor = ColorHelper.FromArgb(32, 255, 255, 255);
                tb.ButtonPressedBackgroundColor = ColorHelper.FromArgb(64, 255, 255, 255);
            }

            tb.BackgroundColor = Microsoft.UI.Colors.Transparent;
            tb.InactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            tb.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            tb.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        }
        #endregion

        #region TitleBar 拖拽区域
        private void EnsureDragRectsOnce()
        {
            if (AppTitleBar == null) return;
            if (AppTitleBar.ActualWidth <= 0 || AppTitleBar.ActualHeight <= 0)
            {
                AppTitleBar.Loaded += (_, __) => UpdateTitleBarDragRegions();
                return;
            }
            UpdateTitleBarDragRegions();
        }

        public void UpdateTitleBarDragRegions()
        {
            if (_pendingDragUpdate) return;
            _pendingDragUpdate = true;

            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                _pendingDragUpdate = false;
                var tb = _appWindow.TitleBar;
                if (tb == null) return;

                try
                {
                    double scale = RootLayout?.XamlRoot?.RasterizationScale ?? 1.0;
                    int windowWidthPx = (int)Math.Round(Bounds.Width * scale);
                    int barHeightPx = tb.Height;
                    int leftInsetPx = tb.LeftInset;
                    int rightInsetPx = tb.RightInset;

                    if (barHeightPx <= 0 || windowWidthPx <= 0) { SetFullDragRectangles(); return; }

                    var excludeRanges = new List<(int L, int R)>();

                    void TryAddExclude(FrameworkElement fe)
                    {
                        if (fe == null || fe.Visibility != Visibility.Visible || fe.ActualWidth < 1) return;
                        if (fe.XamlRoot?.Content == null) return;
                        GeneralTransform t = fe.TransformToVisual(null);
                        var origin = t.TransformPoint(new Windows.Foundation.Point(0, 0));
                        int l = Math.Clamp((int)Math.Round(origin.X * scale), 0, windowWidthPx);
                        int r = Math.Clamp((int)Math.Round((origin.X + fe.ActualWidth) * scale), 0, windowWidthPx);
                        if (r > l) excludeRanges.Add((l, r));
                    }

                    TryAddExclude(AppTitleBar?.FindName("SearchBox") as FrameworkElement);
                    TryAddExclude(AppTitleBar?.FindName("AvatarButton") as FrameworkElement);

                    if (excludeRanges.Count == 0) { SetFullDragRectangles(); return; }

                    excludeRanges.Sort((a, b) => a.L.CompareTo(b.L));
                    var merged = new List<(int L, int R)>();
                    foreach (var range in excludeRanges)
                    {
                        if (merged.Count == 0 || range.L > merged[^1].R) merged.Add(range);
                        else merged[^1] = (merged[^1].L, Math.Max(merged[^1].R, range.R));
                    }

                    var rects = new List<Windows.Graphics.RectInt32>();
                    int currentX = leftInsetPx;
                    foreach (var (L, R) in merged)
                    {
                        if (L > currentX) rects.Add(new Windows.Graphics.RectInt32(currentX, 0, L - currentX, barHeightPx));
                        currentX = Math.Max(currentX, R);
                    }
                    if (currentX < windowWidthPx - rightInsetPx) rects.Add(new Windows.Graphics.RectInt32(currentX, 0, windowWidthPx - rightInsetPx - currentX, barHeightPx));

                    tb.SetDragRectangles(rects.ToArray());
                }
                catch (ObjectDisposedException) { System.Diagnostics.Debug.WriteLine("[TitleBar] 窗口已关闭"); }
            });
        }

        private void SetFullDragRectangles()
        {
            var tb = _appWindow.TitleBar;
            if (tb == null) return;

            double scale = RootLayout?.XamlRoot?.RasterizationScale ?? 1.0;
            int windowWidthPx = (int)Math.Round(Bounds.Width * scale);
            int barHeightPx = tb.Height;
            int leftInsetPx = tb.LeftInset;
            int rightInsetPx = tb.RightInset;

            int x = leftInsetPx;
            int w = Math.Max(0, windowWidthPx - leftInsetPx - rightInsetPx);
            if (w <= 0) return;

            try { tb.SetDragRectangles(new[] { new Windows.Graphics.RectInt32(x, 0, w, barHeightPx) }); }
            catch { }
        }
        #endregion

        #region InfoBar / Toast 提示
        public void ShowToast(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                if (TopInfoBar == null) return;

                if (TopInfoBar.IsOpen)
                {
                    var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                    var slideOut = new DoubleAnimation
                    {
                        To = -Math.Max(80, TopInfoBar.ActualHeight + 4),
                        Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                    };

                    var sbOut = new Storyboard();
                    sbOut.Children.Add(slideOut);
                    Storyboard.SetTarget(slideOut, TopInfoBar.RenderTransform);
                    Storyboard.SetTargetProperty(slideOut, "Y");
                    sbOut.Completed += (_, __) =>
                    {
                        try { TopInfoBar.IsOpen = false; } catch { }
                        tcs.TrySetResult(true);
                    };

                    try { sbOut.Begin(); } catch { tcs.TrySetResult(true); }

                    await tcs.Task;
                    await Task.Delay(50);
                }

                TopInfoBar.Severity = severity switch
                {
                    InfoBarSeverity.Informational => Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational,
                    InfoBarSeverity.Success => Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success,
                    InfoBarSeverity.Warning => Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning,
                    InfoBarSeverity.Error => Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error,
                    _ => Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational
                };
                TopInfoBar.Message = message;
                try { TopInfoBar.IsOpen = true; } catch { }

                var slideIn = new DoubleAnimation
                {
                    To = 0,
                    Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                var sbIn = new Storyboard();
                sbIn.Children.Add(slideIn);
                Storyboard.SetTarget(slideIn, TopInfoBar.RenderTransform);
                Storyboard.SetTargetProperty(slideIn, "Y");
                try { sbIn.Begin(); } catch { }

                _infoTimer?.Stop();
                _infoTimer?.Start();
            });
        }

        public void HideToast()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (TopInfoBar == null || !TopInfoBar.IsOpen) return;

                var slideOut = new DoubleAnimation
                {
                    To = -Math.Max(80, TopInfoBar.ActualHeight + 4),
                    Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                };
                var sb = new Storyboard();
                sb.Children.Add(slideOut);
                Storyboard.SetTarget(slideOut, TopInfoBar.RenderTransform);
                Storyboard.SetTargetProperty(slideOut, "Y");
                sb.Completed += (_, __) =>
                {
                    try { TopInfoBar.IsOpen = false; } catch { }
                };
                try { sb.Begin(); } catch { TopInfoBar.IsOpen = false; }
            });
        }

        public void ShowSuccess(string message) => ShowToast(message, InfoBarSeverity.Success);
        public void ShowWarning(string message) => ShowToast(message, InfoBarSeverity.Warning);
        public void ShowError(string message) => ShowToast(message, InfoBarSeverity.Error);
        public void ShowInfo(string message) => ShowToast(message, InfoBarSeverity.Informational);
        #endregion

        #region Dispatcher 队列 UI 回调
        private Task DispatcherQueueInvokeAsync(Func<Task> uiFunc)
        {
            if (DispatcherQueue.HasThreadAccess)
            {
                return uiFunc();
            }

            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool queued = DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await uiFunc().ConfigureAwait(false);
                    tcs.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            if (!queued) tcs.TrySetException(new InvalidOperationException("无法访问 UI DispatcherQueue"));
            return tcs.Task;
        }
        #endregion

        #region Splash 启动屏显示/隐藏
        // 启动时调用
        public void ShowSplashOverlay()
        {
            SplashOverlay.Visibility = Visibility.Visible;
            SplashOverlay.Opacity = 1;
            SplashOverlay.HorizontalAlignment = HorizontalAlignment.Stretch;
            SplashOverlay.VerticalAlignment = VerticalAlignment.Stretch;

            SplashOverlay.Background = (Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"];

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            uint dpi = GetDpiForWindow(hwnd);
            string scaleSuffix = dpi >= 288 ? "400" : dpi >= 192 ? "200" : "100";
            string imagePath = $"Assets/SplashScreen.scale-{scaleSuffix}.png";

            SplashImage.Stretch = Stretch.Uniform; // 确保比例显示
            SplashImage.Source = new BitmapImage(new Uri($"ms-appx:///{imagePath}"));
        }

        // 隐藏时调用
        public void HideSplashOverlay()
        {
            var visual = ElementCompositionPreview.GetElementVisual(SplashOverlay);
            var compositor = visual.Compositor;

            var fadeAnimation = compositor.CreateScalarKeyFrameAnimation();
            fadeAnimation.InsertKeyFrame(1f, 0f);
            fadeAnimation.Duration = TimeSpan.FromMilliseconds(250);

            var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            visual.StartAnimation("Opacity", fadeAnimation);
            batch.End();

            batch.Completed += (s, e) =>
            {
                SplashOverlay.Visibility = Visibility.Collapsed;
                visual.Opacity = 1f;
            };
        }
        #endregion
    }

    internal static class FrameworkElementExtensions
    {
        public static T As<T>(this object obj) where T : class => (T)obj;
    }
}