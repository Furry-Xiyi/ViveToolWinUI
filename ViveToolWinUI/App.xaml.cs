using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using Microsoft.Windows.AppLifecycle;
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

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // 单实例逻辑：只允许运行一个进程
            var key = "ViveToolWinUI_SingleInstance";
            var instance = AppInstance.FindOrRegisterForKey(key);

            if (!instance.IsCurrent)
            {
                // 已经有实例在运行，把激活请求转发过去并退出
                instance.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs()).AsTask().Wait();
                Environment.Exit(0);
                return;
            }

            // 确保只创建一次窗口
            if (MainWindow == null)
            {
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
            else
            {
                // 如果已有窗口，直接激活它
                MainWindow.Activate();
            }
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