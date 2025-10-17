using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace ViveToolWinUI
{
    public sealed partial class MainWindow : Window
    {
        private AppWindow? _appWindow;

        public MainWindow()
        {
            InitializeComponent();

            // 设置启动窗口大小（XAML 不再设置 Height/Width）
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            _appWindow?.Resize(new Windows.Graphics.SizeInt32(1100, 720));

            NavigateTo("ViveTool"); // 默认导航
        }

        private void NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                NavigateTo("Settings");
                return;
            }

            var selected = (args.SelectedItem as NavigationViewItem)?.Tag?.ToString();
            if (!string.IsNullOrEmpty(selected))
                NavigateTo(selected);
        }

        private void NavigationView_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        {
            if (contentFrame.CanGoBack)
                contentFrame.GoBack();
        }

        private void NavigateTo(string tag)
        {
            Type targetPage = tag switch
            {
                "ViveTool" => typeof(ViveToolWinUI.Pages.ViveToolPage),
                "About" => typeof(ViveToolWinUI.Pages.AboutPage),
                "Settings" => typeof(ViveToolWinUI.Pages.SettingsPage),
                _ => typeof(ViveToolWinUI.Pages.ViveToolPage)
            };

            var current = contentFrame.Content?.GetType();
            if (current == targetPage) return; // 防重复导航

            contentFrame.Navigate(targetPage);
        }
    }
}