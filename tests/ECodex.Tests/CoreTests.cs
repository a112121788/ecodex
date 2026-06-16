using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.Json;
using ECodex.Core.Config;
using ECodex.Core.IPC;
using ECodex.Core.IPC.V2;
using ECodex.Core.Models;
using ECodex.Core.Services;
using ECodex.Core.Terminal;
using ECodex.Services;
using ECodex.Cli.Commands;
using ECodex.Updater;
using FluentAssertions;
using Xunit;

namespace ECodex.Tests;

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
    [InlineData("dotnet test tests/ECodex.Tests/ECodex.Tests.csproj")]
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

public class NotificationApiServiceTests
{
    [Fact]
    public void NotificationList_FiltersUnreadByWorkspace()
    {
        var service = new NotificationService();
        service.Notifications.Add(CreateNotification("read-target", isRead: true, workspaceId: "workspace-1"));
        service.Notifications.Add(CreateNotification("unread-target", isRead: false, workspaceId: "workspace-1"));
        service.Notifications.Add(CreateNotification("unread-other", isRead: false, workspaceId: "workspace-2"));
        var api = new ECodex.Services.NotificationApiService(service);

        var response = api.HandleRequest(CreateV2Request("notification.list", """{"workspaceId":"workspace-1","unreadOnly":true}"""));

        response.Error.Should().BeNull();
        using var result = ParseResult(response);
        var notifications = result.RootElement.GetProperty("notifications");
        notifications.GetArrayLength().Should().Be(1);
        notifications[0].GetProperty("id").GetString().Should().Be("unread-target");
        result.RootElement.GetProperty("unread").GetInt32().Should().Be(2);
    }

    [Fact]
    public void NotificationReadAndUnread_UpdateState()
    {
        var service = new NotificationService();
        service.Notifications.Add(CreateNotification("target", isRead: false));
        var api = new ECodex.Services.NotificationApiService(service);

        var read = api.HandleRequest(CreateV2Request("notification.read", """{"target":"target"}"""));

        read.Error.Should().BeNull();
        service.Notifications.Single().IsRead.Should().BeTrue();

        var unread = api.HandleRequest(CreateV2Request("notification.unread", """{"id":"target"}"""));

        unread.Error.Should().BeNull();
        service.Notifications.Single().IsRead.Should().BeFalse();
    }

    [Fact]
    public void NotificationJumpLatest_UsesLatestUnreadAndCallback()
    {
        var service = new NotificationService();
        service.Notifications.Add(CreateNotification("older", isRead: false, timestamp: new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc)));
        service.Notifications.Add(CreateNotification("latest", isRead: false, timestamp: new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc)));
        string? jumpedId = null;
        var api = new ECodex.Services.NotificationApiService(
            service,
            notification =>
            {
                jumpedId = notification?.Id;
                if (notification != null)
                    service.MarkAsRead(notification.Id);
                return notification != null;
            });

        var response = api.HandleRequest(CreateV2Request("notification.jump-latest", "{}"));

        response.Error.Should().BeNull();
        jumpedId.Should().Be("latest");
        service.Notifications.Single(n => n.Id == "latest").IsRead.Should().BeTrue();
        using var result = ParseResult(response);
        result.RootElement.GetProperty("jumped").GetBoolean().Should().BeTrue();
        result.RootElement.GetProperty("notification").GetProperty("id").GetString().Should().Be("latest");
    }

    [Fact]
    public void NotificationClear_RemovesAllNotifications()
    {
        var service = new NotificationService();
        service.Notifications.Add(CreateNotification("first", isRead: false));
        service.Notifications.Add(CreateNotification("second", isRead: true));
        var api = new ECodex.Services.NotificationApiService(service);

        var response = api.HandleRequest(CreateV2Request("notification.clear", "{}"));

        response.Error.Should().BeNull();
        service.Notifications.Should().BeEmpty();
        using var result = ParseResult(response);
        result.RootElement.GetProperty("cleared").GetInt32().Should().Be(2);
    }

    private static V2Request CreateV2Request(string method, string parameters)
    {
        return new V2Request
        {
            Id = JsonSerializer.SerializeToElement("test-request"),
            Method = method,
            Params = JsonDocument.Parse(parameters).RootElement.Clone(),
        };
    }

    private static JsonDocument ParseResult(V2Response response)
    {
        response.Result.Should().NotBeNull();
        return JsonDocument.Parse(JsonSerializer.Serialize(response.Result));
    }

    private static TerminalNotification CreateNotification(
        string id,
        bool isRead,
        DateTime? timestamp = null,
        string workspaceId = "workspace-1",
        string surfaceId = "surface-1",
        string paneId = "pane-1") => new()
    {
        Id = id,
        WorkspaceId = workspaceId,
        SurfaceId = surfaceId,
        PaneId = paneId,
        IsRead = isRead,
        Title = id,
        Body = id,
        Timestamp = timestamp ?? DateTime.UtcNow,
        Source = NotificationSource.Cli,
    };
}

public class ConfigApiServiceTests
{
    [Fact]
    public void ConfigReload_ReturnsReloadPayloadAndCachesDiagnostics()
    {
        var reloads = 0;
        var api = new ECodex.Services.ConfigApiService(() =>
        {
            reloads++;
            return """
                {
                  "ok": false,
                  "loadedPaths": ["/repo/.ecodex/ecodex.json"],
                  "commandCount": 2,
                  "diagnostics": [
                    { "severity": "error", "path": "/repo/.ecodex/ecodex.json", "message": "bad config" }
                  ]
                }
                """;
        });

        var reload = api.HandleRequest(CreateV2Request("config.reload", "{}"));

        reload.Error.Should().BeNull();
        reloads.Should().Be(1);
        using var reloadResult = ParseResult(reload);
        reloadResult.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        reloadResult.RootElement.GetProperty("commandCount").GetInt32().Should().Be(2);

        var diagnostics = api.HandleRequest(CreateV2Request("config.diagnostics", "{}"));

        diagnostics.Error.Should().BeNull();
        reloads.Should().Be(1);
        using var diagnosticsResult = ParseResult(diagnostics);
        diagnosticsResult.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
        diagnosticsResult.RootElement.GetProperty("loadedPaths")[0].GetString().Should().Be("/repo/.ecodex/ecodex.json");
        diagnosticsResult.RootElement.GetProperty("diagnostics")[0].GetProperty("message").GetString().Should().Be("bad config");
    }

    [Fact]
    public void ConfigDiagnostics_TriggersReloadWhenNoCachedResultExists()
    {
        var reloads = 0;
        var api = new ECodex.Services.ConfigApiService(() =>
        {
            reloads++;
            return """{"ok":true,"loadedPaths":[],"diagnostics":[]}""";
        });

        var diagnostics = api.HandleRequest(CreateV2Request("config.diagnostics", "{}"));

        diagnostics.Error.Should().BeNull();
        reloads.Should().Be(1);
    }

    private static V2Request CreateV2Request(string method, string parameters)
    {
        return new V2Request
        {
            Id = JsonSerializer.SerializeToElement("test-request"),
            Method = method,
            Params = JsonDocument.Parse(parameters).RootElement.Clone(),
        };
    }

    private static JsonDocument ParseResult(V2Response response)
    {
        response.Result.Should().NotBeNull();
        return JsonDocument.Parse(JsonSerializer.Serialize(response.Result));
    }
}

public class StatusApiServiceTests
{
    [Fact]
    public void Status_ReturnsStatusPayload()
    {
        var api = new ECodex.Services.StatusApiService(() => """
            {
              "version": "0.2.0",
              "workspaces": 2,
              "selectedWorkspace": "workspace-a",
              "unreadNotifications": 3
            }
            """);

        var response = api.HandleRequest(CreateV2Request("status", "{}"));

        response.Error.Should().BeNull();
        using var result = ParseResult(response);
        result.RootElement.GetProperty("version").GetString().Should().Be("0.2.0");
        result.RootElement.GetProperty("workspaces").GetInt32().Should().Be(2);
        result.RootElement.GetProperty("selectedWorkspace").GetString().Should().Be("workspace-a");
        result.RootElement.GetProperty("unreadNotifications").GetInt32().Should().Be(3);
    }

    [Fact]
    public void Health_ReturnsOkChecksAndEmbeddedStatus()
    {
        var api = new ECodex.Services.StatusApiService(() => """
            {
              "version": "0.2.0",
              "workspaces": 1,
              "selectedWorkspace": null,
              "unreadNotifications": 0
            }
            """);

        var response = api.HandleRequest(CreateV2Request("health", "{}"));

        response.Error.Should().BeNull();
        using var result = ParseResult(response);
        result.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        result.RootElement.GetProperty("status").GetProperty("version").GetString().Should().Be("0.2.0");
        result.RootElement.GetProperty("status").GetProperty("workspaces").GetInt32().Should().Be(1);
        result.RootElement.GetProperty("checks")[0].GetProperty("name").GetString().Should().Be("status");
        result.RootElement.GetProperty("checks")[0].GetProperty("ok").GetBoolean().Should().BeTrue();
    }

    private static V2Request CreateV2Request(string method, string parameters)
    {
        return new V2Request
        {
            Id = JsonSerializer.SerializeToElement("test-request"),
            Method = method,
            Params = JsonDocument.Parse(parameters).RootElement.Clone(),
        };
    }

    private static JsonDocument ParseResult(V2Response response)
    {
        response.Result.Should().NotBeNull();
        return JsonDocument.Parse(JsonSerializer.Serialize(response.Result));
    }
}

public class ShellSetupTests
{
    [Fact]
    public void InstallThenUninstall_RestoresPathAndProfileBlocks()
    {
        var installDirectory = @"C:\Tools\ECodex";
        var before = new ShellSetupState(
            UserPath: @"C:\Windows;C:\Tools",
            PowerShellProfile: "Write-Host 'hello'",
            CmdAutoRun: "@echo off");

        var installed = ShellSetup.CreateInstallPlan(before, installDirectory);

        installed.UserPath.Should().Be(@"C:\Windows;C:\Tools;C:\Tools\ECodex");
        installed.PowerShellProfile.Should().Contain(ShellSetup.PowerShellBeginMarker);
        installed.PowerShellProfile.Should().Contain("$ecodexBin = 'C:\\Tools\\ECodex'");
        installed.CmdAutoRun.Should().Contain(ShellSetup.CmdBeginMarker);
        installed.CmdAutoRun.Should().Contain(@"doskey ecodex=""C:\Tools\ECodex\ecodex.exe"" $*");

        var uninstalled = ShellSetup.CreateUninstallPlan(installed, installDirectory);

        uninstalled.UserPath.Should().Be(before.UserPath);
        uninstalled.PowerShellProfile.Should().Be(before.PowerShellProfile);
        uninstalled.CmdAutoRun.Should().Be(before.CmdAutoRun);
    }

    [Fact]
    public void InstallPlan_IsIdempotentAndMatchesPathCaseInsensitively()
    {
        var installDirectory = @"C:\Tools\ECodex";
        var before = new ShellSetupState(
            UserPath: @"C:\Windows;c:\tools\ecodex\",
            PowerShellProfile: "",
            CmdAutoRun: "");

        var installed = ShellSetup.CreateInstallPlan(before, installDirectory);
        var installedAgain = ShellSetup.CreateInstallPlan(installed, installDirectory);

        installedAgain.UserPath.Split(';').Count(entry =>
            string.Equals(entry.TrimEnd('\\'), installDirectory, StringComparison.OrdinalIgnoreCase))
            .Should().Be(1);
        CountOccurrences(installedAgain.PowerShellProfile, ShellSetup.PowerShellBeginMarker).Should().Be(1);
        CountOccurrences(installedAgain.CmdAutoRun, ShellSetup.CmdBeginMarker).Should().Be(1);
    }

    [Fact]
    public void IsInstalledAndDiff_ReportSetupDrift()
    {
        var installDirectory = @"C:\Tools\ECodex";
        var current = new ShellSetupState(
            UserPath: @"C:\Windows",
            PowerShellProfile: "",
            CmdAutoRun: "");

        var planned = ShellSetup.CreateInstallPlan(current, installDirectory);
        var diff = ShellSetup.CreateDiff(current, planned);

        ShellSetup.IsInstalled(current, installDirectory).Should().BeFalse();
        diff.AnyChanged.Should().BeTrue();
        diff.UserPathChanged.Should().BeTrue();
        diff.PowerShellProfileChanged.Should().BeTrue();
        diff.CmdAutoRunChanged.Should().BeTrue();
        ShellSetup.FormatDiff(current, planned).Should().Contain("PATH: change");
        ShellSetup.IsInstalled(planned, installDirectory).Should().BeTrue();
    }

    [Fact]
    public void FormatDiff_MarksNoChangeWhenStatesMatch()
    {
        var state = new ShellSetupState(
            UserPath: @"C:\Windows;C:\Tools\ECodex",
            PowerShellProfile: "profile",
            CmdAutoRun: "autorun");

        ShellSetup.CreateDiff(state, state).AnyChanged.Should().BeFalse();
        ShellSetup.FormatDiff(state, state).Should().Contain("PowerShell profile: no change");
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}

public class PowerShellCompletionScriptTests
{
    [Fact]
    public void ECodexCompletionScript_RegistersCommandsAndShortRefs()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "scripts", "completions", "ecodex.ps1");
        var script = File.ReadAllText(path);

        script.Should().Contain("Register-ArgumentCompleter -Native -CommandName ecodex");
        script.Should().Contain("'workspace'");
        script.Should().Contain("'pane'");
        script.Should().Contain("'browser'");
        script.Should().Contain("'completion'");
        script.Should().Contain("'profile'");
        script.Should().Contain("'doctor'");
        script.Should().Contain("'setup'");
        script.Should().Contain("'update'");
        script.Should().Contain("'powershell'");
        script.Should().Contain("'window:'");
        script.Should().Contain("'workspace:'");
        script.Should().Contain("'surface:'");
        script.Should().Contain("'pane:'");
    }
}

