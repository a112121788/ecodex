using System.IO;
using System.Text.Json;
using System.Windows;
using ECodex.Core.IPC;
using ECodex.Core.IPC.V2;
using ECodex.Core.Services;
using ECodex.Services;
using ECodex.Updater;
using ECodex.Views;

namespace ECodex;

/// <summary>应用程序入口，初始化全局服务、守护进程连接和命名管道</summary>
public partial class App : Application
{
    private const string MainInstanceMutexName = @"Global\ECodexMainApp";
    private static TrayIconService? TrayIcon;
    private Mutex? _mainInstanceMutex;
    private NamedPipeServer? _pipeServer;

    public static NotificationService NotificationService { get; } = new();
    public static NamedPipeServer? PipeServer { get; private set; }
    public static SnippetService SnippetService { get; } = new();
    public static CommandLogService CommandLogService { get; } = new();
    public static DaemonClient DaemonClient { get; } = new();
    public static WindowManagerService<MainWindow> WindowManager { get; } = new();
    public static Task<UpdateCheckResult?> UpdateCheckTask { get; private set; } = Task.FromResult<UpdateCheckResult?>(null);
    public static WindowApiService<MainWindow> WindowApi { get; } = new(
        WindowManager,
        title => string.IsNullOrWhiteSpace(title) ? new MainWindow() : new MainWindow { Title = title.Trim() },
        window => window.RestoreFromTray(),
        window => window.RestoreFromTray(),
        window => window.Close());
    public static Task<bool> DaemonConnectTask { get; private set; } = Task.FromResult(false);
    public static bool IsExplicitShutdownRequested { get; private set; }
    public static bool PreserveDaemonSessionsOnExplicitShutdown { get; private set; }

    public static void RequestShutdown(int exitCode = 0)
    {
        IsExplicitShutdownRequested = true;
        var app = Current;
        if (app?.Dispatcher == null)
            return;

        if (app.Dispatcher.CheckAccess())
        {
            app.Shutdown(exitCode);
            return;
        }

        app.Dispatcher.BeginInvoke((Action)(() => app.Shutdown(exitCode)));
    }

    public static void RequestShutdownPreservingDaemonSessions(int exitCode = 0)
    {
        PreserveDaemonSessionsOnExplicitShutdown = true;
        RequestShutdown(exitCode);
    }

