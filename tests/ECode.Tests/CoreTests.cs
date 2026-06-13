using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.Json;
using ECode.Core.IPC;
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

    private static TerminalNotification CreateNotification(
        string id,
        bool isRead,
        DateTime timestamp,
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
        Body = "body",
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
            WorkingDirectory = @"C:\repo",
            Command = "pwsh",
            Data = "hello",
        };

        var roundTripped = RoundTrip<DaemonRequest>(request);

        roundTripped.Type.Should().Be(DaemonMessageTypes.SessionCreate);
        roundTripped.PaneId.Should().Be("pane-1");
        roundTripped.Cols.Should().Be(120);
        roundTripped.Rows.Should().Be(40);
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
            Title = "agent",
            IsRunning = true,
            IsExisting = true,
        };

        var roundTripped = RoundTrip<DaemonSessionInfo>(session);

        roundTripped.PaneId.Should().Be("pane-2");
        roundTripped.Cols.Should().Be(100);
        roundTripped.Rows.Should().Be(30);
        roundTripped.WorkingDirectory.Should().Be(@"D:\work");
        roundTripped.Title.Should().Be("agent");
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
                    Kind = ResumeBindingKinds.Agent,
                    Checkpoint = "work",
                    Shell = "codex resume abc123",
                    WorkingDirectory = @"C:\repo",
                    Environment = new Dictionary<string, string>
                    {
                        ["SAFE_KEY"] = "value",
                    },
                    Trusted = true,
                    TrustReason = "user-approved-prefix",
                    ApprovedPrefix = "codex resume",
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
        binding.Kind.Should().Be(ResumeBindingKinds.Agent);
        binding.Checkpoint.Should().Be("work");
        binding.Shell.Should().Be("codex resume abc123");
        binding.WorkingDirectory.Should().Be(@"C:\repo");
        binding.Environment.Should().ContainKey("SAFE_KEY").WhoseValue.Should().Be("value");
        binding.Trusted.Should().BeTrue();
        binding.TrustReason.Should().Be("user-approved-prefix");
        binding.ApprovedPrefix.Should().Be("codex resume");
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
        loaded.Bindings.Single().Shell.Should().Be("codex resume abc123");
    }

    [Fact]
    public void Add_InsertsAndReplacesById()
    {
        using var temp = TempDirectory.Create();
        var service = new ResumeBindingService(Path.Combine(temp.Path, "resume.json"));
        var binding = CreateResumeBinding("binding-1", "workspace-1", "surface-1", "pane-1");

        service.Add(binding);
        binding.Shell = "codex resume updated";
        service.Add(binding);

        var loaded = service.Load();
        loaded.Bindings.Should().ContainSingle();
        loaded.Bindings.Single().Shell.Should().Be("codex resume updated");
        loaded.Bindings.Single().UpdatedAtUtc.Should().BeAfter(loaded.Bindings.Single().CreatedAtUtc.AddTicks(-1));
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
        service.Add(CreateResumeBinding("target", "workspace-1", "surface-1", "pane-1", @"C:\repo", "codex resume abc123"));
        service.Add(CreateResumeBinding("wrong-prefix", "workspace-1", "surface-1", "pane-2", @"C:\repo", "npm test"));
        service.Add(CreateResumeBinding("wrong-cwd", "workspace-1", "surface-1", "pane-3", @"C:\other", "codex resume abc123"));
        service.Add(CreateResumeBinding("wrong-surface", "workspace-1", "surface-2", "pane-4", @"C:\repo", "codex resume abc123"));

        var trusted = service.TrustPrefix("workspace-1", "surface-1", "codex resume", @"C:\repo");

        trusted.Should().Be(1);
        var bindings = service.Load().Bindings.ToDictionary(b => b.Id);
        bindings["target"].Trusted.Should().BeTrue();
        bindings["target"].TrustReason.Should().Be("user-approved-prefix");
        bindings["target"].ApprovedPrefix.Should().Be("codex resume");
        bindings["wrong-prefix"].Trusted.Should().BeFalse();
        bindings["wrong-cwd"].Trusted.Should().BeFalse();
        bindings["wrong-surface"].Trusted.Should().BeFalse();
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
        string shell = "codex resume abc123") => new()
    {
        Id = id,
        WorkspaceId = workspaceId,
        SurfaceId = surfaceId,
        PaneId = paneId,
        Kind = ResumeBindingKinds.Agent,
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
                "codex": {
                  "type": "command",
                  "title": "Codex",
                  "command": "codex",
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
                "codex": {
                  "type": "command",
                  "title": "Codex Local",
                  "command": "codex --full-auto",
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
        result.Config.Actions["codex"].Title.Should().Be("Codex Local");
        result.Config.Actions["codex"].Target.Should().Be(EcodeActionTargets.NewTabInCurrentPane);
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

        parser.Feed("\x1b]777;notify;Claude;Waiting for input\x07");

        receivedOsc.Should().Be("777;notify;Claude;Waiting for input");
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

        handler.Handle("99;t=Claude Code;b=Waiting for input");

        title.Should().Be("Claude Code");
        body.Should().Be("Waiting for input");
    }

    [Fact]
    public void Handle_Osc777_Notify_ParsesCorrectly()
    {
        var handler = new OscHandler();
        string? title = null, body = null;
        handler.NotificationReceived += (t, s, b) => { title = t; body = b; };

        handler.Handle("777;notify;Claude;Task completed");

        title.Should().Be("Claude");
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
