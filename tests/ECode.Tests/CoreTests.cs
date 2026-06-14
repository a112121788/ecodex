using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.Json;
using ECode.Core.IPC;
using ECode.Core.IPC.V2;
using ECode.Core.Models;
using ECode.Core.Services;
using ECode.Core.Terminal;
using FluentAssertions;
using Xunit;

namespace ECode.Tests;

/// <summary>
/// 命令日志脱敏测试 - 验证敏感信息（密钥、密码、Token）在存储前被正确脱敏
/// </summary>
public class CommandLogSanitizationTests
{
    [Theory]
    [InlineData("OPENAI_API_KEY=sk-test dotnet test", "OPENAI_API_KEY=[REDACTED] dotnet test")]
    [InlineData("export GITHUB_TOKEN=ghp_123456", "export GITHUB_TOKEN=[REDACTED]")]
    [InlineData("PASSWORD='hunter2' npm run deploy", "PASSWORD=[REDACTED] npm run deploy")]
    [InlineData("curl --api-key secret-value https://example.com", "curl --api-key [REDACTED] https://example.com")]
    [InlineData("tool --token=abc123 --name demo", "tool --token=[REDACTED] --name demo")]
    [InlineData("git clone https://user:pass@example.com/repo.git", "git clone https://user:[REDACTED]@example.com/repo.git")]
    public void SanitizeCommandForStorage_RedactsKnownSecretPatterns(string command, string expected)
    {
        var service = new CommandLogService();

        var sanitized = service.SanitizeCommandForStorage(command);

        sanitized.Should().Be(expected);
    }

    [Theory]
    [InlineData("dotnet test tests/ECode.Tests/ECode.Tests.csproj")]
    [InlineData("git status -sb")]
    [InlineData("npm run build -- --mode production")]
    [InlineData("curl https://example.com/api?tokenized=false")]
    public void SanitizeCommandForStorage_PreservesNonSecretCommands(string command)
    {
        var service = new CommandLogService();

        var sanitized = service.SanitizeCommandForStorage(command);

        sanitized.Should().Be(command);
    }

    [Theory]
    [InlineData("password123!")]
    [InlineData("my-token-value")]
    [InlineData("SECRET_VALUE")]
    public void SanitizeCommandForStorage_DropsLikelyStandaloneSecretInput(string command)
    {
        var service = new CommandLogService();

        var sanitized = service.SanitizeCommandForStorage(command);

        sanitized.Should().BeNull();
    }
}

public class ShellPathQuoterTests
{
    [Theory]
    [InlineData(@"C:\work\file.txt", @"C:\work\file.txt")]
    [InlineData(@"C:\work dir\file.txt", @"""C:\work dir\file.txt""")]
    [InlineData("/tmp/with space/file.txt", "\"/tmp/with space/file.txt\"")]
    [InlineData("path-with-\"quote\".txt", "\"path-with-\\\"quote\\\".txt\"")]
    public void QuotePathForShell_QuotesOnlyWhenNeeded(string path, string expected)
    {
        ShellPathQuoter.QuotePathForShell(path).Should().Be(expected);
    }

    [Fact]
    public void JoinQuotedPaths_QuotesAndSkipsBlankPaths()
    {
        var paths = new[] { @"C:\plain.txt", "", @"C:\with space\image.png", "   " };

        ShellPathQuoter.JoinQuotedPaths(paths)
            .Should().Be(@"C:\plain.txt ""C:\with space\image.png""");
    }
}

/// <summary>
/// 通知服务测试 - 验证已读/未读状态管理、排序逻辑和按作用域过滤
/// </summary>
public class NotificationServiceTests
{
    [Fact]
    public void MarkAsRead_KeepsUnreadFirstThenNewestRead()
    {
        var service = new NotificationService();
        var unreadOld = CreateNotification("unread-old", isRead: false, timestamp: new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc));
        var readNewest = CreateNotification("read-newest", isRead: true, timestamp: new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        var unreadMiddle = CreateNotification("unread-middle", isRead: false, timestamp: new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc));
        service.Notifications.Add(readNewest);
        service.Notifications.Add(unreadOld);
        service.Notifications.Add(unreadMiddle);

        service.MarkAsRead(unreadMiddle.Id);

        service.Notifications.Select(n => n.Id)
            .Should().Equal("unread-old", "read-newest", "unread-middle");
        service.UnreadCount.Should().Be(1);
    }

    [Fact]
    public void MarkAsUnread_MovesNewestUnreadBeforeOlderUnread()
    {
        var service = new NotificationService();
        var unreadOld = CreateNotification("unread-old", isRead: false, timestamp: new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc));
        var readNewest = CreateNotification("read-newest", isRead: true, timestamp: new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        service.Notifications.Add(unreadOld);
        service.Notifications.Add(readNewest);

        service.MarkAsUnread(readNewest.Id);

        service.Notifications.Select(n => n.Id)
            .Should().Equal("read-newest", "unread-old");
        service.UnreadCount.Should().Be(2);
    }

    [Fact]
    public void GetUnreadCount_ForSurface_FiltersByWorkspaceAndSurface()
    {
        var service = new NotificationService();
        service.Notifications.Add(CreateNotification("target-1", isRead: false, timestamp: DateTime.UtcNow, workspaceId: "workspace-1", surfaceId: "surface-1"));
        service.Notifications.Add(CreateNotification("target-read", isRead: true, timestamp: DateTime.UtcNow, workspaceId: "workspace-1", surfaceId: "surface-1"));
        service.Notifications.Add(CreateNotification("other-surface", isRead: false, timestamp: DateTime.UtcNow, workspaceId: "workspace-1", surfaceId: "surface-2"));
        service.Notifications.Add(CreateNotification("other-workspace", isRead: false, timestamp: DateTime.UtcNow, workspaceId: "workspace-2", surfaceId: "surface-1"));

        var count = service.GetUnreadCount("workspace-1", "surface-1");

        count.Should().Be(1);
    }

    [Fact]
    public void GetUnreadCount_ForPane_FiltersByWorkspaceSurfaceAndPane()
    {
        var service = new NotificationService();
        service.Notifications.Add(CreateNotification("target-1", isRead: false, timestamp: DateTime.UtcNow, workspaceId: "workspace-1", surfaceId: "surface-1", paneId: "pane-1"));
        service.Notifications.Add(CreateNotification("target-read", isRead: true, timestamp: DateTime.UtcNow, workspaceId: "workspace-1", surfaceId: "surface-1", paneId: "pane-1"));
        service.Notifications.Add(CreateNotification("other-pane", isRead: false, timestamp: DateTime.UtcNow, workspaceId: "workspace-1", surfaceId: "surface-1", paneId: "pane-2"));
        service.Notifications.Add(CreateNotification("other-surface", isRead: false, timestamp: DateTime.UtcNow, workspaceId: "workspace-1", surfaceId: "surface-2", paneId: "pane-1"));

        var count = service.GetUnreadCount("workspace-1", "surface-1", "pane-1");

        count.Should().Be(1);
    }

    [Fact]
    public void GetLatestText_ReturnsNewestUnreadForWorkspace()
    {
        var service = new NotificationService();
        service.Notifications.Add(CreateNotification("newest-read", isRead: true, timestamp: new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc), workspaceId: "workspace-1", body: "read"));
        service.Notifications.Add(CreateNotification("older-unread", isRead: false, timestamp: new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), workspaceId: "workspace-1", body: "older unread"));
        service.Notifications.Add(CreateNotification("newest-unread", isRead: false, timestamp: new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc), workspaceId: "workspace-1", body: "newest unread"));
        service.Notifications.Add(CreateNotification("other-workspace", isRead: false, timestamp: new DateTime(2026, 1, 1, 13, 0, 0, DateTimeKind.Utc), workspaceId: "workspace-2", body: "other"));

        service.GetLatestText("workspace-1").Should().Be("newest unread");

        service.MarkWorkspaceAsRead("workspace-1");

        service.GetLatestText("workspace-1").Should().BeNull();
    }

    private static TerminalNotification CreateNotification(
        string id,
        bool isRead,
        DateTime timestamp,
        string workspaceId = "workspace-1",
        string surfaceId = "surface-1",
        string paneId = "pane-1",
        string body = "body") => new()
    {
        Id = id,
        WorkspaceId = workspaceId,
        SurfaceId = surfaceId,
        PaneId = paneId,
        IsRead = isRead,
        Title = id,
        Body = body,
        Timestamp = timestamp,
        Source = NotificationSource.Cli,
    };
}

/// <summary>
/// Daemon 消息序列化测试 - 验证 IPC 消息（请求/响应/事件）的 JSON 序列化和反序列化
/// </summary>
public class DaemonMessageRoundTripTests
{
    [Fact]
    public void DaemonRequest_RoundTripsAllPublicFields()
    {
        var request = new DaemonRequest
        {
            Type = DaemonMessageTypes.SessionCreate,
            PaneId = "pane-1",
            Cols = 120,
            Rows = 40,
            WorkspaceId = "workspace-1",
            WorkingDirectory = @"C:\repo",
            Command = "pwsh",
            Data = "hello",
        };

        var roundTripped = RoundTrip<DaemonRequest>(request);

        roundTripped.Type.Should().Be(DaemonMessageTypes.SessionCreate);
        roundTripped.PaneId.Should().Be("pane-1");
        roundTripped.Cols.Should().Be(120);
        roundTripped.Rows.Should().Be(40);
        roundTripped.WorkspaceId.Should().Be("workspace-1");
        roundTripped.WorkingDirectory.Should().Be(@"C:\repo");
        roundTripped.Command.Should().Be("pwsh");
        roundTripped.Data.Should().Be("hello");
    }

    [Fact]
    public void DaemonResponse_RoundTripsSuccessErrorAndData()
    {
        var response = new DaemonResponse
        {
            Success = false,
            Error = "boom",
            Data = "{\"ok\":false}",
        };

        var roundTripped = RoundTrip<DaemonResponse>(response);

        roundTripped.Success.Should().BeFalse();
        roundTripped.Error.Should().Be("boom");
        roundTripped.Data.Should().Be("{\"ok\":false}");
    }

    [Fact]
    public void DaemonSessionInfo_RoundTripsAttachMetadata()
    {
        var session = new DaemonSessionInfo
        {
            PaneId = "pane-2",
            Cols = 100,
            Rows = 30,
            WorkingDirectory = @"D:\work",
            Title = "build",
            IsRunning = true,
            IsExisting = true,
        };

        var roundTripped = RoundTrip<DaemonSessionInfo>(session);

        roundTripped.PaneId.Should().Be("pane-2");
        roundTripped.Cols.Should().Be(100);
        roundTripped.Rows.Should().Be(30);
        roundTripped.WorkingDirectory.Should().Be(@"D:\work");
        roundTripped.Title.Should().Be("build");
        roundTripped.IsRunning.Should().BeTrue();
        roundTripped.IsExisting.Should().BeTrue();
    }

