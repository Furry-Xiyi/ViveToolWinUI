using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Storage;

namespace ViveToolWinUI.Pages
{
    public sealed partial class SettingsPage : Page
    {
        private ApplicationDataContainer S => ApplicationData.Current.LocalSettings;

        public SettingsPage()
        {
            InitializeComponent();
            Loaded += SettingsPage_Loaded;
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            // 初始化主题
            var theme = (S.Values["AppTheme"] as string) ?? "System";
            ThemeSystem.IsChecked = theme == "System";
            ThemeLight.IsChecked = theme == "Light";
            ThemeDark.IsChecked = theme == "Dark";

            // 初始化材质
            var mat = (S.Values["BackgroundMaterial"] as string) ?? "Mica";
            MaterialAcrylic.IsChecked = mat == "Acrylic";
            MaterialMica.IsChecked = mat == "Mica";
            MaterialMicaAlt.IsChecked = mat == "MicaAlt";

            // 行为
            RunOnStartupToggle.IsOn = (bool?)(S.Values["RunOnStartup"]) ?? false;
            AutoConnectToggle.IsOn = (bool?)(S.Values["AutoConnectOnStartup"]) ?? false;

            // ViveTool 自动更新
            AutoUpdateViveToolToggle.IsOn = (bool?)(S.Values["AutoUpdateViveTool"]) ?? true;

            // 关于
            try
            {
                var ver = Package.Current.Id.Version;
                AppTitle.Text = Package.Current.DisplayName;
                AppPublisher.Text = Package.Current.PublisherDisplayName;
                AppVersion.Text = $"版本 {ver.Major}.{ver.Minor}.{ver.Build}.{ver.Revision}";

                var logo = Package.Current.Logo;
                AppLogo.Source = new BitmapImage(new Uri(logo.ToString()));
            }
            catch
            {
                AppTitle.Text = "ViveTool WinUI";
                AppPublisher.Text = "作者";
                AppVersion.Text = "版本 1.0.0.0";
            }
        }

        private void Theme_Checked(object sender, RoutedEventArgs e)
        {
            if (ThemeSystem.IsChecked == true) S.Values["AppTheme"] = "System";
            else if (ThemeLight.IsChecked == true) S.Values["AppTheme"] = "Light";
            else if (ThemeDark.IsChecked == true) S.Values["AppTheme"] = "Dark";

            ApplyBackdropToMainWindow();
        }

        private void Material_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Content is string mat)
            {
                S.Values["BackgroundMaterial"] = mat;
                ApplyBackdropToMainWindow();
            }
        }

        private void RunOnStartupToggle_Toggled(object sender, RoutedEventArgs e)
            => S.Values["RunOnStartup"] = RunOnStartupToggle.IsOn;

        private void AutoConnectToggle_Toggled(object sender, RoutedEventArgs e)
            => S.Values["AutoConnectOnStartup"] = AutoConnectToggle.IsOn;

        private void AutoUpdateViveToolToggle_Toggled(object sender, RoutedEventArgs e)
            => S.Values["AutoUpdateViveTool"] = AutoUpdateViveToolToggle.IsOn;

        /// <summary>
        /// 检测并更新 ViveTool 内核（下载 zip 并解压）
        /// </summary>
        private async void CheckViveToolUpdate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var url = "https://github.com/thebookisclosed/ViVe/releases/latest/download/ViveTool.zip";
                var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync("ViveTool", CreationCollisionOption.ReplaceExisting);
                var zipPath = Path.Combine(folder.Path, "ViveTool.zip");

                using var client = new HttpClient();
                var bytes = await client.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(zipPath, bytes);

                // 解压覆盖
                ZipFile.ExtractToDirectory(zipPath, folder.Path, overwriteFiles: true);

                ContentDialog dlg = new ContentDialog
                {
                    Title = "更新完成",
                    Content = "ViveTool 内核已更新，下次运行将使用新版本。",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await dlg.ShowAsync();
            }
            catch (Exception ex)
            {
                ContentDialog dlg = new ContentDialog
                {
                    Title = "更新失败",
                    Content = ex.Message,
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await dlg.ShowAsync();
            }
        }

        private void ApplyBackdropToMainWindow()
        {
            if (App.MainWindow?.Content is Grid rootGrid)
            {
                var mat = (S.Values["BackgroundMaterial"] as string) ?? "Mica";
                rootGrid.Background = mat switch
                {
                    "Acrylic" => new SolidColorBrush(Microsoft.UI.Colors.LightGray),
                    "MicaAlt" => new SolidColorBrush(Microsoft.UI.Colors.DimGray),
                    _ => new SolidColorBrush(Microsoft.UI.Colors.Transparent)
                };
            }
        }
    }
}