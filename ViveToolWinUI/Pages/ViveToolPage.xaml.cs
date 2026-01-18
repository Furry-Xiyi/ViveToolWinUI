using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace ViveToolWinUI.Pages
{
    public class HistoryItem
    {
        public string Verb { get; set; } = "";
        public string Args { get; set; } = "";
    }

    public sealed partial class ViveToolPage : Page
    {
        private readonly ObservableCollection<HistoryItem> _history = new();
        private const string HistoryKey = "CommandHistory";
        private ListView? _dynamicListView;

        public ViveToolPage()
        {
            InitializeComponent();
            LoadHistory();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            // 延迟确保页面完全加载
            await Task.Delay(100);

            // 在 UI 线程上动态创建 ListView
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    CreateHistoryListViewDynamically();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ViveToolPage] Dynamic ListView creation failed: {ex.Message}");
                    ShowError($"Failed to initialize history view: {ex.Message}");
                }
            });
        }

        private void CreateHistoryListViewDynamically()
        {
            // 查找历史记录容器
            var historyContainer = this.FindName("HistoryContainer") as StackPanel;
            if (historyContainer == null)
            {
                Debug.WriteLine("[ViveToolPage] HistoryContainer not found!");
                return;
            }

            // 创建 ListView
            _dynamicListView = new ListView
            {
                MaxHeight = 200,
                SelectionMode = ListViewSelectionMode.Single,
                IsItemClickEnabled = true
            };

            // 创建 ItemTemplate
            var dataTemplate = CreateHistoryItemTemplate();
            _dynamicListView.ItemTemplate = dataTemplate;

            // 注册事件
            _dynamicListView.ItemClick += History_ItemClick;

            // 直接添加项，避免 ItemsSource 的 COM 互操作问题
            foreach (var item in _history)
            {
                _dynamicListView.Items.Add(item);
            }

            // 添加到容器
            historyContainer.Children.Add(_dynamicListView);
        }

        private DataTemplate CreateHistoryItemTemplate()
        {
            // 使用 XamlReader 创建 DataTemplate
            var xaml = @"
<DataTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"">
    <Grid Padding=""8"">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width=""100""/>
            <ColumnDefinition Width=""*""/>
        </Grid.ColumnDefinitions>
        <TextBlock Text=""{Binding Verb}"" FontWeight=""SemiBold"" Grid.Column=""0""/>
        <TextBlock Text=""{Binding Args}"" TextTrimming=""CharacterEllipsis"" Grid.Column=""1""/>
    </Grid>
</DataTemplate>";

            return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
        }

        // 快速命令
        private async void Query_Click(object sender, RoutedEventArgs e)
            => await ExecuteCommandAsync("/query", true);

        private async void FullReset_Click(object sender, RoutedEventArgs e)
        {
            if (await ConfirmAsync("ViveTool_FullResetConfirm"))
                await ExecuteCommandAsync("/fullreset");
        }

        private async void ChangeStamp_Click(object sender, RoutedEventArgs e)
            => await ExecuteCommandAsync("/changestamp");

        private async void Version_Click(object sender, RoutedEventArgs e)
            => await ExecuteCommandAsync("/version", true);

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileSavePicker();
            picker.FileTypeChoices.Add("JSON", new[] { ".json" });
            picker.SuggestedFileName = $"vivetool_export_{DateTime.Now:yyyyMMdd}.json";

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file != null)
                await ExecuteCommandAsync($"/export \"{file.Path}\"");
        }

        private async void Import_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".json");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null && await ConfirmAsync("ViveTool_ImportConfirm"))
                await ExecuteCommandAsync($"/import \"{file.Path}\"");
        }

        private async void LkgStatus_Click(object sender, RoutedEventArgs e)
            => await ExecuteCommandAsync("/lkgstatus", true);

        private async void FixLkg_Click(object sender, RoutedEventArgs e)
        {
            if (await ConfirmAsync("ViveTool_FixLkgConfirm"))
                await ExecuteCommandAsync("/fixlkg");
        }

        private async void QuerySubs_Click(object sender, RoutedEventArgs e)
            => await ExecuteCommandAsync("/querysubs", true);

        private async void AddSub_Click(object sender, RoutedEventArgs e)
        {
            var param = await PromptInputAsync("ViveTool_AddSubTitle", "ViveTool_AddSubMessage");
            if (!string.IsNullOrEmpty(param))
                await ExecuteCommandAsync($"/addsub {param}");
        }

        private async void DelSub_Click(object sender, RoutedEventArgs e)
        {
            var param = await PromptInputAsync("ViveTool_DelSubTitle", "ViveTool_DelSubMessage");
            if (!string.IsNullOrEmpty(param))
                await ExecuteCommandAsync($"/delsub {param}");
        }

        private async void FixPriority_Click(object sender, RoutedEventArgs e)
            => await ExecuteCommandAsync("/fixpriority");

        // 功能操作
        private async void Enable_Click(object sender, RoutedEventArgs e)
        {
            var ids = TxtFeatureId.Text?.Trim();
            if (string.IsNullOrEmpty(ids))
            {
                ShowWarning(GetLocalizedString("ViveTool_EmptyId"));
                return;
            }

            await ExecuteFeatureCommandAsync("enable", ids);
        }

        private async void Disable_Click(object sender, RoutedEventArgs e)
        {
            var ids = TxtFeatureId.Text?.Trim();
            if (string.IsNullOrEmpty(ids))
            {
                ShowWarning(GetLocalizedString("ViveTool_EmptyId"));
                return;
            }

            await ExecuteFeatureCommandAsync("disable", ids);
        }

        private async void Reset_Click(object sender, RoutedEventArgs e)
        {
            var ids = TxtFeatureId.Text?.Trim();
            if (string.IsNullOrEmpty(ids))
            {
                ShowWarning(GetLocalizedString("ViveTool_EmptyId"));
                return;
            }

            await ExecuteFeatureCommandAsync("reset", ids);
        }

        private async void RunCustom_Click(object sender, RoutedEventArgs e)
        {
            var args = TxtCustomArgs.Text?.Trim();
            if (string.IsNullOrEmpty(args))
            {
                ShowWarning(GetLocalizedString("ViveTool_EmptyCommand"));
                return;
            }

            await ExecuteCommandAsync(args);
            AddHistory("custom", args);
        }

        private void ViewOutput_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(OutputPage));
        }

        // 历史记录
        private void History_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is HistoryItem item)
            {
                if (item.Verb == "enable" || item.Verb == "disable" || item.Verb == "reset")
                    TxtFeatureId.Text = item.Args;
                else
                    TxtCustomArgs.Text = item.Args;
            }
        }

        private async void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            if (await ConfirmAsync("ViveTool_ClearHistoryConfirm"))
            {
                _history.Clear();
                SaveHistory();
            }
        }

        // 执行方法
        private async Task ExecuteFeatureCommandAsync(string verb, string ids)
        {
            var result = await ExecuteViveToolAsync($"/{verb} /id:{ids}");

            if (result.Success)
            {
                ShowSuccess(GetLocalizedString($"ViveTool_{verb}Success"));
                AddHistory(verb, ids);

                if (ChkReboot.IsChecked == true)
                    await PromptRebootAsync();
            }
            else
            {
                ShowError(GetLocalizedString("ViveTool_ExecutionFailed"));
                NavigateToOutput(result.Output);
            }
        }

        private async Task ExecuteCommandAsync(string args, bool showOutput = false)
        {
            var result = await ExecuteViveToolAsync(args);

            if (result.Success || showOutput)
            {
                if (showOutput)
                    NavigateToOutput(result.Output);
                else
                    ShowSuccess(GetLocalizedString("ViveTool_ExecutionSuccess"));
            }
            else
            {
                ShowError(GetLocalizedString("ViveTool_ExecutionFailed"));
                NavigateToOutput(result.Output);
            }
        }

        private async Task<(bool Success, string Output)> ExecuteViveToolAsync(string arguments)
        {
            var exePath = GetViveToolExePath();
            if (!File.Exists(exePath))
            {
                ShowError("ViveTool not found");
                return (false, "");
            }

            var tempDir = Path.GetTempPath();
            var id = Guid.NewGuid().ToString("N");
            var outFile = Path.Combine(tempDir, $"vt_out_{id}.txt");
            var scriptFile = Path.Combine(tempDir, $"vt_script_{id}.ps1");

            try
            {
                var script = $@"
$ErrorActionPreference = 'Continue'
Set-Location '{GetViveToolFolder()}'
& '{exePath}' {arguments} *>&1 | Tee-Object -FilePath '{outFile}'
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
                if (process == null) return (false, "");

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
                ShowWarning("User denied UAC elevation");
                return (false, "");
            }
            catch (Exception ex)
            {
                ShowError($"Execution failed: {ex.Message}");
                return (false, "");
            }
        }

        // 辅助方法
        private void NavigateToOutput(string output)
        {
            Frame.Navigate(typeof(OutputPage), output);
        }

        private async Task<bool> ConfirmAsync(string messageKey)
        {
            var dialog = new ContentDialog
            {
                Title = GetLocalizedString("Confirm_Title"),
                Content = GetLocalizedString(messageKey),
                PrimaryButtonText = GetLocalizedString("Confirm_Yes"),
                CloseButtonText = GetLocalizedString("Confirm_No"),
                XamlRoot = this.XamlRoot
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        private async Task<string> PromptInputAsync(string titleKey, string messageKey)
        {
            var tb = new TextBox { PlaceholderText = GetLocalizedString(messageKey) };
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock { Text = GetLocalizedString(messageKey), TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(tb);

            var dialog = new ContentDialog
            {
                Title = GetLocalizedString(titleKey),
                Content = panel,
                PrimaryButtonText = GetLocalizedString("Confirm_OK"),
                CloseButtonText = GetLocalizedString("Confirm_Cancel"),
                XamlRoot = this.XamlRoot
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary ? tb.Text?.Trim() ?? "" : "";
        }

        private async Task PromptRebootAsync()
        {
            var dialog = new ContentDialog
            {
                Title = GetLocalizedString("Reboot_Title"),
                Content = GetLocalizedString("Reboot_Message"),
                PrimaryButtonText = GetLocalizedString("Reboot_Now"),
                CloseButtonText = GetLocalizedString("Reboot_Later"),
                XamlRoot = this.XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await Task.Run(() => Process.Start("shutdown", "/r /t 10"));
            }
        }

        private void LoadHistory()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue(HistoryKey, out var obj) && obj is string raw)
                {
                    var items = raw.Split(";;", StringSplitOptions.RemoveEmptyEntries);
                    foreach (var item in items.Take(20))
                    {
                        var parts = item.Split('|');
                        if (parts.Length == 2)
                            _history.Add(new HistoryItem { Verb = parts[0], Args = parts[1] });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ViveToolPage] LoadHistory failed: {ex.Message}");
            }
        }

        private void AddHistory(string verb, string args)
        {
            try
            {
                var existing = _history.FirstOrDefault(h => h.Verb == verb && h.Args == args);
                if (existing != null) _history.Remove(existing);

                _history.Insert(0, new HistoryItem { Verb = verb, Args = args });
                while (_history.Count > 20) _history.RemoveAt(_history.Count - 1);

                SaveHistory();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ViveToolPage] AddHistory failed: {ex.Message}");
            }
        }

        private void SaveHistory()
        {
            try
            {
                var raw = string.Join(";;", _history.Select(h => $"{h.Verb}|{h.Args}"));
                ApplicationData.Current.LocalSettings.Values[HistoryKey] = raw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ViveToolPage] SaveHistory failed: {ex.Message}");
            }
        }

        private string GetViveToolFolder()
        {
            return Path.Combine(ApplicationData.Current.LocalFolder.Path, "ViveTool");
        }

        private string GetViveToolExePath()
        {
            return Path.Combine(GetViveToolFolder(), "ViveTool.exe");
        }

        private string GetLocalizedString(string key)
        {
            try
            {
                var loader = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
                var result = loader.GetString(key);
                return string.IsNullOrEmpty(result) ? key : result;
            }
            catch { return key; }
        }

        private void ShowSuccess(string msg) => (App.MainWindow as MainWindow)?.ShowSuccess(msg);
        private void ShowWarning(string msg) => (App.MainWindow as MainWindow)?.ShowWarning(msg);
        private void ShowError(string msg) => (App.MainWindow as MainWindow)?.ShowError(msg);
    }
}