    [Fact]
    public void DaemonEvent_RoundTripsEventPayload()
    {
        var evt = new DaemonEvent
        {
            Type = DaemonMessageTypes.EventOutput,
            PaneId = "pane-3",
            Data = "output text",
        };

        var roundTripped = RoundTrip<DaemonEvent>(evt);

        roundTripped.Type.Should().Be(DaemonMessageTypes.EventOutput);
        roundTripped.PaneId.Should().Be("pane-3");
        roundTripped.Data.Should().Be("output text");
    }

    private static T RoundTrip<T>(T value)
    {
        var json = JsonSerializer.Serialize(value);
        var roundTripped = JsonSerializer.Deserialize<T>(json);
        roundTripped.Should().NotBeNull();
        return roundTripped!;
    }
}

public class V2ProtocolTests
{
    [Fact]
    public void ParseRequest_AcceptsEcodeV2Request()
    {
        var parsed = V2Protocol.ParseRequest("""
            {
              "protocol": "ecode.v2",
              "id": "req-1",
              "method": "browser.snapshot",
              "params": {
                "surfaceRef": "surface:1"
              }
            }
            """);

        parsed.Success.Should().BeTrue();
        parsed.Request.Should().NotBeNull();
        parsed.Request!.Protocol.Should().Be(V2Protocol.ProtocolName);
        parsed.Request.Method.Should().Be("browser.snapshot");
        parsed.Request.Id!.Value.GetString().Should().Be("req-1");
        parsed.Request.Params!.Value.GetProperty("surfaceRef").GetString().Should().Be("surface:1");
    }

    [Fact]
    public void ParseRequest_RejectsUnsupportedProtocol()
    {
        var parsed = V2Protocol.ParseRequest("""
            {"protocol":"other","id":"req-2","method":"status"}
            """);

        parsed.Success.Should().BeFalse();
        parsed.ErrorResponse.Should().NotBeNull();
        parsed.ErrorResponse!.Protocol.Should().Be(V2Protocol.ProtocolName);
        parsed.ErrorResponse.Id!.Value.GetString().Should().Be("req-2");
        parsed.ErrorResponse.Error!.Code.Should().Be("invalid_request");
    }

    [Theory]
    [InlineData(V2ErrorCodes.InvalidRef, "invalid_ref")]
    [InlineData(V2ErrorCodes.NotFound, "not_found")]
    [InlineData(V2ErrorCodes.StaleRef, "stale_ref")]
    [InlineData(V2ErrorCodes.NotSupported, "not_supported")]
    [InlineData(V2ErrorCodes.Timeout, "timeout")]
    [InlineData(V2ErrorCodes.InternalError, "internal_error")]
    public void StableErrorCodes_MatchBrowserScriptingContract(string code, string expected)
    {
        code.Should().Be(expected);
        V2ErrorCodes.IsStable(code).Should().BeTrue();
        V2ErrorCodes.All.Should().ContainSingle(item => item == code);
    }

    [Theory]
    [InlineData(V2ErrorCodes.InvalidRef)]
    [InlineData(V2ErrorCodes.NotFound)]
    [InlineData(V2ErrorCodes.StaleRef)]
    [InlineData(V2ErrorCodes.NotSupported)]
    [InlineData(V2ErrorCodes.Timeout)]
    [InlineData(V2ErrorCodes.InternalError)]
    public void FromStableError_SerializesContractErrorShape(string code)
    {
        using var requestId = JsonDocument.Parse("\"req-stable\"");
        var id = requestId.RootElement.Clone();

        var response = V2Response.FromStableError(id, code, "contract error");
        var json = JsonSerializer.Serialize(response);
        using var serialized = JsonDocument.Parse(json);

        response.Protocol.Should().Be(V2Protocol.ProtocolName);
        response.Id!.Value.GetString().Should().Be("req-stable");
        response.Result.Should().BeNull();
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(code);
        response.Error.Message.Should().Be("contract error");
        serialized.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be(code);
    }

    [Fact]
    public void FromStableError_RejectsUnknownCode()
    {
        var act = () => V2Response.FromStableError(null, "invalid_request", "parse error");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("code");
    }
}

public class BrowserScriptingServiceTests
{
    [Fact]
    public void ResolveSurfaceRef_RoutesDirectBrowserSurfaceRef()
    {
        var surfaces = new List<BrowserScriptingSurfaceDescriptor>
        {
            CreateSurface("browser-1", SurfaceKind.Browser),
        };
        var service = new BrowserScriptingService(() => surfaces);

        var result = service.ResolveSurfaceRef(BrowserScriptingService.CreateSurfaceRef("browser-1"));

        result.Success.Should().BeTrue();
        result.Surface.Should().NotBeNull();
        result.Surface!.SurfaceId.Should().Be("browser-1");
        result.Error.Should().BeNull();
        result.Diagnostics.LiveSurfaceCount.Should().Be(1);
        result.Diagnostics.LiveBrowserSurfaceCount.Should().Be(1);
        result.Diagnostics.RegisteredRefCount.Should().Be(1);
    }

    [Fact]
    public void ResolveSurfaceRef_ReturnsStaleRefForTrackedSurfaceAfterRemoval()
    {
        var surfaces = new List<BrowserScriptingSurfaceDescriptor>
        {
            CreateSurface("browser-1", SurfaceKind.Browser),
        };
        var service = new BrowserScriptingService(() => surfaces);
        var surfaceRef = service.TrackSurface(surfaces[0]);

        surfaces.Clear();
        var result = service.ResolveSurfaceRef(surfaceRef);

        result.Success.Should().BeFalse();
        result.Surface.Should().BeNull();
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be(V2ErrorCodes.StaleRef);
        result.Diagnostics.SurfaceRef.Should().Be(surfaceRef);
        result.Diagnostics.LiveSurfaceCount.Should().Be(0);
        result.Diagnostics.RegisteredRefCount.Should().Be(1);
    }

    [Fact]
    public void ResolveSurfaceRef_ReturnsNotSupportedForTerminalSurface()
    {
        var surfaces = new List<BrowserScriptingSurfaceDescriptor>
        {
            CreateSurface("terminal-1", SurfaceKind.Terminal),
        };
        var service = new BrowserScriptingService(() => surfaces);

        var result = service.ResolveSurfaceRef(BrowserScriptingService.CreateSurfaceRef("terminal-1"));

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be(V2ErrorCodes.NotSupported);
        result.Diagnostics.LiveBrowserSurfaceCount.Should().Be(0);
    }

    [Fact]
    public void ResolveSurfaceRef_ReturnsInvalidRefForMalformedSurfaceRef()
    {
        var service = new BrowserScriptingService(() => []);

        var result = service.ResolveSurfaceRef("pane:1");

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be(V2ErrorCodes.InvalidRef);
    }

    [Fact]
    public void GetSnapshot_ReturnsRegisteredBrowserSnapshot()
    {
        var (service, surfaceRef) = CreateSnapshotService();

        var result = service.GetSnapshot(surfaceRef);

        result.Success.Should().BeTrue();
        result.Snapshot.Should().NotBeNull();
        result.Snapshot!.Root.Role.Should().Be("document");
        result.Error.Should().BeNull();
    }

    [Fact]
    public void FindByRole_MatchesRoleAndAccessibleName()
    {
        var (service, surfaceRef) = CreateSnapshotService();

        var result = service.FindByRole(surfaceRef, "button", "Save");

        result.Success.Should().BeTrue();
        result.Nodes.Should().ContainSingle();
        result.Nodes[0].NodeId.Should().Be("save");
    }

    [Fact]
    public void FindByText_MatchesVisibleTextContent()
    {
        var (service, surfaceRef) = CreateSnapshotService();

        var result = service.FindByText(surfaceRef, "welcome");

        result.Success.Should().BeTrue();
        result.Nodes.Should().ContainSingle();
        result.Nodes[0].NodeId.Should().Be("hero");
    }

    [Fact]
    public void FindByTestId_MatchesExactTestId()
    {
        var (service, surfaceRef) = CreateSnapshotService();

        var result = service.FindByTestId(surfaceRef, "email-input");

        result.Success.Should().BeTrue();
        result.Nodes.Should().ContainSingle();
        result.Nodes[0].Role.Should().Be("textbox");
    }

    [Fact]
    public void FindFirstLastNth_SelectFromNestedLocatorMatches()
    {
        var (service, surfaceRef) = CreateSnapshotService();
        var buttons = BrowserScriptingLocator.Role("button");

        var first = service.FindFirst(surfaceRef, buttons);
        var last = service.FindLast(surfaceRef, buttons);
        var second = service.FindNth(surfaceRef, buttons, 1);

        first.Success.Should().BeTrue();
        first.Nodes.Should().ContainSingle();
        first.Nodes[0].NodeId.Should().Be("save");
        last.Success.Should().BeTrue();
        last.Nodes.Should().ContainSingle();
        last.Nodes[0].NodeId.Should().Be("cancel");
        second.Success.Should().BeTrue();
        second.Nodes.Should().ContainSingle();
        second.Nodes[0].NodeId.Should().Be("cancel");
    }

    [Fact]
    public void Fill_ExecutesWithEmptyStringValue()
    {
        var (service, surfaceRef, requests) = CreateActionService();

        var result = service.Fill(surfaceRef, BrowserScriptingLocator.TestId("email-input"), "");

        result.Success.Should().BeTrue();
        requests.Should().ContainSingle();
        requests[0].Action.Should().Be(BrowserScriptingActionKind.Fill);
        requests[0].Node!.NodeId.Should().Be("email");
        requests[0].Value.Should().Be("");
    }

    [Fact]
    public void BrowserActions_DispatchExpectedActionRequests()
    {
        var (service, surfaceRef, requests) = CreateActionService();

        service.Click(surfaceRef, BrowserScriptingLocator.Role("button", "Save")).Success.Should().BeTrue();
        service.Hover(surfaceRef, BrowserScriptingLocator.Role("button", "Cancel")).Success.Should().BeTrue();
        service.Press(surfaceRef, BrowserScriptingLocator.TestId("email-input"), "Enter").Success.Should().BeTrue();
        service.Eval(surfaceRef, "document.title").Success.Should().BeTrue();
        service.Screenshot(surfaceRef).Success.Should().BeTrue();

        requests.Select(request => request.Action).Should().Equal(
            BrowserScriptingActionKind.Click,
            BrowserScriptingActionKind.Hover,
            BrowserScriptingActionKind.Press,
            BrowserScriptingActionKind.Eval,
            BrowserScriptingActionKind.Screenshot);
        requests[2].Key.Should().Be("Enter");
        requests[3].Script.Should().Be("document.title");
        requests[4].Node.Should().BeNull();
    }

    [Fact]
    public void CookiesGetSetClear_DispatchStateRequests()
    {
        var (service, surfaceRef, requests) = CreateStateService();
        var cookie = new BrowserScriptingCookie("sid", "123", Domain: "example.com");

        service.CookiesSet(surfaceRef, cookie).Success.Should().BeTrue();
        service.CookiesGet(surfaceRef, "sid").Success.Should().BeTrue();
        service.CookiesClear(surfaceRef, "sid").Success.Should().BeTrue();

        requests.Select(request => request.Kind).Should().Equal(
            BrowserScriptingStateKind.CookiesSet,
            BrowserScriptingStateKind.CookiesGet,
            BrowserScriptingStateKind.CookiesClear);
        requests.All(request => request.SurfaceId == "browser-1").Should().BeTrue();
        requests[0].Cookie.Should().Be(cookie);
        requests[1].Name.Should().Be("sid");
        requests[2].Name.Should().Be("sid");
    }

