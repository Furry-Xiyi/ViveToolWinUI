using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.ApplicationModel.Resources;
using Microsoft.UI.Xaml.Media;

namespace ViveToolWinUI.Pages
{
    public sealed partial class SettingsPage : Page
    {
        private ApplicationDataContainer S => ApplicationData.Current.LocalSettings;
        private bool _isInitializing;

        // 默认更新链接列表（供恢复默认时使用）
        private static readonly List<string> DefaultUpdateLinks = new()
        {
            "https://github.com/thebookisclosed/ViVe/releases/latest/download/ViveTool.zip",
            "https://download.fastgit.org/thebookisclosed/ViVe/releases/latest/download/ViveTool.zip",
            "https://gitee.com/mirror/ViVe/releases/download/latest/ViveTool.zip"
        };

        // 关于/反馈默认链接（可被设置覆盖）
        public Uri QQUri { get; set; } = new("https://jq.qq.com/");
        public Uri DiscordUri { get; set; } = new("https://discord.com/");
        public Uri FeedbackUri { get; set; } = new("https://yourdomain.com/feedback");

        public SettingsPage()
        {
            InitializeComponent();
            Loaded += SettingsPage_Loaded;
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;

            // Use ResourceLoader for localization (x:Uid in XAML should normally handle localization)
            try
            {
                var rl = ResourceLoader.GetForCurrentView();
                void Apply(string elementName, string resourceKey)
                {
                    try
                    {
                        var tb = this.FindName(elementName) as TextBlock;
                        if (tb == null) return;
                        var v = rl.GetString(resourceKey);
                        if (!string.IsNullOrEmpty(v)) tb.Text = v;
                    }
                    catch { }
                }

                Apply("AppearanceGroupText", "AppearanceGroup.Text");
                Apply("ThemeHeaderTitle", "ThemeHeader.Title.Text");
                Apply("ThemeHeaderSubtitle", "ThemeHeader.Subtitle.Text");
                Apply("ThemeSystemText", "ThemeSystem.Text");
                Apply("ThemeLightText", "ThemeLight.Text");
                Apply("ThemeDarkText", "ThemeDark.Text");

                Apply("MaterialHeaderTitle", "MaterialHeader.Title.Text");
                Apply("MaterialHeaderSubtitle", "MaterialHeader.Subtitle.Text");
                Apply("MaterialAcrylicText", "MaterialAcrylic.Text");
                Apply("MaterialMicaText", "MaterialMica.Text");
                Apply("MaterialMicaAltText", "MaterialMicaAlt.Text");

                Apply("BehaviorGroupText", "BehaviorGroup.Text");
                Apply("KernelHeaderTitle", "KernelHeader.Title.Text");
                Apply("KernelHeaderSubtitle", "KernelHeader.Subtitle.Text");

                Apply("BtnCheckViveToolUpdateText", "BtnCheckViveToolUpdate.Text");
                Apply("BtnCustomUpdateLinkText", "BtnCustomUpdateLink.Text");
                Apply("BtnImportKernelZipText", "BtnImportKernelZip.Text");
                Apply("BtnOpenCoreFolderText", "BtnOpenCoreFolder.Text");
                Apply("BtnBackupCoreText", "BtnBackupCore.Text");
                Apply("BtnRestoreCoreText", "BtnRestoreCore.Text");

                Apply("BtnExportSettingsText", "BtnExportSettings.Text");
                Apply("BtnImportSettingsText", "BtnImportSettings.Text");
                Apply("BtnResetSettingsText", "BtnResetSettings.Text");

                Apply("BtnTestViveToolText", "BtnTestViveTool.Text");
                Apply("AboutTitleText", "AboutTitle.Text");
                Apply("JoinQQButtonText", "JoinQQButton.Text");
                Apply("JoinDiscordButtonText", "JoinDiscordButton.Text");
                Apply("FeedbackButtonText", "FeedbackButton.Text");

                var toggleHeader = this.FindName("AutoUpdateToggleHeaderText") as TextBlock;
                if (toggleHeader != null)
                {
                    try { var s = rl.GetString("AutoUpdateViveToolToggleHeader.Text"); if (!string.IsNullOrEmpty(s)) toggleHeader.Text = s; } catch { }
                }
            }
            catch { }

            //主题设置
            var theme = (S.Values["AppTheme"] as string) ?? "System";
            ThemeSystem.IsChecked = theme == "System";
            ThemeLight.IsChecked = theme == "Light";
            ThemeDark.IsChecked = theme == "Dark";

            // 材质设置
            var mat = (S.Values["BackgroundMaterial"] as string) ?? "Mica";
            MaterialAcrylic.IsChecked = string.Equals(mat, "Acrylic", StringComparison.OrdinalIgnoreCase);
            MaterialMica.IsChecked = string.Equals(mat, "Mica", StringComparison.OrdinalIgnoreCase);
            MaterialMicaAlt.IsChecked = string.Equals(mat, "MicaAlt", StringComparison.OrdinalIgnoreCase);

            // 自动更新开关
            AutoUpdateViveToolToggle.IsOn = (bool?)(S.Values["AutoUpdateViveTool"]) ?? true;

            // 关于面板的 App 图标与版本信息
            try
            {
                var ver = Package.Current.Id.Version;
                AppTitle.Text = Package.Current.DisplayName;
                var publisherName = Package.Current.PublisherDisplayName?.Trim();
                if (!string.IsNullOrEmpty(publisherName))
                {
                    AppPublisher.Text = $"©{DateTime.Now.Year} {publisherName}";
                    AppPublisher.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                }
                else
                {
                    AppPublisher.Text = string.Empty;
                    AppPublisher.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                }
                AppVersion.Text = $"版本 {ver.Major}.{ver.Minor}.{ver.Build}.{ver.Revision}";

                // 尝试加载 Assets/StoreLogo 的合适缩放版本（根据 rasterization scale选择 scale-100/200/400）
                bool logoSet = false;
                var imgElem = this.FindName("AppLogoImage") as Image;

                //计算 scale 后缀（简单基于 XamlRoot.RasterizationScale）
                string scaleSuffix = "100";
                try
                {
                    var raster = (this.XamlRoot?.RasterizationScale) ??1.0;
                    if (raster >=3.0) scaleSuffix = "400";
                    else if (raster >=2.0) scaleSuffix = "200";
                    else scaleSuffix = "100";
                }
                catch { }

                var candidates = new[] {
                    $"ms-appx:///Assets/StoreLogo.scale-{scaleSuffix}.png",
                    "ms-appx:///Assets/StoreLogo.png",
                    $"ms-appx:///Assets/StoreLogo.scale-100.png"
                };

                foreach (var c in candidates)
                {
                    try
                    {
                        var bmp = new BitmapImage(new Uri(c));
                        imgElem.Source = bmp;
                        imgElem.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                        logoSet = true;
                        // hide fallback FontIcon when image is used
                        var fontFallback = this.FindName("AppLogo") as Microsoft.UI.Xaml.Controls.FontIcon;
                        if (fontFallback != null) fontFallback.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                        break;
                    }
                    catch { }
                }

                if (!logoSet) imgElem.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                else
                {
                    imgElem.Width =30;
                    imgElem.Height =30;
                    imgElem.VerticalAlignment = VerticalAlignment.Center;
                }
            }
            catch
            {
                AppTitle.Text = "ViveTool";
                AppPublisher.Text = string.Empty;
                AppPublisher.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                AppVersion.Text = "版本1.0.0.0";
                AppLogo.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            }

            // 关于链接绑定
            try
            {
                var defaultQQ = "https://qun.qq.com/universal-share/share?ac=1";
                var defaultDiscord = "https://discord.gg/4NScc8sEzw";
                var defaultFeedback = "https://github.com/Furry-Xiyi/ViveToolWinUI/issues";

                var qq = (S.Values["JoinQQLink"] as string) ?? defaultQQ;
                if (!string.IsNullOrWhiteSpace(qq) && Uri.TryCreate(qq, UriKind.Absolute, out var uqq)) QQUri = uqq;
                else QQUri = new Uri(defaultQQ);

                var ds = (S.Values["JoinDiscordLink"] as string) ?? defaultDiscord;
                if (!string.IsNullOrWhiteSpace(ds) && Uri.TryCreate(ds, UriKind.Absolute, out var uds)) DiscordUri = uds;
                else DiscordUri = new Uri(defaultDiscord);

                var fb = (S.Values["FeedbackLink"] as string) ?? defaultFeedback;
                if (!string.IsNullOrWhiteSpace(fb) && Uri.TryCreate(fb, UriKind.Absolute, out var ufb)) FeedbackUri = ufb;
                else FeedbackUri = new Uri(defaultFeedback);
            }
            catch
            {
                try { QQUri = new Uri("https://example.com"); } catch { QQUri = null; }
                try { DiscordUri = new Uri("https://example.com"); } catch { DiscordUri = null; }
                try { FeedbackUri = new Uri("https://example.com"); } catch { FeedbackUri = null; }
            }

            if (QQUri != null) JoinQQButton.NavigateUri = QQUri;
            else JoinQQButton.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;

            if (DiscordUri != null) JoinDiscordButton.NavigateUri = DiscordUri;
            else JoinDiscordButton.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;

            if (FeedbackUri != null) FeedbackButton.NavigateUri = FeedbackUri;
            else FeedbackButton.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;

            _isInitializing = false;

            // Some header TextBlocks may be cleared by x:Uid processing after Loaded.
            // Reapply header/group texts once after a short delay if they are still empty.
            try
            {
                DispatcherQueue.TryEnqueue(async () =>
                {
                    await Task.Delay(60);
                    try
                    {
                        var rl = ResourceLoader.GetForCurrentView();
                        void SetIfEmpty(string name, string key)
                        {
                            try
                            {
                                var tb = this.FindName(name) as TextBlock;
                                if (tb == null) return;
                                if (!string.IsNullOrEmpty(tb.Text)) return;
                                var v = rl.GetString(key);
                                if (!string.IsNullOrEmpty(v)) tb.Text = v;
                            }
                            catch { }
                        }

                        SetIfEmpty("AppearanceGroupText", "AppearanceGroup.Text");
                        SetIfEmpty("ThemeHeaderTitle", "ThemeHeader.Title.Text");
                        SetIfEmpty("ThemeHeaderSubtitle", "ThemeHeader.Subtitle.Text");
                        SetIfEmpty("MaterialHeaderTitle", "MaterialHeader.Title.Text");
                        SetIfEmpty("MaterialHeaderSubtitle", "MaterialHeader.Subtitle.Text");
                        SetIfEmpty("BehaviorGroupText", "BehaviorGroup.Text");
                        SetIfEmpty("KernelHeaderTitle", "KernelHeader.Title.Text");
                        SetIfEmpty("KernelHeaderSubtitle", "KernelHeader.Subtitle.Text");
                        SetIfEmpty("AboutTitleText", "AboutTitle.Text");
                    }
                    catch { }
                });
            }
            catch { }
        }

