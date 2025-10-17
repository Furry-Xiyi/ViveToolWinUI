using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Graphics;
using Windows.Storage;

namespace ViveToolWinUI
{
    public sealed partial class MainWindow : Window
    {
        private AppWindow _appWindow = null!;
        private readonly ApplicationDataContainer _settings = ApplicationData.Current.LocalSettings;

        private const string ViveToolFolderName = "ViveTool";
        private const string ViveToolZipName = "ViveTool.zip";
        private const string ViveToolZipUrl = "https://github.com/thebookisclosed/ViVe/releases/latest/download/ViveTool.zip";

        private const int MinWidth = 800;
        private const int MinHeight = 600;
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        // Win32 ���໯
        private delegate IntPtr WndProcDelegate(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        private WndProcDelegate? _newWndProc;
        private IntPtr _oldWndProc;
        private const int GWLP_WNDPROC = -4;
        private const int WM_GETMINMAXINFO = 0x0024;

        [StructLayout(LayoutKind.Sequential)]

        private struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        private bool _pendingDragUpdate = false;

        // InfoBar / Toast fields (�ϲ�����һ��Ӧ�õ�ʵ�ַ��)
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _infoTimer;
        public enum InfoBarSeverity { Informational = 0, Success = 1, Warning = 2, Error = 3 }

        // Ϊ�˱�����ܵ� replace-last ���壬����һ���� pending �����ֶΣ��ɿ����ã�
        private volatile PendingInfoBarRequest? _pendingInfoBarRequest = null;
        private class PendingInfoBarRequest
        {
            public string Title { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public InfoBarSeverity Severity { get; set; } = InfoBarSeverity.Informational;
        }

        // �����Ը������ٸ�Ϊ�����ӵĴ���ѭ�������滻���׼��߼�
        private Storyboard? _runningStoryboard = null;

        public MainWindow()
        {
            InitializeComponent();

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId)!;

            try { _appWindow.ResizeClient(new SizeInt32(1100, 720)); }
            catch { _appWindow.Resize(new SizeInt32(1100, 720)); }

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            var tb = _appWindow.TitleBar;
            tb.ExtendsContentIntoTitleBar = true;
            tb.PreferredHeightOption = TitleBarHeightOption.Tall;
            tb.BackgroundColor = Microsoft.UI.Colors.Transparent;
            tb.InactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            tb.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            tb.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            tb.ButtonHoverBackgroundColor = ColorHelper.FromArgb(32, 255, 255, 255);
            tb.ButtonPressedBackgroundColor = ColorHelper.FromArgb(64, 255, 255, 255);

            AppTitleBar.Loaded += (_, __) => EnsureDragRectsOnce();
            RootLayout.Loaded += (_, __) => UpdateTitleBarDragRegions();
            _appWindow.Changed += (_, __) => DispatcherQueue.TryEnqueue(UpdateTitleBarDragRegions);

            SubclassWindowForMinSize(hwnd);

            _ = MaybeAutoUpdateViveToolAsync();

            NavView.SelectedItem = NavView.MenuItems[0];

            ApplyBackdropFromSettings();
            UpdateTitleBarButtonsForTheme((Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default);

            // ��ʼ�� info timer (������һ��Ӧ�÷��)
            _infoTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _infoTimer.Interval = TimeSpan.FromSeconds(2);
            _infoTimer.Tick += (_, __) =>
            {
                HideToast();
                _infoTimer?.Stop();
            };
        }

        #region WM_GETMINMAXINFO ��С�ߴ磨�޻ص���
        private void SubclassWindowForMinSize(IntPtr hwnd)
        {
            _newWndProc = WndProc;
            _oldWndProc = SetWindowLongPtr(hwnd, GWLP_WNDPROC,
                Marshal.GetFunctionPointerForDelegate(_newWndProc));
        }

        private IntPtr WndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_GETMINMAXINFO)
            {
                var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                mmi.ptMinTrackSize.x = MinWidth;
                mmi.ptMinTrackSize.y = MinHeight;
                Marshal.StructureToPtr(mmi, lParam, true);
            }
            return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        #endregion

        #region ���� / Frame �߼�
        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                ContentFrame.Navigate(typeof(ViveToolWinUI.Pages.SettingsPage), null, new EntranceNavigationTransitionInfo());
                return;
            }