    [Fact]
    public void StorageGetSetClear_DispatchStateRequests()
    {
        var (service, surfaceRef, requests) = CreateStateService();

        service.StorageSet(surfaceRef, "theme", "dark", BrowserScriptingStorageArea.Session).Success.Should().BeTrue();
        service.StorageGet(surfaceRef, "theme", BrowserScriptingStorageArea.Session).Success.Should().BeTrue();
        service.StorageClear(surfaceRef, "theme", BrowserScriptingStorageArea.Session).Success.Should().BeTrue();

        requests.Select(request => request.Kind).Should().Equal(
            BrowserScriptingStateKind.StorageSet,
            BrowserScriptingStateKind.StorageGet,
            BrowserScriptingStateKind.StorageClear);
        requests.All(request => request.Area == BrowserScriptingStorageArea.Session).Should().BeTrue();
        requests[0].Key.Should().Be("theme");
        requests[0].Value.Should().Be("dark");
        requests[1].Key.Should().Be("theme");
        requests[2].Key.Should().Be("theme");
    }

    [Fact]
    public void ConsoleDialogDownloadAndHighlight_DispatchControlRequests()
    {
        var (service, surfaceRef, requests) = CreateControlService();

        service.ConsoleList(surfaceRef).Success.Should().BeTrue();
        service.ConsoleClear(surfaceRef).Success.Should().BeTrue();
        service.DialogAccept(surfaceRef, "ok").Success.Should().BeTrue();
        service.DialogDismiss(surfaceRef).Success.Should().BeTrue();
        service.DownloadWait(surfaceRef, "report.csv", timeoutMs: 5000).Success.Should().BeTrue();
        service.Highlight(surfaceRef, BrowserScriptingLocator.TestId("email-input")).Success.Should().BeTrue();

        requests.Select(request => request.Kind).Should().Equal(
            BrowserScriptingControlKind.ConsoleList,
            BrowserScriptingControlKind.ConsoleClear,
            BrowserScriptingControlKind.DialogAccept,
            BrowserScriptingControlKind.DialogDismiss,
            BrowserScriptingControlKind.DownloadWait,
            BrowserScriptingControlKind.Highlight);
        requests.All(request => request.SurfaceId == "browser-1").Should().BeTrue();
        requests[2].Text.Should().Be("ok");
        requests[4].FileName.Should().Be("report.csv");
        requests[4].TimeoutMs.Should().Be(5000);
        requests[5].Node!.NodeId.Should().Be("email");
    }

    [Fact]
    public void AddScriptStyle_DispatchControlRequests()
    {
        var (service, surfaceRef, requests) = CreateControlService();

        service.AddInitScript(surfaceRef, "window.__ready = true;").Success.Should().BeTrue();
        service.AddScript(surfaceRef, "document.body.dataset.ready = '1';").Success.Should().BeTrue();
        service.AddStyle(surfaceRef, "body { outline: 1px solid red; }").Success.Should().BeTrue();

        requests.Select(request => request.Kind).Should().Equal(
            BrowserScriptingControlKind.AddInitScript,
            BrowserScriptingControlKind.AddScript,
            BrowserScriptingControlKind.AddStyle);
        requests[0].Text.Should().Be("window.__ready = true;");
        requests[1].Text.Should().Be("document.body.dataset.ready = '1';");
        requests[2].Text.Should().Be("body { outline: 1px solid red; }");
    }

    private static BrowserScriptingSurfaceDescriptor CreateSurface(string surfaceId, SurfaceKind kind)
    {
        return new BrowserScriptingSurfaceDescriptor(
            WorkspaceId: "workspace-1",
            WorkspaceName: "Project",
            SurfaceId: surfaceId,
            SurfaceName: surfaceId,
            Kind: kind,
            Url: kind == SurfaceKind.Browser ? "https://example.com" : null,
            Title: kind == SurfaceKind.Browser ? "Example" : null);
    }

    private static (BrowserScriptingService Service, string SurfaceRef) CreateSnapshotService()
    {
        var surfaces = new List<BrowserScriptingSurfaceDescriptor>
        {
            CreateSurface("browser-1", SurfaceKind.Browser),
        };
        var snapshots = new Dictionary<string, BrowserScriptingSnapshot>
        {
            ["browser-1"] = CreateSnapshot(),
        };
        var service = new BrowserScriptingService(
            () => surfaces,
            surfaceId => snapshots.TryGetValue(surfaceId, out var snapshot) ? snapshot : null);

        return (service, BrowserScriptingService.CreateSurfaceRef("browser-1"));
    }

    private static (BrowserScriptingService Service, string SurfaceRef, List<BrowserScriptingActionRequest> Requests) CreateActionService()
    {
        var surfaces = new List<BrowserScriptingSurfaceDescriptor>
        {
            CreateSurface("browser-1", SurfaceKind.Browser),
        };
        var snapshots = new Dictionary<string, BrowserScriptingSnapshot>
        {
            ["browser-1"] = CreateSnapshot(),
        };
        var requests = new List<BrowserScriptingActionRequest>();
        var service = new BrowserScriptingService(
            () => surfaces,
            surfaceId => snapshots.TryGetValue(surfaceId, out var snapshot) ? snapshot : null,
            request =>
            {
                requests.Add(request);
                return BrowserScriptingActionOutcome.FromValue(new { ok = true });
            });

        return (service, BrowserScriptingService.CreateSurfaceRef("browser-1"), requests);
    }

    private static (BrowserScriptingService Service, string SurfaceRef, List<BrowserScriptingStateRequest> Requests) CreateStateService()
    {
        var surfaces = new List<BrowserScriptingSurfaceDescriptor>
        {
            CreateSurface("browser-1", SurfaceKind.Browser),
        };
        var requests = new List<BrowserScriptingStateRequest>();
        var service = new BrowserScriptingService(
            () => surfaces,
            stateExecutor: request =>
            {
                requests.Add(request);
                return BrowserScriptingStateOutcome.FromValue(new { ok = true });
            });

        return (service, BrowserScriptingService.CreateSurfaceRef("browser-1"), requests);
    }

    private static (BrowserScriptingService Service, string SurfaceRef, List<BrowserScriptingControlRequest> Requests) CreateControlService()
    {
        var surfaces = new List<BrowserScriptingSurfaceDescriptor>
        {
            CreateSurface("browser-1", SurfaceKind.Browser),
        };
        var snapshots = new Dictionary<string, BrowserScriptingSnapshot>
        {
            ["browser-1"] = CreateSnapshot(),
        };
        var requests = new List<BrowserScriptingControlRequest>();
        var service = new BrowserScriptingService(
            () => surfaces,
            surfaceId => snapshots.TryGetValue(surfaceId, out var snapshot) ? snapshot : null,
            controlExecutor: request =>
            {
                requests.Add(request);
                return BrowserScriptingControlOutcome.FromValue(new { ok = true });
            });

        return (service, BrowserScriptingService.CreateSurfaceRef("browser-1"), requests);
    }

    private static BrowserScriptingSnapshot CreateSnapshot()
    {
        return new BrowserScriptingSnapshot(new BrowserScriptingNode
        {
            NodeId = "root",
            Role = "document",
            Name = "Example page",
            Children =
            [
                new BrowserScriptingNode
                {
                    NodeId = "hero",
                    Role = "heading",
                    Name = "Welcome",
                    Text = "Welcome to ECode",
                },
                new BrowserScriptingNode
                {
                    NodeId = "email",
                    Role = "textbox",
                    Name = "Email",
                    TestId = "email-input",
                },
                new BrowserScriptingNode
                {
                    NodeId = "save",
                    Role = "button",
                    Name = "Save",
                    Text = "Save",
                },
                new BrowserScriptingNode
                {
                    NodeId = "cancel",
                    Role = "button",
                    Name = "Cancel",
                    Text = "Cancel",
                },
            ],
        });
    }
}

/// <summary>
/// 终端环境变量测试 - 验证启动 shell 前注入 ecode 上下文变量。
/// </summary>
public class TerminalEnvironmentVariablesTests
{
    [Fact]
    public void ForWorkspace_AddsEcodeWorkspaceId()
    {
        var environment = TerminalEnvironmentVariables.ForWorkspace("workspace-1");

        environment.Should().ContainKey(TerminalEnvironmentVariables.WorkspaceId)
            .WhoseValue.Should().Be("workspace-1");
    }

    [Fact]
    public void MergeWithCurrent_OverridesExistingValuesAndSkipsInvalidNames()
    {
        var merged = TerminalEnvironmentVariables.MergeWithCurrent(new Dictionary<string, string>
        {
            ["PATH"] = "test-path",
            [TerminalEnvironmentVariables.WorkspaceId] = "workspace-1",
            ["BAD=NAME"] = "ignored",
            [""] = "ignored",
        });

        merged["PATH"].Should().Be("test-path");
        merged[TerminalEnvironmentVariables.WorkspaceId].Should().Be("workspace-1");
        merged.Should().NotContainKey("BAD=NAME");
        merged.Should().NotContainKey("");
    }
}

/// <summary>
/// Daemon 日志格式测试 - 验证日志行包含稳定的结构化字段，便于 grep 串联 attach 流程
/// </summary>
public class DaemonLogFormatTests
{
    [Fact]
    public void FormatDaemonLogLine_IncludesRequiredFieldsAndEscapesValues()
    {
        var timestamp = new DateTimeOffset(2026, 6, 14, 5, 30, 0, TimeSpan.FromHours(8));

        var line = DaemonClient.FormatDaemonLogLine(
            timestamp,
            "daemon-client",
            "request.send",
            "pane-1",
            "Sending daemon request",
            new Dictionary<string, object?>
            {
                ["requestType"] = DaemonMessageTypes.SessionCreate,
                ["path"] = @"C:\Users\mac\my repo",
            });

        line.Should().Contain("ts=2026-06-14T05:30:00.0000000+08:00");
        line.Should().Contain("component=daemon-client");
        line.Should().Contain("event=request.send");
        line.Should().Contain("paneId=pane-1");
        line.Should().Contain("message=\"Sending daemon request\"");
        line.Should().Contain("requestType=SESSION_CREATE");
        line.Should().Contain("path=\"C:\\\\Users\\\\mac\\\\my repo\"");
    }
}