        private void Theme_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            // Use Tag on RadioButtons to determine chosen theme
            try
            {
                if (sender is RadioButton rb && rb.Tag is string tag)
                {
                    S.Values["AppTheme"] = tag;
                }
                ApplyBackdropToMainWindow();
            }
            catch { }
        }

        private void Material_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            // Use Tag on RadioButtons to determine chosen material
            try
            {
                if (sender is RadioButton rb && rb.Tag is string tag)
                {
                    var current = (S.Values["BackgroundMaterial"] as string) ?? "Mica";
                    if (!string.Equals(current, tag, StringComparison.OrdinalIgnoreCase))
                    {
                        S.Values["BackgroundMaterial"] = tag;
                        ApplyBackdropToMainWindow();
                    }
                }
            }
            catch { }
        }

        private void AutoUpdateViveToolToggle_Toggled(object sender, RoutedEventArgs e)
            => S.Values["AutoUpdateViveTool"] = AutoUpdateViveToolToggle.IsOn;

        // ========== 更新检查（只检查并返回版本字符串，由 SettingsPage 决定是否下载） ==========
        private async void CheckViveToolUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (App.MainWindow is MainWindow mw)
            {
                try
                {
                    //这里调用 MainWindow.UpdateViveToolAsync() 应返回最新版本标识或空字符串/null
                    var latestTag = await mw.UpdateViveToolAsync();
                    if (string.IsNullOrWhiteSpace(latestTag))
                    {
                        (App.MainWindow as MainWindow)?.ShowInfo("未能检测到可用更新或发生错误。");
                        return;
                    }

                    var dlg = new ContentDialog
                    {
                        Title = "检测到新版本",
                        Content = $"检测到最新版本：{latestTag}\n是否下载并导入？（会替换当前内核，建议先备份）",
                        PrimaryButtonText = "下载并导入",
                        CloseButtonText = "取消",
                        XamlRoot = this.Content.XamlRoot
                    };
                    var r = await dlg.ShowAsync();
                    if (r == ContentDialogResult.Primary)
                    {
                        // 尝试使用主窗体的下载导入方法
                        try
                        {
                            // 主窗体需要提供 DownloadAndImportViveToolAsync(string url) 实现
                            // 我们尝试先从设置的自定义链接读 url，否则使用 default download link
                            var url = (S.Values["CustomViveToolUpdateUrl"] as string) ?? DefaultUpdateLinks[0];
                            await mw.DownloadAndImportViveToolAsync(url);
                        }
                        catch (MissingMethodException)
                        {
                            (App.MainWindow as MainWindow)?.ShowError("主窗体未实现 DownloadAndImportViveToolAsync。请更新 MainWindow 实现。");
                        }
                        catch (Exception ex)
                        {
                            (App.MainWindow as MainWindow)?.ShowError("下载或导入失败：" + ex.Message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    (App.MainWindow as MainWindow)?.ShowError("检查更新失败：" + ex.Message);
                }
            }
            else
            {
                (App.MainWindow as MainWindow)?.ShowError("无法获取主窗口实例以执行更新。");
            }
        }

        // 自定义更新链接对话：支持恢复默认、验证 URL，并允许保存后选择立即下载导入
        private async void CustomUpdateLink_Click(object sender, RoutedEventArgs e)
        {
            var initial = (S.Values["CustomViveToolUpdateUrl"] as string) ?? "";

            var tb = new TextBox
            {
                AcceptsReturn = false,
                Text = initial,
                PlaceholderText = "输入更新 Zip 下载地址（http:// 或 https://）",
                Width = 640
            };

            var restoreBtn = new Button
            {
                Content = "恢复默认",
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 6, 0, 0),
                Width = 120
            };

            restoreBtn.Click += (_, __) =>
            {
                tb.Text = DefaultUpdateLinks[0];
            };

            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = "输入或粘贴自定义更新链接，仅支持 http/https", TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(tb);
            panel.Children.Add(restoreBtn);

            var dlg = new ContentDialog
            {
                Title = "自定义更新链接",
                Content = panel,
                PrimaryButtonText = "保存并测试",
                CloseButtonText = "取消",
                XamlRoot = this.Content.XamlRoot,
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await dlg.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            var url = tb.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(url))
            {
                S.Values["CustomViveToolUpdateUrl"] = "";
                (App.MainWindow as MainWindow)?.ShowSuccess("已清除自定义链接");
                return;
            }

            if (!IsHttpUrl(url))
            {
                var err = new ContentDialog
                {
                    Title = "链接无效",
                    Content = "请输入以 http:// 或 https:// 开头的有效链接。",
                    CloseButtonText = "确定",
                    XamlRoot = this.Content.XamlRoot
                };
                await err.ShowAsync();
                return;
            }

            S.Values["CustomViveToolUpdateUrl"] = url;
            (App.MainWindow as MainWindow)?.ShowSuccess("已保存自定义更新链接");

            // 询问是否立即测试下载并导入
            var confirm = new ContentDialog
            {
                Title = "测试下载",
                Content = "是否现在尝试下载并导入该链接？（会覆盖当前内核，建议先备份）",
                PrimaryButtonText = "下载并导入",
                CloseButtonText = "稍后",
                XamlRoot = this.Content.XamlRoot
            };
            var r = await confirm.ShowAsync();
            if (r == ContentDialogResult.Primary)
            {
                if (App.MainWindow is MainWindow mw)
                {
                    try
                    {
                        await mw.DownloadAndImportViveToolAsync(url);
                        (App.MainWindow as MainWindow)?.ShowSuccess("已发起下载并导入操作，请查看弹出的结果窗口。");
                    }
                    catch (MissingMethodException)
                    {
                        (App.MainWindow as MainWindow)?.ShowError("主窗体未实现 DownloadAndImportViveToolAsync。请更新 MainWindow 实现。");
                    }
                    catch (Exception ex)
                    {
                        (App.MainWindow as MainWindow)?.ShowError("下载或导入失败：" + ex.Message);
                    }
                }
            }
        }

        // 导入 zip（复用你已有逻辑封装）
        private async void ImportKernelZip_Click(object sender, RoutedEventArgs e)
        {
            await ImportKernelZipInternalAsync();
        }

        // 封装后的导入实现（基于你提供的代码）
        private async Task ImportKernelZipInternalAsync()
        {
            void Log(string s)
            {
                try
                {
                    var path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "infobar_log.txt");
                    File.AppendAllText(path, $"[{DateTime.Now:O}] {s}\r\n");
                }
                catch { }
                try { System.Diagnostics.Debug.WriteLine("[Import] " + s); } catch { }
            }

            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".zip");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            Log("Picker created, showing picker");
            Windows.Storage.StorageFile? file = null;
            try
            {
                file = await picker.PickSingleFileAsync();
            }
            catch (Exception ex)
            {
                Log("Picker threw: " + ex.Message);
            }

            if (file == null)
            {
                Log("User cancelled picker or no file returned");
                return;
            }

            Log("Picked file: " + file.Name);

            var confirm = new ContentDialog
            {
                Title = "确认导入",
                Content = $"是否使用选中的内核文件：{file.Name}？\n这将覆盖现有的 ViveTool 内核。",
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot
            };
            var result = await confirm.ShowAsync();
            Log("Confirm dialog result: " + result);
            if (result != ContentDialogResult.Primary)
            {
                Log("User cancelled confirm");
                return;
            }

            try
            {
                var localFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync("ViveTool", CreationCollisionOption.OpenIfExists);
                var localPath = localFolder.Path;
                Log("Local path: " + localPath);

                // 清理旧文件
                try
                {
                    foreach (var path in Directory.EnumerateFiles(localPath, "*", SearchOption.TopDirectoryOnly))
                    {
                        try { File.Delete(path); } catch (Exception ex) { Log("Delete file failed: " + ex.Message); }
                    }
                    foreach (var dir in Directory.EnumerateDirectories(localPath))
                    {
                        try { Directory.Delete(dir, recursive: true); } catch (Exception ex) { Log("Delete dir failed: " + ex.Message); }
                    }
                    Log("Old files cleaned");
                }
                catch (Exception ex)
                {
                    Log("Cleanup exception: " + ex.Message);
                }

                // 复制并解压
                var zipName = "import.zip";
                await file.CopyAsync(localFolder, zipName, NameCollisionOption.ReplaceExisting);
                var zipPath = Path.Combine(localPath, zipName);
                Log("Zip copied to: " + zipPath);

                try
                {
                    System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, localPath, overwriteFiles: true);
                    Log("Zip extracted");
                }
                catch (Exception ex)
                {
                    Log("Extract failed: " + ex.Message);
                    try { (App.MainWindow as MainWindow)?.ShowError("解压失败: " + ex.Message); } catch { }
                    try { File.Delete(zipPath); } catch { }
                    return;
                }
                try { File.Delete(zipPath); } catch { }

                var exePath = Path.Combine(localPath, "ViveTool.exe");
                Log("Checking exe path: " + exePath);
                if (!File.Exists(exePath))
                {
                    Log("ViveTool.exe not found after extract");
                    try { (App.MainWindow as MainWindow)?.ShowError("导入失败：包内未找到 ViveTool.exe"); } catch { }
                    var failDlg = new ContentDialog { Title = "导入失败", Content = "导入后未找到 ViveTool.exe", CloseButtonText = "确定", XamlRoot = this.Content.XamlRoot };
                    await failDlg.ShowAsync();
                    return;
                }

                Log("Exe verified exists. About to call ShowSuccess.");
                var mw = App.MainWindow as MainWindow;
                if (mw != null)
                {
                    mw.DispatcherQueue.TryEnqueue(() =>
                    {
                        try
                        {
                            mw.ShowSuccess("内核已导入并替换成功");
                        }
                        catch { }
                    });
                }
            }
            catch (Exception ex)
            {
                Log("Outer try failed: " + ex.Message);
                try { (App.MainWindow as MainWindow)?.ShowError("导入失败: " + ex.Message); } catch { }
            }
        }

        // 打开内核文件夹
        private async void BtnOpenCoreFolder_Click(object sender, RoutedEventArgs e)
        {
            var folder = GetViveToolFolder();
            if (!Directory.Exists(folder))
            {
                (App.MainWindow as MainWindow)?.ShowWarning("ViveTool 文件夹不存在：" + folder);
                return;
            }
            var success = await Launcher.LaunchFolderPathAsync(folder);
            if (!success) (App.MainWindow as MainWindow)?.ShowError("无法打开 ViveTool 文件夹");
        }

        // 备份当前内核到桌面
        private async void BtnBackupCore_Click(object sender, RoutedEventArgs e)
        {
            var folder = GetViveToolFolder();
            if (!Directory.Exists(folder))
            {
                (App.MainWindow as MainWindow)?.ShowWarning("ViveTool 文件夹不存在：" + folder);
                return;
            }

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var backupDir = Path.Combine(desktop, "ViveTool_Backups");
            Directory.CreateDirectory(backupDir);
            var dst = Path.Combine(backupDir, $"ViveTool_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

            var confirm = new ContentDialog
            {
                Title = "备份内核",
                Content = $"将把当前内核压缩并保存到：{dst}\n继续吗？",
                PrimaryButtonText = "备份",
                CloseButtonText = "取消",
                XamlRoot = this.Content.XamlRoot
            };
            var r = await confirm.ShowAsync();
            if (r != ContentDialogResult.Primary) return;

            try
            {
                System.IO.Compression.ZipFile.CreateFromDirectory(folder, dst, System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: false);
                (App.MainWindow as MainWindow)?.ShowSuccess("已备份到桌面 ViveTool_Backups 文件夹");
            }
            catch (Exception ex)
            {
                (App.MainWindow as MainWindow)?.ShowError("备份失败：" + ex.Message);
            }
        }

        // 还原内核（选择 zip 并替换）
        private async void BtnRestoreCore_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".zip");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            Windows.Storage.StorageFile? file = null;
            try
            {
                file = await picker.PickSingleFileAsync();
            }
            catch (Exception ex)
            {
                (App.MainWindow as MainWindow)?.ShowError("文件选择失败：" + ex.Message);
                return;
            }

            if (file == null) return;

            var confirm = new ContentDialog
            {
                Title = "确认还原",
                Content = $"将使用选中的备份覆盖当前 ViveTool 内核：{file.Name}。是否继续？",
                PrimaryButtonText = "还原",
                CloseButtonText = "取消",
                XamlRoot = this.Content.XamlRoot
            };
            var r = await confirm.ShowAsync();
            if (r != ContentDialogResult.Primary) return;

            try
            {
                var localFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync("ViveTool", CreationCollisionOption.OpenIfExists);
                var localPath = localFolder.Path;

                // 清理旧文件（尽量小心）
                try
                {
                    foreach (var path in Directory.EnumerateFiles(localPath)) File.Delete(path);
                    foreach (var dir in Directory.EnumerateDirectories(localPath)) Directory.Delete(dir, true);
                }
                catch { }

                // 解压导入
                await file.CopyAsync(localFolder, "restore.zip", NameCollisionOption.ReplaceExisting);
                System.IO.Compression.ZipFile.ExtractToDirectory(Path.Combine(localPath, "restore.zip"), localPath, overwriteFiles: true);
                File.Delete(Path.Combine(localPath, "restore.zip"));

                (App.MainWindow as MainWindow)?.ShowSuccess("已从备份还原内核");
            }
            catch (Exception ex)
            {
                (App.MainWindow as MainWindow)?.ShowError("还原失败：" + ex.Message);
            }
        }

        // 导出设置到桌面 JSON
        private async void BtnExportSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dst = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"ViveTool_Settings_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                var dict = new Dictionary<string, object>();
                foreach (var kv in ApplicationData.Current.LocalSettings.Values)
                {
                    dict[kv.Key] = kv.Value;
                }
                var json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(dst, json);
                (App.MainWindow as MainWindow)?.ShowSuccess("设置已导出到桌面");
            }
            catch (Exception ex)
            {
                (App.MainWindow as MainWindow)?.ShowError("导出失败：" + ex.Message);
            }
        }

        // 导入设置 JSON（会覆盖当前设置）
        private async void BtnImportSettings_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".json");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            Windows.Storage.StorageFile? file = null;
            try
            {
                file = await picker.PickSingleFileAsync();
            }
            catch (Exception ex)
            {
                (App.MainWindow as MainWindow)?.ShowError("文件选择失败：" + ex.Message);
                return;
            }
            if (file == null) return;

            var confirm = new ContentDialog
            {
                Title = "导入设置",
                Content = $"是否从文件导入设置并覆盖当前本地设置：{file.Name}？请确保此文件来源可信。",
                PrimaryButtonText = "导入",
                CloseButtonText = "取消",
                XamlRoot = this.Content.XamlRoot
            };
            var r = await confirm.ShowAsync();
            if (r != ContentDialogResult.Primary) return;

            try
            {
                var text = await FileIO.ReadTextAsync(file);
                var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(text);
                if (dict != null)
                {
                    foreach (var kv in dict)
                    {
                        try
                        {
                            if (kv.Value.ValueKind == JsonValueKind.String)
                                S.Values[kv.Key] = kv.Value.GetString();
                            else if (kv.Value.ValueKind == JsonValueKind.Number)
                                S.Values[kv.Key] = kv.Value.GetDouble();
                            else if (kv.Value.ValueKind == JsonValueKind.True || kv.Value.ValueKind == JsonValueKind.False)
                                S.Values[kv.Key] = kv.Value.GetBoolean();
                        }
                        catch { }
                    }
                }
                (App.MainWindow as MainWindow)?.ShowSuccess("设置导入完成（请重启应用以确保全部生效）");
            }
            catch (Exception ex)
            {
                (App.MainWindow as MainWindow)?.ShowError("导入失败：" + ex.Message);
            }
        }

        // 重置所有设置（谨慎）
        private async void BtnResetSettings_Click(object sender, RoutedEventArgs e)
        {
            var confirm = new ContentDialog
            {
                Title = "重置设置",
                Content = "此操作将清除所有本地设置并恢复到默认值，可能需要重启应用。是否确认？",
                PrimaryButtonText = "重置",
                CloseButtonText = "取消",
                XamlRoot = this.Content.XamlRoot
            };
            var r = await confirm.ShowAsync();
            if (r != ContentDialogResult.Primary) return;

            try
            {
                ApplicationData.Current.LocalSettings.Values.Clear();
                (App.MainWindow as MainWindow)?.ShowSuccess("已重置本地设置，请重启应用以确保完全恢复");
            }
            catch (Exception ex)
            {
                (App.MainWindow as MainWindow)?.ShowError("重置失败：" + ex.Message);
            }
        }

        // 运行 vivetool /version 并把输出追加到中央日志（非提升）
        private async void BtnTestViveTool_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folder = GetViveToolFolder();
                var exe = Path.Combine(folder, "ViveTool.exe");
                if (!File.Exists(exe))
                {
                    (App.MainWindow as MainWindow)?.ShowError("未找到 ViveTool.exe");
                    return;
                }

                AppendLog("执行 vivetool /version（捕获输出）");
                var (outText, errText, exit) = await RunProcessCaptureAsync(exe, "/version", folder, TimeSpan.FromSeconds(20), CancellationToken.None);
                if (!string.IsNullOrEmpty(outText)) AppendLog(outText);
                if (!string.IsNullOrEmpty(errText)) AppendLog(errText);
                (App.MainWindow as MainWindow)?.ShowInfo("已将结果写入日志（可在输出页查看）");
            }
            catch (Exception ex)
            {
                (App.MainWindow as MainWindow)?.ShowError("运行失败：" + ex.Message);
            }
        }

        // 反馈按钮
        private async void SendFeedback_Click(object sender, RoutedEventArgs e)
        {
            var url = (S.Values["FeedbackLink"] as string) ?? FeedbackUri.ToString();
            if (!IsHttpUrl(url)) url = FeedbackUri.ToString();
            await Launcher.LaunchUriAsync(new Uri(url));
        }

        // ----------------- 辅助方法 -----------------

        private string GetViveToolFolder()
        {
            var localFolder = Path.Combine(ApplicationData.Current.LocalFolder.Path, "ViveTool");
            if (Directory.Exists(localFolder) && File.Exists(Path.Combine(localFolder, "ViveTool.exe")))
                return localFolder;
            return Path.Combine(AppContext.BaseDirectory, "Assets", "ViveTool");
        }

        private Task<(string Output, string Error, int ExitCode)> RunProcessCaptureAsync(
            string exePath, string arguments, string workingDir, TimeSpan timeout, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arguments,
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };

                using var proc = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("无法启动 ViveTool 进程。");

                bool exited;
                if (timeout > TimeSpan.Zero)
                {
                    exited = proc.WaitForExit((int)timeout.TotalMilliseconds);
                    if (!exited) { try { proc.Kill(entireProcessTree: true); } catch { } }
                }
                else
                {
                    proc.WaitForExit();
                    exited = true;
                }

                if (ct.IsCancellationRequested)
                {
                    try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
                    throw new OperationCanceledException();
                }

                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                int code = proc.ExitCode;

                return (stdout?.Trim() ?? string.Empty, stderr?.Trim() ?? string.Empty, code);
            }, ct);
        }

        private bool IsHttpUrl(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (!Uri.TryCreate(s, UriKind.Absolute, out var u)) return false;
            return u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps;
        }

        private void DebugWrite(string s)
        {
            try { System.Diagnostics.Debug.WriteLine("[Settings] " + s); } catch { }
        }

        private void ApplyBackdropToMainWindow()
        {
            if (App.MainWindow is MainWindow mw) mw.ApplyBackdropFromSettings();
        }

        private void AppendLog(string text)
        {
            var line = $"{DateTime.Now:HH:mm:ss} {text}";
            try
            {
                // 写入中央日志缓存，ViveToolPage.GetLog 读取
                // 这里直接写入静态文件供 OutputPage 查看（也保持与之前实现兼容）
                var path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "settings_log.txt");
                File.AppendAllText(path, $"[{DateTime.Now:O}] {text}\r\n");
            }
            catch { }
            try { System.Diagnostics.Debug.WriteLine("[Settings] " + text); } catch { }
        }

        // PromptForInputAsync 小工具（若需要）
        private async Task<string> PromptForInputAsync(string title, string message)
        {
            var tb = new TextBox { AcceptsReturn = false, PlaceholderText = "" };
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(tb);

            var dlg = new ContentDialog
            {
                Title = title,
                Content = panel,
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dlg.ShowAsync();
            if (result == ContentDialogResult.Primary) return tb.Text?.Trim() ?? "";
            return "";
        }
    }
}