public class InnoSetupScriptTests
{
    [Fact]
    public void ECodexInnoScript_InstallsAppCliAndCleanUninstallOnlyAppDir()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "installer", "ecodex.iss");
        var script = File.ReadAllText(path);

        script.Should().Contain("AppId={{5F31F460-32C4-4B16-BB0A-3B74E5D7E0A1}");
        script.Should().Contain("Source: \"..\\publish\\ecodex-win-x64-sc\\*\"");
        script.Should().Contain("Source: \"..\\publish\\ecodex-cli\\*\"");
        script.Should().Contain("Name: \"{group}\\ECodex\"");
        script.Should().Contain("Type: filesandordirs; Name: \"{app}\"");
        script.Should().NotContain("%USERPROFILE%\\.ecodex");
        script.Should().NotContain("{userappdata}\\.ecodex");
    }

    [Fact]
    public void ECodexInnoScript_UsesSimplifiedChineseInstallerAndUninstallerText()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "installer", "ecodex.iss");
        var script = File.ReadAllText(path);

        script.Should().Contain("Name: \"chinesesimplified\"; MessagesFile: \".\\Languages\\ChineseSimplified.isl\"");
        File.Exists(Path.Combine(AppContext.BaseDirectory, "installer", "Languages", "ChineseSimplified.isl")).Should().BeTrue();
        script.Should().Contain("Description: \"{cm:CreateDesktopIcon}\"");
        script.Should().Contain("GroupDescription: \"{cm:AdditionalIcons}\"");
        script.Should().Contain("Description: \"{cm:LaunchProgram,{#MyAppName}}\"");
        script.Should().NotContain("MessagesFile: \"compiler:Default.isl\"");
        script.Should().NotContain("Create a &desktop shortcut");
        script.Should().NotContain("Additional icons:");
        script.Should().NotContain("Launch ECodex");
    }
}

public class ReleaseWorkflowTests
{
    [Fact]
    public void ReleaseWorkflow_PublishesThreeAppRidsAndCliArtifact()
    {
        var path = Path.Combine(AppContext.BaseDirectory, ".github", "workflows", "release.yml");
        var workflow = File.ReadAllText(path);
        var publishScript = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scripts", "publish.ps1"));

        workflow.Should().Contain("workflow_dispatch:");
        workflow.Should().Contain("schedule:");
        workflow.Should().Contain("rid: [win-x64, win-x86, win-arm64]");
        workflow.Should().Contain("-Flavor SelfContained");
        workflow.Should().Contain("name: ecodex-${{ matrix.rid }}-sc");
        workflow.Should().Contain("-Flavor Cli");
        workflow.Should().Contain("name: ecodex-cli-win-x64");
        workflow.Should().Contain("actions/upload-artifact@v4");
        publishScript.Should().Contain("-p:NuGetAudit=false");
    }

    [Fact]
    public void ReleaseDrafter_UsesBacklogConfigNameAndCategorizedNotes()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, ".github", "release.yml");
        var workflowPath = Path.Combine(AppContext.BaseDirectory, ".github", "workflows", "release-drafter.yml");
        var config = File.ReadAllText(configPath);
        var workflow = File.ReadAllText(workflowPath);

        config.Should().Contain("name-template: 'v$RESOLVED_VERSION'");
        config.Should().Contain("version-resolver:");
        config.Should().Contain("exclude-labels:");
        config.Should().Contain("'skip-changelog'");
        config.Should().Contain("## Changes");
        workflow.Should().Contain("release-drafter/release-drafter@v6");
        workflow.Should().Contain("config-name: release.yml");
        workflow.Should().Contain("contents: write");
    }

    [Fact]
    public void DiscordReleaseNotification_PostsPublishedReleasePayload()
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "scripts", "discord-notify.ps1");
        var workflowPath = Path.Combine(AppContext.BaseDirectory, ".github", "workflows", "discord-release-notify.yml");
        var script = File.ReadAllText(scriptPath);
        var workflow = File.ReadAllText(workflowPath);

        script.Should().Contain("DISCORD_WEBHOOK_URL");
        script.Should().Contain("New-DiscordReleasePayload");
        script.Should().Contain("allowed_mentions");
        script.Should().Contain("Invoke-RestMethod");
        script.Should().Contain("ConvertTo-Json -Depth 8");
        script.Should().Contain("DryRun");
        workflow.Should().Contain("release:");
        workflow.Should().Contain("types: [published]");
        workflow.Should().Contain("workflow_dispatch:");
        workflow.Should().Contain("secrets.DISCORD_WEBHOOK_URL");
        workflow.Should().Contain("RELEASE_PRERELEASE");
        workflow.Should().Contain("./scripts/discord-notify.ps1 -Prerelease:$prerelease -DryRun:$dryRun");
    }
}

public class SmokeWorkflowTests
{
    [Fact]
    public void CiRunsSmokeWithUnicodeWorkingDirectory()
    {
        var workflow = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, ".github", "workflows", "ci.yml"));
        var ciScript = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scripts", "ci.ps1"));
        var smoke = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "tests", "ECodex.Smoke", "Program.cs"));

        workflow.Should().Contain("-IncludeSmoke");
        ciScript.Should().Contain("中文 目录/项目/");
        ciScript.Should().Contain("'run', '--project', $SmokeProject, '--configuration', $Config");
        smoke.Should().Contain("UnicodeRelativePathForCi = \"中文 目录/项目/\"");
        smoke.Should().Contain("TestUnicodeWorkingDirectory");
        smoke.Should().Contain("workingDirectory: unicodeProjectDir");
        smoke.Should().Contain("childProofFileName");
        smoke.Should().Contain("WaitForFile");
        smoke.Should().Contain("echo OK>");
        smoke.Should().Contain("ECODEX_SMOKE_UNICODE");
    }
}

public class TerminalControlSourceTests
{
    [Fact]
    public void TerminalControl_MarshalsSessionEventsToDispatcher()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "src", "ECodex", "Controls", "TerminalControl.cs"));

        source.Should().Contain("private int _redrawQueued;");
        source.Should().Contain("if (!Dispatcher.CheckAccess())");
        source.Should().Contain("Dispatcher.BeginInvoke(() =>");
        source.Should().Contain("OnRedraw();");
        source.Should().Contain("DispatchIfRequired(OnBell");
        source.Should().Contain("DispatchIfRequired(UpdateImeProxyPosition");
    }
}

public class PerfBudgetScriptTests
{
    [Fact]
    public void PerfBudgetScript_DefinesBudgetsReportsAndReleaseArtifact()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scripts", "perf", "measure.ps1"));
        var workflow = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, ".github", "workflows", "release.yml"));
        var ci = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "scripts", "ci.ps1"));

        script.Should().Contain("cold_start_no_restore");
        script.Should().Contain("ecodex_status");
        script.Should().Contain("browser_snapshot");
        script.Should().Contain("save_session_10_panes_3000_lines");
        script.Should().Contain("BudgetMs 1500");
        script.Should().Contain("BudgetMs 100");
        script.Should().Contain("BudgetMs 500");
        script.Should().Contain("BudgetMs 300");
        script.Should().Contain("perf-report.json");
        script.Should().Contain("perf-report.md");
        script.Should().Contain("ConvertTo-Json -Depth 20 -Compress");
        script.Should().Contain("FailOnBudget");
        workflow.Should().Contain("./scripts/perf/measure.ps1");
        workflow.Should().Contain("ecodex-perf-report");
        ci.Should().Contain("Get-ChildItem -LiteralPath (Join-Path $RepoRoot 'scripts') -Filter '*.ps1' -File -Recurse");
    }
}

public class CommunityTemplateTests
{
    [Fact]
    public void IssueTemplates_ProvideBugFeatureAndSpecializedFlows()
    {
        var root = Path.Combine(AppContext.BaseDirectory, ".github", "ISSUE_TEMPLATE");
        var bug = File.ReadAllText(Path.Combine(root, "bug_report.yml"));
        var feature = File.ReadAllText(Path.Combine(root, "feature_request.yml"));
        var ecodexJson = File.ReadAllText(Path.Combine(root, "ecodex_json_schema.yml"));
        var browser = File.ReadAllText(Path.Combine(root, "browser_pane.yml"));
        var sessionRestore = File.ReadAllText(Path.Combine(root, "session_restore.yml"));
        var config = File.ReadAllText(Path.Combine(root, "config.yml"));
        var combined = string.Join("\n", bug, feature, ecodexJson, browser, sessionRestore, config);

        bug.Should().Contain("name: Bug Report");
        bug.Should().Contain("labels: [\"bug\", \"triage\"]");
        bug.Should().Contain("ecodex-app.exe");
        bug.Should().Contain("%USERPROFILE%/.ecodex/daemon-debug.log");
        feature.Should().Contain("name: Feature / 能力请求");
        feature.Should().Contain("labels: [\"enhancement\", \"triage\"]");
        feature.Should().Contain("M1 - UI/UX 与 ecodex.json 基础");
        ecodexJson.Should().Contain("name: ecodex.json Schema / 命令面板");
        ecodexJson.Should().Contain("%USERPROFILE%\\.config\\ecodex\\ecodex.json");
        browser.Should().Contain("Browser Pane / WebView2 / Browser API");
        sessionRestore.Should().Contain("%USERPROFILE%/.ecodex/resume.json");
        sessionRestore.Should().Contain("AutoResumeTrustedBindings");
        config.Should().Contain("blank_issues_enabled: false");
        config.Should().Contain("github.com/a112121788/ecodex");
        combined.Should().NotContain("ecodexw.exe");
        combined.Should().NotContain("%LOCALAPPDATA%/ecodex");
        combined.Should().NotContain("AutoResumeAgentSessions");
        combined.Should().NotContain("manaflow-ai/cmux");
    }

    [Fact]
    public void PullRequestTemplate_CoversTestingDocsRiskAndCurrentPaths()
    {
        var template = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, ".github", "PULL_REQUEST_TEMPLATE.md"));

        template.Should().Contain("Backlog ID");
        template.Should().Contain("改动类型");
        template.Should().Contain(".\\.dotnet\\dotnet.exe build ECodex.sln -c Debug -p:NuGetAudit=false");
        template.Should().Contain(".\\.dotnet\\dotnet.exe test tests\\ECodex.Tests\\ECodex.Tests.csproj -p:NuGetAudit=false");
        template.Should().Contain("npm run docs:build");
        template.Should().Contain("CHANGELOG.md");
        template.Should().Contain("风险与回滚");
        template.Should().Contain("%USERPROFILE%\\.ecodex\\*.json");
        template.Should().NotContain("%LOCALAPPDATA%/ecodex");
        template.Should().NotContain("%LOCALAPPDATA%\\cmux");
    }
}

public class DocsSiteTests
{
    [Fact]
    public void VitePressDocs_DefinesBuildScriptAndCorePages()
    {
        var packageJsonPath = Path.Combine(AppContext.BaseDirectory, "package.json");
        var configPath = Path.Combine(AppContext.BaseDirectory, "docs", ".vitepress", "config.mts");
        var docsRoot = Path.Combine(AppContext.BaseDirectory, "docs");

        using var packageJson = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
        packageJson.RootElement.GetProperty("scripts").GetProperty("build").GetString()
            .Should().Be("vitepress build docs --outDir web");
        packageJson.RootElement.GetProperty("scripts").GetProperty("docs:build").GetString()
            .Should().Be("vitepress build docs");
        packageJson.RootElement.GetProperty("devDependencies").TryGetProperty("vitepress", out _)
            .Should().BeTrue();

        var config = File.ReadAllText(configPath);
        config.Should().Contain("title: 'ECodex'");
        config.Should().Contain("provider: 'local'");
        config.Should().Contain("link: '/getting-started'");

        foreach (var page in new[] { "index.md", "installation.md", "getting-started.md", "custom-commands.md", "session-restore.md", "cli.md", "browser-api.md", "troubleshooting.md", "release-readiness.md", "roadmap.md" })
            File.Exists(Path.Combine(docsRoot, page)).Should().BeTrue(page);
        File.Exists(Path.Combine(docsRoot, "release-notes", "1.0.0.md")).Should().BeTrue();
    }