/// <summary>
/// ResumeBinding DTO 测试 - 验证 resume.json 根对象与 binding 字段可稳定 JSON 往返
/// </summary>
public class ResumeBindingDtoTests
{
    [Fact]
    public void ResumeBindingFile_RoundTripsJson()
    {
        var createdAt = new DateTime(2026, 6, 14, 1, 2, 3, DateTimeKind.Utc);
        var updatedAt = new DateTime(2026, 6, 14, 4, 5, 6, DateTimeKind.Utc);
        var file = new ResumeBindingFile
        {
            Version = 1,
            Bindings =
            [
                new ResumeBinding
                {
                    Id = "binding-1",
                    WorkspaceId = "workspace-1",
                    SurfaceId = "surface-1",
                    PaneId = "pane-1",
                    Kind = ResumeBindingKinds.Tmux,
                    Checkpoint = "work",
                    Shell = "tmux attach -t work",
                    WorkingDirectory = @"C:\repo",
                    Environment = new Dictionary<string, string>
                    {
                        ["SAFE_KEY"] = "value",
                    },
                    Trusted = true,
                    TrustReason = "user-approved-prefix",
                    ApprovedPrefix = "tmux attach",
                    CreatedAtUtc = createdAt,
                    UpdatedAtUtc = updatedAt,
                },
            ],
        };

        var json = JsonSerializer.Serialize(file);
        var roundTripped = JsonSerializer.Deserialize<ResumeBindingFile>(json);

        roundTripped.Should().NotBeNull();
        roundTripped!.Version.Should().Be(1);
        roundTripped.Bindings.Should().ContainSingle();

        var binding = roundTripped.Bindings.Single();
        binding.Id.Should().Be("binding-1");
        binding.WorkspaceId.Should().Be("workspace-1");
        binding.SurfaceId.Should().Be("surface-1");
        binding.PaneId.Should().Be("pane-1");
        binding.Kind.Should().Be(ResumeBindingKinds.Tmux);
        binding.Checkpoint.Should().Be("work");
        binding.Shell.Should().Be("tmux attach -t work");
        binding.WorkingDirectory.Should().Be(@"C:\repo");
        binding.Environment.Should().ContainKey("SAFE_KEY").WhoseValue.Should().Be("value");
        binding.Trusted.Should().BeTrue();
        binding.TrustReason.Should().Be("user-approved-prefix");
        binding.ApprovedPrefix.Should().Be("tmux attach");
        binding.CreatedAtUtc.Should().Be(createdAt);
        binding.UpdatedAtUtc.Should().Be(updatedAt);
    }

    [Fact]
    public void ResumeBinding_DefaultsToVersionOneAndCustomKind()
    {
        var file = new ResumeBindingFile();
        var binding = new ResumeBinding();

        file.Version.Should().Be(1);
        file.Bindings.Should().BeEmpty();
        binding.Id.Should().NotBeNullOrWhiteSpace();
        binding.Kind.Should().Be(ResumeBindingKinds.Custom);
        binding.Environment.Should().BeEmpty();
        binding.CreatedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        binding.UpdatedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
    }
}

/// <summary>
/// ResumeBindingService 测试 - 覆盖加载、保存、增删、按 Surface 查询和信任前缀更新
/// </summary>
public class ResumeBindingServiceTests
{
    [Fact]
    public void Load_WhenFileMissing_ReturnsEmptyVersionOneFile()
    {
        using var temp = TempDirectory.Create();
        var service = new ResumeBindingService(Path.Combine(temp.Path, "resume.json"));

        var file = service.Load();

        file.Version.Should().Be(1);
        file.Bindings.Should().BeEmpty();
    }

    [Fact]
    public void SaveAndLoad_RoundTripsBindings()
    {
        using var temp = TempDirectory.Create();
        var path = Path.Combine(temp.Path, "resume.json");
        var service = new ResumeBindingService(path);
        var file = new ResumeBindingFile
        {
            Bindings =
            [
                CreateResumeBinding("binding-1", "workspace-1", "surface-1", "pane-1"),
            ],
        };

        service.Save(file);
        var loaded = service.Load();

        loaded.Bindings.Should().ContainSingle();
        loaded.Bindings.Single().Id.Should().Be("binding-1");
        loaded.Bindings.Single().Shell.Should().Be("tmux attach -t work");
    }

    [Fact]
    public void Add_InsertsAndReplacesById()
    {
        using var temp = TempDirectory.Create();
        var service = new ResumeBindingService(Path.Combine(temp.Path, "resume.json"));
        var binding = CreateResumeBinding("binding-1", "workspace-1", "surface-1", "pane-1");

        service.Add(binding);
        binding.Shell = "tmux attach -t updated";
        service.Add(binding);

        var loaded = service.Load();
        loaded.Bindings.Should().ContainSingle();
        loaded.Bindings.Single().Shell.Should().Be("tmux attach -t updated");
        loaded.Bindings.Single().UpdatedAtUtc.Should().BeAfter(loaded.Bindings.Single().CreatedAtUtc.AddTicks(-1));
    }

    [Fact]
    public void SetForPane_ReplacesExistingPaneBindings()
    {
        using var temp = TempDirectory.Create();
        var service = new ResumeBindingService(Path.Combine(temp.Path, "resume.json"));
        service.Add(CreateResumeBinding("old-1", "workspace-1", "surface-1", "pane-1"));
        service.Add(CreateResumeBinding("old-2", "workspace-1", "surface-1", "pane-1", shell: "tmux attach -t old"));
        service.Add(CreateResumeBinding("other-pane", "workspace-1", "surface-1", "pane-2"));

        var updated = service.SetForPane(CreateResumeBinding("", "workspace-1", "surface-1", "pane-1", shell: "tmux attach -t new"));

        updated.Id.Should().NotBeNullOrWhiteSpace();
        var loaded = service.Load().Bindings;
        loaded.Where(b => b.PaneId == "pane-1").Should().ContainSingle()
            .Which.Shell.Should().Be("tmux attach -t new");
        loaded.Should().Contain(b => b.Id == "other-pane");
    }

    [Fact]
    public void Remove_DeletesBindingById()
    {
        using var temp = TempDirectory.Create();
        var service = new ResumeBindingService(Path.Combine(temp.Path, "resume.json"));
        service.Add(CreateResumeBinding("binding-1", "workspace-1", "surface-1", "pane-1"));
        service.Add(CreateResumeBinding("binding-2", "workspace-1", "surface-1", "pane-2"));

        var removed = service.Remove("binding-1");

        removed.Should().BeTrue();
        service.Load().Bindings.Select(b => b.Id).Should().Equal("binding-2");
        service.Remove("missing").Should().BeFalse();
    }

    [Fact]
    public void RemoveForPane_DeletesOnlyMatchingPane()
    {
        using var temp = TempDirectory.Create();
        var service = new ResumeBindingService(Path.Combine(temp.Path, "resume.json"));
        service.Add(CreateResumeBinding("target-1", "workspace-1", "surface-1", "pane-1"));
        service.Add(CreateResumeBinding("target-2", "workspace-1", "surface-1", "pane-1", shell: "tmux attach -t work"));
        service.Add(CreateResumeBinding("other-pane", "workspace-1", "surface-1", "pane-2"));
        service.Add(CreateResumeBinding("other-surface", "workspace-1", "surface-2", "pane-1"));

        var removed = service.RemoveForPane("workspace-1", "surface-1", "pane-1");

        removed.Should().Be(2);
        service.Load().Bindings.Select(b => b.Id).Should().BeEquivalentTo("other-pane", "other-surface");
        service.RemoveForPane("workspace-1", "surface-1", "missing").Should().Be(0);
    }

    [Fact]
    public void FindForSurface_FiltersByWorkspaceAndSurface()
    {
        using var temp = TempDirectory.Create();
        var service = new ResumeBindingService(Path.Combine(temp.Path, "resume.json"));
        service.Add(CreateResumeBinding("target-1", "workspace-1", "surface-1", "pane-1"));
        service.Add(CreateResumeBinding("other-surface", "workspace-1", "surface-2", "pane-2"));
        service.Add(CreateResumeBinding("other-workspace", "workspace-2", "surface-1", "pane-3"));

        var matches = service.FindForSurface("workspace-1", "surface-1");

        matches.Select(b => b.Id).Should().Equal("target-1");
    }

    [Fact]
    public void TrustPrefix_OnlyTrustsMatchingSurfaceCwdAndPrefix()
    {
        using var temp = TempDirectory.Create();
        var service = new ResumeBindingService(Path.Combine(temp.Path, "resume.json"));
        service.Add(CreateResumeBinding("target", "workspace-1", "surface-1", "pane-1", @"C:\repo", "tmux attach -t work"));
        service.Add(CreateResumeBinding("wrong-prefix", "workspace-1", "surface-1", "pane-2", @"C:\repo", "npm test"));
        service.Add(CreateResumeBinding("wrong-cwd", "workspace-1", "surface-1", "pane-3", @"C:\other", "tmux attach -t work"));
        service.Add(CreateResumeBinding("wrong-surface", "workspace-1", "surface-2", "pane-4", @"C:\repo", "tmux attach -t work"));

        var trusted = service.TrustPrefix("workspace-1", "surface-1", "tmux attach", @"C:\repo");

        trusted.Should().Be(1);
        var bindings = service.Load().Bindings.ToDictionary(b => b.Id);
        bindings["target"].Trusted.Should().BeTrue();
        bindings["target"].TrustReason.Should().Be("user-approved-prefix");
        bindings["target"].ApprovedPrefix.Should().Be("tmux attach");
        bindings["wrong-prefix"].Trusted.Should().BeFalse();
        bindings["wrong-cwd"].Trusted.Should().BeFalse();
        bindings["wrong-surface"].Trusted.Should().BeFalse();
    }

    [Fact]
    public void TrustBinding_TrustsSingleBindingById()
    {
        using var temp = TempDirectory.Create();
        var service = new ResumeBindingService(Path.Combine(temp.Path, "resume.json"));
        service.Add(CreateResumeBinding("target", "workspace-1", "surface-1", "pane-1", @"C:\repo", "tmux attach -t work"));
        service.Add(CreateResumeBinding("other", "workspace-1", "surface-1", "pane-2", @"C:\repo", "npm test"));

        var trusted = service.TrustBinding("target");

        trusted.Should().BeTrue();
        var bindings = service.Load().Bindings.ToDictionary(b => b.Id);
        bindings["target"].Trusted.Should().BeTrue();
        bindings["target"].TrustReason.Should().Be("user-approved-binding");
        bindings["target"].ApprovedPrefix.Should().Be("tmux attach -t work");
        bindings["other"].Trusted.Should().BeFalse();
    }

    [Fact]
    public void Save_DropsSensitiveEnv()
    {
        using var temp = TempDirectory.Create();
        var service = new ResumeBindingService(Path.Combine(temp.Path, "resume.json"));
        var binding = CreateResumeBinding("binding-1", "workspace-1", "surface-1", "pane-1");
        binding.Environment = new Dictionary<string, string>
        {
            ["PATH"] = @"C:\Windows",
            ["OPENAI_API_KEY"] = "sk-secret",
            ["GITHUB_TOKEN"] = "ghp_secret",
            ["PASSWORD"] = "hunter2",
            ["MY_SECRET_VALUE"] = "secret",
            ["AWS_ACCESS_KEY_ID"] = "access",
            ["TOKEN_CACHE_DISABLED"] = "true",
            ["SAFE_KEY"] = "safe",
        };

        service.Save(new ResumeBindingFile { Bindings = [binding] });

        var loadedEnv = service.Load().Bindings.Single().Environment;
        loadedEnv.Keys.Should().BeEquivalentTo("PATH", "SAFE_KEY");
        loadedEnv["PATH"].Should().Be(@"C:\Windows");
        loadedEnv["SAFE_KEY"].Should().Be("safe");
    }

