using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.IO;
using Windows.ApplicationModel.DataTransfer;

namespace ViveToolWinUI.Pages
{
    public sealed partial class OutputPage : Page
    {
        private string _output = string.Empty;

        public OutputPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is string output)
            {
                _output = output;
                TxtOutput.Text = output;
            }
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_output)) return;

            var package = new DataPackage();
            package.SetText(_output);
            Clipboard.SetContent(package);

            ShowSuccess("Copied to clipboard");
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var path = Path.Combine(desktop, $"ViveTool_Output_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

                await File.WriteAllTextAsync(path, _output);
                ShowSuccess($"Saved to: {path}");
            }
            catch (Exception ex)
            {
                ShowError($"Save failed: {ex.Message}");
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
                Frame.GoBack();
        }

        private void ShowSuccess(string msg) => (App.MainWindow as MainWindow)?.ShowSuccess(msg);
        private void ShowError(string msg) => (App.MainWindow as MainWindow)?.ShowError(msg);
    }
}