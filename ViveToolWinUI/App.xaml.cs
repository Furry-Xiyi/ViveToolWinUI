using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;

namespace ViveToolWinUI
{
    public partial class App : Application
    {
        public static MainWindow? MainWindow { get; private set; }

        // 保留常量以备将来需要（当前不用于自动重启）
        private const string ElevationGuardFlag = "--elevated";

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            // 不在启动时自动尝试提权或重启为管理员
            // 由具体需要提权的操作在运行时显式触发 UAC（例如通过 ProcessStartInfo.Verb="runas" 或 helper.exe）
            MainWindow = new MainWindow();
            MainWindow.Activate();

            // 启动即显示假 Splash 覆盖层
            MainWindow.ShowSplashOverlay();

            // 异步初始化完成后隐藏（你可以替换为真实初始化任务的完成点）
            _ = Task.Run(async () =>
            {
                await Task.Delay(500); // UI 稳定
                MainWindow!.DispatcherQueue.TryEnqueue(() =>
                {
                    // 在此加入非关键初始化逻辑（读取设置、准备资源等）
                });

                await Task.Delay(1000); // 模拟其他初始化
                MainWindow!.DispatcherQueue.TryEnqueue(MainWindow.HideSplashOverlay);
            });
        }

        private static bool IsElevated()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private static bool HasGuardFlag(string flag)
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                return args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        // 如果将来需要可复用的重启为管理员方法，可保留或恢复此方法并在需要时调用
        private static bool TryRelaunchAsAdmin(string guardFlag)
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                if (string.IsNullOrEmpty(exePath)) return false;

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = guardFlag
                };

                Process.Start(psi);
                return true;
            }
            catch
            {
                // 用户取消 UAC 或启动失败
                return false;
            }
        }
    }
}