    [Fact]
    public void Add_DropsSensitiveEnvBeforePersisting()
    {
        using var temp = TempDirectory.Create();
        var service = new ResumeBindingService(Path.Combine(temp.Path, "resume.json"));
        var binding = CreateResumeBinding("binding-1", "workspace-1", "surface-1", "pane-1");
        binding.Environment = new Dictionary<string, string>
        {
            ["SAFE_KEY"] = "safe",
            ["API_KEY"] = "secret",
        };

        service.Add(binding);

        var loadedEnv = service.Load().Bindings.Single().Environment;
        loadedEnv.Should().ContainKey("SAFE_KEY").WhoseValue.Should().Be("safe");
        loadedEnv.Should().NotContainKey("API_KEY");
    }

    private static ResumeBinding CreateResumeBinding(
        string id,
        string workspaceId,
        string surfaceId,
        string paneId,
        string workingDirectory = @"C:\repo",
        string shell = "tmux attach -t work") => new()
    {
        Id = id,
        WorkspaceId = workspaceId,
        SurfaceId = surfaceId,
        PaneId = paneId,
        Kind = ResumeBindingKinds.Tmux,
        Shell = shell,
        WorkingDirectory = workingDirectory,
        CreatedAtUtc = new DateTime(2025, 1, 1, 1, 0, 0, DateTimeKind.Utc),
        UpdatedAtUtc = new DateTime(2025, 1, 1, 1, 0, 0, DateTimeKind.Utc),
    };

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "ecode-resume-tests-" + Guid.NewGuid().ToString("N"));

        private TempDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public static TempDirectory Create() => new();

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}

public class ResumeProcessDetectorTests
{
    [Fact]
    public void DetectFromTaskListCsv_FindsTmuxAndKnownShell()
    {
        const string csv = """
            "Image Name","PID","Session Name","Session#","Mem Usage","Status","User Name","CPU Time","Window Title"
            "tmux.exe","4242","Console","1","10,000 K","Running","user","0:00:01","tmux attach -t work"
            "pwsh.exe","4243","Console","1","20,000 K","Running","user","0:00:02","C:\Program Files\PowerShell\7\pwsh.exe"
            """;

        var detection = ResumeProcessDetector.DetectFromTaskListCsv(
            csv,
            [@"C:\Program Files\PowerShell\7\pwsh.exe"]);

        detection.Processes.Should().HaveCount(2);
        detection.HasTmux.Should().BeTrue();
        detection.HasKnownShell.Should().BeTrue();
        detection.MatchingShellPath.Should().Be(@"C:\Program Files\PowerShell\7\pwsh.exe");
    }

    [Fact]
    public void DetectFromTaskListCsv_ReturnsFalseWhenNoTmuxOrShellExists()
    {
        const string csv = """
            "Image Name","PID","Session Name","Session#","Mem Usage","Status","User Name","CPU Time","Window Title"
            "node.exe","5000","Console","1","30,000 K","Running","user","0:00:03","vite dev server"
            """;

        var detection = ResumeProcessDetector.DetectFromTaskListCsv(csv, [@"C:\Windows\System32\cmd.exe"]);

        detection.Processes.Should().HaveCount(1);
        detection.HasTmux.Should().BeFalse();
        detection.HasKnownShell.Should().BeFalse();
        detection.MatchingShellPath.Should().BeNull();
    }
}

/// <summary>
/// 会话持久化测试 - 验证 SurfaceKind 与 Browser metadata 的兼容存储。
/// </summary>
public class SessionPersistenceServiceTests
{
    [Fact]
    public void BuildState_PersistsBrowserSurfaceMetadata()
    {
        var workspace = new Workspace
        {
            Id = "workspace-1",
            Name = "Workspace",
        };
        var surface = new Surface
        {
            Id = "surface-1",
            Name = "Docs",
            Kind = SurfaceKind.Browser,
            BrowserUrl = "https://example.com/docs",
            BrowserTitle = "Docs",
            BrowserHistory = ["https://example.com", "https://example.com/docs"],
        };
        workspace.Surfaces.Add(surface);
        workspace.SelectedSurface = surface;

        var state = SessionPersistenceService.BuildState(
            [workspace],
            selectedWorkspaceIndex: 0,
            windowX: 1,
            windowY: 2,
            windowWidth: 1200,
            windowHeight: 800,
            isMaximized: false,
            sidebarWidth: 280,
            sidebarVisible: true,
            compactSidebar: false);

        var persisted = state.Workspaces.Single().Surfaces.Single();
        persisted.Kind.Should().Be(SurfaceKind.Browser);
        persisted.BrowserUrl.Should().Be("https://example.com/docs");
        persisted.BrowserTitle.Should().Be("Docs");
        persisted.BrowserHistory.Should().Equal("https://example.com", "https://example.com/docs");
    }

    [Fact]
    public void SurfaceState_RoundTripsBrowserMetadata()
    {
        var state = new SessionState
        {
            Workspaces =
            [
                new WorkspaceState
                {
                    Id = "workspace-1",
                    Name = "Workspace",
                    Surfaces =
                    [
                        new SurfaceState
                        {
                            Id = "surface-1",
                            Name = "Browser",
                            Kind = SurfaceKind.Browser,
                            BrowserUrl = "http://localhost:5173",
                            BrowserTitle = "Local App",
                            BrowserHistory = ["http://localhost:5173", "http://localhost:5173/dashboard"],
                        },
                    ],
                },
            ],
        };

        var json = JsonSerializer.Serialize(state);
        var roundTripped = JsonSerializer.Deserialize<SessionState>(json);

        var surface = roundTripped!.Workspaces.Single().Surfaces.Single();
        surface.Kind.Should().Be(SurfaceKind.Browser);
        surface.BrowserUrl.Should().Be("http://localhost:5173");
        surface.BrowserTitle.Should().Be("Local App");
        surface.BrowserHistory.Should().Equal("http://localhost:5173", "http://localhost:5173/dashboard");
        json.Should().Contain("\"kind\":\"Browser\"");
    }

    [Fact]
    public void SurfaceState_WhenKindMissing_DefaultsToTerminal()
    {
        var json = """
            {
              "version": 1,
              "workspaces": [
                {
                  "id": "workspace-1",
                  "name": "Workspace",
                  "surfaces": [
                    {
                      "id": "surface-1",
                      "name": "Terminal",
                      "rootNode": { "isLeaf": true, "paneId": "pane-1" }
                    }
                  ]
                }
              ]
            }
            """;

        var state = JsonSerializer.Deserialize<SessionState>(json);

        state!.Workspaces.Single().Surfaces.Single().Kind.Should().Be(SurfaceKind.Terminal);
        state.Workspaces.Single().Surfaces.Single().BrowserHistory.Should().BeEmpty();
    }
}

/// <summary>
/// ecode.json 配置服务测试 - 验证全局配置与本地配置的加载、合并、验证和 JSONC 支持
/// </summary>
public class EcodeJsonServiceTests
{
    [Fact]
    public void Load_MergesLocalOverGlobal()
    {
        using var temp = TempDirectory.Create();
        var workspaceDir = Path.Combine(temp.Path, "workspace");
        var localDir = Path.Combine(workspaceDir, ".ecode");
        Directory.CreateDirectory(localDir);

        var globalPath = Path.Combine(temp.Path, "global.json");
        File.WriteAllText(globalPath, """
            {
              "commands": [
                {
                  "name": "Run Tests",
                  "description": "global test command",
                  "command": "dotnet test --no-build"
                },
                {
                  "name": "Format",
                  "command": "dotnet format"
                }
              ],
              "actions": {
                "devServer": {
                  "type": "command",
                  "title": "Dev Server",
                  "command": "npm run dev",
                  "target": "currentTerminal"
                }
              }
            }
            """);

        File.WriteAllText(Path.Combine(localDir, "ecode.json"), """
            {
              "commands": [
                {
                  "name": "Run Tests",
                  "description": "local override",
                  "keywords": ["test", "verify", "test"],
                  "command": "dotnet test",
                  "confirm": true
                }
              ],
              "actions": {
                "devServer": {
                  "type": "command",
                  "title": "Dev Server Local",
                  "command": "npm run dev -- --host 0.0.0.0",
                  "target": "newTabInCurrentPane",
                  "palette": true,
                  "confirm": true
                }
              }
            }
            """);

        var result = new EcodeJsonService().Load(workspaceDir, globalPath);

        result.Diagnostics.Should().BeEmpty();
        result.LoadedPaths.Should().HaveCount(2);
        result.Config.Commands.Should().HaveCount(2);
        result.Config.Commands.Single(c => c.Name == "Run Tests").Command.Should().Be("dotnet test");
        result.Config.Commands.Single(c => c.Name == "Run Tests").Confirm.Should().BeTrue();
        result.Config.Commands.Single(c => c.Name == "Run Tests").Keywords.Should().Equal("test", "verify");
        result.Config.Commands.Single(c => c.Name == "Format").Command.Should().Be("dotnet format");
        result.Config.Actions["devServer"].Title.Should().Be("Dev Server Local");
        result.Config.Actions["devServer"].Target.Should().Be(EcodeActionTargets.NewTabInCurrentPane);
    }

