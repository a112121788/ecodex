using System.Diagnostics;
using System.Runtime.InteropServices;
using ECode.Core.Terminal;

namespace ECode.Smoke;

internal static class Program
{
    private const string UnicodeRelativePathForCi = "中文 目录/项目/";

    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool FreeConsole();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AllocConsole();

    private static StreamWriter _log = StreamWriter.Null;
    private static int _failures;
    private static int _outputEvents;
    private static int _outputBytes;

    private static void Check(string label, bool ok, string? detail = null)
    {
        var line = ok ? $"  PASS  {label}" : $"  FAIL  {label}{(detail is null ? "" : $"  -- {detail}")}";
        _log.WriteLine(line);
        if (!ok) _failures++;
    }

    private static void Log(string line) => _log.WriteLine(line);

    [STAThread]
    private static async Task<int> Main()
    {
        var logPath = Path.Combine(Path.GetTempPath(), "ecode-smoke.log");
        _log = new StreamWriter(logPath, append: false) { AutoFlush = true };
        Log($"=== ECode ConPTY smoke (log: {logPath}) ===");
        Log($"  [setup] FreeConsole result: {FreeConsole()}");

        try
        {
            await TestEnvironmentInjection();
            await TestUnicodeWorkingDirectory();
        }
        finally
        {
            AllocConsole();
        }

        Log("");
        Log(_failures == 0 ? "ALL CHECKS PASSED" : $"{_failures} CHECK(S) FAILED");
        _log.Dispose();
        return _failures == 0 ? 0 : 1;
    }

    private static async Task TestEnvironmentInjection()
    {
        Log("\n[1] Environment injection (raw output event)");
        using var session = new TerminalSession("smoke-env", 120, 30);
        session.OutputReceived += () => Interlocked.Increment(ref _outputEvents);
        session.RawOutputReceived += bytes => Interlocked.Add(ref _outputBytes, bytes.Length);
        session.ProcessExited += () => Log("    [env] ProcessExited");
        session.Start();
        var pid = session.ProcessId;
        Log($"    [env] ProcessId={pid}");
        if (pid != null)
        {
            try { var p = Process.GetProcessById(pid.Value); Log($"    [env] process alive, hasExited={p.HasExited}"); }
            catch (Exception ex) { Log($"    [env] process lookup failed: {ex.Message}"); }
        }
        await Task.Delay(3000);
        if (pid != null)
        {
            try { var p = Process.GetProcessById(pid.Value); Log($"    [env] process after 3s, hasExited={p.HasExited}"); }
            catch (Exception ex) { Log($"    [env] process after 3s lookup failed: {ex.Message}"); }
        }
        var events = Interlocked.CompareExchange(ref _outputEvents, 0, 0);
        var bytes = Interlocked.CompareExchange(ref _outputBytes, 0, 0);
        Log($"    [env] events={events} bytes={bytes}");
        Check("ConPTY produced raw output", events > 0 && bytes > 0, $"events={events} bytes={bytes}");
    }

    private static async Task TestUnicodeWorkingDirectory()
    {
        Log($"\n[2] Windows path / Chinese path smoke ({UnicodeRelativePathForCi})");
        var root = Path.Combine(Path.GetTempPath(), "ecode-smoke-" + Guid.NewGuid().ToString("N"));
        var unicodeProjectDir = Path.Combine(new[] { root }.Concat(
            UnicodeRelativePathForCi.Split('/', StringSplitOptions.RemoveEmptyEntries)).ToArray());
        var markerFileName = "ecode-smoke-marker.txt";
        Directory.CreateDirectory(unicodeProjectDir);
        await File.WriteAllTextAsync(Path.Combine(unicodeProjectDir, markerFileName), "ok");

        var outputEvents = 0;
        var outputBytes = 0;
        var childProofFileName = "ecode-smoke-child-ok.txt";
        var childProofPath = Path.Combine(unicodeProjectDir, childProofFileName);
        var command = BuildUnicodePathCommand(markerFileName, childProofFileName);

        try
        {
            using var session = new TerminalSession("smoke-unicode-cwd", 140, 30);
            session.OutputReceived += () => Interlocked.Increment(ref outputEvents);
            session.RawOutputReceived += bytes => Interlocked.Add(ref outputBytes, bytes.Length);
            session.ProcessExited += () => Log("    [unicode] ProcessExited");
            session.WorkingDirectoryChanged += dir => Log($"    [unicode] cwd event={dir}");
            session.Start(
                command: command,
                workingDirectory: unicodeProjectDir,
                environment: new Dictionary<string, string>
                {
                    ["ECODE_SMOKE_UNICODE"] = unicodeProjectDir,
                });

            var proofCreated = await WaitForFile(childProofPath, TimeSpan.FromSeconds(8));
            await Task.Delay(250);

            Log($"    [unicode] expected cwd={unicodeProjectDir}");
            Log($"    [unicode] proof file={childProofPath}");
            Log($"    [unicode] events={outputEvents} bytes={outputBytes}");

            Check("Unicode cwd proof file created", proofCreated, $"missing '{childProofPath}'");
            Check(
                "Unicode cwd used by child process",
                proofCreated && string.Equals((await File.ReadAllTextAsync(childProofPath)).Trim(), "OK", StringComparison.Ordinal),
                $"marker file was not found from cwd '{unicodeProjectDir}'");
            Check(
                "TerminalSession WorkingDirectory preserved",
                string.Equals(session.WorkingDirectory, unicodeProjectDir, StringComparison.OrdinalIgnoreCase),
                $"session cwd='{session.WorkingDirectory}'");
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch (Exception ex)
            {
                Log($"    [unicode] cleanup failed: {ex.Message}");
            }
        }
    }

    private static string BuildUnicodePathCommand(string markerFileName, string childProofFileName)
    {
        var cmd = Environment.GetEnvironmentVariable("COMSPEC");
        if (string.IsNullOrWhiteSpace(cmd) || !File.Exists(cmd))
            cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");

        return $"\"{cmd}\" /d /c \"if exist {markerFileName} echo OK>{childProofFileName}\"";
    }

    private static async Task<bool> WaitForFile(string path, TimeSpan timeout)
    {
        var started = Stopwatch.StartNew();
        while (started.Elapsed < timeout)
        {
            if (File.Exists(path))
                return true;

            await Task.Delay(100);
        }

        return File.Exists(path);
    }

}
