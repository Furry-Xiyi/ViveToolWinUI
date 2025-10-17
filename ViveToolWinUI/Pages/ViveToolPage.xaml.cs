using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;

namespace ViveToolWinUI.Pages
{
    public sealed partial class ViveToolPage : Page
    {
        private static readonly StringBuilder s_logBuffer = new();
        private readonly List<string> _history = new();
        private CancellationTokenSource? _cts;
        private bool _isRunning = false;

        public ViveToolPage()
        {
            InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Enabled;

            cmbAction.SelectedIndex = 0;
            lvHistory.SelectionChanged += LvHistory_SelectionChanged;

            this.Loaded += (_, __) => ApplyRunButtonAccent();
        }

        #region Appearance helpers
        private void ApplyRunButtonAccent()
        {
            try
            {
                object? brushObj = null;
                if (Application.Current.Resources.ContainsKey("SystemControlBackgroundAccentBrush"))
                    brushObj = Application.Current.Resources["SystemControlBackgroundAccentBrush"];
                else if (Application.Current.Resources.ContainsKey("SystemControlForegroundAccentBrush"))
                    brushObj = Application.Current.Resources["SystemControlForegroundAccentBrush"];
                else if (Application.Current.Resources.ContainsKey("AccentFillRest"))
                    brushObj = Application.Current.Resources["AccentFillRest"];

                if (brushObj is Brush b)
                {
                    btnRun.Background = b;
                    btnRun.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
                    btnRunCustom.Background = b;
                    btnRunCustom.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
                }
            }
            catch { }
        }
        #endregion

        private void LvHistory_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (lvHistory.SelectedItem is string item)
            {
                var parts = item.Split('|');
                if (parts.Length >= 1) cmbAction.SelectedIndex = parts[0] == "enable" ? 0 : 1;
                if (parts.Length >= 2) txtFeatureId.Text = parts[1];
            }
        }

        private string GetViveToolFolder()
        {
            var localFolder = Path.Combine(ApplicationData.Current.LocalFolder.Path, "ViveTool");
            if (Directory.Exists(localFolder) && File.Exists(Path.Combine(localFolder, "ViveTool.exe")))
                return localFolder;
            return Path.Combine(AppContext.BaseDirectory, "Assets", "ViveTool");
        }

        private string GetViveToolExePath() => Path.Combine(GetViveToolFolder(), "ViveTool.exe");

        #region Preset handlers (部分命令需要参数，弹窗收集)
        private void Preset_Query_Click(object s, RoutedEventArgs e) => _ = RunAsAdminCmd("/query");
        private void Preset_Status_Click(object s, RoutedEventArgs e) => _ = RunAsAdminCmd("/status");
        private void Preset_Version_Click(object s, RoutedEventArgs e) => _ = RunAsAdminCmd("/version");

        private void Preset_Enable_Click(object s, RoutedEventArgs e) => InsertFeatureToCustom("/enable /id:");
        private void Preset_Disable_Click(object s, RoutedEventArgs e) => InsertFeatureToCustom("/disable /id:");
        private void Preset_Reset_Click(object s, RoutedEventArgs e) => InsertFeatureToCustom("/reset /id:");

        private void Preset_FullReset_Click(object s, RoutedEventArgs e) => _ = RunAsAdminCmd("/fullreset");
        private void Preset_ChangeStamp_Click(object s, RoutedEventArgs e) => _ = RunAsAdminCmd("/changestamp");

        private void Preset_QuerySubs_Click(object s, RoutedEventArgs e) => _ = RunAsAdminCmd("/querysubs");
        private async void Preset_AddSub_Click(object s, RoutedEventArgs e)
        {
            var param = await PromptForInputAsync("addsub 参数", "请输入 addsub 参数（例如: /id:12345 /user:...）");
            if (!string.IsNullOrEmpty(param)) await RunAsAdminCmd($"/addsub {param}");
        }
        private async void Preset_DelSub_Click(object s, RoutedEventArgs e)
        {
            var param = await PromptForInputAsync("delsub 参数", "请输入 delsub 参数（例如: /id:12345 /user:...）");
            if (!string.IsNullOrEmpty(param)) await RunAsAdminCmd($"/delsub {param}");
        }

        private async void Preset_NotifyUsage_Click(object s, RoutedEventArgs e)
        {
            var param = await PromptForInputAsync("notifyusage 参数", "请输入 notifyusage 参数（例如: /id:12345 /usage:1）");
            if (!string.IsNullOrEmpty(param)) await RunAsAdminCmd($"/notifyusage {param}");
        }

