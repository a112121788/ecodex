using System.IO;
using System.Windows;
using ECodeX.Core.IPC;
using ECodeX.Core.Services;
using ECodeX.Services;
using ECodeX.Updater;
using ECodeX.Views;

namespace ECodeX;

/// <summary>应用程序入口，初始化全局服务、守护进程连接和命名管道</summary>
public partial class App : Application
{
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
        window =>
        {
            if (!window.IsVisible)
                window.Show();
            window.Activate();
        },
        window => window.Activate(),
        window => window.Close());
    public static Task<bool> DaemonConnectTask { get; private set; } = Task.FromResult(false);

    protected override void OnStartup(StartupEventArgs e)
    {
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
        _pipeServer?.Dispose();
        DaemonClient.Dispose();
        base.OnExit(e);
    }

    internal static void DaemonLog(string message) => DaemonClient.LogDaemon(message);
}