    [Fact]
    public void InstallationDocs_CoverZipVelopackAndMsix()
    {
        var installation = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "installation.md"));

        installation.Should().Contain("zip / self-contained 目录");
        installation.Should().Contain("Velopack 安装器与 feed");
        installation.Should().Contain("MSIX 企业包");
        installation.Should().Contain("%USERPROFILE%\\.ecodex");
        installation.Should().Contain("ecodex doctor");
    }

    [Fact]
    public void GettingStartedDocs_AreSimplifiedChineseAndCoverFirstRunFlow()
    {
        var gettingStarted = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "getting-started.md"));

        gettingStarted.Should().Contain("# 快速上手");
        gettingStarted.Should().Contain("本页用 15 分钟带你完成 ECodex 的第一次使用");
        gettingStarted.Should().NotContain("## English");
        gettingStarted.Should().NotContain("## 中文");
        gettingStarted.Should().Contain("ecodex doctor");
        gettingStarted.Should().Contain("ecodex workspace create");
        gettingStarted.Should().Contain("ecodex pane split right");
        gettingStarted.Should().Contain("ecodex browser new");
        gettingStarted.Should().Contain("ecodex restore-session");
    }

    [Fact]
    public void CustomCommandsDocs_CoverSchemaTargetsLayoutAndReload()
    {
        var customCommands = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "custom-commands.md"));

        customCommands.Should().Contain("%USERPROFILE%\\.config\\ecodex\\ecodex.json");
        customCommands.Should().Contain("commands");
        customCommands.Should().Contain("actions");
        customCommands.Should().Contain("ui.surfaceTabBar.buttons");
        customCommands.Should().Contain("currentTerminal");
        customCommands.Should().Contain("newTabInCurrentPane");
        customCommands.Should().Contain("workspace.surfaces");
        customCommands.Should().Contain("selectedSurfaceIndex");
        customCommands.Should().Contain("type\": \"browser\"");
        customCommands.Should().Contain("ecodex config reload");
        customCommands.Should().Contain("ecodex config diagnostics");
        customCommands.Should().Contain("confirm: true");
    }

    [Fact]
    public void BrowserApiDocs_CoverSurfaceRefsActionsContractsAndUnsupportedMatrix()
    {
        var browserApi = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "browser-api.md"));

        browserApi.Should().Contain("ecodex browser open");
        browserApi.Should().Contain("surfaceRef");
        browserApi.Should().Contain("browser snapshot");
        browserApi.Should().Contain("--testid");
        browserApi.Should().Contain("--role");
        browserApi.Should().Contain("ecodex browser click");
        browserApi.Should().Contain("ecodex browser fill");
        browserApi.Should().Contain("ecodex browser hover");
        browserApi.Should().Contain("ecodex browser press");
        browserApi.Should().Contain("ecodex browser eval");
        browserApi.Should().Contain("ecodex browser screenshot");
        browserApi.Should().Contain("browser.cookies.get");
        browserApi.Should().Contain("browser.storage.get");
        browserApi.Should().Contain("browser.console.list");
        browserApi.Should().Contain("browser.addinitscript");
        browserApi.Should().Contain("browser.network.route");
        browserApi.Should().Contain("not_supported");
    }

    [Fact]
    public void SessionRestoreDocs_CoverRuntimeFilesSchemaTrustAndCli()
    {
        var sessionRestore = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "session-restore.md"));

        sessionRestore.Should().Contain("%USERPROFILE%\\.ecodex\\session.json");
        sessionRestore.Should().Contain("%USERPROFILE%\\.ecodex\\resume.json");
        sessionRestore.Should().Contain("paneSnapshots");
        sessionRestore.Should().Contain("browserHistory");
        sessionRestore.Should().Contain("kind\": \"tmux\"");
        sessionRestore.Should().Contain("trusted");
        sessionRestore.Should().Contain("AutoResumeTrustedBindings");
        sessionRestore.Should().Contain("PreserveDaemonSessionsOnClose");
        sessionRestore.Should().Contain("app.exit");
        sessionRestore.Should().Contain("ecodex surface resume set");
        sessionRestore.Should().Contain("ecodex surface resume show");
        sessionRestore.Should().Contain("ecodex surface resume clear");
        sessionRestore.Should().Contain("ecodex restore-session");
        sessionRestore.Should().Contain("Ctrl+Shift+O");
        sessionRestore.Should().Contain("ECODEX_WORKSPACE_ID");
        sessionRestore.Should().Contain("主动关闭 ECodex 只断开主程序与 daemon 的客户端连接");
        sessionRestore.Should().Contain("重开时仅自动挂载 `session.json` 中已有 paneId 对应的 daemon 会话");
    }

    [Fact]
    public void CliDocs_CoverGlobalFlagsV1V2AndOperationalCommands()
    {
        var cli = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "cli.md"));

        cli.Should().Contain("--id-format");
        cli.Should().Contain("--json");
        cli.Should().Contain("v1 兼容");
        cli.Should().Contain("ecodex.v2");
        cli.Should().Contain("ecodex notification list");
        cli.Should().Contain("window.list");
        cli.Should().Contain("workspace.reorder");
        cli.Should().Contain("surface.move");
        cli.Should().Contain("pane.write");
        cli.Should().Contain("ecodex browser open");
        cli.Should().Contain("ecodex reload-config");
        cli.Should().Contain("ecodex config reload");
        cli.Should().Contain("ecodex restore-session");
        cli.Should().Contain("单窗口模式下会聚焦现有窗口");
        cli.Should().Contain(@"Global\ECodexMainApp");
        cli.Should().Contain("app.exit");
        cli.Should().Contain("terminateTerminals");
        cli.Should().Contain("ecodex setup install");
        cli.Should().Contain("ecodex update check");
        cli.Should().Contain("ecodex doctor");
        cli.Should().Contain("completion powershell");
    }

    [Fact]
    public void TroubleshootingDocs_CoverDoctorDaemonLogsAndCommonFailures()
    {
        var troubleshooting = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "troubleshooting.md"));

        troubleshooting.Should().Contain("ecodex doctor");
        troubleshooting.Should().Contain("ecodex --json doctor");
        troubleshooting.Should().Contain("daemon-debug.log");
        troubleshooting.Should().Contain("ts=");
        troubleshooting.Should().Contain("component=");
        troubleshooting.Should().Contain("event=");
        troubleshooting.Should().Contain("paneId=");
        troubleshooting.Should().Contain("WebView2 Runtime");
        troubleshooting.Should().Contain("ecodex setup status");
        troubleshooting.Should().Contain("ecodex config diagnostics");
        troubleshooting.Should().Contain("AutoResumeTrustedBindings");
        troubleshooting.Should().Contain("ecodex update check");
        troubleshooting.Should().Contain("NuGetAudit=false");
    }

    [Fact]
    public void ReleaseReadinessDocs_CoverP0P1GateAndValidation()
    {
        var config = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", ".vitepress", "config.mts"));
        var readiness = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "release-readiness.md"));

        config.Should().Contain("发布就绪");
        config.Should().Contain("link: '/release-readiness'");
        readiness.Should().Contain("P0 缺陷数量必须为 0");
        readiness.Should().Contain("P1 缺陷数量必须不超过 3");
        readiness.Should().Contain("| P0 | 0 | 0 | 通过 | N/A |");
        readiness.Should().Contain("| P1 | 0 | 3 | 通过 | N/A；当前无仓库跟踪的 P1 阻塞项 |");
        readiness.Should().Contain("可执行的规避方案");
        readiness.Should().Contain("阻塞项台账");
        readiness.Should().Contain("npm run docs:build");
        readiness.Should().Contain("NuGetAudit=false");
        readiness.Should().Contain("scripts\\ci.ps1");
    }

    [Fact]
    public void ReleaseNotesDocs_CoverInstallUpgradeHighlightsAndLimits()
    {
        var config = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", ".vitepress", "config.mts"));
        var notes = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "release-notes", "1.0.0.md"));

        config.Should().Contain("发布说明");
        config.Should().Contain("link: '/release-notes/1.0.0'");
        notes.Should().Contain("ECodex 1.0.0 发布说明");
        notes.Should().Contain("推荐下载");
        notes.Should().Contain("ecodex-win-x64-sc");
        notes.Should().Contain("Velopack 安装器/feed");
        notes.Should().Contain("新特性");
        notes.Should().Contain("ecodex browser");
        notes.Should().Contain("ecodex.v2");
        notes.Should().Contain("%USERPROFILE%\\.ecodex");
        notes.Should().Contain("升级注意事项");
        notes.Should().Contain("已知限制");
        notes.Should().Contain("P0 = 0、P1 <= 3");
        notes.Should().Contain("故障排查");
    }

    [Fact]
    public void RoadmapDocs_CoverPublicVersionLineMilestonesAndReleaseGate()
    {
        var config = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", ".vitepress", "config.mts"));
        var index = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "index.md"));
        var roadmap = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "roadmap.md"));

        config.Should().Contain("link: '/roadmap'");
        index.Should().Contain("发布就绪");
        roadmap.Should().Contain("本公开路线图同步 `spec/06-roadmap.md`");
        roadmap.Should().Contain("## 版本线");
        roadmap.Should().Contain("| `0.1.x` | 工程基线");
        roadmap.Should().Contain("| `1.0.0` | Windows 稳定版发布");
        roadmap.Should().Contain("### M0 - 工程基线");
        roadmap.Should().Contain("### M7 - 文档、社区与 1.0");
        roadmap.Should().Contain("## 1.0 门槛");
        roadmap.Should().Contain("P0 缺陷数量 = 0");
        roadmap.Should().Contain("P1 缺陷数量 <= 3");
        roadmap.Should().Contain("%USERPROFILE%\\.ecodex");
        roadmap.Should().Contain("[发布就绪](./release-readiness.md)");
    }

    [Fact]
    public void SpecDocs_DefineSimplifiedChineseDocsPolicy()
    {
        var roadmapSpec = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "spec", "06-roadmap.md"));
        var backlogSpec = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "spec", "07-implementation-backlog.md"));

        roadmapSpec.Should().Contain("文档站语言要求");
        roadmapSpec.Should().Contain("简体中文单语");
        roadmapSpec.Should().Contain("不再维护同页中英双语内容");
        backlogSpec.Should().Contain("| `M7-A-03`");
        backlogSpec.Should().Contain("简体中文单语");
    }

    [Fact]
    public void ContributingDocs_CoverBuildTestAndPullRequestFlow()
    {
        var contributing = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CONTRIBUTING.md"));

        contributing.Should().Contain(".NET 10 SDK");
        contributing.Should().Contain("npm install");
        contributing.Should().Contain("npm run docs:build");
        contributing.Should().Contain("dotnet.exe test tests\\ECodex.Tests\\ECodex.Tests.csproj");
        contributing.Should().Contain("scripts\\ci.ps1");
        contributing.Should().Contain("-IncludeSmoke");
        contributing.Should().Contain("-IncludeBrowserIntegration");
        contributing.Should().Contain("Pull Request Flow");
        contributing.Should().Contain("CHANGELOG.md");
        contributing.Should().Contain("spec/07-implementation-backlog.md");
        contributing.Should().Contain("skip-changelog");
    }

    [Fact]
    public void SecurityDocs_CoverPrivateReportingScopeAndDisclosure()
    {
        var security = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SECURITY.md"));

        security.Should().Contain("Security Policy");
        security.Should().Contain("Supported Versions");
        security.Should().Contain("Report A Vulnerability");
        security.Should().Contain("Do not open a public issue");
        security.Should().Contain("GitHub's private vulnerability reporting");
        security.Should().Contain("What To Avoid Sharing Publicly");
        security.Should().Contain("%USERPROFILE%\\.ecodex\\daemon-debug.log");
        security.Should().Contain("Scope");
        security.Should().Contain("Handling Process");
        security.Should().Contain("Coordinated Disclosure");
    }
}

public class MsixManifestTests
{
    [Fact]
    public void AppXManifest_DefinesFullTrustDesktopPackage()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "installer", "AppXManifest.xml");
        var manifest = File.ReadAllText(path);
        var doc = new System.Xml.XmlDocument();

        doc.LoadXml(manifest);
        var ns = new System.Xml.XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("m", "http://schemas.microsoft.com/appx/manifest/foundation/windows10");
        ns.AddNamespace("uap", "http://schemas.microsoft.com/appx/manifest/uap/windows10");
        ns.AddNamespace("rescap", "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities");

        doc.SelectSingleNode("/m:Package/m:Identity[@Name='ECodex']", ns).Should().NotBeNull();
        doc.SelectSingleNode("/m:Package/m:Applications/m:Application[@Executable='ecodex-app.exe' and @EntryPoint='Windows.FullTrustApplication']", ns)
            .Should().NotBeNull();
        doc.SelectSingleNode("/m:Package/m:Applications/m:Application/uap:VisualElements[@Square150x150Logo='Assets\\Square150x150Logo.png']", ns)
            .Should().NotBeNull();
        doc.SelectSingleNode("/m:Package/m:Capabilities/rescap:Capability[@Name='runFullTrust']", ns)
            .Should().NotBeNull();
        doc.SelectSingleNode("/m:Package/m:Dependencies/m:TargetDeviceFamily[@Name='Windows.Desktop']", ns)
            .Should().NotBeNull();
    }
}

public class ReleaseVersionTests
{
    [Fact]
    public void ReleaseFiles_ArePinnedToVersionOne()
    {
        var props = new System.Xml.XmlDocument();
        props.Load(Path.Combine(AppContext.BaseDirectory, "Directory.Build.props"));

        var manifest = new System.Xml.XmlDocument();
        manifest.Load(Path.Combine(AppContext.BaseDirectory, "installer", "AppXManifest.xml"));
        var ns = new System.Xml.XmlNamespaceManager(manifest.NameTable);
        ns.AddNamespace("m", "http://schemas.microsoft.com/appx/manifest/foundation/windows10");

        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CHANGELOG.md"))
            .Should().Contain("## [1.0.0] - 2026-06-15");
        props.SelectSingleNode("/Project/PropertyGroup/Version")!.InnerText.Should().Be("1.0.0");
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "installer", "ecodex.iss"))
            .Should().Contain("#define MyAppVersion \"1.0.0\"");
        manifest.SelectSingleNode("/m:Package/m:Identity[@Version='1.0.0.0']", ns)
            .Should().NotBeNull();
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "src", "ECodex", "Views", "SettingsWindow.xaml"))
            .Should().Contain("Text=\"v1.0.0\"");
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "installation.md"))
            .Should().Contain("ECodex-win-x64-1.0.0.0.msix");
    }
}

public class RiskRegistryTests
{
    [Fact]
    public void RoadmapRiskRegistry_IsRefreshedForOnePointOh()
    {
        var roadmapSpec = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "spec", "06-roadmap.md"));
        var backlog = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "spec", "07-implementation-backlog.md"));

        roadmapSpec.Should().Contain("1.0.0 发布前刷新：2026-06-15");
        roadmapSpec.Should().Contain("P0=0、P1=0");
        roadmapSpec.Should().Contain("CI 已加入 `中文 目录/项目/` smoke");
        roadmapSpec.Should().Contain("release 上传 `ecodex-perf-report`");
        roadmapSpec.Should().Contain("R11");
        roadmapSpec.Should().Contain("Release webhook / token 缺失");
        roadmapSpec.Should().Contain("R12");
        roadmapSpec.Should().Contain("性能预算随功能增长漂移");
        backlog.Should().Contain("| `X-03` | [x] 风险登记刷新");
    }
}

public class DoctorTests
{
    [Fact]
    public void CreateReport_ReturnsExpectedChecksForHealthySnapshot()
    {
        var snapshot = new DoctorSnapshot(
            IsWindows: true,
            OsVersion: new Version(10, 0, 22631),
            PathValue: @"C:\Windows;C:\Tools\ECodex",
            AppDirectory: @"C:\Tools\ECodex",
            AppDataDirectory: @"C:\Users\me\.ecodex",
            AppDataDirectoryExists: true,
            WebView2Available: true,
            WebView2Detail: "WebView2 Runtime found",
            DaemonAvailable: true,
            DaemonDetail: "Main app pipe responded");

        var report = Doctor.CreateReport(snapshot);

        report.Ok.Should().BeTrue();
        report.Checks.Select(check => check.Name)
            .Should().Equal("conpty", "webview2", "path", "daemon", "config");
        report.Checks.Should().OnlyContain(check => check.Status == "ok");
        Doctor.FormatHuman(report).Should().Contain("[ok] conpty:");
    }

    [Fact]
    public void CreateReport_FailsConPtyOnOldWindowsButKeepsRecoverableWarningsNonFatal()
    {
        var snapshot = new DoctorSnapshot(
            IsWindows: true,
            OsVersion: new Version(10, 0, 17134),
            PathValue: @"C:\Windows",
            AppDirectory: @"C:\Tools\ECodex",
            AppDataDirectory: @"C:\Users\me\.ecodex",
            AppDataDirectoryExists: false,
            WebView2Available: false,
            WebView2Detail: "missing",
            DaemonAvailable: false,
            DaemonDetail: "not running");

        var report = Doctor.CreateReport(snapshot);

        report.Ok.Should().BeFalse();
        report.Checks.Single(check => check.Name == "conpty").Status.Should().Be("fail");
        report.Checks.Single(check => check.Name == "webview2").Status.Should().Be("warn");
        report.Checks.Single(check => check.Name == "path").Status.Should().Be("warn");
        report.Checks.Single(check => check.Name == "daemon").Status.Should().Be("warn");
        report.Checks.Single(check => check.Name == "config").Status.Should().Be("warn");
    }