    [Fact]
    public void Load_InvalidSchema_ReturnsDiagnostic()
    {
        using var temp = TempDirectory.Create();
        var path = Path.Combine(temp.Path, "ecode.json");
        File.WriteAllText(path, """
            {
              "commands": [
                {
                  "name": "",
                  "command": ""
                }
              ],
              "actions": {
                "broken": {
                  "type": "command",
                  "title": "Broken"
                }
              }
            }
            """);

        var result = new EcodeJsonService().LoadFromFiles([path]);

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Severity == EcodeJsonDiagnosticSeverity.Error
            && d.Message.Contains("commands[0].name", StringComparison.Ordinal));
        result.Diagnostics.Should().Contain(d => d.Severity == EcodeJsonDiagnosticSeverity.Error
            && d.Message.Contains("commands[0].command", StringComparison.Ordinal));
        result.Diagnostics.Should().Contain(d => d.Severity == EcodeJsonDiagnosticSeverity.Error
            && d.Message.Contains("actions.broken.command", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_SupportsJsonCommentsAndTrailingCommas()
    {
        using var temp = TempDirectory.Create();
        var path = Path.Combine(temp.Path, "ecode.json");
        File.WriteAllText(path, """
            {
              // ecode accepts jsonc-style project files.
              "commands": [
                {
                  "name": "Build",
                  "command": "dotnet build",
                },
              ],
            }
            """);

        var result = new EcodeJsonService().LoadFromFiles([path]);

        result.Diagnostics.Should().BeEmpty();
        result.Config.Commands.Single().Name.Should().Be("Build");
        result.Config.Commands.Single().Command.Should().Be("dotnet build");
    }

    [Fact]
    public void Load_ParsesWorkspaceBrowserSurface()
    {
        using var temp = TempDirectory.Create();
        var path = Path.Combine(temp.Path, "ecode.json");
        File.WriteAllText(path, """
            {
              "workspace": {
                "selectedSurfaceIndex": 1,
                "surfaces": [
                  { "type": "terminal", "name": "Shell" },
                  { "type": "browser", "name": "Docs", "url": " https://example.com/docs " }
                ]
              }
            }
            """);

        var result = new EcodeJsonService().LoadFromFiles([path]);

        result.Diagnostics.Should().BeEmpty();
        result.Config.Workspace.Should().NotBeNull();
        result.Config.Workspace!.SelectedSurfaceIndex.Should().Be(1);
        result.Config.Workspace.Surfaces.Should().HaveCount(2);
        result.Config.Workspace.Surfaces[0].Type.Should().Be(EcodeSurfaceTypes.Terminal);
        result.Config.Workspace.Surfaces[0].Name.Should().Be("Shell");
        result.Config.Workspace.Surfaces[1].Type.Should().Be(EcodeSurfaceTypes.Browser);
        result.Config.Workspace.Surfaces[1].Name.Should().Be("Docs");
        result.Config.Workspace.Surfaces[1].Url.Should().Be("https://example.com/docs");
    }

    [Fact]
    public void Load_LocalWorkspaceOverridesGlobalWorkspace()
    {
        using var temp = TempDirectory.Create();
        var workspaceDir = Path.Combine(temp.Path, "workspace");
        var localDir = Path.Combine(workspaceDir, ".ecode");
        Directory.CreateDirectory(localDir);

        var globalPath = Path.Combine(temp.Path, "global.json");
        File.WriteAllText(globalPath, """
            {
              "workspace": {
                "surfaces": [
                  { "type": "browser", "name": "Global Docs", "url": "https://global.example" }
                ]
              }
            }
            """);

        File.WriteAllText(Path.Combine(localDir, "ecode.json"), """
            {
              "workspace": {
                "surfaces": [
                  { "type": "browser", "name": "Local Docs", "url": "https://local.example" }
                ]
              }
            }
            """);

        var result = new EcodeJsonService().Load(workspaceDir, globalPath);

        result.Diagnostics.Should().BeEmpty();
        result.Config.Workspace!.Surfaces.Should().ContainSingle();
        result.Config.Workspace.Surfaces.Single().Name.Should().Be("Local Docs");
        result.Config.Workspace.Surfaces.Single().Url.Should().Be("https://local.example");
    }

    [Fact]
    public void Load_BrowserSurfaceWithoutUrl_ReturnsDiagnostic()
    {
        using var temp = TempDirectory.Create();
        var path = Path.Combine(temp.Path, "ecode.json");
        File.WriteAllText(path, """
            {
              "workspace": {
                "surfaces": [
                  { "type": "browser", "name": "Preview" }
                ]
              }
            }
            """);

        var result = new EcodeJsonService().LoadFromFiles([path]);

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Severity == EcodeJsonDiagnosticSeverity.Error
            && d.Message.Contains("workspace.surfaces[0].url", StringComparison.Ordinal));
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "ecode-tests-" + Guid.NewGuid().ToString("N"));

        private TempDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public static TempDirectory Create() => new();

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}

/// <summary>
/// 版本服务测试 - 验证版本号提取逻辑（去除源代码版本控制后缀）
/// </summary>
public class VersionServiceTests
{
    [Fact]
    public void GetInformationalVersion_StripsSourceRevisionSuffix()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("VersionServiceTestAssembly"),
            AssemblyBuilderAccess.Run);
        var ctor = typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!;
        assembly.SetCustomAttribute(new CustomAttributeBuilder(ctor, ["1.2.3+abcdef"]));

        var version = VersionService.GetInformationalVersion(assembly);

        version.Should().NotContain("+");
        version.Should().Be("1.2.3");
    }
}

/// <summary>
/// VT 解析器测试 - 验证终端转义序列解析（CSI、OSC、ESC 序列及 UTF-8 处理）
/// </summary>
public class VtParserTests
{
    [Fact]
    public void Feed_PrintableCharacters_RaisesOnPrint()
    {
        var parser = new VtParser();
        var printed = new List<char>();
        parser.OnPrint = c => printed.Add(c);

        parser.Feed("Hello");

        printed.Should().Equal('H', 'e', 'l', 'l', 'o');
    }

    [Fact]
    public void Feed_C0Controls_RaisesOnExecute()
    {
        var parser = new VtParser();
        var executed = new List<byte>();
        parser.OnExecute = b => executed.Add(b);

        parser.Feed("\r\n");

        executed.Should().Contain(0x0D); // CR
        executed.Should().Contain(0x0A); // LF
    }

    [Fact]
    public void Feed_CsiSequence_RaisesOnCsiDispatch()
    {
        var parser = new VtParser();
        List<int>? receivedParams = null;
        char receivedFinal = '\0';
        parser.OnCsiDispatch = (parameters, final, qualifier) =>
        {
            receivedParams = new List<int>(parameters);
            receivedFinal = final;
        };

        // CSI 10;20H = 光标定位（第 10 行，第 20 列）
        parser.Feed("\x1b[10;20H");

        receivedFinal.Should().Be('H');
        receivedParams.Should().NotBeNull();
        receivedParams.Should().Equal(10, 20);
    }

    [Fact]
    public void Feed_SgrReset_RaisesOnCsiDispatch()
    {
        var parser = new VtParser();
        char receivedFinal = '\0';
        parser.OnCsiDispatch = (parameters, final, qualifier) =>
        {
            receivedFinal = final;
        };

        parser.Feed("\x1b[0m");

        receivedFinal.Should().Be('m');
    }

    [Fact]
    public void Feed_OscString_RaisesOnOscDispatch()
    {
        var parser = new VtParser();
        string? receivedOsc = null;
        parser.OnOscDispatch = osc => receivedOsc = osc;

        // OSC 0 ; My Title BEL
        parser.Feed("\x1b]0;My Title\x07");

        receivedOsc.Should().Be("0;My Title");
    }

    [Fact]
    public void Feed_OscStringTerminatedByEscBackslash_RaisesOnOscDispatch()
    {
        var parser = new VtParser();
        string? receivedOsc = null;
        parser.OnOscDispatch = osc => receivedOsc = osc;

        parser.Feed("\x1b]0;Esc Title\x1b\\");

        receivedOsc.Should().Be("0;Esc Title");
    }

    [Fact]
    public void Feed_OscStringTerminatedByEightBitSt_RaisesOnOscDispatch()
    {
        var parser = new VtParser();
        string? receivedOsc = null;
        parser.OnOscDispatch = osc => receivedOsc = osc;

        parser.Feed(new byte[] { 0x1B, (byte)']', (byte)'0', (byte)';', (byte)'T', (byte)'i', (byte)'t', (byte)'l', (byte)'e', 0x9C });

        receivedOsc.Should().Be("0;Title");
    }

    [Fact]
    public void Feed_Osc9Notification_Detected()
    {
        var parser = new VtParser();
        string? receivedOsc = null;
        parser.OnOscDispatch = osc => receivedOsc = osc;

        parser.Feed("\x1b]9;Background task needs input\x07");

        receivedOsc.Should().Be("9;Background task needs input");
    }

    [Fact]
    public void Feed_Osc777Notification_Detected()
    {
        var parser = new VtParser();
        string? receivedOsc = null;
        parser.OnOscDispatch = osc => receivedOsc = osc;

        parser.Feed("\x1b]777;notify;Build;Waiting for input\x07");

        receivedOsc.Should().Be("777;notify;Build;Waiting for input");
    }

    [Fact]
    public void Feed_EscSequence_RaisesOnEscDispatch()
    {
        var parser = new VtParser();
        byte? dispatched = null;
        parser.OnEscDispatch = b => dispatched = b;

        // ESC 7 = DECSC（保存光标）
        parser.Feed("\u001b7");

        dispatched.Should().Be((byte)'7');
    }

    [Fact]
    public void Feed_PrivateModeSet_ParsesCorrectly()
    {
        var parser = new VtParser();
        string? receivedQualifier = null;
        List<int>? receivedParams = null;
        parser.OnCsiDispatch = (parameters, final, qualifier) =>
        {
            receivedParams = new List<int>(parameters);
            receivedQualifier = qualifier;
        };

        // CSI ? 25 h = 显示光标（DECTCEM）
        parser.Feed("\x1b[?25h");

        receivedParams.Should().Equal(25);
        receivedQualifier.Should().Contain("?");
    }

    [Fact]
    public void Feed_Utf8AcrossChunks_PrintsSingleCharacter()
    {
        var parser = new VtParser();
        var printed = new List<char>();
        parser.OnPrint = c => printed.Add(c);
        var bytes = Encoding.UTF8.GetBytes("中");

        parser.Feed(bytes.AsSpan(0, 1));
        parser.Feed(bytes.AsSpan(1));

        printed.Should().Equal('中');
    }

    [Fact]
    public void Feed_InvalidUtf8Continuation_RecoversAndPrintsFollowingAscii()
    {
        var parser = new VtParser();
        var printed = new List<char>();
        parser.OnPrint = c => printed.Add(c);

        parser.Feed(new byte[] { 0xE4, (byte)'A' });

        printed.Should().Equal('A');
    }

    [Fact]
    public void Feed_CanCancelsIncompleteCsiAndReturnsToGround()
    {
        var parser = new VtParser();
        var printed = new List<char>();
        var dispatched = false;
        parser.OnPrint = c => printed.Add(c);
        parser.OnCsiDispatch = (_, _, _) => dispatched = true;

        parser.Feed("\u001b[31\u0018A");

        dispatched.Should().BeFalse();
        printed.Should().Equal('A');
    }
}

/// <summary>
/// 终端缓冲区测试 - 验证字符写入、光标移动、滚动、擦除、CJK 字符处理和窗口调整
/// </summary>
public class TerminalBufferTests
{
    [Fact]
    public void WriteChar_AdvancesCursor()
    {
        var buffer = new TerminalBuffer(80, 24);
        buffer.WriteChar('A');

        buffer.CursorCol.Should().Be(1);
        buffer.CellAt(0, 0).Character.Should().Be('A');
    }

    [Fact]
    public void LineFeed_AtBottom_ScrollsUp()
    {
        var buffer = new TerminalBuffer(80, 3);

        buffer.WriteString("Line1");
        buffer.NewLine();
        buffer.WriteString("Line2");
        buffer.NewLine();
        buffer.WriteString("Line3");
        buffer.NewLine(); // 应触发滚动

        buffer.ScrollbackCount.Should().Be(1);
    }

    [Fact]
    public void EraseInDisplay_Mode2_ClearsAll()
    {
        var buffer = new TerminalBuffer(80, 24);
        buffer.WriteString("Hello World");

        buffer.EraseInDisplay(2);

        buffer.CellAt(0, 0).Character.Should().Be(' ');
    }

