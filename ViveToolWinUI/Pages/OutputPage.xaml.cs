using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.IO;
using System.Threading.Tasks;

namespace ViveToolWinUI.Pages
{
    public sealed partial class OutputPage : Page
    {

        public OutputPage()
        {
            InitializeComponent();
            this.NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Enabled;
        }

        protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            string content = null;

            if (e.Parameter is string s && !string.IsNullOrEmpty(s))
            {
                content = s;
            }
            else
            {
                // 优先尝试从已缓存的 ViveToolPage 取回日志
                try
                {
                    content = ViveToolWinUI.Pages.ViveToolPage.GetLog();
                }
                catch
                {
                    content = null;
                }
            }

            txtFullOutput.Text = content ?? string.Empty;
        }

        private void BtnCopyOutput_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(txtFullOutput.Text)) return;
            _ = DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    package.SetText(txtFullOutput.Text);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
                    (App.MainWindow as MainWindow)?.ShowSuccess("已复制输出到剪贴板");
                }
                catch (Exception ex)
                {
                    (App.MainWindow as MainWindow)?.ShowError("复制失败：" + ex.Message);
                }
            });
        }

        private async void BtnSaveOutput_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var file = Path.Combine(folder, $"ViveTool_Output_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                await File.WriteAllTextAsync(file, txtFullOutput.Text ?? string.Empty);
                (App.MainWindow as MainWindow)?.ShowSuccess("已保存输出到桌面");
            }
            catch (Exception ex)
            {
                (App.MainWindow as MainWindow)?.ShowError("保存失败：" + ex.Message);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }
    }
}