    [Fact]
    public void FormatJson_IncludesOverallOkAndChecks()
    {
        var report = Doctor.CreateReport(new DoctorSnapshot(
            IsWindows: true,
            OsVersion: new Version(10, 0, 22631),
            PathValue: @"C:\Tools\ECodex",
            AppDirectory: @"C:\Tools\ECodex",
            AppDataDirectory: @"C:\Users\me\.ecodex",
            AppDataDirectoryExists: true,
            WebView2Available: true,
            WebView2Detail: "found",
            DaemonAvailable: true,
            DaemonDetail: "running"));

        using var doc = JsonDocument.Parse(Doctor.FormatJson(report));

        doc.RootElement.GetProperty("Ok").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("Checks")[0].GetProperty("Name").GetString().Should().Be("conpty");
    }
}

public class ProfileImportTests
{
    [Fact]
    public void CreateImportPlan_AddsWindowsTerminalProfileSchemeAndFont()
    {
        var options = new ProfileImportOptions(
            ProfileName: "ECodex Dev",
            ProfileGuid: "{11111111-2222-3333-4444-555555555555}",
            CommandLine: "pwsh.exe -NoLogo",
            StartingDirectory: @"C:\repo",
            ColorSchemeName: "ECodex Test",
            FontFace: "Cascadia Code",
            FontSize: 12);

        var plan = ProfileImport.CreateImportPlan("{}", options);

        plan.ProfileAdded.Should().BeTrue();
        plan.SchemeAdded.Should().BeTrue();
        using var doc = JsonDocument.Parse(plan.SettingsJson);
        var profile = doc.RootElement.GetProperty("profiles").GetProperty("list")[0];
        profile.GetProperty("guid").GetString().Should().Be(options.ProfileGuid);
        profile.GetProperty("name").GetString().Should().Be(options.ProfileName);
        profile.GetProperty("commandline").GetString().Should().Be(options.CommandLine);
        profile.GetProperty("startingDirectory").GetString().Should().Be(options.StartingDirectory);
        profile.GetProperty("colorScheme").GetString().Should().Be(options.ColorSchemeName);
        profile.GetProperty("font").GetProperty("face").GetString().Should().Be(options.FontFace);
        profile.GetProperty("font").GetProperty("size").GetDouble().Should().Be(12);
        doc.RootElement.GetProperty("schemes")[0].GetProperty("name").GetString().Should().Be(options.ColorSchemeName);
    }

    [Fact]
    public void CreateImportPlan_UpdatesExistingProfileByNameWithoutDuplicatingScheme()
    {
        var existing = """
            {
              // Windows Terminal allows comments and trailing commas.
              "profiles": {
                "list": [
                  {
                    "guid": "{aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee}",
                    "name": "ECodex Shell",
                    "commandline": "powershell.exe",
                    "font": { "face": "Consolas", "size": 10 }
                  },
                ],
              },
              "schemes": [
                { "name": "ECodex Dark", "background": "#000000" },
              ],
            }
            """;

        var plan = ProfileImport.CreateImportPlan(existing, new ProfileImportOptions(FontFace: "Cascadia Mono"));

        plan.ProfileAdded.Should().BeFalse();
        plan.ProfileUpdated.Should().BeTrue();
        plan.SchemeAdded.Should().BeFalse();
        using var doc = JsonDocument.Parse(plan.SettingsJson);
        var profiles = doc.RootElement.GetProperty("profiles").GetProperty("list");
        profiles.GetArrayLength().Should().Be(1);
        profiles[0].GetProperty("guid").GetString().Should().Be("{7f4f7d8d-7a1f-45f3-b0c7-ec0de0000001}");
        profiles[0].GetProperty("commandline").GetString().Should().Be("pwsh.exe -NoLogo");
        profiles[0].GetProperty("font").GetProperty("face").GetString().Should().Be("Cascadia Mono");
        doc.RootElement.GetProperty("schemes").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void CreateImportPlan_MigratesLegacyProfilesArray()
    {
        var existing = """
            {
              "profiles": [
                { "name": "PowerShell", "commandline": "powershell.exe" }
              ]
            }
            """;

        var plan = ProfileImport.CreateImportPlan(existing, new ProfileImportOptions(ProfileName: "ECodex Legacy"));

        using var doc = JsonDocument.Parse(plan.SettingsJson);
        var profiles = doc.RootElement.GetProperty("profiles").GetProperty("list");
        profiles.GetArrayLength().Should().Be(2);
        profiles[0].GetProperty("name").GetString().Should().Be("PowerShell");
        profiles[1].GetProperty("name").GetString().Should().Be("ECodex Legacy");
    }
}

public class VelopackFeedCheckerTests
{
    [Fact]
    public void ParseReleases_ReturnsPackageEntries()
    {
        var releases = """
            0123456789abcdef ECodex-0.2.0-full.nupkg 1024
            abcdef0123456789 ECodex-0.3.0-full.nupkg 2048
            ignored.txt 1
            """;

        var entries = VelopackFeedChecker.ParseReleases(releases);

        entries.Should().HaveCount(2);
        entries[0].FileName.Should().Be("ECodex-0.2.0-full.nupkg");
        entries[0].Version.Should().Be("0.2.0");
        entries[0].SizeBytes.Should().Be(1024);
    }

    [Fact]
    public void TryGetLatestRelease_ChoosesHighestVersion()
    {
        var releases = """
            hash ECodex-0.2.1-full.nupkg 1024
            hash ECodex-0.10.0-full.nupkg 1024
            hash ECodex-0.3.0-full.nupkg 1024
            """;

        var latest = VelopackFeedChecker.TryGetLatestRelease(releases);

        latest.Should().NotBeNull();
        latest!.Version.Should().Be("0.10.0");
    }

    [Theory]
    [InlineData("0.3.0", "0.2.0", true)]
    [InlineData("0.2.0", "0.2.0", false)]
    [InlineData("0.1.9", "0.2.0", false)]
    public void IsNewer_ComparesSemanticVersionCore(string candidate, string current, bool expected)
    {
        VelopackFeedChecker.IsNewer(candidate, current).Should().Be(expected);
    }

    [Fact]
    public void ResolveUris_HandlesFeedRootAndReleasesUrl()
    {
        var feedRoot = new Uri("https://example.test/ecodex");
        var releases = new Uri("https://example.test/ecodex/RELEASES");

        VelopackFeedChecker.ResolveReleasesUri(feedRoot).ToString()
            .Should().Be("https://example.test/ecodex/RELEASES");
        VelopackFeedChecker.GetFeedRootUri(releases).ToString()
            .Should().Be("https://example.test/ecodex/");
        VelopackFeedChecker.ResolvePackageUri(releases, "ECodex-0.3.0-full.nupkg").ToString()
            .Should().Be("https://example.test/ecodex/ECodex-0.3.0-full.nupkg");
    }
}

public class VelopackUpdateInstallerTests
{
    [Fact]
    public void CreatePlan_DefaultsToSilentSetupBesideFeed()
    {
        var plan = VelopackUpdateInstaller.CreatePlan(
            new Uri("https://example.test/releases/RELEASES"),
            "ECodex",
            @"C:\Users\me\.ecodex\updates");

        plan.SetupUri.ToString().Should().Be("https://example.test/releases/ECodex-Setup.exe");
        plan.SetupFileName.Should().Be("ECodex-Setup.exe");
        plan.SetupPath.Should().Be(@"C:\Users\me\.ecodex\updates\ECodex-Setup.exe");
        plan.Arguments.Should().Equal("--silent");
    }

