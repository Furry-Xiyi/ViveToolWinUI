using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace ViveToolWinUI.Pages
{
    public class FeatureIdItem
    {
        public string Id { get; set; } = "";
        public string State { get; set; } = "";
        public int StateValue { get; set; }
        public string StatusText => StateValue switch
        {
            0 => "Disabled",
            1 => "Enabled",
            2 => "Default",
            _ => "Unknown"
        };
        public object StatusBrush => StateValue switch
        {
            0 => Microsoft.UI.Colors.Red,
            1 => Microsoft.UI.Colors.Green,
            2 => Microsoft.UI.Colors.Gray,
            _ => Microsoft.UI.Colors.Gray
        };
    }

    public sealed partial class FeatureIDFinderPage : Page
    {
        private ObservableCollection<FeatureIdItem> _allFeatures = new();
        private ObservableCollection<FeatureIdItem> _displayedFeatures = new();
        private FeatureIdItem? _selectedFeature;

        public FeatureIDFinderPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await LoadSystemInfoAsync();

            // 延迟并使用 Items.Add 代替 ItemsSource
            await Task.Delay(100);
            DispatcherQueue.TryEnqueue(() =>
            {
                // 不使用 ItemsSource，直接操作 Items
                // FeaturesListView.ItemsSource = _displayedFeatures; // 删除这行
            });
        }

        private async Task LoadSystemInfoAsync()
        {
            try
            {
                var osVersion = Environment.OSVersion;
                TxtWindowsVersion.Text = $"{osVersion.Platform} {osVersion.Version.Major}.{osVersion.Version.Minor}";
                TxtBuildNumber.Text = osVersion.Version.Build.ToString();
                TxtArchitecture.Text = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";
            }
            catch (Exception ex)
            {
                ShowError($"Failed to load system info: {ex.Message}");
            }
        }

        private async void Query_Click(object sender, RoutedEventArgs e)
        {
            await QueryFeaturesAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await QueryFeaturesAsync();
        }

        private async Task QueryFeaturesAsync()
        {
            BtnQuery.IsEnabled = false;
            BtnRefresh.IsEnabled = false;
            ProgressBar.Visibility = Visibility.Visible;
            ProgressBar.IsIndeterminate = true;
            TxtStatus.Visibility = Visibility.Visible;
            TxtStatus.Text = GetLocalizedString("IDFinder_StatusQuerying");

            try
            {
                var result = await ExecuteViveToolAsync("/query");

                if (!result.Success)
                {
                    ShowError(GetLocalizedString("IDFinder_QueryFailed"));
                    return;
                }

                var features = ParseQueryOutput(result.Output);
                _allFeatures.Clear();
                foreach (var feature in features)
                    _allFeatures.Add(feature);

                ApplyFilters();
                UpdateStatistics();

                ResultsPanel.Visibility = Visibility.Visible;
                BtnExportJson.IsEnabled = true;
                BtnExportCsv.IsEnabled = true;

                TxtStatus.Text = GetLocalizedString("IDFinder_StatusComplete");
                ShowSuccess(GetLocalizedString("IDFinder_QuerySuccess"));
            }
            catch (Exception ex)
            {
                ShowError($"Query failed: {ex.Message}");
                TxtStatus.Text = GetLocalizedString("IDFinder_StatusError");
            }
            finally
            {
                BtnQuery.IsEnabled = true;
                BtnRefresh.IsEnabled = true;
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private List<FeatureIdItem> ParseQueryOutput(string output)
        {
            var features = new List<FeatureIdItem>();
            var lines = output.Split('\n');

            FeatureIdItem? currentFeature = null;

            foreach (var line in lines)
            {
                // 匹配 Feature ID: [12345678]
                var idMatch = Regex.Match(line, @"\[(\d{8})\]");
                if (idMatch.Success)
                {
                    if (currentFeature != null)
                        features.Add(currentFeature);

                    currentFeature = new FeatureIdItem
                    {
                        Id = idMatch.Groups[1].Value
                    };
                    continue;
                }

                if (currentFeature != null)
                {
                    // 匹配 State: 0/1/2
                    var stateMatch = Regex.Match(line, @"State:\s*(\d+)", RegexOptions.IgnoreCase);
                    if (stateMatch.Success)
                    {
                        currentFeature.StateValue = int.Parse(stateMatch.Groups[1].Value);
                        currentFeature.State = currentFeature.StatusText;
                    }
                }
            }

            if (currentFeature != null)
                features.Add(currentFeature);

            return features;
        }

        private void ApplyFilters()
        {
            // 清空 UI 列表
            if (FeaturesListView != null)
                FeaturesListView.Items.Clear();

            // 同时清空内部集合（用于导出等操作）
            _displayedFeatures.Clear();

            var showEnabled = ChkShowEnabled.IsChecked.GetValueOrDefault();
            var showDisabled = ChkShowDisabled.IsChecked.GetValueOrDefault();
            var showDefault = ChkShowDefault.IsChecked.GetValueOrDefault();

            var searchQuery = SearchBox?.Text?.Trim().ToLowerInvariant() ?? "";

            var filtered = _allFeatures.Where(f =>
            {
                if (f.StateValue == 1 && !showEnabled) return false;
                if (f.StateValue == 0 && !showDisabled) return false;
                if (f.StateValue == 2 && !showDefault) return false;

                if (!string.IsNullOrEmpty(searchQuery) && !f.Id.Contains(searchQuery))
                    return false;

                return true;
            });

            var sortBy = (SortOptions.SelectedItem as RadioButton)?.Tag?.ToString() ?? "Id";
            filtered = sortBy == "State"
                ? filtered.OrderBy(f => f.StateValue).ThenBy(f => f.Id)
                : filtered.OrderBy(f => f.Id);

            foreach (var feature in filtered)
            {
                _displayedFeatures.Add(feature); // 保留用于导出

                // 添加到 UI
                if (FeaturesListView != null)
                    FeaturesListView.Items.Add(feature);
            }
        }

        private void UpdateStatistics()
        {
            TxtTotalCount.Text = _allFeatures.Count.ToString();
            TxtEnabledCount.Text = _allFeatures.Count(f => f.StateValue == 1).ToString();
            TxtDisabledCount.Text = _allFeatures.Count(f => f.StateValue == 0).ToString();
        }

        private void Search_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                ApplyFilters();
            }
        }

        private void Features_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FeaturesListView.SelectedItem is FeatureIdItem feature)
            {
                _selectedFeature = feature;
                UpdateDetailPanel(feature);
            }
        }

        private void UpdateDetailPanel(FeatureIdItem feature)
        {
            TxtDetailId.Text = feature.Id;
            TxtDetailState.Text = feature.StatusText;
            TxtDetailValue.Text = feature.StateValue.ToString();

            BtnEnable.IsEnabled = feature.StateValue != 1;
            BtnDisable.IsEnabled = feature.StateValue != 0;
            BtnReset.IsEnabled = feature.StateValue != 2;
        }

        private async void Enable_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFeature == null) return;
            await ExecuteActionAsync("enable", _selectedFeature.Id);
        }

        private async void Disable_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFeature == null) return;
            await ExecuteActionAsync("disable", _selectedFeature.Id);
        }

        private async void Reset_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFeature == null) return;
            await ExecuteActionAsync("reset", _selectedFeature.Id);
        }

        private async void QuickAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is FeatureIdItem feature)
            {
                var action = feature.StateValue == 1 ? "disable" : "enable";
                await ExecuteActionAsync(action, feature.Id);
            }
        }

        private void CopyId_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFeature == null) return;

            var package = new DataPackage();
            package.SetText(_selectedFeature.Id);
            Clipboard.SetContent(package);

            ShowSuccess(GetLocalizedString("IDFinder_CopySuccess"));
        }

        private async Task ExecuteActionAsync(string action, string featureId)
        {
            var result = await ExecuteViveToolAsync($"/{action} /id:{featureId}");

            if (result.Success)
            {
                ShowSuccess($"Feature {featureId} {action}d successfully");
                await Task.Delay(500);
                await QueryFeaturesAsync();
            }
            else
            {
                ShowError($"Failed to {action} feature");
            }
        }

        private async void ExportJson_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileSavePicker();
            picker.FileTypeChoices.Add("JSON", new[] { ".json" });
            picker.SuggestedFileName = $"features_{DateTime.Now:yyyyMMdd_HHmmss}.json";

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                var json = JsonSerializer.Serialize(_allFeatures.ToList(), new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                await File.WriteAllTextAsync(file.Path, json);
                ShowSuccess(GetLocalizedString("IDFinder_ExportSuccess"));
            }
        }

        private async void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileSavePicker();
            picker.FileTypeChoices.Add("CSV", new[] { ".csv" });
            picker.SuggestedFileName = $"features_{DateTime.Now:yyyyMMdd_HHmmss}.csv";

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                var csv = new System.Text.StringBuilder();
                csv.AppendLine("FeatureID,State,StateValue");

                foreach (var feature in _allFeatures)
                {
                    csv.AppendLine($"{feature.Id},{feature.StatusText},{feature.StateValue}");
                }

                await File.WriteAllTextAsync(file.Path, csv.ToString());
                ShowSuccess(GetLocalizedString("IDFinder_ExportSuccess"));
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

        private string GetViveToolFolder()
        {
            return Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "ViveTool");
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
            catch
            {
                return key;
            }
        }

        private void ShowSuccess(string msg) => (App.MainWindow as MainWindow)?.ShowSuccess(msg);
        private void ShowWarning(string msg) => (App.MainWindow as MainWindow)?.ShowWarning(msg);
        private void ShowError(string msg) => (App.MainWindow as MainWindow)?.ShowError(msg);
    }
}