    public static async Task RequestShutdownAfterTerminatingDaemonSessionsAsync(int exitCode = 0)
    {
        try
        {
            var result = await DaemonSessionTerminator.TerminateAllAsync(DaemonClient).ConfigureAwait(false);
            DaemonLog($"[Tray] Exit and terminate requested; terminated {result.Terminated}/{result.Requested} daemon sessions");
            if (result.Terminated < result.Requested)
                DaemonLog($"[Tray] Exit and terminate incomplete; {result.Requested - result.Terminated} daemon sessions were not terminated");
        }
        catch (Exception ex)
        {
            DaemonLog($"[Tray] Exit and terminate failed: {ex.Message}");
        }

        RequestShutdownPreservingDaemonSessions(exitCode);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        _mainInstanceMutex = new Mutex(true, MainInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            _mainInstanceMutex.Dispose();
            _mainInstanceMutex = null;
            TryActivateExistingInstance();
            Shutdown(0);
            return;
        }

        base.OnStartup(e);

        // 添加全局异常处理器以便诊断崩溃问题
        DispatcherUnhandledException += (s, args) =>
        {
            System.Diagnostics.Debug.WriteLine($"[CRASH] DispatcherUnhandledException: {args.Exception}");
            System.Windows.MessageBox.Show($"意外错误：{args.Exception.Message}\n\n{args.Exception.StackTrace}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            System.Diagnostics.Debug.WriteLine($"[CRASH] UnhandledException: {ex}");
            System.Windows.MessageBox.Show($"严重错误：{ex?.Message}\n\n{ex?.StackTrace}", "严重错误", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        InitializeTrayIcon();
        SeedDefaultSkills();
        InstallDefaultPowerShellHook();

        // 启动用于 CLI 通信的命名管道服务器
        _pipeServer = new NamedPipeServer();
        PipeServer = _pipeServer;
        _pipeServer.Start();
        // 兼容期：额外监听旧管道名（cmux / cmux-{tag}）
        _pipeServer.StartLegacyListener();

        // 守护进程连接：先尝试已有守护进程，必要时再启动一个。
        // 会话在决定本地模式还是守护进程模式之前会等待此任务。
        DaemonConnectTask = Task.Run(() =>
        {
            DaemonLog("[App] Phase 1: Quick daemon check (300ms)...");
            if (DaemonClient.TryConnect(300))
            {
                DaemonLog("[App] Phase 1: Daemon connected!");
                DaemonClient.RaiseConnected();
                return true;
            }
            DaemonLog("[App] Phase 1: Daemon not available, starting daemon...");

            var connected = DaemonClient.StartDaemonAndConnect();
            DaemonLog(connected
                ? "[App] Phase 2: Daemon started and connected"
                : "[App] Phase 2: Daemon failed to start");
            if (connected) DaemonClient.RaiseConnected();
            return connected;
        });

        StartUpdateCheck();

        // 接入 Windows Toast 通知
        NotificationService.NotificationAdded += notification =>
        {
            // 仅当应用窗口未获得焦点时才显示 Toast
            var mainWindow = Current.MainWindow;
            if (mainWindow != null && !mainWindow.IsActive)
            {
                var workspaceName = "终端"; // 将由 MainViewModel 补充完善
                Services.ToastNotificationHelper.ShowToast(notification, workspaceName);
            }
        };
    }

    private static void SeedDefaultSkills()
    {
        try
        {
            var sourceDirectory = Path.Combine(AppContext.BaseDirectory, DefaultSkillSeedService.BundledSkillsDirectoryName);
            var targetDirectory = DefaultSkillSeedService.GetDefaultTargetDirectory();
            var result = new DefaultSkillSeedService().Seed(sourceDirectory, targetDirectory);
            if (result.CopiedSkills.Count > 0 || result.SkippedSkills.Count > 0 || result.Errors.Count > 0)
            {
                DaemonLog($"[Skills] seed default skills copied={result.CopiedSkills.Count} skipped={result.SkippedSkills.Count} errors={result.Errors.Count}");
            }
        }
        catch (Exception ex)
        {
            DaemonLog($"[Skills] seed default skills failed: {ex.Message}");
        }
    }

    private static void InitializeTrayIcon()
    {
        try
        {
            TrayIcon = new TrayIconService(RestoreMainWindowFromTray);
        }
        catch (Exception ex)
        {
            DaemonLog($"[Tray] initialize failed: {ex.Message}");
        }
    }

    private static void InstallDefaultPowerShellHook()
    {
        try
        {
            var result = new PowerShellHookSetupService().Install(new PowerShellHookInstallOptions(
                PowerShellHookSetupService.GetDefaultPowerShellProfilePath(),
                PowerShellHookSetupService.GetDefaultBackupDirectory(),
                AppContext.BaseDirectory));
            if (result.Changed || result.Status == PowerShellHookSetupStatus.Conflict)
            {
                DaemonLog($"[Hook] PowerShell setup status={result.Status} changed={result.Changed} backup={result.BackupPath ?? "none"}");
            }
        }
        catch (Exception ex)
        {
            DaemonLog($"[Hook] PowerShell setup failed: {ex.Message}");
        }
    }

    private static void RestoreMainWindowFromTray()
    {
        var dispatcher = Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted)
            return;

        if (dispatcher.CheckAccess())
        {
            RestoreMainWindowFromTrayCore();
            return;
        }

        dispatcher.BeginInvoke(RestoreMainWindowFromTrayCore);
    }

    private static void RestoreMainWindowFromTrayCore()
    {
        if (Current.MainWindow is MainWindow mainWindow)
        {
            mainWindow.RestoreFromTray();
            return;
        }

        if (Current.MainWindow is { } window)
        {
            window.ShowInTaskbar = true;
            window.Show();
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;
            window.Activate();
        }
    }

    private static void StartUpdateCheck()
    {
        var feedUrl = Environment.GetEnvironmentVariable("ECODEX_UPDATE_FEED_URL");
        if (string.IsNullOrWhiteSpace(feedUrl) || !Uri.TryCreate(feedUrl, UriKind.Absolute, out var feedUri))
        {
            UpdateCheckTask = Task.FromResult<UpdateCheckResult?>(null);
            return;
        }

        var currentVersion = VersionService.GetInformationalVersion(typeof(App).Assembly);
        UpdateCheckTask = Task.Run<UpdateCheckResult?>(async () =>
        {
            var result = await new VelopackFeedChecker().CheckAsync(feedUri, currentVersion);
            if (result.UpdateAvailable)
                DaemonLog($"[Update] New version {result.LatestVersion} available from {result.FeedUrl}");
            else if (!string.IsNullOrWhiteSpace(result.Error))
                DaemonLog($"[Update] Check failed: {result.Error}");

            return result;
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        TrayIcon?.Dispose();
        TrayIcon = null;
        _pipeServer?.Dispose();
        DaemonClient.Dispose();
        try
        {
            _mainInstanceMutex?.ReleaseMutex();
        }
        catch
        {
            // 退出路径不应因 mutex 状态影响应用关闭。
        }
        finally
        {
            _mainInstanceMutex?.Dispose();
            _mainInstanceMutex = null;
        }
        base.OnExit(e);
    }

    private static void TryActivateExistingInstance()
    {
        try
        {
            var request = new V2Request
            {
                Id = JsonSerializer.SerializeToElement("activate-existing-window"),
                Method = "window.focus",
                Params = JsonSerializer.SerializeToElement(new Dictionary<string, string> { { "target", "current" } }),
            };
            _ = NamedPipeClient.SendV2Request(request, timeoutMs: 1000).GetAwaiter().GetResult();
        }
        catch
        {
            // 如果既有实例还未完成 pipe 初始化，第二实例仍保持只退出不新开窗口。
        }
    }

    internal static void DaemonLog(string message) => DaemonClient.LogDaemon(message);
}