    [Fact]
    public void CreatePlan_UsesExplicitSetupUrlAndCanDisableSilent()
    {
        var plan = VelopackUpdateInstaller.CreatePlan(
            new Uri("https://example.test/releases"),
            "ECodex",
            @"C:\cache",
            silent: false,
            waitForExit: true,
            setupUri: new Uri("https://cdn.test/ECodexPreviewSetup.exe"));

        plan.SetupUri.ToString().Should().Be("https://cdn.test/ECodexPreviewSetup.exe");
        plan.SetupFileName.Should().Be("ECodexPreviewSetup.exe");
        plan.WaitForExit.Should().BeTrue();
        plan.Arguments.Should().BeEmpty();
    }
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
    public void DaemonProtocol_DoesNotExposeStandaloneCloseAllRequest()
    {
        var messages = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "src", "ECodex.Core", "IPC", "DaemonMessages.cs"));
        var client = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "src", "ECodex.Core", "IPC", "DaemonClient.cs"));
        var server = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "src", "ECodex.Daemon", "DaemonPipeServer.cs"));

        messages.Should().NotContain("SESSION_CLOSE_ALL");
        messages.Should().NotContain("SessionCloseAll");
        client.Should().NotContain("CloseAllSessionsAsync");
        server.Should().NotContain("HandleSessionCloseAll");
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
    public void ParseRequest_AcceptsECodexV2Request()
    {
        var parsed = V2Protocol.ParseRequest("""
            {
              "protocol": "ecodex.v2",
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

public class NamedPipeProtocolTests
{
    [Fact]
    public void ParseFirstLine_ParsesLegacyV1Command()
    {
        var parsed = NamedPipeProtocol.ParseFirstLine("BROWSER.OPEN {\"url\":\"https://example.com\"}");

        parsed.Kind.Should().Be(NamedPipeProtocolKind.V1);
        parsed.V1.Should().NotBeNull();
        parsed.V1!.Command.Should().Be("BROWSER.OPEN");
        parsed.V1.Args.Should().ContainKey("url").WhoseValue.Should().Be("https://example.com");
        parsed.V2.Should().BeNull();
        parsed.V2ErrorResponse.Should().BeNull();
    }

    [Fact]
    public void ParseFirstLine_ParsesECodexV2JsonRequest()
    {
        var parsed = NamedPipeProtocol.ParseFirstLine("""
            {"protocol":"ecodex.v2","id":"req-1","method":"status","params":{"verbose":true}}
            """);

        parsed.Kind.Should().Be(NamedPipeProtocolKind.V2);
        parsed.V2.Should().NotBeNull();
        parsed.V2!.Method.Should().Be("status");
        parsed.V2.Id!.Value.GetString().Should().Be("req-1");
        parsed.V2.Params!.Value.GetProperty("verbose").GetBoolean().Should().BeTrue();
        parsed.V1.Should().BeNull();
        parsed.V2ErrorResponse.Should().BeNull();
    }

    [Fact]
    public void ParseFirstLine_ReturnsV2ErrorForInvalidJsonEnvelope()
    {
        var parsed = NamedPipeProtocol.ParseFirstLine("{not-json");

        parsed.Kind.Should().Be(NamedPipeProtocolKind.V2);
        parsed.V2.Should().BeNull();
        parsed.V2ErrorResponse.Should().NotBeNull();
        parsed.V2ErrorResponse!.Error.Should().NotBeNull();
        parsed.V2ErrorResponse.Error!.Code.Should().Be("invalid_request");
    }

    [Fact]
    public void ParseFirstLine_ReturnsV2ErrorForUnsupportedProtocol()
    {
        var parsed = NamedPipeProtocol.ParseFirstLine("""
            {"protocol":"other","id":"req-2","method":"status"}
            """);

        parsed.Kind.Should().Be(NamedPipeProtocolKind.V2);
        parsed.V2.Should().BeNull();
        parsed.V2ErrorResponse.Should().NotBeNull();
        parsed.V2ErrorResponse!.Id!.Value.GetString().Should().Be("req-2");
        parsed.V2ErrorResponse.Error!.Code.Should().Be("invalid_request");
    }
}

public class ShortRefTests
{
    [Theory]
    [InlineData("window:1", ShortRefKind.Window, 1)]
    [InlineData("workspace:2", ShortRefKind.Workspace, 2)]
    [InlineData("surface:3", ShortRefKind.Surface, 3)]
    [InlineData("pane:4", ShortRefKind.Pane, 4)]
    [InlineData("WORKSPACE:5", ShortRefKind.Workspace, 5)]
    public void Parse_AcceptsSupportedRefKinds(string value, ShortRefKind kind, int index)
    {
        var reference = ShortRef.Parse(value);

        reference.Kind.Should().Be(kind);
        reference.Index.Should().Be(index);
    }

    [Theory]
    [InlineData("")]
    [InlineData("workspace")]
    [InlineData("workspace:0")]
    [InlineData("workspace:-1")]
    [InlineData("unknown:1")]
    [InlineData("pane:not-number")]
    public void TryParse_RejectsInvalidRefs(string value)
    {
        ShortRef.TryParse(value, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(ShortRefKind.Window, "window:1")]
    [InlineData(ShortRefKind.Workspace, "workspace:1")]
    [InlineData(ShortRefKind.Surface, "surface:1")]
    [InlineData(ShortRefKind.Pane, "pane:1")]
    public void ToString_FormatsRefs(ShortRefKind kind, string expected)
    {
        new ShortRef(kind, 1).ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData(ShortRefKind.Window)]
    [InlineData(ShortRefKind.Workspace)]
    [InlineData(ShortRefKind.Surface)]
    [InlineData(ShortRefKind.Pane)]
    public void ShortRefIndex_ResolvesUuidAndRefBothWays(ShortRefKind kind)
    {
        var index = ShortRefIndex.FromIds(kind, ["id-a", "id-b"]);

        index.TryResolve(new ShortRef(kind, 2), out var id).Should().BeTrue();
        id.Should().Be("id-b");
        index.TryGetRef("id-a", out var reference).Should().BeTrue();
        reference.Should().Be(new ShortRef(kind, 1));
    }

    [Fact]
    public void ShortRefIndex_IgnoresDuplicateIdsWhenAssigningRefs()
    {
        var index = ShortRefIndex.FromIds(ShortRefKind.Workspace, ["workspace-a", "workspace-a", "workspace-b"]);

        index.GetRef("workspace-a").Should().Be(new ShortRef(ShortRefKind.Workspace, 1));
        index.GetRef("workspace-b").Should().Be(new ShortRef(ShortRefKind.Workspace, 2));
    }
}

public class CliGlobalOptionsTests
{
    [Fact]
    public void TryExtract_DefaultsHumanOutputToRefs()
    {
        var ok = CliGlobalOptions.TryExtract(
            ["workspace", "list"],
            out var options,
            out var remaining,
            out var error);

        ok.Should().BeTrue(error);
        options.IdFormat.Should().Be(CliIdFormat.Refs);
        options.Json.Should().BeFalse();
        remaining.Should().Equal("workspace", "list");
    }

    [Fact]
    public void TryExtract_DefaultsJsonOutputToBoth()
    {
        var ok = CliGlobalOptions.TryExtract(
            ["--json", "workspace", "list"],
            out var options,
            out var remaining,
            out var error);

        ok.Should().BeTrue(error);
        options.IdFormat.Should().Be(CliIdFormat.Both);
        options.Json.Should().BeTrue();
        remaining.Should().Equal("workspace", "list");
    }

    [Theory]
    [InlineData("refs", CliIdFormat.Refs)]
    [InlineData("uuids", CliIdFormat.Uuids)]
    [InlineData("both", CliIdFormat.Both)]
    public void TryExtract_ExplicitIdFormatOverridesDefault(string raw, CliIdFormat expected)
    {
        var ok = CliGlobalOptions.TryExtract(
            ["--json", "--id-format", raw, "status"],
            out var options,
            out var remaining,
            out var error);

        ok.Should().BeTrue(error);
        options.IdFormat.Should().Be(expected);
        options.Json.Should().BeTrue();
        remaining.Should().Equal("status");
    }

    [Fact]
    public void TryExtract_SupportsEqualsSyntax()
    {
        var ok = CliGlobalOptions.TryExtract(
            ["--id-format=uuids", "status"],
            out var options,
            out var remaining,
            out var error);

        ok.Should().BeTrue(error);
        options.IdFormat.Should().Be(CliIdFormat.Uuids);
        remaining.Should().Equal("status");
    }

    [Fact]
    public void TryExtract_RejectsInvalidIdFormat()
    {
        var ok = CliGlobalOptions.TryExtract(
            ["--id-format", "short", "status"],
            out _,
            out _,
            out var error);

        ok.Should().BeFalse();
        error.Should().Contain("Invalid --id-format");
    }

    [Fact]
    public void ToPipeArgs_EmitsIdFormatAndJsonFlag()
    {
        var args = new CliGlobalOptions(CliIdFormat.Both, Json: true).ToPipeArgs();

        args.Should().Contain("idFormat", "both");
        args.Should().Contain("json", "true");
    }
}

public class WindowManagerServiceTests
{
    [Fact]
    public void RegisterWindow_AssignsStableWindowRefsAndCurrentWindow()
    {
        var service = new ECodex.Services.WindowManagerService<object>();
        var first = new object();
        var second = new object();

        var firstInfo = service.RegisterWindow(first, "First");
        var secondInfo = service.RegisterWindow(second, "Second");
        var windows = service.ListWindows();

        firstInfo.Ref.Should().Be(new ShortRef(ShortRefKind.Window, 1));
        secondInfo.Ref.Should().Be(new ShortRef(ShortRefKind.Window, 2));
        windows.Should().HaveCount(2);
        windows[0].IsCurrent.Should().BeFalse();
        windows[1].IsCurrent.Should().BeTrue();
        service.CurrentWindowId.Should().Be(secondInfo.Id);
    }

    [Fact]
    public void FocusWindow_UpdatesCurrentWithoutChangingWindowOrder()
    {
        var service = new ECodex.Services.WindowManagerService<object>();
        var first = service.RegisterWindow(new object(), "First");
        var second = service.RegisterWindow(new object(), "Second");
        var focused = false;

        service.FocusWindow(first.Id, _ => focused = true).Should().BeTrue();
        var windows = service.ListWindows();

        focused.Should().BeTrue();
        service.CurrentWindowId.Should().Be(first.Id);
        windows.Select(w => w.Id).Should().Equal(first.Id, second.Id);
        windows[0].IsCurrent.Should().BeTrue();
        windows[1].IsCurrent.Should().BeFalse();
    }

    [Fact]
    public void UnregisterCurrentWindow_PromotesLastRemainingWindow()
    {
        var service = new ECodex.Services.WindowManagerService<object>();
        var first = service.RegisterWindow(new object(), "First");
        var second = service.RegisterWindow(new object(), "Second");

        service.UnregisterWindow(second.Id).Should().BeTrue();

        service.CurrentWindowId.Should().Be(first.Id);
        service.ListWindows().Should().ContainSingle()
            .Which.IsCurrent.Should().BeTrue();
    }

    [Fact]
    public void CreateAndCloseWindow_KeepIndependentLifecycle()
    {
        var service = new ECodex.Services.WindowManagerService<object>();
        var closed = false;

        var info = service.CreateWindow(() => new object(), show: _ => { }, title: "Created");
        var closedResult = service.CloseWindow(info.Id, _ => closed = true);

        closedResult.Should().BeTrue();
        closed.Should().BeTrue();
        service.Count.Should().Be(0);
        service.CurrentWindowId.Should().BeNull();
    }

    [Fact]
    public void RegisterWindow_DoesNotDuplicateSameWindowInstance()
    {
        var service = new ECodex.Services.WindowManagerService<object>();
        var window = new object();

        var first = service.RegisterWindow(window, "First");
        var second = service.RegisterWindow(window, "Renamed");

        first.Id.Should().Be(second.Id);
        service.ListWindows().Should().ContainSingle()
            .Which.Title.Should().Be("Renamed");
    }
}

public class WindowApiServiceTests
{
    [Fact]
    public void WindowList_ReturnsRefsAndIdsForAllRegisteredWindows()
    {
        var manager = new ECodex.Services.WindowManagerService<object>();
        var first = manager.RegisterWindow(new object(), "First");
        var second = manager.RegisterWindow(new object(), "Second");
        var api = new ECodex.Services.WindowApiService<object>(manager);

        var response = api.HandleRequest(CreateV2Request("window.list", """{"idFormat":"both"}"""));

        response.Error.Should().BeNull();
        using var result = ParseResult(response);
        var windows = result.RootElement.GetProperty("windows");
        windows.GetArrayLength().Should().Be(2);
        windows[0].GetProperty("ref").GetString().Should().Be("window:1");
        windows[0].GetProperty("id").GetString().Should().Be(first.Id);
        windows[1].GetProperty("ref").GetString().Should().Be("window:2");
        windows[1].GetProperty("id").GetString().Should().Be(second.Id);
        result.RootElement.GetProperty("current").GetProperty("id").GetString().Should().Be(second.Id);
    }

    [Fact]
    public void WindowCurrent_ReturnsCurrentWindowOnly()
    {
        var manager = new ECodex.Services.WindowManagerService<object>();
        manager.RegisterWindow(new object(), "First");
        var second = manager.RegisterWindow(new object(), "Second");
        var api = new ECodex.Services.WindowApiService<object>(manager);

        var response = api.HandleRequest(CreateV2Request("window.current", """{"idFormat":"refs"}"""));

        response.Error.Should().BeNull();
        using var result = ParseResult(response);
        var window = result.RootElement.GetProperty("window");
        window.GetProperty("ref").GetString().Should().Be("window:2");
        window.TryGetProperty("id", out _).Should().BeFalse();
        second.Ref.Should().Be(new ShortRef(ShortRefKind.Window, 2));
    }

    [Fact]
    public void WindowFocus_AcceptsShortRefAndUpdatesCurrentWindow()
    {
        var manager = new ECodex.Services.WindowManagerService<object>();
        var first = manager.RegisterWindow(new object(), "First");
        manager.RegisterWindow(new object(), "Second");
        var focused = false;
        var api = new ECodex.Services.WindowApiService<object>(
            manager,
            focusWindow: _ => focused = true);

        var response = api.HandleRequest(CreateV2Request("window.focus", """{"target":"window:1","idFormat":"both"}"""));

        response.Error.Should().BeNull();
        focused.Should().BeTrue();
        manager.CurrentWindowId.Should().Be(first.Id);
        using var result = ParseResult(response);
        result.RootElement.GetProperty("focused").GetBoolean().Should().BeTrue();
        result.RootElement.GetProperty("window").GetProperty("id").GetString().Should().Be(first.Id);
    }

    [Fact]
    public void WindowCreateAndClose_UseConfiguredLifecycleCallbacks()
    {
        var manager = new ECodex.Services.WindowManagerService<object>();
        var shown = false;
        var closed = false;
        string? createdTitle = null;
        var api = new ECodex.Services.WindowApiService<object>(
            manager,
            windowFactory: title =>
            {
                createdTitle = title;
                return new object();
            },
            showWindow: _ => shown = true,
            closeWindow: _ => closed = true);

        var create = api.HandleRequest(CreateV2Request("window.create", """{"title":"Aux","idFormat":"both"}"""));

        create.Error.Should().BeNull();
        shown.Should().BeTrue();
        createdTitle.Should().Be("Aux");
        manager.Count.Should().Be(1);
        using var createResult = ParseResult(create);
        var createdId = createResult.RootElement.GetProperty("window").GetProperty("id").GetString();
        createResult.RootElement.GetProperty("window").GetProperty("title").GetString().Should().Be("Aux");

        var close = api.HandleRequest(CreateV2Request("window.close", $$"""{"id":"{{createdId}}","idFormat":"both"}"""));

        close.Error.Should().BeNull();
        closed.Should().BeTrue();
        manager.Count.Should().Be(0);
        using var closeResult = ParseResult(close);
        closeResult.RootElement.GetProperty("closed").GetBoolean().Should().BeTrue();
        closeResult.RootElement.GetProperty("window").GetProperty("id").GetString().Should().Be(createdId);
    }

    [Fact]
    public void WindowCreate_FocusesExistingWindowInsteadOfCreatingSecondWindow()
    {
        var manager = new ECodex.Services.WindowManagerService<object>();
        var existing = new object();
        var existingInfo = manager.RegisterWindow(existing, "Primary");
        var factoryCalled = false;
        var focused = false;
        var api = new ECodex.Services.WindowApiService<object>(
            manager,
            windowFactory: _ =>
            {
                factoryCalled = true;
                return new object();
            },
            showWindow: _ => throw new InvalidOperationException("Single-window mode should not show a new window."),
            focusWindow: window =>
            {
                focused = ReferenceEquals(window, existing);
            });

        var create = api.HandleRequest(CreateV2Request("window.create", """{"title":"Aux","idFormat":"both"}"""));

        create.Error.Should().BeNull();
        factoryCalled.Should().BeFalse();
        focused.Should().BeTrue();
        manager.Count.Should().Be(1);
        using var result = ParseResult(create);
        result.RootElement.GetProperty("created").GetBoolean().Should().BeFalse();
        result.RootElement.GetProperty("window").GetProperty("id").GetString().Should().Be(existingInfo.Id);
    }

    [Fact]
    public void WindowFocus_ReturnsInvalidRefForNonWindowRef()
    {
        var manager = new ECodex.Services.WindowManagerService<object>();
        manager.RegisterWindow(new object(), "First");
        var api = new ECodex.Services.WindowApiService<object>(manager);

        var response = api.HandleRequest(CreateV2Request("window.focus", """{"target":"workspace:1"}"""));

        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(V2ErrorCodes.InvalidRef);
    }

    private static V2Request CreateV2Request(string method, string? parameters = null)
    {
        return new V2Request
        {
            Id = JsonSerializer.SerializeToElement("test-request"),
            Method = method,
            Params = parameters == null
                ? null
                : JsonDocument.Parse(parameters).RootElement.Clone(),
        };
    }

    private static JsonDocument ParseResult(V2Response response)
    {
        response.Result.Should().NotBeNull();
        return JsonDocument.Parse(JsonSerializer.Serialize(response.Result));
    }
}

/// <summary>
/// Daemon 会话终止策略测试 - 默认保留后台终端，只有显式退出策略才逐个终止 daemon 会话。
/// </summary>
public class DaemonSessionTerminationPolicyTests
{
    [Fact]
    public void Settings_DefaultToPreserveDaemonSessionsOnWindowClose()
    {
        var settings = new ECodexSettings();

        settings.PreserveDaemonSessionsOnClose.Should().BeTrue();

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        json.Should().Contain("preserveDaemonSessionsOnClose");
    }

    [Fact]
    public async Task Terminator_ClosesListedDaemonSessionsWithoutCloseAllMessage()
    {
        var closed = new List<string>();

        var result = await DaemonSessionTerminator.TerminateAllAsync(
            () => Task.FromResult(new List<DaemonSessionInfo>
            {
                new() { PaneId = "pane-1" },
                new() { PaneId = "pane-2" },
                new() { PaneId = "" },
            }),
            paneId =>
            {
                closed.Add(paneId);
                return Task.CompletedTask;
            });

        closed.Should().Equal("pane-1", "pane-2");
        result.Terminated.Should().Be(2);
        result.Requested.Should().Be(2);
    }

    [Fact]
    public void MainWindow_ClosePathHonorsPreserveDaemonSessionsSetting()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "src", "ECodex", "Views", "MainWindow.xaml.cs"));

        source.Should().Contain("TerminateDaemonSessionsOnCloseIfConfigured();");
        source.Should().Contain("SettingsService.Current.PreserveDaemonSessionsOnClose");
        source.Should().Contain("DaemonSessionTerminator.TerminateAllAsync(App.DaemonClient)");
    }
}

/// <summary>
/// 主应用生命周期 IPC 测试 - 退出并终止终端走 ecodex.v2 主应用管道，而不是 daemon close-all 协议。
/// </summary>
public class AppLifecycleApiServiceTests
{
    [Fact]
    public async Task Exit_WithTerminateTerminals_RequestsTerminationThenShutdown()
    {
        var terminated = false;
        var shutdownRequested = false;
        var api = new AppLifecycleApiService(
            terminateDaemonSessions: () =>
            {
                terminated = true;
                return Task.FromResult(new DaemonSessionTerminationResult(1, 1));
            },
            requestShutdown: () => shutdownRequested = true);

        var response = await api.HandleRequestAsync(CreateV2Request("app.exit", """{"terminateTerminals":true}"""));

        response.Error.Should().BeNull();
        terminated.Should().BeTrue();
        shutdownRequested.Should().BeTrue();
        using var result = ParseResult(response);
        result.RootElement.GetProperty("exiting").GetBoolean().Should().BeTrue();
        result.RootElement.GetProperty("terminateTerminals").GetBoolean().Should().BeTrue();
        result.RootElement.GetProperty("terminatedDaemonSessions").GetInt32().Should().Be(1);
    }

    private static V2Request CreateV2Request(string method, string parameters)
    {
        return new V2Request
        {
            Id = JsonSerializer.SerializeToElement("test"),
            Method = method,
            Params = JsonSerializer.Deserialize<JsonElement>(parameters),
        };
    }

    private static JsonDocument ParseResult(V2Response response)
    {
        response.Result.Should().NotBeNull();
        return JsonDocument.Parse(JsonSerializer.Serialize(response.Result));
    }
}

/// <summary>
/// 主应用单实例策略测试 - 第二次启动只激活现有窗口，不创建第二个主窗口。
/// </summary>
public class AppSingleWindowSourceTests
{
    [Fact]
    public void App_UsesMainInstanceMutexAndActivatesExistingInstance()
    {
        var source = Normalize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "src", "ECodex", "App.xaml.cs")));

        source.Should().Contain(@"private const string MainInstanceMutexName = @""Global\ECodexMainApp"";");
        source.Should().Contain("new Mutex(true, MainInstanceMutexName, out var createdNew)");
        source.Should().Contain("if (!createdNew)");
        source.Should().Contain("TryActivateExistingInstance()");
        source.Should().Contain("Shutdown(0);");
        source.Should().Contain("NamedPipeClient.SendV2Request");
        source.Should().Contain(@"""window.focus""");
        source.Should().Contain(@"""target"", ""current""");
    }

    private static string Normalize(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);
}

public class SurfaceApiServiceTests
{
    [Fact]
    public void SurfaceMove_AcceptsShortRefAndMovesWithinWorkspace()
    {
        var workspace = CreateWorkspace("workspace-1", "Project", "surface-a", "surface-b", "surface-c");
        var api = CreateSurfaceApi([workspace]);

        var response = api.HandleRequest(CreateV2Request("surface.move", """{"target":"surface:2","targetIndex":1,"idFormat":"both"}"""));

        response.Error.Should().BeNull();
        workspace.Surfaces.Select(surface => surface.Id).Should().Equal("surface-b", "surface-a", "surface-c");
        workspace.SelectedSurfaceId.Should().Be("surface-b");
        using var result = ParseResult(response);
        result.RootElement.GetProperty("moved").GetBoolean().Should().BeTrue();
        result.RootElement.GetProperty("surface").GetProperty("ref").GetString().Should().Be("surface:1");
        result.RootElement.GetProperty("surface").GetProperty("id").GetString().Should().Be("surface-b");
    }

    [Fact]
    public void SurfaceReorder_AcceptsFullRefOrder()
    {
        var workspace = CreateWorkspace("workspace-1", "Project", "surface-a", "surface-b", "surface-c");
        var api = CreateSurfaceApi([workspace]);

        var response = api.HandleRequest(CreateV2Request("surface.reorder", """{"order":["surface:3","surface:1","surface:2"],"idFormat":"refs"}"""));

        response.Error.Should().BeNull();
        workspace.Surfaces.Select(surface => surface.Id).Should().Equal("surface-c", "surface-a", "surface-b");
        using var result = ParseResult(response);
        var surfaces = result.RootElement.GetProperty("surfaces");
        surfaces[0].GetProperty("ref").GetString().Should().Be("surface:1");
        surfaces[0].GetProperty("name").GetString().Should().Be("surface-c");
        surfaces[0].TryGetProperty("id", out _).Should().BeFalse();
    }

    [Fact]
    public void SurfaceReorder_RejectsDuplicateOrderEntries()
    {
        var workspace = CreateWorkspace("workspace-1", "Project", "surface-a", "surface-b");
        var api = CreateSurfaceApi([workspace]);

        var response = api.HandleRequest(CreateV2Request("surface.reorder", """{"order":["surface:1","surface:1"]}"""));

        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(V2ErrorCodes.InvalidRef);
        workspace.Surfaces.Select(surface => surface.Id).Should().Equal("surface-a", "surface-b");
    }

    private static ECodex.Services.SurfaceApiService<TestWorkspace, TestSurface> CreateSurfaceApi(List<TestWorkspace> workspaces)
    {
        return new ECodex.Services.SurfaceApiService<TestWorkspace, TestSurface>(
            workspaceProvider: () => workspaces.Select((workspace, workspaceIndex) =>
                new ECodex.Services.SurfaceApiWorkspace<TestWorkspace, TestSurface>(
                    Workspace: workspace,
                    WorkspaceId: workspace.Id,
                    WorkspaceName: workspace.Name,
                    WorkspaceRef: new ShortRef(ShortRefKind.Workspace, workspaceIndex + 1),
                    IsCurrent: workspaceIndex == 0,
                    Surfaces: workspace.Surfaces
                        .Select((surface, surfaceIndex) =>
                            new ECodex.Services.SurfaceApiSurface<TestSurface>(
                                Surface: surface,
                                SurfaceId: surface.Id,
                                SurfaceName: surface.Name,
                                SurfaceRef: new ShortRef(ShortRefKind.Surface, surfaceIndex + 1),
                                IsCurrent: surface.Id == workspace.SelectedSurfaceId,
                                Kind: surface.Kind))
                        .ToList())),
            moveSurface: (workspace, surface, targetIndex) =>
            {
                var sourceIndex = workspace.Surfaces.IndexOf(surface);
                if (sourceIndex < 0)
                    return false;

                if (sourceIndex == targetIndex)
                    return true;

                workspace.Surfaces.RemoveAt(sourceIndex);
                workspace.Surfaces.Insert(targetIndex, surface);
                return true;
            },
            reorderSurfaces: (workspace, surfaceIds) =>
            {
                var surfaces = surfaceIds
                    .Select(id => workspace.Surfaces.FirstOrDefault(surface => surface.Id == id))
                    .ToList();
                if (surfaces.Any(surface => surface == null))
                    return false;

                workspace.Surfaces.Clear();
                foreach (var surface in surfaces)
                    workspace.Surfaces.Add(surface!);

                return true;
            },
            selectSurface: (workspace, surface) => workspace.SelectedSurfaceId = surface.Id);
    }

    private static TestWorkspace CreateWorkspace(string id, string name, params string[] surfaceIds)
    {
        return new TestWorkspace(
            id,
            name,
            surfaceIds.Select(surfaceId => new TestSurface(surfaceId, surfaceId)).ToList(),
            surfaceIds.FirstOrDefault());
    }

    private static V2Request CreateV2Request(string method, string parameters)
    {
        return new V2Request
        {
            Id = JsonSerializer.SerializeToElement("test-request"),
            Method = method,
            Params = JsonDocument.Parse(parameters).RootElement.Clone(),
        };
    }

    private static JsonDocument ParseResult(V2Response response)
    {
        response.Result.Should().NotBeNull();
        return JsonDocument.Parse(JsonSerializer.Serialize(response.Result));
    }

    private sealed class TestWorkspace(string id, string name, List<TestSurface> surfaces, string? selectedSurfaceId)
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public List<TestSurface> Surfaces { get; } = surfaces;
        public string? SelectedSurfaceId { get; set; } = selectedSurfaceId;
    }

    private sealed record TestSurface(string Id, string Name, string Kind = "Terminal");
}

public class WorkspaceApiServiceTests
{
    [Fact]
    public void WorkspaceList_ReturnsRefsAndIdsForAllWorkspaces()
    {
        var workspaces = new List<TestWorkspace>
        {
            new("workspace-a", "Alpha", 2),
            new("workspace-b", "Beta", 1),
        };
        workspaces[1].IsCurrent = true;
        var api = CreateWorkspaceApi(workspaces);

        var response = api.HandleRequest(CreateV2Request("workspace.list", """{"idFormat":"both"}"""));

        response.Error.Should().BeNull();
        using var result = ParseResult(response);
        var items = result.RootElement.GetProperty("workspaces");
        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("ref").GetString().Should().Be("workspace:1");
        items[0].GetProperty("id").GetString().Should().Be("workspace-a");
        items[0].GetProperty("surfaces").GetInt32().Should().Be(2);
        result.RootElement.GetProperty("current").GetProperty("id").GetString().Should().Be("workspace-b");
    }

    [Fact]
    public void WorkspaceCreateSelectRenameAndClose_UpdateLifecycle()
    {
        var workspaces = new List<TestWorkspace>
        {
            new("workspace-a", "Alpha", 1) { IsCurrent = true },
        };
        var api = CreateWorkspaceApi(workspaces);

        var create = api.HandleRequest(CreateV2Request("workspace.create", """{"name":"Beta","cwd":"C:\\Repos\\Beta","idFormat":"both"}"""));

        create.Error.Should().BeNull();
        workspaces.Should().HaveCount(2);
        workspaces[1].Name.Should().Be("Beta");
        workspaces[1].WorkingDirectory.Should().Be(@"C:\Repos\Beta");
        workspaces[1].IsCurrent.Should().BeTrue();
        using var createResult = ParseResult(create);
        var createdId = createResult.RootElement.GetProperty("workspace").GetProperty("id").GetString();
        createResult.RootElement.GetProperty("workspace").GetProperty("workingDirectory").GetString()
            .Should().Be(@"C:\Repos\Beta");

        var select = api.HandleRequest(CreateV2Request("workspace.select", """{"target":"workspace:1","idFormat":"both"}"""));

        select.Error.Should().BeNull();
        workspaces[0].IsCurrent.Should().BeTrue();

        var rename = api.HandleRequest(CreateV2Request("workspace.rename", $$"""{"id":"{{createdId}}","name":"Renamed","idFormat":"both"}"""));

        rename.Error.Should().BeNull();
        workspaces[1].Name.Should().Be("Renamed");
        using var renameResult = ParseResult(rename);
        renameResult.RootElement.GetProperty("workspace").GetProperty("name").GetString().Should().Be("Renamed");

        var close = api.HandleRequest(CreateV2Request("workspace.close", $$"""{"id":"{{createdId}}","idFormat":"both"}"""));

        close.Error.Should().BeNull();
        workspaces.Select(workspace => workspace.Id).Should().Equal("workspace-a");
        using var closeResult = ParseResult(close);
        closeResult.RootElement.GetProperty("closed").GetBoolean().Should().BeTrue();
        closeResult.RootElement.GetProperty("workspace").GetProperty("id").GetString().Should().Be(createdId);
    }

    [Fact]
    public void WorkspaceCreate_RequiresUniqueWorkingDirectory()
    {
        var workspaces = new List<TestWorkspace>
        {
            new("workspace-a", "Alpha", 1)
            {
                IsCurrent = true,
                WorkingDirectory = @"C:\Repos\Alpha",
            },
        };
        var api = CreateWorkspaceApi(workspaces);

        var missing = api.HandleRequest(CreateV2Request("workspace.create", """{"name":"No Folder"}"""));
        var duplicate = api.HandleRequest(CreateV2Request("workspace.create", """{"name":"Duplicate","workingDirectory":"C:\\Repos\\Alpha\\"}"""));

        missing.Error.Should().NotBeNull();
        missing.Error!.Code.Should().Be(V2ErrorCodes.InvalidRef);
        duplicate.Error.Should().NotBeNull();
        duplicate.Error!.Code.Should().Be(V2ErrorCodes.InvalidRef);
        workspaces.Should().HaveCount(1);
    }

    [Fact]
    public void WorkspaceReorder_AcceptsFullRefOrder()
    {
        var workspaces = new List<TestWorkspace>
        {
            new("workspace-a", "Alpha", 1),
            new("workspace-b", "Beta", 1),
            new("workspace-c", "Gamma", 1),
        };
        workspaces[0].IsCurrent = true;
        var api = CreateWorkspaceApi(workspaces);

        var response = api.HandleRequest(CreateV2Request("workspace.reorder", """{"order":["workspace:3","workspace:1","workspace:2"],"idFormat":"refs"}"""));

        response.Error.Should().BeNull();
        workspaces.Select(workspace => workspace.Id).Should().Equal("workspace-c", "workspace-a", "workspace-b");
        using var result = ParseResult(response);
        var items = result.RootElement.GetProperty("workspaces");
        items[0].GetProperty("ref").GetString().Should().Be("workspace:1");
        items[0].GetProperty("name").GetString().Should().Be("Gamma");
        items[0].TryGetProperty("id", out _).Should().BeFalse();
    }

    [Fact]
    public void WorkspaceReorder_RejectsIncompleteOrder()
    {
        var workspaces = new List<TestWorkspace>
        {
            new("workspace-a", "Alpha", 1),
            new("workspace-b", "Beta", 1),
        };
        var api = CreateWorkspaceApi(workspaces);

        var response = api.HandleRequest(CreateV2Request("workspace.reorder", """{"order":["workspace:1"]}"""));

        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(V2ErrorCodes.InvalidRef);
        workspaces.Select(workspace => workspace.Id).Should().Equal("workspace-a", "workspace-b");
    }

    private static ECodex.Services.WorkspaceApiService<TestWorkspace> CreateWorkspaceApi(List<TestWorkspace> workspaces)
    {
        return new ECodex.Services.WorkspaceApiService<TestWorkspace>(
            workspaceProvider: () => workspaces.Select((workspace, index) =>
                new ECodex.Services.WorkspaceApiWorkspace<TestWorkspace>(
                    Workspace: workspace,
                    WorkspaceId: workspace.Id,
                    WorkspaceName: workspace.Name,
                    WorkspaceRef: new ShortRef(ShortRefKind.Workspace, index + 1),
                    IsCurrent: workspace.IsCurrent,
                    SurfaceCount: workspace.SurfaceCount,
                    WorkingDirectory: workspace.WorkingDirectory)),
            createWorkspace: request =>
            {
                foreach (var workspace in workspaces)
                    workspace.IsCurrent = false;

                var created = new TestWorkspace($"workspace-{workspaces.Count + 1}", request.Name ?? $"Project {workspaces.Count + 1}", 1)
                {
                    IsCurrent = true,
                    WorkingDirectory = request.WorkingDirectory,
                };
                workspaces.Add(created);
                return created;
            },
            selectWorkspace: selected =>
            {
                foreach (var workspace in workspaces)
                    workspace.IsCurrent = ReferenceEquals(workspace, selected);
            },
            closeWorkspace: workspace =>
            {
                if (workspaces.Count <= 1 || !workspaces.Remove(workspace))
                    return false;

                if (workspace.IsCurrent && workspaces.Count > 0)
                    workspaces[0].IsCurrent = true;
                return true;
            },
            renameWorkspace: (workspace, name) =>
            {
                workspace.Name = name;
                return true;
            },
            reorderWorkspaces: workspaceIds =>
            {
                var reordered = workspaceIds
                    .Select(id => workspaces.FirstOrDefault(workspace => workspace.Id == id))
                    .ToList();
                if (reordered.Any(workspace => workspace == null))
                    return false;

                workspaces.Clear();
                foreach (var workspace in reordered)
                    workspaces.Add(workspace!);

                return true;
            });
    }

    private static V2Request CreateV2Request(string method, string parameters)
    {
        return new V2Request
        {
            Id = JsonSerializer.SerializeToElement("test-request"),
            Method = method,
            Params = JsonDocument.Parse(parameters).RootElement.Clone(),
        };
    }

    private static JsonDocument ParseResult(V2Response response)
    {
        response.Result.Should().NotBeNull();
        return JsonDocument.Parse(JsonSerializer.Serialize(response.Result));
    }

    private sealed class TestWorkspace(string id, string name, int surfaceCount)
    {
        public string Id { get; } = id;
        public string Name { get; set; } = name;
        public int SurfaceCount { get; } = surfaceCount;
        public bool IsCurrent { get; set; }
        public string? WorkingDirectory { get; set; }
    }
}

public class PaneApiServiceTests
{
    [Fact]
    public void PaneListFocusWriteAndRead_UseResolvedPaneRefs()
    {
        var workspace = CreateWorkspace();
        var api = CreatePaneApi([workspace]);

        var list = api.HandleRequest(CreateV2Request("pane.list", """{"idFormat":"both"}"""));

        list.Error.Should().BeNull();
        using var listResult = ParseResult(list);
        var panes = listResult.RootElement.GetProperty("panes");
        panes.GetArrayLength().Should().Be(2);
        panes[0].GetProperty("ref").GetString().Should().Be("pane:1");
        panes[1].GetProperty("id").GetString().Should().Be("pane-b");

        var focus = api.HandleRequest(CreateV2Request("pane.focus", """{"target":"pane:2"}"""));

        focus.Error.Should().BeNull();
        workspace.Surfaces[0].FocusedPaneId.Should().Be("pane-b");

        var write = api.HandleRequest(CreateV2Request("pane.write", """{"target":"pane:2","text":"npm test","submit":true}"""));

        write.Error.Should().BeNull();
        workspace.Surfaces[0].Panes[1].Writes.Should().Equal("npm test", "<auto>");

        var read = api.HandleRequest(CreateV2Request("pane.read", """{"target":"pane:2","lines":12,"maxChars":1024}"""));

        read.Error.Should().BeNull();
        using var readResult = ParseResult(read);
        readResult.RootElement.GetProperty("text").GetString().Should().Be("pane-b output");
        readResult.RootElement.GetProperty("lines").GetInt32().Should().Be(12);
    }

    [Fact]
    public void PaneSplitCloseResizeSwapAndZoom_UpdatePaneState()
    {
        var workspace = CreateWorkspace();
        var surface = workspace.Surfaces[0];
        var api = CreatePaneApi([workspace]);

        var split = api.HandleRequest(CreateV2Request("pane.split", """{"target":"pane:1","direction":"down"}"""));

        split.Error.Should().BeNull();
        surface.Panes.Should().HaveCount(3);
        surface.FocusedPaneId.Should().Be("pane-3");

        var resize = api.HandleRequest(CreateV2Request("pane.resize", """{"target":"pane:1","delta":0.2}"""));

        resize.Error.Should().BeNull();
        surface.LastResize.Should().Be(("pane-a", 0.2));

        var swap = api.HandleRequest(CreateV2Request("pane.swap", """{"target":"pane:1","other":"pane:2"}"""));

        swap.Error.Should().BeNull();
        surface.Panes.Select(pane => pane.Id).Take(2).Should().Equal("pane-3", "pane-a");

        var zoom = api.HandleRequest(CreateV2Request("pane.zoom", """{"zoomed":true}"""));

        zoom.Error.Should().BeNull();
        surface.IsZoomed.Should().BeTrue();

        var close = api.HandleRequest(CreateV2Request("pane.close", """{"target":"pane:2"}"""));

        close.Error.Should().BeNull();
        surface.Panes.Select(pane => pane.Id).Should().NotContain("pane-a");
    }

    private static ECodex.Services.PaneApiService<TestWorkspace, TestSurface, TestPane> CreatePaneApi(List<TestWorkspace> workspaces)
    {
        return new ECodex.Services.PaneApiService<TestWorkspace, TestSurface, TestPane>(
            workspaceProvider: () => workspaces.Select((workspace, workspaceIndex) =>
                new ECodex.Services.PaneApiWorkspace<TestWorkspace, TestSurface, TestPane>(
                    Workspace: workspace,
                    WorkspaceId: workspace.Id,
                    WorkspaceName: workspace.Name,
                    WorkspaceRef: new ShortRef(ShortRefKind.Workspace, workspaceIndex + 1),
                    IsCurrent: workspace.IsCurrent,
                    Surfaces: workspace.Surfaces.Select((surface, surfaceIndex) =>
                        new ECodex.Services.PaneApiSurface<TestSurface, TestPane>(
                            Surface: surface,
                            SurfaceId: surface.Id,
                            SurfaceName: surface.Name,
                            SurfaceRef: new ShortRef(ShortRefKind.Surface, surfaceIndex + 1),
                            IsCurrent: surface.IsCurrent,
                            Kind: "Terminal",
                            IsZoomed: surface.IsZoomed,
                            Panes: surface.Panes.Select((pane, paneIndex) =>
                                new ECodex.Services.PaneApiPane<TestPane>(
                                    Pane: pane,
                                    PaneId: pane.Id,
                                    PaneRef: new ShortRef(ShortRefKind.Pane, paneIndex + 1),
                                    PaneName: pane.Name,
                                    IsFocused: pane.Id == surface.FocusedPaneId,
                                    WorkingDirectory: pane.WorkingDirectory))
                                .ToList()))
                        .ToList())),
            selectSurface: (workspace, surface) =>
            {
                foreach (var candidateWorkspace in workspaces)
                    candidateWorkspace.IsCurrent = ReferenceEquals(candidateWorkspace, workspace);
                foreach (var candidateSurface in workspace.Surfaces)
                    candidateSurface.IsCurrent = ReferenceEquals(candidateSurface, surface);
            },
            focusPane: (surface, paneId) =>
            {
                if (surface.Panes.All(pane => pane.Id != paneId))
                    return false;
                surface.FocusedPaneId = paneId;
                return true;
            },
            writePane: (surface, paneId, text, submit, submitKey) =>
            {
                var pane = surface.Panes.FirstOrDefault(item => item.Id == paneId);
                if (pane == null)
                    return false;
                if (!string.IsNullOrEmpty(text))
                    pane.Writes.Add(text);
                if (submit)
                    pane.Writes.Add($"<{submitKey}>");
                return true;
            },
            readPane: (surface, paneId, lines, maxChars) =>
            {
                var pane = surface.Panes.FirstOrDefault(item => item.Id == paneId);
                return pane == null ? null : new ECodex.Services.PaneApiReadResult(pane.ReadText, lines, maxChars);
            },
            splitPane: (surface, paneId, direction, shell) =>
            {
                var index = surface.Panes.FindIndex(pane => pane.Id == paneId);
                if (index < 0)
                    return false;
                var pane = new TestPane($"pane-{surface.Panes.Count + 1}", $"Pane {surface.Panes.Count + 1}");
                surface.Panes.Insert(index + 1, pane);
                surface.FocusedPaneId = pane.Id;
                surface.LastSplitDirection = direction;
                return true;
            },
            closePane: (surface, paneId) =>
            {
                if (surface.Panes.Count <= 1)
                    return false;
                var pane = surface.Panes.FirstOrDefault(item => item.Id == paneId);
                if (pane == null)
                    return false;
                surface.Panes.Remove(pane);
                surface.FocusedPaneId = surface.Panes[0].Id;
                return true;
            },
            resizePane: (surface, paneId, delta) =>
            {
                if (surface.Panes.All(pane => pane.Id != paneId))
                    return false;
                surface.LastResize = (paneId, delta);
                return true;
            },
            swapPanes: (surface, paneId, otherPaneId) =>
            {
                var first = surface.Panes.FindIndex(pane => pane.Id == paneId);
                var second = surface.Panes.FindIndex(pane => pane.Id == otherPaneId);
                if (first < 0 || second < 0)
                    return false;
                (surface.Panes[first], surface.Panes[second]) = (surface.Panes[second], surface.Panes[first]);
                return true;
            },
            zoomSurface: (surface, zoomed) =>
            {
                surface.IsZoomed = zoomed ?? !surface.IsZoomed;
                return true;
            });
    }

    private static TestWorkspace CreateWorkspace()
    {
        return new TestWorkspace("workspace-1", "Project")
        {
            IsCurrent = true,
            Surfaces =
            [
                new TestSurface("surface-1", "Terminal")
                {
                    IsCurrent = true,
                    FocusedPaneId = "pane-a",
                    Panes =
                    [
                        new TestPane("pane-a", "Pane A") { ReadText = "pane-a output" },
                        new TestPane("pane-b", "Pane B") { ReadText = "pane-b output" },
                    ],
                },
            ],
        };
    }

    private static V2Request CreateV2Request(string method, string parameters)
    {
        return new V2Request
        {
            Id = JsonSerializer.SerializeToElement("test-request"),
            Method = method,
            Params = JsonDocument.Parse(parameters).RootElement.Clone(),
        };
    }

    private static JsonDocument ParseResult(V2Response response)
    {
        response.Result.Should().NotBeNull();
        return JsonDocument.Parse(JsonSerializer.Serialize(response.Result));
    }

    private sealed class TestWorkspace(string id, string name)
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public bool IsCurrent { get; set; }
        public List<TestSurface> Surfaces { get; set; } = [];
    }

    private sealed class TestSurface(string id, string name)
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public bool IsCurrent { get; set; }
        public bool IsZoomed { get; set; }
        public string? FocusedPaneId { get; set; }
        public SplitDirection? LastSplitDirection { get; set; }
        public (string PaneId, double Delta)? LastResize { get; set; }
        public List<TestPane> Panes { get; set; } = [];
    }

    private sealed class TestPane(string id, string name)
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public string WorkingDirectory { get; set; } = "";
        public string ReadText { get; set; } = "";
        public List<string> Writes { get; } = [];
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

    [Theory]
    [InlineData(BrowserScriptingUnsupportedFeature.Viewport)]
    [InlineData(BrowserScriptingUnsupportedFeature.Geolocation)]
    [InlineData(BrowserScriptingUnsupportedFeature.Offline)]
    [InlineData(BrowserScriptingUnsupportedFeature.Trace)]
    [InlineData(BrowserScriptingUnsupportedFeature.NetworkRoute)]
    [InlineData(BrowserScriptingUnsupportedFeature.Screencast)]
    [InlineData(BrowserScriptingUnsupportedFeature.InputMouse)]
    [InlineData(BrowserScriptingUnsupportedFeature.InputKeyboard)]
    [InlineData(BrowserScriptingUnsupportedFeature.InputTouch)]
    public void NotSupportedMatrix_ReturnsStableNotSupportedCode(BrowserScriptingUnsupportedFeature feature)
    {
        var surfaces = new List<BrowserScriptingSurfaceDescriptor>
        {
            CreateSurface("browser-1", SurfaceKind.Browser),
        };
        var service = new BrowserScriptingService(() => surfaces);
        var surfaceRef = BrowserScriptingService.CreateSurfaceRef("browser-1");

        var result = service.NotSupported(surfaceRef, feature);

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be(V2ErrorCodes.NotSupported);
        result.Error.Message.Should().Contain(feature.ToString());
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
                    Text = "Welcome to ECodex",
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

public class BrowserScriptingCliCommandTests
{
    [Theory]
    [InlineData("open", BrowserScriptingCliCommands.Open)]
    [InlineData("new", BrowserScriptingCliCommands.New)]
    [InlineData("open-split", BrowserScriptingCliCommands.OpenSplit)]
    [InlineData("snapshot", BrowserScriptingCliCommands.Snapshot)]
    [InlineData("click", BrowserScriptingCliCommands.Click)]
    [InlineData("fill", BrowserScriptingCliCommands.Fill)]
    [InlineData("hover", BrowserScriptingCliCommands.Hover)]
    [InlineData("press", BrowserScriptingCliCommands.Press)]
    [InlineData("eval", BrowserScriptingCliCommands.Eval)]
    [InlineData("screenshot", BrowserScriptingCliCommands.Screenshot)]
    public void TryResolve_MapsBrowserSubcommandsToPipeCommands(string subcommand, string expected)
    {
        var ok = BrowserScriptingCliCommands.TryResolve(subcommand, out var pipeCommand);

        ok.Should().BeTrue();
        pipeCommand.Should().Be(expected);
    }

    [Fact]
    public void TryResolve_RejectsUnknownBrowserSubcommand()
    {
        var ok = BrowserScriptingCliCommands.TryResolve("trace", out var pipeCommand);

        ok.Should().BeFalse();
        pipeCommand.Should().BeEmpty();
    }
}

/// <summary>
/// 终端环境变量测试 - 验证启动 shell 前注入 ecodex 上下文变量。
/// </summary>
public class TerminalEnvironmentVariablesTests
{
    [Fact]
    public void ForWorkspace_AddsECodexWorkspaceId()
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
/// Daemon 客户端生命周期测试 - 主动关闭应用时只断开客户端，不触发运行时断线回退。
/// </summary>
public class DaemonClientLifecycleSourceTests
{
    [Fact]
    public void Dispose_DoesNotRaiseDisconnectedFallback()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "src", "ECodex.Core", "IPC", "DaemonClient.cs"));
        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        normalized.Should().Contain("if (!_disposed)\n                Disconnected?.Invoke();");
    }
}

/// <summary>
/// 崩溃恢复 checkpoint 测试 - 结构变化应实时写 session.json，但不能生成关闭 transcript。
/// </summary>
public class CrashRecoveryCheckpointSourceTests
{
    [Fact]
    public void SaveSession_SupportsCheckpointWithoutTranscriptCapture()
    {
        var source = Normalize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "src", "ECodex", "ViewModels", "MainViewModel.cs")));

        source.Should().Contain("bool captureTranscripts = true");
        source.Should().Contain("if (captureTranscripts)");
        source.Should().Contain("surface.CaptureAllPaneTranscripts(\"session-close\");");
    }

    [Fact]
    public void MainWindow_UsesCheckpointForRuntimeStructureChanges()
    {
        var source = Normalize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "src", "ECodex", "Views", "MainWindow.xaml.cs")));

        source.Should().Contain("SurfaceTabBarControl.SurfaceOrderChanged += CheckpointCurrentSession;");
        source.Should().Contain("ViewModel.SessionCheckpointRequested += CheckpointCurrentSession;");
        source.Should().Contain("private void CheckpointCurrentSession()");
        source.Should().Contain("PersistCurrentSession(captureTranscripts: false);");
        source.Should().Contain("ViewModel.SaveSession(Left, Top, Width, Height, WindowState == WindowState.Maximized, captureTranscripts);");
    }

    [Fact]
    public void SurfaceAndWorkspaceViewModels_RequestCheckpointAfterTopologyChanges()
    {
        var workspace = Normalize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "src", "ECodex", "ViewModels", "WorkspaceViewModel.cs")));
        var surface = Normalize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "src", "ECodex", "ViewModels", "SurfaceViewModel.cs")));

        workspace.Should().Contain("public event Action? SessionCheckpointRequested;");
        workspace.Should().Contain("surfaceVm.SessionCheckpointRequested += OnSurfaceSessionCheckpointRequested;");
        workspace.Should().Contain("SessionCheckpointRequested?.Invoke();");
        surface.Should().Contain("public event Action? SessionCheckpointRequested;");
        surface.Should().Contain("SessionCheckpointRequested?.Invoke();");
    }

    private static string Normalize(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);
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
            "ecodex-resume-tests-" + Guid.NewGuid().ToString("N"));

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
/// ecodex.json 配置服务测试 - 验证全局配置与本地配置的加载、合并、验证和 JSONC 支持
/// </summary>
public class ECodexJsonServiceTests
{
    [Fact]
    public void Load_MergesLocalOverGlobal()
    {
        using var temp = TempDirectory.Create();
        var workspaceDir = Path.Combine(temp.Path, "workspace");
        var localDir = Path.Combine(workspaceDir, ".ecodex");
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

        File.WriteAllText(Path.Combine(localDir, "ecodex.json"), """
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

        var result = new ECodexJsonService().Load(workspaceDir, globalPath);

        result.Diagnostics.Should().BeEmpty();
        result.LoadedPaths.Should().HaveCount(2);
        result.Config.Commands.Should().HaveCount(2);
        result.Config.Commands.Single(c => c.Name == "Run Tests").Command.Should().Be("dotnet test");
        result.Config.Commands.Single(c => c.Name == "Run Tests").Confirm.Should().BeTrue();
        result.Config.Commands.Single(c => c.Name == "Run Tests").Keywords.Should().Equal("test", "verify");
        result.Config.Commands.Single(c => c.Name == "Format").Command.Should().Be("dotnet format");
        result.Config.Actions["devServer"].Title.Should().Be("Dev Server Local");
        result.Config.Actions["devServer"].Target.Should().Be(ECodexActionTargets.NewTabInCurrentPane);
    }

    [Fact]
    public void Load_InvalidSchema_ReturnsDiagnostic()
    {
        using var temp = TempDirectory.Create();
        var path = Path.Combine(temp.Path, "ecodex.json");
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

        var result = new ECodexJsonService().LoadFromFiles([path]);

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Severity == ECodexJsonDiagnosticSeverity.Error
            && d.Message.Contains("commands[0].name", StringComparison.Ordinal));
        result.Diagnostics.Should().Contain(d => d.Severity == ECodexJsonDiagnosticSeverity.Error
            && d.Message.Contains("commands[0].command", StringComparison.Ordinal));
        result.Diagnostics.Should().Contain(d => d.Severity == ECodexJsonDiagnosticSeverity.Error
            && d.Message.Contains("actions.broken.command", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_SupportsJsonCommentsAndTrailingCommas()
    {
        using var temp = TempDirectory.Create();
        var path = Path.Combine(temp.Path, "ecodex.json");
        File.WriteAllText(path, """
            {
              // ecodex accepts jsonc-style project files.
              "commands": [
                {
                  "name": "Build",
                  "command": "dotnet build",
                },
              ],
            }
            """);

        var result = new ECodexJsonService().LoadFromFiles([path]);

        result.Diagnostics.Should().BeEmpty();
        result.Config.Commands.Single().Name.Should().Be("Build");
        result.Config.Commands.Single().Command.Should().Be("dotnet build");
    }

    [Fact]
    public void Load_ParsesWorkspaceBrowserSurface()
    {
        using var temp = TempDirectory.Create();
        var path = Path.Combine(temp.Path, "ecodex.json");
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

        var result = new ECodexJsonService().LoadFromFiles([path]);

        result.Diagnostics.Should().BeEmpty();
        result.Config.Workspace.Should().NotBeNull();
        result.Config.Workspace!.SelectedSurfaceIndex.Should().Be(1);
        result.Config.Workspace.Surfaces.Should().HaveCount(2);
        result.Config.Workspace.Surfaces[0].Type.Should().Be(ECodexSurfaceTypes.Terminal);
        result.Config.Workspace.Surfaces[0].Name.Should().Be("Shell");
        result.Config.Workspace.Surfaces[1].Type.Should().Be(ECodexSurfaceTypes.Browser);
        result.Config.Workspace.Surfaces[1].Name.Should().Be("Docs");
        result.Config.Workspace.Surfaces[1].Url.Should().Be("https://example.com/docs");
    }

    [Fact]
    public void Load_LocalWorkspaceOverridesGlobalWorkspace()
    {
        using var temp = TempDirectory.Create();
        var workspaceDir = Path.Combine(temp.Path, "workspace");
        var localDir = Path.Combine(workspaceDir, ".ecodex");
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

        File.WriteAllText(Path.Combine(localDir, "ecodex.json"), """
            {
              "workspace": {
                "surfaces": [
                  { "type": "browser", "name": "Local Docs", "url": "https://local.example" }
                ]
              }
            }
            """);

        var result = new ECodexJsonService().Load(workspaceDir, globalPath);

        result.Diagnostics.Should().BeEmpty();
        result.Config.Workspace!.Surfaces.Should().ContainSingle();
        result.Config.Workspace.Surfaces.Single().Name.Should().Be("Local Docs");
        result.Config.Workspace.Surfaces.Single().Url.Should().Be("https://local.example");
    }

    [Fact]
    public void Load_BrowserSurfaceWithoutUrl_ReturnsDiagnostic()
    {
        using var temp = TempDirectory.Create();
        var path = Path.Combine(temp.Path, "ecodex.json");
        File.WriteAllText(path, """
            {
              "workspace": {
                "surfaces": [
                  { "type": "browser", "name": "Preview" }
                ]
              }
            }
            """);

        var result = new ECodexJsonService().LoadFromFiles([path]);

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().Contain(d => d.Severity == ECodexJsonDiagnosticSeverity.Error
            && d.Message.Contains("workspace.surfaces[0].url", StringComparison.Ordinal));
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "ecodex-tests-" + Guid.NewGuid().ToString("N"));

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

public class TerminalSessionTests
{
    [Fact]
    public void Start_InvalidCommand_DoesNotThrowAndMarksSessionStopped()
    {
        using var session = new TerminalSession("invalid-shell", 80, 8);
        var missingShell = Path.Combine(
            Path.GetTempPath(),
            $"ecodex-missing-shell-{Guid.NewGuid():N}.exe");

        var act = () => session.Start(command: missingShell);

        act.Should().NotThrow();
        session.IsRunning.Should().BeFalse();
        session.Buffer.ExportPlainText().Should().Contain("Failed to start terminal");
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
        var node = ECodex.Core.Models.SplitNode.CreateLeaf("pane-1");
        node.IsLeaf.Should().BeTrue();
        node.PaneId.Should().Be("pane-1");
    }

    [Fact]
    public void Split_TurnsLeafIntoContainer()
    {
        var node = ECodex.Core.Models.SplitNode.CreateLeaf("pane-1");

        var newChild = node.Split(ECodex.Core.Models.SplitDirection.Vertical);

        node.IsLeaf.Should().BeFalse();
        node.First.Should().NotBeNull();
        node.Second.Should().NotBeNull();
        node.First!.PaneId.Should().Be("pane-1");
        newChild.PaneId.Should().NotBeNull();
    }

    [Fact]
    public void Split_NonLeaf_ThrowsInvalidOperation()
    {
        var node = ECodex.Core.Models.SplitNode.CreateLeaf("pane-1");
        node.Split(ECodex.Core.Models.SplitDirection.Vertical);

        var act = () => node.Split(ECodex.Core.Models.SplitDirection.Horizontal);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FindNode_FindsLeaf()
    {
        var node = ECodex.Core.Models.SplitNode.CreateLeaf("pane-1");
        node.Split(ECodex.Core.Models.SplitDirection.Vertical);

        var found = node.FindNode("pane-1");

        found.Should().NotBeNull();
        found!.PaneId.Should().Be("pane-1");
    }

    [Fact]
    public void GetLeaves_ReturnsAllLeaves()
    {
        var node = ECodex.Core.Models.SplitNode.CreateLeaf("pane-1");
        node.Split(ECodex.Core.Models.SplitDirection.Vertical);

        var leaves = node.GetLeaves().ToList();

        leaves.Should().HaveCount(2);
        leaves[0].PaneId.Should().Be("pane-1");
    }

    [Fact]
    public void Remove_CollapsesParent()
    {
        var node = ECodex.Core.Models.SplitNode.CreateLeaf("pane-1");
        var newChild = node.Split(ECodex.Core.Models.SplitDirection.Vertical);
        var newPaneId = newChild.PaneId!;

        bool removed = node.Remove(newPaneId);

        removed.Should().BeTrue();
        node.IsLeaf.Should().BeTrue();
        node.PaneId.Should().Be("pane-1");
    }

    [Fact]
    public void GetNextLeaf_CyclesCorrectly()
    {
        var node = ECodex.Core.Models.SplitNode.CreateLeaf("pane-1");
        var child2 = node.Split(ECodex.Core.Models.SplitDirection.Vertical);

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
        var node = ECodex.Core.Models.SplitNode.CreateLeaf("pane-1");
        var child2 = node.Split(ECodex.Core.Models.SplitDirection.Vertical);

        var previous = node.GetPreviousLeaf("pane-1");

        previous.Should().NotBeNull();
        previous!.PaneId.Should().Be(child2.PaneId);
    }

    [Fact]
    public void Remove_NestedLeaf_CollapsesOnlyContainingParent()
    {
        var root = ECodex.Core.Models.SplitNode.CreateLeaf("pane-1");
        var right = root.Split(ECodex.Core.Models.SplitDirection.Vertical);
        var rightPaneId = right.PaneId!;
        var nested = right.Split(ECodex.Core.Models.SplitDirection.Horizontal);

        var removed = root.Remove(nested.PaneId!);

        removed.Should().BeTrue();
        root.IsLeaf.Should().BeFalse();
        root.GetLeaves().Select(l => l.PaneId).Should().Equal("pane-1", rightPaneId);
    }

    [Fact]
    public void Remove_MissingPane_ReturnsFalse()
    {
        var node = ECodex.Core.Models.SplitNode.CreateLeaf("pane-1");
        node.Split(ECodex.Core.Models.SplitDirection.Vertical);

        var removed = node.Remove("missing-pane");

        removed.Should().BeFalse();
        node.GetLeaves().Should().HaveCount(2);
    }

    [Fact]
    public void CreateColumns_CreatesRequestedVerticalLeaves()
    {
        var node = ECodex.Core.Models.SplitNode.CreateColumns(3);

        node.Direction.Should().Be(ECodex.Core.Models.SplitDirection.Vertical);
        node.GetLeaves().Should().HaveCount(3);
        node.SplitRatio.Should().BeApproximately(2d / 3d, 0.0001);
    }

    [Fact]
    public void CreateRows_CreatesRequestedHorizontalLeaves()
    {
        var node = ECodex.Core.Models.SplitNode.CreateRows(3);

        node.Direction.Should().Be(ECodex.Core.Models.SplitDirection.Horizontal);
        node.GetLeaves().Should().HaveCount(3);
        node.SplitRatio.Should().BeApproximately(2d / 3d, 0.0001);
    }

    [Fact]
    public void CreateGrid_CreatesFourLeaves()
    {
        var node = ECodex.Core.Models.SplitNode.CreateGrid();

        node.Direction.Should().Be(ECodex.Core.Models.SplitDirection.Horizontal);
        node.GetLeaves().Should().HaveCount(4);
        node.First!.Direction.Should().Be(ECodex.Core.Models.SplitDirection.Vertical);
        node.Second!.Direction.Should().Be(ECodex.Core.Models.SplitDirection.Vertical);
    }

    [Fact]
    public void CreateMainStack_CreatesMainPaneAndStack()
    {
        var node = ECodex.Core.Models.SplitNode.CreateMainStack(stackCount: 3);

        node.Direction.Should().Be(ECodex.Core.Models.SplitDirection.Vertical);
        node.SplitRatio.Should().Be(0.6);
        node.GetLeaves().Should().HaveCount(4);
        node.Second!.Direction.Should().Be(ECodex.Core.Models.SplitDirection.Horizontal);
    }

    [Fact]
    public void Equalize_ResetsNestedSplitRatios()
    {
        var node = ECodex.Core.Models.SplitNode.CreateMainStack(stackCount: 3);
        node.SplitRatio = 0.8;
        node.Second!.SplitRatio = 0.2;

        node.Equalize();

        node.SplitRatio.Should().Be(0.5);
        node.Second!.SplitRatio.Should().Be(0.5);
    }

    [Fact]
    public void ResizePane_AdjustsNearestDirectParentAndClamps()
    {
        var node = ECodex.Core.Models.SplitNode.CreateLeaf("pane-1");
        var child2 = node.Split(ECodex.Core.Models.SplitDirection.Vertical);

        var resizedFirst = node.ResizePane("pane-1", 0.2);
        var resizedSecond = node.ResizePane(child2.PaneId!, 1.0);

        resizedFirst.Should().BeTrue();
        resizedSecond.Should().BeTrue();
        node.SplitRatio.Should().Be(0.1);
    }

    [Fact]
    public void SwapPanes_ExchangesLeafPaneIds()
    {
        var node = ECodex.Core.Models.SplitNode.CreateLeaf("pane-1");
        var child2 = node.Split(ECodex.Core.Models.SplitDirection.Vertical);
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