    [Fact]
    public void Resize_PreservesContent()
    {
        var buffer = new TerminalBuffer(80, 24);
        buffer.WriteString("ABC");

        buffer.Resize(40, 12);

        buffer.CellAt(0, 0).Character.Should().Be('A');
        buffer.CellAt(0, 1).Character.Should().Be('B');
        buffer.CellAt(0, 2).Character.Should().Be('C');
        buffer.Cols.Should().Be(40);
        buffer.Rows.Should().Be(12);
    }

    [Fact]
    public void ScrollRegion_ScrollsOnlyWithinRegion()
    {
        var buffer = new TerminalBuffer(10, 5);
        buffer.SetScrollRegion(1, 3);
        buffer.MoveCursorTo(3, 0); // 滚动区域的底部
        buffer.WriteString("X");
        buffer.LineFeed(); // 应只滚动第 1-3 行

        buffer.CellAt(0, 0).Character.Should().Be(' '); // 第 0 行不受影响
    }

    [Fact]
    public void SaveRestore_CursorPosition()
    {
        var buffer = new TerminalBuffer(80, 24);
        buffer.MoveCursorTo(5, 10);
        buffer.SaveCursor();

        buffer.MoveCursorTo(0, 0);
        buffer.RestoreCursor();

        buffer.CursorRow.Should().Be(5);
        buffer.CursorCol.Should().Be(10);
    }

    [Fact]
    public void WriteChar_Ascii_AdvancesOneColumn()
    {
        var buffer = new TerminalBuffer(20, 3);
        buffer.WriteChar('A');
        buffer.CursorCol.Should().Be(1);
        buffer.CellAt(0, 0).Width.Should().Be(1);
    }

    [Fact]
    public void WriteChar_Cjk_AdvancesTwoColumnsAndPlacesPlaceholder()
    {
        var buffer = new TerminalBuffer(20, 3);
        buffer.WriteChar('中');

        buffer.CursorCol.Should().Be(2);
        buffer.CellAt(0, 0).Character.Should().Be('中');
        buffer.CellAt(0, 0).Width.Should().Be(2);
        buffer.CellAt(0, 1).Character.Should().Be('\0');
        buffer.CellAt(0, 1).Width.Should().Be(0);
    }

    [Fact]
    public void WriteString_Cjk_AdvancesColumnPerGlyph()
    {
        var buffer = new TerminalBuffer(20, 3);
        buffer.WriteString("中文");

        buffer.CursorCol.Should().Be(4);
        buffer.CellAt(0, 0).Character.Should().Be('中');
        buffer.CellAt(0, 0).Width.Should().Be(2);
        buffer.CellAt(0, 1).Width.Should().Be(0);
        buffer.CellAt(0, 2).Character.Should().Be('文');
        buffer.CellAt(0, 2).Width.Should().Be(2);
        buffer.CellAt(0, 3).Width.Should().Be(0);
    }

    [Fact]
    public void WriteChar_Cjk_AtRightEdge_WrapsToNextLine()
    {
        var buffer = new TerminalBuffer(4, 3);
        buffer.WriteString("abc");
        buffer.CursorCol.Should().Be(3);

        buffer.WriteChar('中');
        buffer.CursorRow.Should().Be(1);
        buffer.CursorCol.Should().Be(2);
    }
}

/// <summary>
/// OSC 处理器测试 - 验证 OSC 命令解析（标题、工作目录、通知、Shell 提示标记）
/// </summary>
public class OscHandlerTests
{
    [Fact]
    public void Handle_Osc0_ChangesTitleEvent()
    {
        var handler = new OscHandler();
        string? title = null;
        handler.TitleChanged += t => title = t;

        handler.Handle("0;My Terminal Title");

        title.Should().Be("My Terminal Title");
    }

    [Fact]
    public void Handle_Osc7_ChangesWorkingDirectory()
    {
        var handler = new OscHandler();
        string? dir = null;
        handler.WorkingDirectoryChanged += d => dir = d;

        handler.Handle("7;file://localhost/C:/Users/test/project");

        dir.Should().NotBeNull();
    }

    [Fact]
    public void Handle_Osc9_FiresNotification()
    {
        var handler = new OscHandler();
        string? body = null;
        handler.NotificationReceived += (t, s, b) => body = b;

        handler.Handle("9;Background task is waiting for your input");

        body.Should().Be("Background task is waiting for your input");
    }

    [Fact]
    public void Handle_Osc99_KeyValue_ParsesCorrectly()
    {
        var handler = new OscHandler();
        string? title = null, body = null;
        handler.NotificationReceived += (t, s, b) => { title = t; body = b; };

        handler.Handle("99;t=Build Watcher;b=Waiting for input");

        title.Should().Be("Build Watcher");
        body.Should().Be("Waiting for input");
    }

    [Fact]
    public void Handle_Osc777_Notify_ParsesCorrectly()
    {
        var handler = new OscHandler();
        string? title = null, body = null;
        handler.NotificationReceived += (t, s, b) => { title = t; body = b; };

        handler.Handle("777;notify;Build;Task completed");

        title.Should().Be("Build");
        body.Should().Be("Task completed");
    }

    [Fact]
    public void Handle_Osc133_FiresPromptMarker()
    {
        var handler = new OscHandler();
        char? marker = null;
        handler.ShellPromptMarker += (m, payload) => marker = m;

        handler.Handle("133;A");

        marker.Should().Be('A');
    }
}

/// <summary>
/// 分屏布局树测试 - 验证叶节点和容器节点的创建、分割、合并、导航和布局调整
/// </summary>
public class SplitNodeTests
{
    [Fact]
    public void CreateLeaf_IsLeaf()
    {
        var node = ECode.Core.Models.SplitNode.CreateLeaf("pane-1");
        node.IsLeaf.Should().BeTrue();
        node.PaneId.Should().Be("pane-1");
    }

    [Fact]
    public void Split_TurnsLeafIntoContainer()
    {
        var node = ECode.Core.Models.SplitNode.CreateLeaf("pane-1");

        var newChild = node.Split(ECode.Core.Models.SplitDirection.Vertical);

        node.IsLeaf.Should().BeFalse();
        node.First.Should().NotBeNull();
        node.Second.Should().NotBeNull();
        node.First!.PaneId.Should().Be("pane-1");
        newChild.PaneId.Should().NotBeNull();
    }

    [Fact]
    public void Split_NonLeaf_ThrowsInvalidOperation()
    {
        var node = ECode.Core.Models.SplitNode.CreateLeaf("pane-1");
        node.Split(ECode.Core.Models.SplitDirection.Vertical);

        var act = () => node.Split(ECode.Core.Models.SplitDirection.Horizontal);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FindNode_FindsLeaf()
    {
        var node = ECode.Core.Models.SplitNode.CreateLeaf("pane-1");
        node.Split(ECode.Core.Models.SplitDirection.Vertical);

        var found = node.FindNode("pane-1");

        found.Should().NotBeNull();
        found!.PaneId.Should().Be("pane-1");
    }

    [Fact]
    public void GetLeaves_ReturnsAllLeaves()
    {
        var node = ECode.Core.Models.SplitNode.CreateLeaf("pane-1");
        node.Split(ECode.Core.Models.SplitDirection.Vertical);

        var leaves = node.GetLeaves().ToList();

        leaves.Should().HaveCount(2);
        leaves[0].PaneId.Should().Be("pane-1");
    }

    [Fact]
    public void Remove_CollapsesParent()
    {
        var node = ECode.Core.Models.SplitNode.CreateLeaf("pane-1");
        var newChild = node.Split(ECode.Core.Models.SplitDirection.Vertical);
        var newPaneId = newChild.PaneId!;

        bool removed = node.Remove(newPaneId);

        removed.Should().BeTrue();
        node.IsLeaf.Should().BeTrue();
        node.PaneId.Should().Be("pane-1");
    }

    [Fact]
    public void GetNextLeaf_CyclesCorrectly()
    {
        var node = ECode.Core.Models.SplitNode.CreateLeaf("pane-1");
        var child2 = node.Split(ECode.Core.Models.SplitDirection.Vertical);

        var next = node.GetNextLeaf("pane-1");
        next.Should().NotBeNull();
        next!.PaneId.Should().Be(child2.PaneId);

        // 循环回到起点
        var wrap = node.GetNextLeaf(child2.PaneId!);
        wrap.Should().NotBeNull();
        wrap!.PaneId.Should().Be("pane-1");
    }

    [Fact]
    public void GetPreviousLeaf_CyclesCorrectly()
    {
        var node = ECode.Core.Models.SplitNode.CreateLeaf("pane-1");
        var child2 = node.Split(ECode.Core.Models.SplitDirection.Vertical);

        var previous = node.GetPreviousLeaf("pane-1");

        previous.Should().NotBeNull();
        previous!.PaneId.Should().Be(child2.PaneId);
    }

    [Fact]
    public void Remove_NestedLeaf_CollapsesOnlyContainingParent()
    {
        var root = ECode.Core.Models.SplitNode.CreateLeaf("pane-1");
        var right = root.Split(ECode.Core.Models.SplitDirection.Vertical);
        var rightPaneId = right.PaneId!;
        var nested = right.Split(ECode.Core.Models.SplitDirection.Horizontal);

        var removed = root.Remove(nested.PaneId!);

        removed.Should().BeTrue();
        root.IsLeaf.Should().BeFalse();
        root.GetLeaves().Select(l => l.PaneId).Should().Equal("pane-1", rightPaneId);
    }

    [Fact]
    public void Remove_MissingPane_ReturnsFalse()
    {
        var node = ECode.Core.Models.SplitNode.CreateLeaf("pane-1");
        node.Split(ECode.Core.Models.SplitDirection.Vertical);

        var removed = node.Remove("missing-pane");

        removed.Should().BeFalse();
        node.GetLeaves().Should().HaveCount(2);
    }

    [Fact]
    public void CreateColumns_CreatesRequestedVerticalLeaves()
    {
        var node = ECode.Core.Models.SplitNode.CreateColumns(3);

        node.Direction.Should().Be(ECode.Core.Models.SplitDirection.Vertical);
        node.GetLeaves().Should().HaveCount(3);
        node.SplitRatio.Should().BeApproximately(2d / 3d, 0.0001);
    }

    [Fact]
    public void CreateRows_CreatesRequestedHorizontalLeaves()
    {
        var node = ECode.Core.Models.SplitNode.CreateRows(3);

        node.Direction.Should().Be(ECode.Core.Models.SplitDirection.Horizontal);
        node.GetLeaves().Should().HaveCount(3);
        node.SplitRatio.Should().BeApproximately(2d / 3d, 0.0001);
    }

    [Fact]
    public void CreateGrid_CreatesFourLeaves()
    {
        var node = ECode.Core.Models.SplitNode.CreateGrid();

        node.Direction.Should().Be(ECode.Core.Models.SplitDirection.Horizontal);
        node.GetLeaves().Should().HaveCount(4);
        node.First!.Direction.Should().Be(ECode.Core.Models.SplitDirection.Vertical);
        node.Second!.Direction.Should().Be(ECode.Core.Models.SplitDirection.Vertical);
    }

    [Fact]
    public void CreateMainStack_CreatesMainPaneAndStack()
    {
        var node = ECode.Core.Models.SplitNode.CreateMainStack(stackCount: 3);

        node.Direction.Should().Be(ECode.Core.Models.SplitDirection.Vertical);
        node.SplitRatio.Should().Be(0.6);
        node.GetLeaves().Should().HaveCount(4);
        node.Second!.Direction.Should().Be(ECode.Core.Models.SplitDirection.Horizontal);
    }

    [Fact]
    public void Equalize_ResetsNestedSplitRatios()
    {
        var node = ECode.Core.Models.SplitNode.CreateMainStack(stackCount: 3);
        node.SplitRatio = 0.8;
        node.Second!.SplitRatio = 0.2;

        node.Equalize();

        node.SplitRatio.Should().Be(0.5);
        node.Second!.SplitRatio.Should().Be(0.5);
    }

    [Fact]
    public void ResizePane_AdjustsNearestDirectParentAndClamps()
    {
        var node = ECode.Core.Models.SplitNode.CreateLeaf("pane-1");
        var child2 = node.Split(ECode.Core.Models.SplitDirection.Vertical);

        var resizedFirst = node.ResizePane("pane-1", 0.2);
        var resizedSecond = node.ResizePane(child2.PaneId!, 1.0);

        resizedFirst.Should().BeTrue();
        resizedSecond.Should().BeTrue();
        node.SplitRatio.Should().Be(0.1);
    }

    [Fact]
    public void SwapPanes_ExchangesLeafPaneIds()
    {
        var node = ECode.Core.Models.SplitNode.CreateLeaf("pane-1");
        var child2 = node.Split(ECode.Core.Models.SplitDirection.Vertical);
        var pane2 = child2.PaneId!;

        var swapped = node.SwapPanes("pane-1", pane2);

        swapped.Should().BeTrue();
        node.GetLeaves().Select(l => l.PaneId).Should().Equal(pane2, "pane-1");
    }
}

/// <summary>
/// 终端颜色测试 - 验证 256 色索引和 RGB 颜色的创建与解析
/// </summary>
public class TerminalColorTests
{
    [Fact]
    public void FromIndex_BasicColors_ReturnsExpected()
    {
        var black = TerminalColor.FromIndex(0);
        black.R.Should().Be(0);
        black.G.Should().Be(0);
        black.B.Should().Be(0);

        var white = TerminalColor.FromIndex(15);
        white.R.Should().Be(0xFF);
        white.G.Should().Be(0xFF);
        white.B.Should().Be(0xFF);
    }