        private async void Preset_Export_Click(object s, RoutedEventArgs e)
        {
            var param = await PromptForInputAsync("export 参数", "可选：输出文件路径或留空（将在当前工作目录生成）");
            await RunAsAdminCmd(string.IsNullOrEmpty(param) ? "/export" : $"/export \"{param}\"");
        }
        private async void Preset_Import_Click(object s, RoutedEventArgs e)
        {
            var param = await PromptForInputAsync("import 参数", "请输入要导入的文件路径（必填）");
            if (!string.IsNullOrEmpty(param))
                await RunAsAdminCmd($"/import \"{param}\"");
        }

        private void Preset_LkgStatus_Click(object s, RoutedEventArgs e) => _ = RunAsAdminCmd("/lkgstatus");
        private void Preset_FixLkg_Click(object s, RoutedEventArgs e) => _ = RunAsAdminCmd("/fixlkg");
        private void Preset_FixPriority_Click(object s, RoutedEventArgs e) => _ = RunAsAdminCmd("/fixpriority");

        private void Preset_AppUpdate_Click(object s, RoutedEventArgs e) => _ = RunAsAdminCmd("/appupdate");
        private void Preset_DictUpdate_Click(object s, RoutedEventArgs e) => _ = RunAsAdminCmd("/dictupdate");

        private void Preset_Install_Click(object s, RoutedEventArgs e) => _ = RunAsAdminCmd("/install");
        private void Preset_Uninstall_Click(object s, RoutedEventArgs e) => _ = RunAsAdminCmd("/uninstall");
        private void Preset_Help_Click(object s, RoutedEventArgs e) => _ = RunAsAdminCmd("/help");

        private void Preset_Apply_Click(object s, RoutedEventArgs e) => _ = RunAsAdminCmd("/apply");
        private void Preset_Rollback_Click(object s, RoutedEventArgs e) => _ = RunAsAdminCmd("/rollback");
        private void Preset_EnableAll_Click(object s, RoutedEventArgs e) => _ = RunAsAdminCmd("/enable-all");
        private void Preset_DisableAll_Click(object s, RoutedEventArgs e) => _ = RunAsAdminCmd("/disable-all");
        private void Preset_QueryFormat_Click(object s, RoutedEventArgs e) => _ = RunAsAdminCmd("/query-format");

