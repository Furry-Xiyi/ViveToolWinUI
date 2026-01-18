using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace ViveToolWinUI.Pages
{
    public sealed partial class SettingsPage : Page
    {
        private readonly ApplicationDataContainer _settings = ApplicationData.Current.LocalSettings;
        private bool _isLoading;

        public SettingsPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            _isLoading = true;

            // 加载主题
            var theme = GetSetting("AppTheme", "System");
            ThemeSystem.IsChecked = theme == "System";
            ThemeLight.IsChecked = theme == "Light";
            ThemeDark.IsChecked = theme == "Dark";

            // 加载背景材质
            var backdrop = GetSetting("BackgroundMaterial", "Mica");
            MaterialAcrylic.IsChecked = backdrop == "Acrylic";
            MaterialMica.IsChecked = backdrop == "Mica";
            MaterialMicaAlt.IsChecked = backdrop == "Mica_Alt";

            // 加载自动更新
            AutoUpdateToggle.IsOn = GetSettingBool("AutoUpdateViveTool", true);

            // 显示版本
            await LoadVersionInfoAsync();

            // 加载应用信息
            LoadAppInfo();

            _isLoading = false;
        }
        private void Theme_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoading || sender is not RadioButton rb) return;

            var selected = rb.Tag?.ToString();
            if (selected != null)
            {
                SaveSetting("AppTheme", selected);
                ApplyToMainWindow();
            }
        }
        private void Material_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoading || sender is not RadioButton rb) return;

            var selected = rb.Tag?.ToString();
            if (selected != null)
            {
                SaveSetting("BackgroundMaterial", selected);
                ApplyToMainWindow();
            }
        }

        // 自动更新开关
        private void AutoUpdate_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoading)
                SaveSetting("AutoUpdateViveTool", AutoUpdateToggle.IsOn.ToString());
        }

        // 检查更新
        private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (App.MainWindow is MainWindow mw)
                await mw.CheckAndPromptUpdateAsync(false);
        }

        // 打开文件夹
        private async void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (App.MainWindow is MainWindow mw)
                await mw.OpenKernelFolderAsync();
        }

        // 备份内核
        private async void Backup_Click(object sender, RoutedEventArgs e)
        {
            if (App.MainWindow is MainWindow mw)
            {
                var path = await mw.BackupKernelAsync();
                if (!string.IsNullOrEmpty(path))
                    ShowSuccess($"Backup saved: {Path.GetFileName(path)}");
            }
        }

        // 还原内核
        private async void Restore_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".zip");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null && await ConfirmAsync("Restore kernel from backup?"))
            {
                if (App.MainWindow is MainWindow mw)
                {
                    var success = await mw.RestoreKernelAsync(file.Path);
                    if (success)
                        ShowSuccess("Kernel restored successfully");
                    else
                        ShowError("Restore failed");
                }
            }
        }

        // 导出设置
        private async void ExportSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var path = Path.Combine(desktop, $"ViveTool_Settings_{DateTime.Now:yyyyMMdd_HHmmss}.json");

                var dict = new Dictionary<string, string>();
                foreach (var kv in _settings.Values)
                    dict[kv.Key] = kv.Value?.ToString() ?? "";

                var json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(path, json);

                ShowSuccess("Settings exported to desktop");
            }
            catch (Exception ex)
            {
                ShowError($"Export failed: {ex.Message}");
            }
        }

        // 导入设置
        private async void ImportSettings_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".json");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null && await ConfirmAsync("Import settings and overwrite current?"))
            {
                try
                {
                    var json = await FileIO.ReadTextAsync(file);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                    if (dict != null)
                    {
                        foreach (var kv in dict)
                            _settings.Values[kv.Key] = kv.Value;

                        ShowSuccess("Settings imported (restart recommended)");
                    }
                }
                catch (Exception ex)
                {
                    ShowError($"Import failed: {ex.Message}");
                }
            }
        }

        // 重置设置
        private async void ResetSettings_Click(object sender, RoutedEventArgs e)
        {
            if (await ConfirmAsync("Reset all settings to default?"))
            {
                _settings.Values.Clear();
                ShowSuccess("Settings reset (please restart app)");
            }
        }

        // 反馈
        private async void Feedback_Click(object sender, RoutedEventArgs e)
        {
            await Launcher.LaunchUriAsync(new Uri("https://github.com/Furry-Xiyi/ViveToolWinUI/issues"));
        }

        // 加载版本信息
        private async Task LoadVersionInfoAsync()
        {
            if (App.MainWindow is MainWindow mw)
            {
                var version = await mw.GetKernelVersionAsync();
                TxtVersion.Text = string.IsNullOrEmpty(version) ? "Unknown" : version;
            }
        }

        // 加载应用信息
        private void LoadAppInfo()
        {
            try
            {
                var ver = Package.Current.Id.Version;
                TxtAppName.Text = Package.Current.DisplayName;
                TxtAppVersion.Text = $"v{ver.Major}.{ver.Minor}.{ver.Build}";

                // 加载图标
                try
                {
                    var scale = this.XamlRoot?.RasterizationScale ?? 1.0;
                    var suffix = scale >= 3.0 ? "400" : scale >= 2.0 ? "200" : "100";
                    AppLogo.Source = new BitmapImage(new Uri($"ms-appx:///Assets/StoreLogo.scale-{suffix}.png"));
                }
                catch
                {
                    AppLogo.Source = new BitmapImage(new Uri("ms-appx:///Assets/StoreLogo.png"));
                }
            }
            catch
            {
                TxtAppName.Text = "ViveTool";
                TxtAppVersion.Text = "v1.0.0";
            }
        }

        // 应用到主窗口
        private void ApplyToMainWindow()
        {
            if (App.MainWindow is MainWindow mw)
                mw.ApplySettings();
        }

        // 确认对话框
        private async Task<bool> ConfirmAsync(string message)
        {
            var dialog = new ContentDialog
            {
                Title = "Confirm",
                Content = message,
                PrimaryButtonText = "Yes",
                CloseButtonText = "No",
                XamlRoot = this.XamlRoot
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        // 设置读写
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

        private void SaveSetting(string key, string value)
        {
            _settings.Values[key] = value;
        }

        // 通知
        private void ShowSuccess(string msg) => (App.MainWindow as MainWindow)?.ShowSuccess(msg);
        private void ShowError(string msg) => (App.MainWindow as MainWindow)?.ShowError(msg);
    }
}