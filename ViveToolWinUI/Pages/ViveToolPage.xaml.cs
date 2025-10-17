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
        /// ��ȡ ViveTool �ļ���·�������� LocalFolder����� Assets��
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
                txtOutput.Text = "��ѡ������";
                return;
            }

            string verb = actionText.StartsWith("����", StringComparison.OrdinalIgnoreCase) ? "enable" :
                          actionText.StartsWith("����", StringComparison.OrdinalIgnoreCase) ? "disable" :
                          "query";

            var exePath = GetViveToolExePath();
            if (!File.Exists(exePath))
            {
                txtOutput.Text = "δ�ҵ� ViveTool �ںˣ�����������ҳ�����»�ȷ�� Assets/ViveTool ���ڡ�";
                return;
            }

            string args = verb == "query"
                ? "/query"
                : string.IsNullOrEmpty(featureId) ? "" : $"/{verb} {featureId}";

            if (verb != "query" && string.IsNullOrEmpty(args))
            {
                txtOutput.Text = "������ Feature ID��";
                return;
            }

            try
            {
                var result = await RunProcessAsync(exePath, args, GetViveToolFolder());
                txtOutput.Text = result;
            }
            catch (Exception ex)
            {
                txtOutput.Text = $"ִ��ʧ�ܣ�{ex.Message}";
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
                    WorkingDirectory = workingDir, // ȷ�� dll/pfs ��ͬĿ¼
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) throw new InvalidOperationException("�޷����� ViveTool ���̡�");

                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();

                proc.WaitForExit();

                return string.IsNullOrWhiteSpace(stderr) ? stdout : (stdout + Environment.NewLine + stderr);
            });
        }
    }
}