            var selected = (args.SelectedItem as NavigationViewItem)?.Tag?.ToString();
            if (string.IsNullOrEmpty(selected)) return;

            Type targetPage = selected switch
            {
                "ViveTool" => typeof(ViveToolWinUI.Pages.ViveToolPage),
                "Output" => typeof(ViveToolWinUI.Pages.OutputPage),
                _ => typeof(ViveToolWinUI.Pages.ViveToolPage)
            };

            if (ContentFrame.CurrentSourcePageType != targetPage)
                ContentFrame.Navigate(targetPage, null, new EntranceNavigationTransitionInfo());
        }

        private void NavView_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        {
            if (ContentFrame.CanGoBack) ContentFrame.GoBack();
        }

        private void ContentFrame_Navigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            NavView.IsBackEnabled = ContentFrame.CanGoBack;

            if (e.SourcePageType == typeof(ViveToolWinUI.Pages.SettingsPage))
            {
                NavView.SelectedItem = NavView.SettingsItem;
                return;
            }

            if (e.SourcePageType == typeof(ViveToolWinUI.Pages.ViveToolPage))
            {
                SelectNavItemByTag("ViveTool");
            }
            else if (e.SourcePageType == typeof(ViveToolWinUI.Pages.OutputPage))
            {
                SelectNavItemByTag("Output");
            }
        }
        private void SelectNavItemByTag(string tag)
        {
            foreach (var mi in NavView.MenuItems)
            {
                if (mi is NavigationViewItem nvi && nvi.Tag is string t && t == tag)
                {
                    NavView.SelectedItem = nvi;
                    return;
                }
            }
        }
        #endregion

        #region ViveTool �����߼�
        // === �滻��������ԭ������ LocalFolder��ApplicationData.Current.LocalFolder��·��
        private string GetLocalViveToolFolderPath()
        {
            // ����ԭ����Ϊ������������ָ�� ApplicationData.Current.LocalFolder\ViveTool
            return Path.Combine(ApplicationData.Current.LocalFolder.Path, ViveToolFolderName);
        }

        // ��������ѡ����ִ�еĿ�дĿ¼��%LocalAppData%\ViveToolWinUI\ViveTool��
        private string GetWritableLocalViveToolFolderPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ViveToolWinUI", "ViveTool");
        }


        // === �滻���������ȼ������ȿ�д LocalAppData����� ApplicationData.Current.LocalFolder�������� Assets
        public string GetEffectiveViveToolFolder()
        {
            // 1) ���ȼ�� LocalAppData �µĿ�дĿ¼�����ȣ�
            var writable = GetWritableLocalViveToolFolderPath();
            if (Directory.Exists(writable) && File.Exists(Path.Combine(writable, "ViveTool.exe")))
                return writable;

            // 2) ��μ�� ApplicationData.Current.LocalFolder����ԭ�е��Զ�����Ŀ�꣩
            var local = GetLocalViveToolFolderPath();
            if (Directory.Exists(local) && File.Exists(Path.Combine(local, "ViveTool.exe")))
                return local;

            // 3) ������˰��� Assets��ֻ����
            return Path.Combine(AppContext.BaseDirectory, "Assets", "ViveTool");
        }

        // === �滻��Exe ·��Ҳ��֮����
        public string GetEffectiveViveToolExePath() =>
            Path.Combine(GetEffectiveViveToolFolder(), "ViveTool.exe");


        // === �滻�����Զ�����ǰȷ����ִ���Ѱ�װ����дĿ¼��fire-and-forget ��Ϊ�ȴ� Ensure��
        private async Task MaybeAutoUpdateViveToolAsync()
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                var auto = (bool?)(localSettings.Values["AutoUpdateViveTool"]) ?? true;
                if (!auto) return;

                // ȷ�����ؿ�ִ�д��ڣ��Ӱ��� LocalFolder ���Ƶ� LocalAppData��
                await EnsureViveToolInstalledAsync();

                await UpdateViveToolAsync();
            }
            catch { }
        }

        // ������� release �����ذ汾��ʶ������ "v0.3.3"����ʧ��ʱ���� null ����ַ���
        public async Task<string?> UpdateViveToolAsync()
        {
            try
            {
                // �������Ĳֿ��滻���������ֶ�
                var owner = "thebookisclosed";
                var repo = "ViVe";

                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("ViveToolWinUI-Updater");

                var apiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
                var json = await http.GetStringAsync(apiUrl);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("tag_name", out var tagEl))
                {
                    var tag = tagEl.GetString();
                    // ���� tag���� SettingsPage ������ʾ�û���
                    return tag;
                }

                return null;
            }
            catch
            {
                // ������ؿգ����÷�����ʾ������ʾ
                return null;
            }
        }
        // === �滻/��ǿ�����غ�д�� ApplicationData.Current.LocalFolder��������ԭ����Ϊ��
        // ���������Ⳣ�԰� LocalFolder �����ݸ��Ƶ���д LocalAppData �Ա�ִ��/�ⲿ����
        public async Task DownloadAndImportViveToolAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                this.DispatcherQueue.TryEnqueue(() => (App.MainWindow as MainWindow)?.ShowError("��������Ϊ��"));
                return;
            }

            string tmpFolder = Path.Combine(Path.GetTempPath(), "ViveTool_Download_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpFolder);
            string tmpZip = Path.Combine(tmpFolder, "ViveTool.zip");

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                using var resp = await http.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                {
                    this.DispatcherQueue.TryEnqueue(() => (App.MainWindow as MainWindow)?.ShowError($"����ʧ�ܣ�{resp.StatusCode}"));
                    return;
                }

                var bytes = await resp.Content.ReadAsByteArrayAsync();
                await File.WriteAllBytesAsync(tmpZip, bytes);

                // ���������ںˣ�����ڣ�
                var currentFolder = Path.Combine(ApplicationData.Current.LocalFolder.Path, "ViveTool");
                if (Directory.Exists(currentFolder))
                {
                    try
                    {
                        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                        var backupDir = Path.Combine(desktop, "ViveTool_Backups");
                        Directory.CreateDirectory(backupDir);
                        var backupPath = Path.Combine(backupDir, $"ViveTool_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
                        ZipFile.CreateFromDirectory(currentFolder, backupPath, CompressionLevel.Optimal, includeBaseDirectory: false);
                    }
                    catch
                    {
                        System.Diagnostics.Debug.WriteLine("ViveTool backup failed, continuing replacement.");
                    }
                }

                // ����Ŀ��Ŀ¼����ѹ�� ApplicationData.Current.LocalFolder\ViveTool
                var localFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync("ViveTool", CreationCollisionOption.OpenIfExists);
                var localPath = localFolder.Path;

                try
                {
                    foreach (var f in Directory.EnumerateFiles(localPath)) File.Delete(f);
                    foreach (var d in Directory.EnumerateDirectories(localPath)) Directory.Delete(d, true);
                }
                catch { /* ����������� */ }

                ZipFile.ExtractToDirectory(tmpZip, localPath, overwriteFiles: true);
                try { File.Delete(tmpZip); } catch { }

                // У�� ViveTool.exe ����
                var exePath = Path.Combine(localPath, "ViveTool.exe");
                if (!File.Exists(exePath))
                {
                    this.DispatcherQueue.TryEnqueue(() => (App.MainWindow as MainWindow)?.ShowError("����ʧ�ܣ���ѹ��δ�ҵ� ViveTool.exe"));
                    return;
                }

                // ���԰� LocalFolder �����ݸ��Ƶ� LocalAppData ��ִ��Ŀ¼�����ǣ����А��ⲿ CMD / ��Ȩִ��
                try
                {
                    var writable = GetWritableLocalViveToolFolderPath();
                    Directory.CreateDirectory(writable);

                    foreach (var f in Directory.EnumerateFiles(localPath))
                    {
                        var name = Path.GetFileName(f);
                        try { File.Copy(f, Path.Combine(writable, name), overwrite: true); }
                        catch { /* ���Ե����ļ����ƴ��� */ }
                    }
                }
                catch { /* ���Ը��ƴ��� */ }

                this.DispatcherQueue.TryEnqueue(() => (App.MainWindow as MainWindow)?.ShowSuccess("ViveTool �ں��Ѹ��²����롣"));
            }
            catch (Exception ex)
            {
                this.DispatcherQueue.TryEnqueue(() => (App.MainWindow as MainWindow)?.ShowError("���ػ���ʧ�ܣ�" + ex.Message));
            }
            finally
            {
                try { if (File.Exists(tmpZip)) File.Delete(tmpZip); } catch { }
                try { if (Directory.Exists(tmpFolder)) Directory.Delete(tmpFolder, recursive: true); } catch { }
            }
        }
        // === ������ȷ����ִ���Ѹ��Ƶ� %LocalAppData%\ViveToolWinUI\ViveTool��ͬ���棩
        public void EnsureViveToolInstalled()
        {
            try
            {
                var destDir = GetWritableLocalViveToolFolderPath();
                Directory.CreateDirectory(destDir);
                var destExe = Path.Combine(destDir, "ViveTool.exe");
                if (File.Exists(destExe)) return;

                // ���ȴ� ApplicationData.Current.LocalFolder�������ʱд���λ�ã�����
                var appDataFolder = GetLocalViveToolFolderPath();
                var srcExeInLocalFolder = Path.Combine(appDataFolder, "ViveTool.exe");
                if (File.Exists(srcExeInLocalFolder))
                {
                    try
                    {
                        foreach (var f in Directory.EnumerateFiles(appDataFolder))
                        {
                            var name = Path.GetFileName(f);
                            try { File.Copy(f, Path.Combine(destDir, name), overwrite: true); } catch { }
                        }
                        return;
                    }
                    catch { /* ���Բ����˵����ڸ��� */ }
                }

                // ���ˣ��Ӱ��� Assets ����
                var packageDir = Path.Combine(Package.Current.InstalledLocation.Path, "Assets", "ViveTool");
                var packageExe = Path.Combine(packageDir, "ViveTool.exe");
                if (File.Exists(packageExe))
                {
                    try
                    {
                        foreach (var f in Directory.EnumerateFiles(packageDir))
                        {
                            var name = Path.GetFileName(f);
                            try { File.Copy(f, Path.Combine(destDir, name), overwrite: true); } catch { }
                        }
                    }
                    catch { System.Diagnostics.Debug.WriteLine("Copy from package failed."); }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("ViveTool not found in package or local folder.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("EnsureViveToolInstalled error: " + ex);
            }
        }

        // === �������첽�汾
        public Task EnsureViveToolInstalledAsync() => Task.Run(() => EnsureViveToolInstalled());

        // === �������Թ���ԱȨ����� ViveTool�������� stdout��UseShellExecute = true��
        public Process? RunViveToolWithElevation(string arguments, bool waitForExit = true, int timeoutMs = 120000)
        {
            try
            {
                EnsureViveToolInstalled();

                var exePath = Path.Combine(GetWritableLocalViveToolFolderPath(), "ViveTool.exe");
                if (!File.Exists(exePath))
                {
                    ShowError("ViveTool �ں�δ�ҵ����޷�ִ�����");
                    return null;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arguments ?? string.Empty,
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = Path.GetDirectoryName(exePath)
                };

                var proc = Process.Start(psi);
                if (proc == null)
                {
                    ShowError("�޷���� ViveTool ���̡�");
                    return null;
                }

                if (waitForExit)
                {
                    if (!proc.WaitForExit(timeoutMs))
                    {
                        try { proc.Kill(); } catch { }
                        ShowWarning("ViveTool ִ�г�ʱ������ֹ��");
                    }
                }

                return proc;
            }
            catch (System.ComponentModel.Win32Exception ex) when ((uint)ex.NativeErrorCode == 1223) // ERROR_CANCELLED
            {
                ShowWarning("��ȡ������Ա��Ȩ��");
                return null;
            }
            catch (Exception ex)
            {
                ShowError("���� ViveTool ʧ�ܣ�" + ex.Message);
                return null;
            }
        }

        // === �������Թ���ԱȨ��ִ�в������д����ʱ�ļ����ȡ���ʺ��� UI ����ʾ���������
        public async Task<string?> RunViveToolElevatedAndReadOutputAsync(string args, int timeoutMs = 120000)
        {
            try
            {
                EnsureViveToolInstalled();
                var exePath = Path.Combine(GetWritableLocalViveToolFolderPath(), "ViveTool.exe");
                if (!File.Exists(exePath))
                {
                    ShowError("ViveTool �ں�δ�ҵ����޷�ִ�����");
                    return null;
                }

                var tmpOut = Path.Combine(Path.GetTempPath(), $"vivetool_out_{Guid.NewGuid():N}.txt");

                // ʹ�� cmd.exe /c "vivetool ... > tmpOut 2>&1" ��������ض����ļ�
                var cmdArgs = $"/c \"\\\"{exePath}\\\" {args} > \\\"{tmpOut}\\\" 2>&1\"";

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = cmdArgs,
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                var proc = Process.Start(psi);
                if (proc == null) return null;

                if (!proc.WaitForExit(timeoutMs))
                {
                    try { proc.Kill(); } catch { }
                    ShowWarning("ViveTool ִ�г�ʱ��");
                }

                if (File.Exists(tmpOut))
                {
                    var txt = await File.ReadAllTextAsync(tmpOut);
                    try { File.Delete(tmpOut); } catch { }
                    return txt;
                }

                return null;
            }
            catch (System.ComponentModel.Win32Exception ex) when ((uint)ex.NativeErrorCode == 1223)
            {
                ShowWarning("��ȡ������Ա��Ȩ��");
                return null;
            }
            catch (Exception ex)
            {
                ShowError("ִ��ʧ�ܣ�" + ex.Message);
                return null;
            }
        }

        #endregion

        #region ��������������
        public void ApplyBackdropFromSettings()
        {
            var s = ApplicationData.Current.LocalSettings.Values;
            var material = (s["BackgroundMaterial"] as string) ?? "Mica";

            // First apply theme so controls update colors before backdrop changes
            var theme = (s["AppTheme"] as string) ?? "System";
            if (theme == "Light") (Content as FrameworkElement)!.RequestedTheme = ElementTheme.Light;
            else if (theme == "Dark") (Content as FrameworkElement)!.RequestedTheme = ElementTheme.Dark;
            else (Content as FrameworkElement)!.RequestedTheme = ElementTheme.Default;

            UpdateTitleBarButtonsForTheme((Content as FrameworkElement)!.RequestedTheme);

            // Only change SystemBackdrop if needed to avoid unnecessary recreations/flicker
            try
            {
                var current = this.SystemBackdrop;

                bool needsNewBackdrop = false;
                dynamic? newBackdrop = null;

                // Check if we need a new backdrop type
                if (material == "Mica")
                {
                    if (current is not MicaBackdrop mb || mb.Kind != MicaKind.Base)
                    {
                        needsNewBackdrop = true;
                        newBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
                    }
                }
                else if (material == "MicaAlt")
                {
                    if (current is not MicaBackdrop mb || mb.Kind != MicaKind.BaseAlt)
                    {
                        needsNewBackdrop = true;
                        newBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
                    }
                }
                else if (material == "Acrylic")
                {
                    if (current is not DesktopAcrylicBackdrop)
                    {
                        needsNewBackdrop = true;
                        newBackdrop = new DesktopAcrylicBackdrop();
                    }
                }
                else if (current != null)
                {
                    // clear backdrop if not recognized
                    this.SystemBackdrop = null;
                }

                // Apply new backdrop if needed
                if (needsNewBackdrop && newBackdrop != null)
                {
                    // Detach old backdrop first
                    this.SystemBackdrop = null;

                    // Small delay to let the old backdrop detach, then apply new backdrop on UI thread
                    DispatcherQueue.TryEnqueue(async () =>
                    {
                        await Task.Delay(10);
                        try { this.SystemBackdrop = newBackdrop; } catch { }
                    });
                }
            }
            catch { }
        }

        public void UpdateTitleBarButtonsForTheme(ElementTheme theme)
        {
            var tb = _appWindow.TitleBar;
            tb.ExtendsContentIntoTitleBar = true;

            Windows.UI.Color fg = theme switch
            {
                ElementTheme.Light => Microsoft.UI.Colors.Black,
                ElementTheme.Dark => Microsoft.UI.Colors.White,
                _ => ((SolidColorBrush)Application.Current.Resources["TextFillColorPrimaryBrush"]).Color
            };

            tb.ButtonForegroundColor = fg;
            tb.ButtonInactiveForegroundColor = fg;

            if (theme == ElementTheme.Light)
            {
                tb.ButtonHoverBackgroundColor = ColorHelper.FromArgb(24, 0, 0, 0);
                tb.ButtonPressedBackgroundColor = ColorHelper.FromArgb(48, 0, 0, 0);
            }
            else
            {
                tb.ButtonHoverBackgroundColor = ColorHelper.FromArgb(32, 255, 255, 255);
                tb.ButtonPressedBackgroundColor = ColorHelper.FromArgb(64, 255, 255, 255);
            }

            tb.BackgroundColor = Microsoft.UI.Colors.Transparent;
            tb.InactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            tb.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            tb.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        }
        #endregion

        #region �������϶�����
        private void EnsureDragRectsOnce()
        {
            if (AppTitleBar == null) return;
            if (AppTitleBar.ActualWidth <= 0 || AppTitleBar.ActualHeight <= 0)
            {
                AppTitleBar.Loaded += (_, __) => UpdateTitleBarDragRegions();
                return;
            }
            UpdateTitleBarDragRegions();
        }

        public void UpdateTitleBarDragRegions()
        {
            if (_pendingDragUpdate) return;
            _pendingDragUpdate = true;

            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                _pendingDragUpdate = false;
                var tb = _appWindow.TitleBar;
                if (tb == null) return;

                try
                {
                    double scale = RootLayout?.XamlRoot?.RasterizationScale ?? 1.0;
                    int windowWidthPx = (int)Math.Round(Bounds.Width * scale);
                    int barHeightPx = tb.Height;
                    int leftInsetPx = tb.LeftInset;
                    int rightInsetPx = tb.RightInset;

                    if (barHeightPx <= 0 || windowWidthPx <= 0) { SetFullDragRectangles(); return; }

                    var excludeRanges = new List<(int L, int R)>();

                    void TryAddExclude(FrameworkElement fe)
                    {
                        if (fe == null || fe.Visibility != Visibility.Visible || fe.ActualWidth < 1) return;
                        if (fe.XamlRoot?.Content == null) return;
                        GeneralTransform t = fe.TransformToVisual(null);
                        var origin = t.TransformPoint(new Windows.Foundation.Point(0, 0));
                        int l = Math.Clamp((int)Math.Round(origin.X * scale), 0, windowWidthPx);
                        int r = Math.Clamp((int)Math.Round((origin.X + fe.ActualWidth) * scale), 0, windowWidthPx);
                        if (r > l) excludeRanges.Add((l, r));
                    }

                    TryAddExclude(AppTitleBar?.FindName("SearchBox") as FrameworkElement);
                    TryAddExclude(AppTitleBar?.FindName("AvatarButton") as FrameworkElement);

                    if (excludeRanges.Count == 0) { SetFullDragRectangles(); return; }

                    excludeRanges.Sort((a, b) => a.L.CompareTo(b.L));
                    var merged = new List<(int L, int R)>();
                    foreach (var range in excludeRanges)
                    {
                        if (merged.Count == 0 || range.L > merged[^1].R) merged.Add(range);
                        else merged[^1] = (merged[^1].L, Math.Max(merged[^1].R, range.R));
                    }

                    var rects = new List<Windows.Graphics.RectInt32>();
                    int currentX = leftInsetPx;
                    foreach (var (L, R) in merged)
                    {
                        if (L > currentX) rects.Add(new Windows.Graphics.RectInt32(currentX, 0, L - currentX, barHeightPx));
                        currentX = Math.Max(currentX, R);
                    }
                    if (currentX < windowWidthPx - rightInsetPx) rects.Add(new Windows.Graphics.RectInt32(currentX, 0, windowWidthPx - rightInsetPx - currentX, barHeightPx));

                    tb.SetDragRectangles(rects.ToArray());
                }
                catch (ObjectDisposedException) { System.Diagnostics.Debug.WriteLine("[TitleBar] ���ͷ�"); }
            });
        }

        private void SetFullDragRectangles()
        {
            var tb = _appWindow.TitleBar;
            if (tb == null) return;

            double scale = RootLayout?.XamlRoot?.RasterizationScale ?? 1.0;
            int windowWidthPx = (int)Math.Round(Bounds.Width * scale);
            int barHeightPx = tb.Height;
            int leftInsetPx = tb.LeftInset;
            int rightInsetPx = tb.RightInset;

            int x = leftInsetPx;
            int w = Math.Max(0, windowWidthPx - leftInsetPx - rightInsetPx);
            if (w <= 0) return;

            try { tb.SetDragRectangles(new[] { new Windows.Graphics.RectInt32(x, 0, w, barHeightPx) }); }
            catch { }
        }
        #endregion

        #region InfoBar / Toast Ǩ������һ��Ӧ�� (����ʵ��)
        public void ShowToast(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                if (TopInfoBar == null) return;

                if (TopInfoBar.IsOpen)
                {
                    var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                    var slideOut = new DoubleAnimation
                    {
                        To = -Math.Max(80, TopInfoBar.ActualHeight + 4),
                        Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                    };

                    var sbOut = new Storyboard();
                    sbOut.Children.Add(slideOut);
                    Storyboard.SetTarget(slideOut, TopInfoBar.RenderTransform);
                    Storyboard.SetTargetProperty(slideOut, "Y");
                    sbOut.Completed += (_, __) =>
                    {
                        try { TopInfoBar.IsOpen = false; } catch { }
                        tcs.TrySetResult(true);
                    };

                    try { sbOut.Begin(); } catch { tcs.TrySetResult(true); }

                    await tcs.Task;
                    await Task.Delay(50);
                }

                TopInfoBar.Severity = severity switch
                {
                    InfoBarSeverity.Informational => Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational,
                    InfoBarSeverity.Success => Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success,
                    InfoBarSeverity.Warning => Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning,
                    InfoBarSeverity.Error => Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error,
                    _ => Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational
                };
                TopInfoBar.Message = message;
                try { TopInfoBar.IsOpen = true; } catch { }

                var slideIn = new DoubleAnimation
                {
                    To = 0,
                    Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                var sbIn = new Storyboard();
                sbIn.Children.Add(slideIn);
                Storyboard.SetTarget(slideIn, TopInfoBar.RenderTransform);
                Storyboard.SetTargetProperty(slideIn, "Y");
                try { sbIn.Begin(); } catch { }

                _infoTimer?.Stop();
                _infoTimer?.Start();
            });
        }

        public void HideToast()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (TopInfoBar == null || !TopInfoBar.IsOpen) return;

                var slideOut = new DoubleAnimation
                {
                    To = -Math.Max(80, TopInfoBar.ActualHeight + 4),
                    Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                };
                var sb = new Storyboard();
                sb.Children.Add(slideOut);
                Storyboard.SetTarget(slideOut, TopInfoBar.RenderTransform);
                Storyboard.SetTargetProperty(slideOut, "Y");
                sb.Completed += (_, __) =>
                {
                    try { TopInfoBar.IsOpen = false; } catch { }
                };
                try { sb.Begin(); } catch { TopInfoBar.IsOpen = false; }
            });
        }

        public void ShowSuccess(string message) => ShowToast(message, InfoBarSeverity.Success);
        public void ShowWarning(string message) => ShowToast(message, InfoBarSeverity.Warning);
        public void ShowError(string message) => ShowToast(message, InfoBarSeverity.Error);
        public void ShowInfo(string message) => ShowToast(message, InfoBarSeverity.Informational);
        #endregion

        #region Dispatcher ���������������������첽 UI ���ã�
        private Task DispatcherQueueInvokeAsync(Func<Task> uiFunc)
        {
            if (DispatcherQueue.HasThreadAccess)
            {
                return uiFunc();
            }

            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool queued = DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await uiFunc().ConfigureAwait(false);
                    tcs.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            if (!queued) tcs.TrySetException(new InvalidOperationException("�޷����������� UI DispatcherQueue��"));
            return tcs.Task;
        }
        #endregion

        #region �� Splash ��ʾ/���أ����ǲ㷽����ʹ�ð���Դ�Զ�������
        // ��ʾ�� Splash������λ�ã�App.OnLaunched �
        public void ShowSplashOverlay()
        {
            SplashOverlay.Visibility = Visibility.Visible;
            SplashOverlay.Opacity = 1;
            SplashOverlay.HorizontalAlignment = HorizontalAlignment.Stretch;
            SplashOverlay.VerticalAlignment = VerticalAlignment.Stretch;

            SplashOverlay.Background = (Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"];

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            uint dpi = GetDpiForWindow(hwnd);
            string scaleSuffix = dpi >= 288 ? "400" : dpi >= 192 ? "200" : "100";
            string imagePath = $"Assets/SplashScreen.scale-{scaleSuffix}.png";

            SplashImage.Stretch = Stretch.Uniform; // ��֤������ʾ
            SplashImage.Source = new BitmapImage(new Uri($"ms-appx:///{imagePath}"));
        }

        // ���� Splash��������������
        public void HideSplashOverlay()
        {
            var visual = ElementCompositionPreview.GetElementVisual(SplashOverlay);
            var compositor = visual.Compositor;

            var fadeAnimation = compositor.CreateScalarKeyFrameAnimation();
            fadeAnimation.InsertKeyFrame(1f, 0f);
            fadeAnimation.Duration = TimeSpan.FromMilliseconds(250);

            var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            visual.StartAnimation(nameof(visual.Opacity), fadeAnimation);
            batch.End();

            batch.Completed += (s, e) =>
            {
                SplashOverlay.Visibility = Visibility.Collapsed;
                visual.Opacity = 1f;
            };
        }
        #endregion
    }

    internal static class FrameworkElementExtensions
    {
        public static T As<T>(this object obj) where T : class => (T)obj;
    }
}