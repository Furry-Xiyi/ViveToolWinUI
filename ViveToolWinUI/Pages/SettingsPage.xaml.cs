using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.ApplicationModel;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace ViveToolWinUI.Pages
{
    [JsonSerializable(typeof(Dictionary<string, string>))]
    public partial class SettingsJsonContext : JsonSerializerContext
    {
    }
    public sealed partial class SettingsPage : Page
    {
        private ApplicationDataContainer S => ApplicationData.Current.LocalSettings;
        private bool _isInitializing;

        private static readonly List<string> DefaultUpdateLinks = new()
        {
            "https://github.com/thebookisclosed/ViVe/releases/latest/download/ViveTool.zip",
            "https://download.fastgit.org/thebookisclosed/ViVe/releases/latest/download/ViveTool.zip",
            "https://gitee.com/mirror/ViVe/releases/download/latest/ViveTool.zip"
        };

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

            // 主题设置
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

            // 关于面板
            try
            {
                var ver = Package.Current.Id.Version;
                AppTitle.Text = Package.Current.DisplayName;

                var publisherName = Package.Current.PublisherDisplayName?.Trim();
                if (!string.IsNullOrEmpty(publisherName))
                {
                    AppPublisher.Text = $"©{DateTime.Now.Year} {publisherName}";
                    AppPublisher.Visibility = Visibility.Visible;
                }
                else
                {
                    AppPublisher.Visibility = Visibility.Collapsed;
                }

                AppVersion.Text = $"版本 {ver.Major}.{ver.Minor}.{ver.Build}.{ver.Revision}";
                LoadAppLogo();
            }
            catch
            {
                AppTitle.Text = "ViveTool";
                AppPublisher.Visibility = Visibility.Collapsed;
                AppVersion.Text = "版本 1.0.0.0";
            }

            LoadAboutLinks();
            _isInitializing = false;
        }

        private void LoadAppLogo()
        {
            try
            {
                string scaleSuffix = "100";
                try
                {
                    var raster = this.XamlRoot?.RasterizationScale ?? 1.0;
                    if (raster >= 3.0) scaleSuffix = "400";
                    else if (raster >= 2.0) scaleSuffix = "200";
                }
                catch { }

                var candidates = new[] {
                    $"ms-appx:///Assets/StoreLogo.scale-{scaleSuffix}.png",
                    "ms-appx:///Assets/StoreLogo.png"
                };

                foreach (var candidate in candidates)
                {
                    try
                    {
                        AppLogoImage.Source = new BitmapImage(new Uri(candidate));
                        AppLogoImage.Visibility = Visibility.Visible;
                        AppLogo.Visibility = Visibility.Collapsed;
                        return;
                    }
                    catch { }
                }
                AppLogoImage.Visibility = Visibility.Collapsed;
            }
            catch
            {
                AppLogoImage.Visibility = Visibility.Collapsed;
            }
        }

        private void LoadAboutLinks()
        {
            try
            {
                var defaultQQ = "https://qun.qq.com/universal-share/share?ac=1";
                var defaultDiscord = "https://discord.gg/4NScc8sEzw";
                var defaultFeedback = "https://github.com/Furry-Xiyi/ViveToolWinUI/issues";

                var qq = (S.Values["JoinQQLink"] as string) ?? defaultQQ;
                if (Uri.TryCreate(qq, UriKind.Absolute, out var uqq))
                {
                    JoinQQButton.NavigateUri = uqq;
                }
                else
                {
                    JoinQQButton.Visibility = Visibility.Collapsed;
                }

                var ds = (S.Values["JoinDiscordLink"] as string) ?? defaultDiscord;
                if (Uri.TryCreate(ds, UriKind.Absolute, out var uds))
                {
                    JoinDiscordButton.NavigateUri = uds;
                }
                else
                {
                    JoinDiscordButton.Visibility = Visibility.Collapsed;
                }

                var fb = (S.Values["FeedbackLink"] as string) ?? defaultFeedback;
                if (Uri.TryCreate(fb, UriKind.Absolute, out var ufb))
                {
                    FeedbackButton.NavigateUri = ufb;
                }
                else
                {
                    FeedbackButton.Visibility = Visibility.Collapsed;
                }
            }
            catch { }
        }

        private void Theme_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            if (sender is RadioButton rb && rb.Tag is string tag)
            {
                S.Values["AppTheme"] = tag;
                ApplyBackdropToMainWindow();
            }
        }

        private void Material_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            if (sender is RadioButton rb && rb.Tag is string tag)
            {
                S.Values["BackgroundMaterial"] = tag;
                ApplyBackdropToMainWindow();
            }
        }

        private void AutoUpdateViveToolToggle_Toggled(object sender, RoutedEventArgs e)
            => S.Values["AutoUpdateViveTool"] = AutoUpdateViveToolToggle.IsOn;

        // ===== 更新检查 =====
        private async void CheckViveToolUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (App.MainWindow is MainWindow mw)
            {
                await mw.CheckAndPromptViveToolUpdateAsync();
            }
        }

        // ===== 自定义更新链接 =====
        private async void CustomUpdateLink_Click(object sender, RoutedEventArgs e)
        {
            var initial = (S.Values["CustomViveToolUpdateUrl"] as string) ?? "";

            var tb = new TextBox
            {
                Text = initial,
                PlaceholderText = "输入更新 Zip 下载地址",
                Width = 640
            };

            var restoreBtn = new Button { Content = "恢复默认", Width = 120, Margin = new Thickness(0, 6, 0, 0) };
            restoreBtn.Click += (_, __) => tb.Text = DefaultUpdateLinks[0];

            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = "输入自定义更新链接", TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(tb);
            panel.Children.Add(restoreBtn);

            var dlg = new ContentDialog
            {
                Title = "自定义更新链接",
                Content = panel,
                PrimaryButtonText = "保存",
                CloseButtonText = "取消",
                XamlRoot = this.XamlRoot
            };

            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

            var url = tb.Text?.Trim() ?? "";
            S.Values["CustomViveToolUpdateUrl"] = string.IsNullOrWhiteSpace(url) ? "" : url;
            ShowSuccess(string.IsNullOrWhiteSpace(url) ? "已清除自定义链接" : "已保存自定义链接");
        }

        // ===== 导入内核 Zip =====
        private async void ImportKernelZip_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".zip");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            var confirm = new ContentDialog
            {
                Title = "确认导入",
                Content = $"将导入：{file.Name}\n这会覆盖现有的 ViveTool。",
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                XamlRoot = this.XamlRoot
            };

            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

            try
            {
                if (App.MainWindow is not MainWindow mw) return;

                var folder = Path.Combine(ApplicationData.Current.LocalFolder.Path, "ViveTool");
                Directory.CreateDirectory(folder);

                // 清理
                foreach (var f in Directory.EnumerateFiles(folder))
                    try { File.Delete(f); } catch { }
                foreach (var d in Directory.EnumerateDirectories(folder))
                    try { Directory.Delete(d, true); } catch { }

                // 解压
                var tmpZip = Path.Combine(folder, "import.zip");
                await file.CopyAsync(await StorageFolder.GetFolderFromPathAsync(folder), "import.zip", NameCollisionOption.ReplaceExisting);
                System.IO.Compression.ZipFile.ExtractToDirectory(tmpZip, folder, true);
                File.Delete(tmpZip);

                if (File.Exists(Path.Combine(folder, "ViveTool.exe")))
                    ShowSuccess("导入成功");
                else
                    ShowError("导入失败：未找到 ViveTool.exe");
            }
            catch (Exception ex)
            {
                ShowError($"导入失败: {ex.Message}");
            }
        }

        // ===== 打开文件夹 =====
        private async void BtnOpenCoreFolder_Click(object sender, RoutedEventArgs e)
        {
            if (App.MainWindow is MainWindow mw)
            {
                await mw.OpenViveToolFolderAsync();
            }
        }

        // ===== 备份 =====
        private async void BtnBackupCore_Click(object sender, RoutedEventArgs e)
        {
            if (App.MainWindow is not MainWindow mw) return;

            mw.EnsureViveToolInstalled();
            var folder = Path.Combine(ApplicationData.Current.LocalFolder.Path, "ViveTool");

            if (!Directory.Exists(folder) || !File.Exists(Path.Combine(folder, "ViveTool.exe")))
            {
                ShowError("ViveTool 未安装，无法备份");
                return;
            }

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var backupDir = Path.Combine(desktop, "ViveTool_Backups");
            Directory.CreateDirectory(backupDir);
            var dst = Path.Combine(backupDir, $"ViveTool_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

            try
            {
                System.IO.Compression.ZipFile.CreateFromDirectory(folder, dst, System.IO.Compression.CompressionLevel.Optimal, false);
                ShowSuccess("已备份到桌面");
            }
            catch (Exception ex)
            {
                ShowError($"备份失败: {ex.Message}");
            }
        }

        // ===== 还原 =====
        private async void BtnRestoreCore_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".zip");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            var confirm = new ContentDialog
            {
                Title = "确认还原",
                Content = "将使用备份覆盖当前 ViveTool。是否继续？",
                PrimaryButtonText = "还原",
                CloseButtonText = "取消",
                XamlRoot = this.XamlRoot
            };

            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

            try
            {
                var folder = Path.Combine(ApplicationData.Current.LocalFolder.Path, "ViveTool");

                // 清理
                foreach (var f in Directory.EnumerateFiles(folder)) File.Delete(f);
                foreach (var d in Directory.EnumerateDirectories(folder)) Directory.Delete(d, true);

                // 解压
                var tmpZip = Path.Combine(folder, "restore.zip");
                await file.CopyAsync(await StorageFolder.GetFolderFromPathAsync(folder), "restore.zip", NameCollisionOption.ReplaceExisting);
                System.IO.Compression.ZipFile.ExtractToDirectory(tmpZip, folder, true);
                File.Delete(tmpZip);

                ShowSuccess("还原成功");
            }
            catch (Exception ex)
            {
                ShowError($"还原失败: {ex.Message}");
            }
        }

        // ===== 导出/导入/重置设置 =====
        private async void BtnExportSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dst = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    $"ViveTool_Settings_{DateTime.Now:yyyyMMdd_HHmmss}.json");

                var dict = new Dictionary<string, string>();
                foreach (var kv in S.Values)
                {
                    dict[kv.Key] = kv.Value?.ToString() ?? "";
                }

                var json = JsonSerializer.Serialize(dict, SettingsJsonContext.Default.DictionaryStringString);
                await File.WriteAllTextAsync(dst, json);

                ShowSuccess("已导出到桌面");
            }
            catch (Exception ex)
            {
                ShowError($"导出失败: {ex.Message}");
            }
        }

        private async void BtnImportSettings_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".json");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            var confirm = new ContentDialog
            {
                Title = "导入设置",
                Content = "将覆盖当前设置，是否继续？",
                PrimaryButtonText = "导入",
                CloseButtonText = "取消",
                XamlRoot = this.XamlRoot
            };

            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

            try
            {
                var text = await FileIO.ReadTextAsync(file);
                var dict = JsonSerializer.Deserialize(text, SettingsJsonContext.Default.DictionaryStringString);

                if (dict != null)
                {
                    foreach (var kv in dict)
                    {
                        S.Values[kv.Key] = kv.Value;
                    }
                }

                ShowSuccess("导入成功（建议重启应用）");
            }
            catch (Exception ex)
            {
                ShowError($"导入失败: {ex.Message}");
            }
        }

        private async void BtnResetSettings_Click(object sender, RoutedEventArgs e)
        {
            var confirm = new ContentDialog
            {
                Title = "重置设置",
                Content = "将清除所有设置，是否确认？",
                PrimaryButtonText = "重置",
                CloseButtonText = "取消",
                XamlRoot = this.XamlRoot
            };

            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

            try
            {
                S.Values.Clear();
                ShowSuccess("已重置，请重启应用");
            }
            catch (Exception ex) { ShowError($"重置失败: {ex.Message}"); }
        }

        // ===== 测试 ViveTool =====
        private async void BtnTestViveTool_Click(object sender, RoutedEventArgs e)
        {
            if (App.MainWindow is MainWindow mw)
            {
                var msg = await mw.GetViveToolVersionAsync();
                var dlg = new ContentDialog
                {
                    Title = "ViveTool 版本",
                    Content = new ScrollViewer
                    {
                        Content = new TextBlock { Text = msg, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
                        MaxHeight = 400
                    },
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await dlg.ShowAsync();
            }
        }

        private async void SendFeedback_Click(object sender, RoutedEventArgs e)
        {
            await Launcher.LaunchUriAsync(FeedbackButton.NavigateUri);
        }

        private void ApplyBackdropToMainWindow()
        {
            if (App.MainWindow is MainWindow mw) mw.ApplyBackdropFromSettings();
        }

        private void ShowSuccess(string msg) => (App.MainWindow as MainWindow)?.ShowSuccess(msg);
        private void ShowWarning(string msg) => (App.MainWindow as MainWindow)?.ShowWarning(msg);
        private void ShowError(string msg) => (App.MainWindow as MainWindow)?.ShowError(msg);
    }
}