        private void InsertFeatureToCustom(string prefix)
        {
            var ids = txtFeatureId.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(ids)) txtCustomArgs.Text = prefix;
            else txtCustomArgs.Text = prefix.EndsWith(":") ? $"{prefix}{NormalizeIds(ids)}" : $"{prefix} {NormalizeIds(ids)}";
            txtCustomArgs.Focus(FocusState.Programmatic);
        }
        #endregion

        #region Run / Custom -> 管理员 CMD
        private async void Run_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) { _cts?.Cancel(); return; }

            var actionIndex = cmbAction.SelectedIndex;
            string verb = actionIndex switch { 0 => "enable", 1 => "disable", _ => "enable" };
            var featureId = txtFeatureId.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(featureId)) { (App.MainWindow as MainWindow)?.ShowWarning("请输入 Feature ID"); return; }
            if (!IsValidFeatureIdInput(featureId)) { (App.MainWindow as MainWindow)?.ShowError("Feature ID 格式无效"); return; }

            var normalized = NormalizeIds(featureId);
            var sb = new StringBuilder();
            sb.Append($"/{verb} /id:{normalized}");
            if (chkVerbose.IsChecked == true) sb.Append(" /verbose");
            if (chkReboot.IsChecked == true) sb.Append(" /reboot");

            lvHistoryAdd($"{verb}|{normalized}");
            AppendLog($"将以管理员 CMD 执行: vivetool {sb}");

            await RunAsAdminCmd(sb.ToString());
        }

        private async void RunCustom_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) { _cts?.Cancel(); return; }
            var args = txtCustomArgs?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(args)) { (App.MainWindow as MainWindow)?.ShowWarning("请输入参数"); return; }
            lvHistoryAdd($"custom|{args}");
            AppendLog($"将以管理员 CMD 执行: vivetool {args}");
            await RunAsAdminCmd(args);
        }

        private async Task RunAsAdminCmd(string vivetoolArguments)
        {
            var exePath = GetViveToolExePath();
            if (!File.Exists(exePath)) { (App.MainWindow as MainWindow)?.ShowError("未找到 ViveTool"); return; }

            _cts = new CancellationTokenSource();
            _isRunning = true;
            try
            {
                SetRunningUI(true, "以管理员 CMD 执行中...");
                var cmdArgs = $"/k \"\"{exePath}\" {vivetoolArguments}\"";
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = cmdArgs,
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = GetViveToolFolder()
                };

                AppendLog($"启动管理员 CMD: cmd.exe {cmdArgs}");
                try
                {
                    Process.Start(psi);
                    (App.MainWindow as MainWindow)?.ShowInfo("命令已以管理员模式提交，输出显示在弹出的管理员命令窗口中。");
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    AppendLog("用户取消了 UAC 或管理员启动被拒绝。");
                    (App.MainWindow as MainWindow)?.ShowError("管理员启动被取消或拒绝。");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"启动失败：{ex.Message}");
                (App.MainWindow as MainWindow)?.ShowError($"启动失败：{ex.Message}");
            }
            finally
            {
                FinishRun();
            }
        }
        #endregion

        #region 小工具：弹出输入对话
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
                XamlRoot = this.XamlRoot
            };

            var result = await dlg.ShowAsync();
            if (result == ContentDialogResult.Primary) return tb.Text?.Trim() ?? "";
            return "";
        }
        #endregion

        #region UI / Process helpers（保留 RunProcessCaptureAsync 以便需要时使用）
        private Task<(string Output, string Error, int ExitCode)> RunProcessCaptureAsync(
            string exePath, string arguments, string workingDir, TimeSpan timeout, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arguments,
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using var proc = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 ViveTool 进程。");

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

        private void SetRunningUI(bool running, string status)
        {
            btnRun.Content = running ? "处理中..." : "执行（管理员 CMD）";
            btnRun.IsEnabled = !running;
            btnRunCustom.IsEnabled = !running;
            btnOpenOutputPage.IsEnabled = !running && s_logBuffer.Length > 0;
        }

        

        private void OpenOutputPage_Click(object sender, RoutedEventArgs e)
        {
            var log = GetLog();
            Frame?.Navigate(typeof(OutputPage), string.IsNullOrEmpty(log) ? null : log);
        }

        private void FinishRun()
        {
            _isRunning = false;
            _cts?.Dispose();
            _cts = null;
            SetRunningUI(false, "就绪");
        }
        #endregion

        #region Validation / normalize / detection
        private static bool IsValidFeatureIdInput(string input)
        {
            foreach (var ch in input) if (!(char.IsDigit(ch) || ch == ',' || ch == ' ' || ch == ';')) return false;
            return true;
        }

        private static string NormalizeIds(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var sb = new StringBuilder();
            foreach (var ch in raw)
            {
                if (char.IsDigit(ch) || ch == ',') sb.Append(ch);
                else if (ch == ';' || ch == ' ') sb.Append(',');
            }
            var s = sb.ToString();
            while (s.Contains(",,")) s = s.Replace(",,", ",");
            return s.Trim(',');
        }

        private static bool IsExecutionSuccessful(int exitCode, string stdout, string stderr)
        {
            var combined = (stdout + "\n" + stderr).ToLowerInvariant();
            if (combined.Contains("an error occurred while setting feature configurations in the runtime store")
                || combined.Contains("access is denied") || combined.Contains("permission") || combined.Contains("failed"))
                return false;
            if (combined.Contains("enabled") || combined.Contains("disabled") || combined.Contains("successfully") || combined.Contains("applied"))
                return true;
            if (combined.Contains("unrecognized") || combined.Contains("no features were specified") || combined.Contains("unrecognized command"))
                return false;
            return exitCode == 0;
        }
        #endregion

        #region Log cache
        private void AppendLog(string text)
        {
            var line = $"{DateTime.Now:HH:mm:ss} {text}";
            lock (s_logBuffer) { s_logBuffer.AppendLine(line); }
        }

        private void ClearLog()
        {
            lock (s_logBuffer) { s_logBuffer.Clear(); }
        }

        public static string GetLog()
        {
            lock (s_logBuffer) { return s_logBuffer.ToString(); }
        }

        private void lvHistoryAdd(string entry)
        {
            if (!_history.Contains(entry))
            {
                _history.Insert(0, entry);
                if (_history.Count > 50) _history.RemoveAt(_history.Count - 1);
                lvHistory.ItemsSource = null;
                lvHistory.ItemsSource = _history;
            }
        }
        #endregion
    }
}