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

            // Safety: reject obviously abusive/accidental long inputs to avoid vivetool hanging
            var parts = normalized.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 100) { (App.MainWindow as MainWindow)?.ShowError("指定的 Feature ID 数量太多"); return; }
            foreach (var p in parts)
            {
                // Normal feature IDs are 8 digits (e.g. 56848060). Reject inputs that are not 8 digits to avoid hangs.
                if (p.Length != 8) { (App.MainWindow as MainWindow)?.ShowError("单个 Feature ID 长度应为 8 位（例如 56848060）"); return; }
            }

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
                SetRunningUI(true, "正在以管理员模式执行...");

                // Create temp files
                var tempDir = Path.GetTempPath();
                var id = Guid.NewGuid().ToString("N");
                var outFile = Path.Combine(tempDir, $"vivetool_out_{id}.txt");
                var batFile = Path.Combine(tempDir, $"vivetool_cmd_{id}.bat");

                // Batch writes stdout+stderr to outFile and returns vivetool exit code
                var batContent = new StringBuilder();
                batContent.AppendLine("@echo off");
                // Use pushd to ensure working dir
                batContent.AppendLine($"pushd \"{GetViveToolFolder()}\"");
                batContent.AppendLine($"\"{exePath}\" {vivetoolArguments} > \"{outFile}\" 2>&1");

                // If this is an enable/disable/reset that targets specific IDs, run a verification query and append
                try
                {
                    var m = System.Text.RegularExpressions.Regex.Match(vivetoolArguments, "/id:([0-9,]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        var ids = m.Groups[1].Value; // e.g. "123,456"
                        batContent.AppendLine($"echo ----QUERY-RESULTS---- >> \"{outFile}\"");
                        batContent.AppendLine($"\"{exePath}\" /query /id:{ids} >> \"{outFile}\" 2>&1");
                      }
                }
                catch { }

                batContent.AppendLine("exit /b %ERRORLEVEL%");

                await File.WriteAllTextAsync(batFile, batContent.ToString(), System.Text.Encoding.UTF8);

                // Start elevated hidden via PowerShell and wait
                var psCommand = "Start-Process -FilePath 'cmd.exe' -ArgumentList '/c \"" + batFile + "\"' -Verb RunAs -WindowStyle Hidden -Wait; exit $LASTEXITCODE";
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{psCommand}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = GetViveToolFolder()
                };

                AppendLog($"启动提升进程: powershell.exe {psi.Arguments}");

                Process? proc = null;
                try
                {
                    proc = Process.Start(psi);
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    AppendLog("用户拒绝 UAC 或无法提升权限");
                    (App.MainWindow as MainWindow)?.ShowError("请求管理员权限被拒绝或无法获取");
                    return;
                }

                if (proc == null)
                {
                    (App.MainWindow as MainWindow)?.ShowError("无法启动提升的命令进程");
                    return;
                }

                // Wait for process to exit with a reasonable timeout (2 minutes)
                var exited = proc.WaitForExit(120000);
                if (!exited)
                {
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    AppendLog("提升进程超时并已被终止");
                    (App.MainWindow as MainWindow)?.ShowWarning("ViveTool 执行超时");
                }

                string output = string.Empty;
                try { if (File.Exists(outFile)) output = await File.ReadAllTextAsync(outFile); } catch { }

                AppendLog($"vivetool 输出:\n{output}");

                var exitCode = proc.HasExited ? proc.ExitCode : -1;
                AppendLog($"提升进程退出码: {exitCode}");

                var success = IsExecutionSuccessful(exitCode, output ?? string.Empty, string.Empty);

                // Verification for /enable|/disable|/reset when IDs provided
                try
                {
                    var verbMatch = System.Text.RegularExpressions.Regex.Match(vivetoolArguments, "^\\/(enable|disable|reset)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    var idMatch = System.Text.RegularExpressions.Regex.Match(vivetoolArguments, "/id:([0-9,]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (verbMatch.Success && idMatch.Success)
                    {
                        var verb = verbMatch.Groups[1].Value.ToLowerInvariant();
                        var ids = idMatch.Groups[1].Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                        var combinedLower = (output ?? string.Empty).ToLowerInvariant();

                        // Early failure detection
                        if (combinedLower.Contains("unable to parse feature id") || combinedLower.Contains("no features were specified"))
                        {
                            success = false;
                            (App.MainWindow as MainWindow)?.ShowError("ViveTool 返回解析错误或未指定 Feature ID，请检查输入。");
                        }
                        else
                        {
                            var marker = "----QUERY-RESULTS----";
                            var idx = combinedLower.IndexOf(marker);
                            var queryOutput = idx >= 0 ? combinedLower.Substring(idx + marker.Length) : combinedLower;
                            if (queryOutput.Length > 200_000) queryOutput = queryOutput.Substring(queryOutput.Length - 200_000);

                            var failed = new List<string>();
                            foreach (var featureId in ids)
                            {
                                var idTrim = featureId.Trim();
                                if (string.IsNullOrEmpty(idTrim)) continue;

                                var bracket = "[" + idTrim + "]";
                                var pos = queryOutput.IndexOf(bracket, StringComparison.OrdinalIgnoreCase);
                                var ok = false;
                                if (pos >= 0)
                                {
                                    var windowStart = pos;
                                    var windowLen = Math.Min(400, queryOutput.Length - windowStart);
                                    var snippet = queryOutput.Substring(windowStart, windowLen);
                                    // look for 'state' line nearby
                                    if (snippet.Contains("state") && snippet.Contains("enabled") && verb == "enable") ok = true;
                                    if (snippet.Contains("state") && snippet.Contains("disabled") && verb == "disable") ok = true;
                                    if (verb == "reset" && snippet.Contains("state") && !snippet.Contains("enabled")) ok = true;
                                }

                                if (!ok) failed.Add(idTrim);
                            }

                          if (failed.Count > 0)
                          {
                              success = false;
                              (App.MainWindow as MainWindow)?.ShowError($"部分 Feature 未按预期应用: {string.Join(',', failed)}。请检查输出并重试。");
                          }
                        }
                    }
                }
                catch { }

                if (success)
                {
                    (App.MainWindow as MainWindow)?.ShowSuccess("命令执行成功");
                }
                else
                {
                    (App.MainWindow as MainWindow)?.ShowError("命令执行失败，详情见输出。");
                }

                // If command included reboot flag, notify user that system will restart when vivetool triggers it.
                if (vivetoolArguments.IndexOf("/reboot", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    (App.MainWindow as MainWindow)?.ShowInfo("包含 /reboot 参数；系统将在命令执行后按 vivetool 的行为重启（若需要）。");
                }

                // cleanup temp files or keep for debugging if empty output
                if (string.IsNullOrWhiteSpace(output))
                {
                    AppendLog($"未捕获到 vivetool 输出，保留临时文件: {batFile}, {outFile}");
                    (App.MainWindow as MainWindow)?.ShowWarning("未捕获到 vivetool 输出，已保留临时输出文件以便排查（查看日志）。");
                }
                else
                {
                    try { if (File.Exists(batFile)) File.Delete(batFile); } catch { }
                    try { if (File.Exists(outFile)) File.Delete(outFile); } catch { }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"执行失败: {ex.Message}");
                (App.MainWindow as MainWindow)?.ShowError("执行失败: " + ex.Message);
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