    [Fact]
    public void FromIndex_256Colors_DoesNotThrow()
    {
        for (int i = 0; i < 256; i++)
        {
            var act = () => TerminalColor.FromIndex(i);
            act.Should().NotThrow();
        }
    }

    [Fact]
    public void FromRgb_StoresCorrectValues()
    {
        var color = TerminalColor.FromRgb(0x12, 0x34, 0x56);
        color.R.Should().Be(0x12);
        color.G.Should().Be(0x34);
        color.B.Should().Be(0x56);
        color.IsDefault.Should().BeFalse();
    }

    [Fact]
    public void Default_IsMarkedAsDefault()
    {
        var def = TerminalColor.Default;
        def.IsDefault.Should().BeTrue();
    }
}

/// <summary>
/// 终端选择测试 - 验证文本选择的开始、扩展、清除和跨行选择提取
/// </summary>
public class TerminalSelectionTests
{
    [Fact]
    public void StartSelection_WithoutDrag_DoesNotSelectSingleCell()
    {
        var selection = new TerminalSelection();

        selection.StartSelection(4, 8);

        selection.HasSelection.Should().BeFalse();
        selection.IsSelected(4, 8).Should().BeFalse();
    }

    [Fact]
    public void StartAndExtend_CreatesSelection()
    {
        var selection = new TerminalSelection();
        selection.StartSelection(0, 5);
        selection.ExtendSelection(0, 10);

        selection.HasSelection.Should().BeTrue();
        selection.IsSelected(0, 7).Should().BeTrue();
        selection.IsSelected(0, 12).Should().BeFalse();
    }

    [Fact]
    public void Clear_RemovesSelection()
    {
        var selection = new TerminalSelection();
        selection.StartSelection(0, 0);
        selection.ExtendSelection(0, 10);

        selection.ClearSelection();

        selection.HasSelection.Should().BeFalse();
    }

    [Fact]
    public void GetSelectedText_ExtractsCorrectly()
    {
        var buffer = new TerminalBuffer(80, 24);
        buffer.WriteString("Hello World");

        var selection = new TerminalSelection();
        selection.StartSelection(0, 0);
        selection.ExtendSelection(0, 4);

        var text = selection.GetSelectedText(buffer);
        text.Should().Be("Hello");
    }

    [Fact]
    public void IsSelected_MultiLine_Works()
    {
        var selection = new TerminalSelection();
        selection.StartSelection(0, 5);
        selection.ExtendSelection(2, 10);

        selection.IsSelected(0, 6).Should().BeTrue();
        selection.IsSelected(1, 0).Should().BeTrue(); // 中间行，整行选中
        selection.IsSelected(2, 5).Should().BeTrue();
        selection.IsSelected(2, 11).Should().BeFalse();
    }
}


/// <summary>
/// 备用屏幕缓冲区测试 - 验证主屏幕和备用屏幕的切换及状态保存/恢复
/// </summary>
public class AlternateScreenBufferTests
{
    [Fact]
    public void SwitchToAlternateScreen_ClearsAndSavesMainBuffer()
    {
        var buffer = new TerminalBuffer(80, 24);
        buffer.WriteChar('X');
        buffer.CursorCol.Should().Be(1);

        buffer.SwitchToAlternateScreen();

        buffer.IsAlternateScreen.Should().BeTrue();
        buffer.CursorRow.Should().Be(0);
        buffer.CursorCol.Should().Be(0);
        buffer.CellAt(0, 0).Character.Should().Be(' ');
    }

    [Fact]
    public void SwitchToMainScreen_RestoresPreviousState()
    {
        var buffer = new TerminalBuffer(80, 24);
        buffer.WriteChar('A');
        buffer.WriteChar('B');
        int savedCol = buffer.CursorCol;

        buffer.SwitchToAlternateScreen();
        buffer.WriteChar('Z');

        buffer.SwitchToMainScreen();

        buffer.IsAlternateScreen.Should().BeFalse();
        buffer.CursorCol.Should().Be(savedCol);
        buffer.CellAt(0, 0).Character.Should().Be('A');
        buffer.CellAt(0, 1).Character.Should().Be('B');
    }

    [Fact]
    public void SwitchToAlternateScreen_DoubleSwitchIsNoop()
    {
        var buffer = new TerminalBuffer(80, 24);
        buffer.WriteChar('X');

        buffer.SwitchToAlternateScreen();
        buffer.WriteChar('Y');

        buffer.SwitchToAlternateScreen();

        buffer.CellAt(0, 0).Character.Should().Be('Y');
    }

    [Fact]
    public void SwitchToMainScreen_WhenNotAlternate_IsNoop()
    {
        var buffer = new TerminalBuffer(80, 24);
        buffer.WriteChar('X');

        buffer.SwitchToMainScreen();

        buffer.IsAlternateScreen.Should().BeFalse();
        buffer.CellAt(0, 0).Character.Should().Be('X');
    }
}

/// <summary>
/// 终端模式测试 - 验证光标键模式和括号粘贴模式的默认值和设置
/// </summary>
public class TerminalModeTests
{
    [Fact]
    public void ApplicationCursorKeys_DefaultsToFalse()
    {
        var buffer = new TerminalBuffer(80, 24);
        buffer.ApplicationCursorKeys.Should().BeFalse();
    }

    [Fact]
    public void BracketedPasteMode_DefaultsToFalse()
    {
        var buffer = new TerminalBuffer(80, 24);
        buffer.BracketedPasteMode.Should().BeFalse();
    }

    [Fact]
    public void ApplicationCursorKeys_CanBeSet()
    {
        var buffer = new TerminalBuffer(80, 24);
        buffer.ApplicationCursorKeys = true;
        buffer.ApplicationCursorKeys.Should().BeTrue();
    }

    [Fact]
    public void BracketedPasteMode_CanBeSet()
    {
        var buffer = new TerminalBuffer(80, 24);
        buffer.BracketedPasteMode = true;
        buffer.BracketedPasteMode.Should().BeTrue();
    }
}

/// <summary>
/// URL 检测器测试 - 验证从终端缓冲区行中检测和提取 HTTP/HTTPS URL
/// </summary>
public class UrlDetectorTests
{
    [Fact]
    public void FindUrls_DetectsHttps()
    {
        var urls = UrlDetector.FindUrls("Visit https://example.com/path for info");
        urls.Should().HaveCount(1);
        urls[0].url.Should().Be("https://example.com/path");
        urls[0].startCol.Should().Be(6);
    }

    [Fact]
    public void FindUrls_DetectsMultipleUrls()
    {
        var urls = UrlDetector.FindUrls("Go to http://a.com and https://b.io/x");
        urls.Should().HaveCount(2);
    }

    [Fact]
    public void FindUrls_NoUrlsReturnsEmpty()
    {
        var urls = UrlDetector.FindUrls("No urls here just text");
        urls.Should().BeEmpty();
    }

    [Fact]
    public void GetRowText_ExtractsBufferRow()
    {
        var buffer = new TerminalBuffer(10, 1);
        buffer.WriteChar('H');
        buffer.WriteChar('i');
        var text = UrlDetector.GetRowText(buffer, 0);
        text.Should().StartWith("Hi");
        text.Should().HaveLength(10);
    }
}

/// <summary>
/// 鼠标模式测试 - 验证鼠标跟踪模式（Normal/Button/Any）和 SGR 扩展模式的默认值和启用状态
/// </summary>
public class MouseModeTests
{
    [Fact]
    public void MouseTrackingModes_DefaultToFalse()
    {
        var buffer = new TerminalBuffer(80, 24);
        buffer.MouseTrackingNormal.Should().BeFalse();
        buffer.MouseTrackingButton.Should().BeFalse();
        buffer.MouseTrackingAny.Should().BeFalse();
        buffer.MouseSgrExtended.Should().BeFalse();
        buffer.MouseEnabled.Should().BeFalse();
    }

    [Fact]
    public void MouseEnabled_TrueWhenAnyTrackingSet()
    {
        var buffer = new TerminalBuffer(80, 24);
        buffer.MouseTrackingNormal = true;
        buffer.MouseEnabled.Should().BeTrue();
    }

    [Fact]
    public void MouseEnabled_TrueWhenButtonTrackingSet()
    {
        var buffer = new TerminalBuffer(80, 24);
        buffer.MouseTrackingButton = true;
        buffer.MouseEnabled.Should().BeTrue();
    }
}
