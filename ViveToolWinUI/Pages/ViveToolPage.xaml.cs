using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;

namespace ViveToolWinUI.Pages
{
    public sealed partial class ViveToolPage : Page
    {
        public ViveToolPage()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 获取 ViveTool 文件夹路径（优先 LocalFolder，其次 Assets）
        /// </summary>
        private string GetViveToolFolder()
        {
            var localFolder = Path.Combine(ApplicationData.Current.LocalFolder.Path, "ViveTool");
            if (Directory.Exists(localFolder) && File.Exists(Path.Combine(localFolder, "ViveTool.exe")))
                return localFolder;

            return Path.Combine(AppContext.BaseDirectory, "Assets", "ViveTool");
        }

        private string GetViveToolExePath() => Path.Combine(GetViveToolFolder(), "ViveTool.exe");

        private async void Run_Click(object sender, RoutedEventArgs e)
        {
            txtOutput.Text = "";

            var actionItem = cmbAction.SelectedItem as ComboBoxItem;
            var actionText = actionItem?.Content?.ToString() ?? "";
            var featureId = txtFeatureId.Text?.Trim();

            if (string.IsNullOrEmpty(actionText))
            {
                txtOutput.Text = "请选择动作。";
                return;
            }

            string verb = actionText.StartsWith("启用", StringComparison.OrdinalIgnoreCase) ? "enable" :
                          actionText.StartsWith("禁用", StringComparison.OrdinalIgnoreCase) ? "disable" :
                          "query";

            var exePath = GetViveToolExePath();
            if (!File.Exists(exePath))
            {
                txtOutput.Text = "未找到 ViveTool 内核，请先在设置页检测更新或确认 Assets/ViveTool 存在。";
                return;
            }

            string args = verb == "query"
                ? "/query"
                : string.IsNullOrEmpty(featureId) ? "" : $"/{verb} {featureId}";

            if (verb != "query" && string.IsNullOrEmpty(args))
            {
                txtOutput.Text = "请输入 Feature ID。";
                return;
            }

            try
            {
                var result = await RunProcessAsync(exePath, args, GetViveToolFolder());
                txtOutput.Text = result;
            }
            catch (Exception ex)
            {
                txtOutput.Text = $"执行失败：{ex.Message}";
            }
        }

        private static Task<string> RunProcessAsync(string exePath, string arguments, string workingDir)
        {
            return Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arguments,
                    WorkingDirectory = workingDir, // 确保 dll/pfs 在同目录
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) throw new InvalidOperationException("无法启动 ViveTool 进程。");

                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();

                proc.WaitForExit();

                return string.IsNullOrWhiteSpace(stderr) ? stdout : (stdout + Environment.NewLine + stderr);
            });
        }
    }
}