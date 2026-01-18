using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Graphics;
using Windows.Storage;
using Windows.System;

namespace ViveToolWinUI
{
    public sealed partial class MainWindow : Window
    {
        private AppWindow _appWindow;
        private readonly ApplicationDataContainer _settings = ApplicationData.Current.LocalSettings;
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _toastTimer;
        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern uint GetDpiForWindow(IntPtr hWnd);


        // 内核路径
        private string KernelFolder => Path.Combine(ApplicationData.Current.LocalFolder.Path, "ViveTool");
        private string KernelExe => Path.Combine(KernelFolder, "ViveTool.exe");
        private string AssetsKernelFolder => Path.Combine(AppContext.BaseDirectory, "Assets", "ViveTool");

        public MainWindow()
        {
            InitializeComponent();
            InitializeWindow();
            EnsureKernelInstalled();
            ApplySettings();

            // 自动检查更新
            if (GetSettingBool("AutoUpdateViveTool", true))
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(2000);
                    await CheckAndPromptUpdateAsync(true);
                });
            }
            NavView.SelectedItem = NavView.MenuItems[0];
        }

        #region 窗口初始化

        private void InitializeWindow()
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);

            // 设置窗口大小
            _appWindow.Resize(new SizeInt32(1100, 720));

            // 设置图标
            var iconPath = Path.Combine(Package.Current.InstalledLocation.Path, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
                _appWindow.SetIcon(iconPath);

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

            // Toast 定时器
            RootLayout.Loaded += (_, __) =>
            {
                _toastTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
                _toastTimer.Interval = TimeSpan.FromSeconds(3);
                _toastTimer.Tick += (s, e) =>
                {
                    HideToast();
                    _toastTimer?.Stop();
                };
            };
        }

        #endregion
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

            SplashImage.Stretch = Stretch.Uniform; // ��֤������ʾ
            SplashImage.Source = new BitmapImage(new Uri($"ms-appx:///{imagePath}"));
        }

        // Splash
        public void HideSplashOverlay()
        {
            var visual = ElementCompositionPreview.GetElementVisual(SplashOverlay);
            var compositor = visual.Compositor;

            var fadeAnimation = compositor.CreateScalarKeyFrameAnimation();
            fadeAnimation.InsertKeyFrame(1f, 0f);
            fadeAnimation.Duration = TimeSpan.FromMilliseconds(250);

            var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            visual.StartAnimation(nameof(visual.Opacity), fadeAnimation);
            batch.End();

            batch.Completed += (s, e) =>
            {
                SplashOverlay.Visibility = Visibility.Collapsed;
                visual.Opacity = 0f;
            };
        }
        #region 导航

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                ContentFrame.Navigate(typeof(Pages.SettingsPage), null, new EntranceNavigationTransitionInfo());
                return;
            }

            var selected = (args.SelectedItem as NavigationViewItem)?.Tag?.ToString();
            if (string.IsNullOrEmpty(selected)) return;

            Type targetPage = selected switch
            {
                "QuickActions" => typeof(Pages.ViveToolPage),
                "IDFinder" => typeof(Pages.FeatureIDFinderPage),
                "Output" => typeof(Pages.OutputPage),
                _ => typeof(Pages.ViveToolPage)
            };

            if (ContentFrame.CurrentSourcePageType != targetPage)
                ContentFrame.Navigate(targetPage, null, new EntranceNavigationTransitionInfo());
        }

        private void NavView_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        {
            if (ContentFrame.CanGoBack)
                ContentFrame.GoBack();
        }

        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            NavView.IsBackEnabled = ContentFrame.CanGoBack;

            if (e.SourcePageType == typeof(Pages.SettingsPage))
            {
                NavView.SelectedItem = NavView.SettingsItem;
                return;
            }

            foreach (var item in NavView.MenuItems)
            {
                if (item is NavigationViewItem nvi)
                {
                    var tag = nvi.Tag?.ToString();
                    if (tag == "QuickActions" && e.SourcePageType == typeof(Pages.ViveToolPage))
                        NavView.SelectedItem = nvi;
                    else if (tag == "IDFinder" && e.SourcePageType == typeof(Pages.FeatureIDFinderPage))
                        NavView.SelectedItem = nvi;
                    else if (tag == "Output" && e.SourcePageType == typeof(Pages.OutputPage))
                        NavView.SelectedItem = nvi;
                }
            }
        }

        #endregion

        #region ViveTool 内核管理

        // 确保内核已安装
        public void EnsureKernelInstalled()
        {
            try
            {
                // 检查是否已存在
                if (Directory.Exists(KernelFolder) && File.Exists(KernelExe))
                {
                    var fileCount = Directory.GetFiles(KernelFolder, "*", SearchOption.AllDirectories).Length;
                    if (fileCount > 2) return; // 已安装
                }

                // 检查 Assets 源
                if (!Directory.Exists(AssetsKernelFolder) || !File.Exists(Path.Combine(AssetsKernelFolder, "ViveTool.exe")))
                {
                    ShowError("ViveTool kernel not found in Assets folder");
                    return;
                }

                // 创建目标文件夹
                Directory.CreateDirectory(KernelFolder);

                // 清理旧文件
                foreach (var file in Directory.EnumerateFiles(KernelFolder))
                    try { File.Delete(file); } catch { }

                // 复制所有文件
                CopyDirectory(AssetsKernelFolder, KernelFolder);

                Debug.WriteLine($"[Kernel] Installed from Assets, files: {Directory.GetFiles(KernelFolder).Length}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Kernel] Installation failed: {ex.Message}");
            }
        }

        // 递归复制目录
        private void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);

            foreach (var file in Directory.GetFiles(source))
            {
                var fileName = Path.GetFileName(file);
                File.Copy(file, Path.Combine(dest, fileName), true);
            }

            foreach (var dir in Directory.GetDirectories(source))
            {
                var dirName = Path.GetFileName(dir);
                CopyDirectory(dir, Path.Combine(dest, dirName));
            }
        }

        // 获取内核版本
        public async Task<string> GetKernelVersionAsync()
        {
            try
            {
                if (!File.Exists(KernelExe))
                    return "Not installed";

                var result = await ExecuteViveToolAsync("/version");
                return result.Success ? result.Output : "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        // 备份内核
        public async Task<string?> BackupKernelAsync()
        {
            try
            {
                if (!Directory.Exists(KernelFolder) || !File.Exists(KernelExe))
                {
                    ShowError("Kernel not found");
                    return null;
                }

                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var backupDir = Path.Combine(desktop, "ViveTool_Backups");
                Directory.CreateDirectory(backupDir);

                var zipPath = Path.Combine(backupDir, $"ViveTool_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
                await Task.Run(() => ZipFile.CreateFromDirectory(KernelFolder, zipPath));

                return zipPath;
            }
            catch (Exception ex)
            {
                ShowError($"Backup failed: {ex.Message}");
                return null;
            }
        }

        // 还原内核
        public async Task<bool> RestoreKernelAsync(string zipPath)
        {
            try
            {
                if (!File.Exists(zipPath))
                {
                    ShowError("Backup file not found");
                    return false;
                }

                // 清理现有文件
                foreach (var file in Directory.EnumerateFiles(KernelFolder))
                    try { File.Delete(file); } catch { }

                // 解压
                await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, KernelFolder, true));

                return File.Exists(KernelExe);
            }
            catch (Exception ex)
            {
                ShowError($"Restore failed: {ex.Message}");
                return false;
            }
        }

        // 打开内核文件夹
        public async Task OpenKernelFolderAsync()
        {
            try
            {
                if (!Directory.Exists(KernelFolder))
                {
                    ShowWarning("Kernel folder not found");
                    return;
                }

                await Launcher.LaunchFolderPathAsync(KernelFolder);
            }
            catch
            {
                // 降级：使用 explorer
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{KernelFolder}\"",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    ShowError($"Failed to open folder: {ex.Message}");
                }
            }
        }

        // 检查并提示更新
        public async Task CheckAndPromptUpdateAsync(bool silent)
        {
            try
            {
                EnsureKernelInstalled();

                if (!File.Exists(KernelExe))
                {
                    if (!silent) ShowError("Kernel not installed");
                    return;
                }

                // 获取当前版本
                var currentVersion = await GetKernelVersionAsync();
                var currentMatch = Regex.Match(currentVersion, @"v?(\d+\.\d+\.\d+)");
                var current = currentMatch.Success ? currentMatch.Groups[1].Value : "0.0.0";

                // 执行 /appupdate 检查
                var result = await ExecuteViveToolAsync("/appupdate");
                var latestMatch = Regex.Match(result.Output, @"v?(\d+\.\d+\.\d+)");
                var latest = latestMatch.Success ? latestMatch.Groups[1].Value : "";

                var hasUpdate = !string.IsNullOrEmpty(latest) &&
                               Version.TryParse(latest, out var latestVer) &&
                               Version.TryParse(current, out var currentVer) &&
                               latestVer > currentVer;

                if (silent && !hasUpdate)
                {
                    Debug.WriteLine($"[Update] No update available (current: {current})");
                    return;
                }

                // 显示更新对话框
                await DispatcherQueue.EnqueueAsync(async () =>
                {
                    var content = hasUpdate
                        ? $"Current: {current}\nLatest: {latest}\n\nNew version available!"
                        : $"Current: {current}\nYou are up to date.";

                    var dialog = new ContentDialog
                    {
                        Title = "Check for Updates",
                        Content = new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap },
                        PrimaryButtonText = hasUpdate ? "Download & Install" : null,
                        CloseButtonText = hasUpdate ? "Later" : "Close",
                        XamlRoot = Content.XamlRoot
                    };

                    var dlgResult = await dialog.ShowAsync();
                    if (dlgResult == ContentDialogResult.Primary && hasUpdate)
                    {
                        await DownloadAndInstallUpdateAsync();
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Update] Check failed: {ex.Message}");
                if (!silent) ShowError("Update check failed");
            }
        }

        // 下载并安装更新
        private async Task DownloadAndInstallUpdateAsync()
        {
            var progressDialog = new ContentDialog
            {
                Title = "Downloading Update",
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock { Text = "Downloading ViveTool..." },
                        new ProgressBar { IsIndeterminate = true }
                    }
                },
                XamlRoot = Content.XamlRoot
            };

            var showTask = progressDialog.ShowAsync();

            try
            {
                var url = GetSetting("CustomViveToolUpdateUrl", "https://github.com/thebookisclosed/ViVe/releases/latest/download/ViveTool.zip");
                var tempZip = Path.Combine(Path.GetTempPath(), $"ViveTool_{Guid.NewGuid():N}.zip");

                // 下载
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                var response = await http.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    progressDialog.Hide();
                    ShowError($"Download failed: HTTP {response.StatusCode}");
                    return;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync();
                await File.WriteAllBytesAsync(tempZip, bytes);

                // 备份当前版本
                var backupPath = await BackupKernelAsync();
                if (backupPath != null)
                    Debug.WriteLine($"[Update] Backed up to: {backupPath}");

                // 清理并解压
                foreach (var file in Directory.EnumerateFiles(KernelFolder))
                    try { File.Delete(file); } catch { }

                await Task.Run(() => ZipFile.ExtractToDirectory(tempZip, KernelFolder, true));

                // 清理临时文件
                try { File.Delete(tempZip); } catch { }

                progressDialog.Hide();

                if (File.Exists(KernelExe))
                    ShowSuccess("Update installed successfully!");
                else
                    ShowError("Update failed: ViveTool.exe not found after extraction");
            }
            catch (Exception ex)
            {
                try { progressDialog.Hide(); } catch { }
                ShowError($"Update failed: {ex.Message}");
            }
        }

        // 执行 ViveTool 命令
        private async Task<(bool Success, string Output)> ExecuteViveToolAsync(string arguments)
        {
            if (!File.Exists(KernelExe))
                return (false, "ViveTool not found");

            var tempDir = Path.GetTempPath();
            var id = Guid.NewGuid().ToString("N");
            var outFile = Path.Combine(tempDir, $"vt_out_{id}.txt");
            var scriptFile = Path.Combine(tempDir, $"vt_script_{id}.ps1");

            try
            {
                var script = $@"
$ErrorActionPreference = 'Continue'
Set-Location '{KernelFolder}'
& '{KernelExe}' {arguments} *>&1 | Tee-Object -FilePath '{outFile}'
exit $LASTEXITCODE
";
                await File.WriteAllTextAsync(scriptFile, script);

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptFile}\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var process = Process.Start(psi);
                if (process == null) return (false, "Failed to start process");

                await process.WaitForExitAsync();

                var output = File.Exists(outFile) ? await File.ReadAllTextAsync(outFile) : "";
                var success = process.ExitCode == 0 && !output.ToLower().Contains("error");

                try
                {
                    if (File.Exists(scriptFile)) File.Delete(scriptFile);
                    if (File.Exists(outFile)) File.Delete(outFile);
                }
                catch { }

                return (success, output);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return (false, "UAC denied");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        #endregion

        #region 主题和背景

        public void ApplySettings()
        {
            // 应用主题
            var theme = GetSetting("AppTheme", "System");
            if (Content is FrameworkElement fe)
            {
                fe.RequestedTheme = theme switch
                {
                    "Light" => ElementTheme.Light,
                    "Dark" => ElementTheme.Dark,
                    _ => ElementTheme.Default
                };
                UpdateTitleBarForTheme(fe.RequestedTheme);
            }

            // 应用背景
            var material = GetSetting("BackgroundMaterial", "Mica");
            try
            {
                SystemBackdrop = material switch
                {
                    "Acrylic" => new DesktopAcrylicBackdrop(),
                    "Mica_Alt" => new MicaBackdrop { Kind = MicaKind.BaseAlt },
                    _ => new MicaBackdrop { Kind = MicaKind.Base }
                };
            }
            catch { }
        }

        private void UpdateTitleBarForTheme(ElementTheme theme)
        {
            var tb = _appWindow.TitleBar;
            var fg = theme switch
            {
                ElementTheme.Light => Colors.Black,
                ElementTheme.Dark => Colors.White,
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
        }

        #endregion

        #region Toast 通知

        public void ShowToast(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                TopInfoBar.Severity = severity;
                TopInfoBar.Message = message;
                TopInfoBar.IsOpen = true;

                var slideIn = new DoubleAnimation
                {
                    To = 0,
                    Duration = new Duration(TimeSpan.FromMilliseconds(250))
                };
                var sb = new Storyboard();
                sb.Children.Add(slideIn);
                Storyboard.SetTarget(slideIn, TopInfoBar.RenderTransform);
                Storyboard.SetTargetProperty(slideIn, "Y");
                sb.Begin();

                _toastTimer?.Stop();
                _toastTimer?.Start();
            });
        }

        private void HideToast()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!TopInfoBar.IsOpen) return;

                var slideOut = new DoubleAnimation
                {
                    To = -80,
                    Duration = new Duration(TimeSpan.FromMilliseconds(250))
                };
                var sb = new Storyboard();
                sb.Children.Add(slideOut);
                Storyboard.SetTarget(slideOut, TopInfoBar.RenderTransform);
                Storyboard.SetTargetProperty(slideOut, "Y");
                sb.Completed += (_, __) => TopInfoBar.IsOpen = false;
                sb.Begin();
            });
        }

        public void ShowSuccess(string msg) => ShowToast(msg, InfoBarSeverity.Success);
        public void ShowWarning(string msg) => ShowToast(msg, InfoBarSeverity.Warning);
        public void ShowError(string msg) => ShowToast(msg, InfoBarSeverity.Error);
        public void ShowInfo(string msg) => ShowToast(msg, InfoBarSeverity.Informational);

        #endregion

        #region 设置读写

        private string GetSetting(string key, string defaultValue)
        {
            return _settings.Values.TryGetValue(key, out var value) && value is string s
                ? s : defaultValue;
        }

        private bool GetSettingBool(string key, bool defaultValue)
        {
            if (_settings.Values.TryGetValue(key, out var value))
            {
                if (value is bool b) return b;
                if (value is string s && bool.TryParse(s, out var result)) return result;
            }
            return defaultValue;
        }

        #endregion
    }

    // DispatcherQueue 扩展
    public static class DispatcherQueueExtensions
    {
        public static Task EnqueueAsync(this Microsoft.UI.Dispatching.DispatcherQueue queue, Func<Task> action)
        {
            var tcs = new TaskCompletionSource();
            queue.TryEnqueue(async () =>
            {
                try
                {
                    await action();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            return tcs.Task;
        }
    }
}