using System.ComponentModel;
using System.Text;
using System.Runtime.InteropServices;
using static ECodeX.Core.Terminal.ConPtyInterop;

namespace ECodeX.Core.Terminal;

/// <summary>
/// 管理附加到 ConPTY 伪控制台的 Shell 进程。
/// </summary>
public sealed class TerminalProcess : IDisposable
{
    private readonly PROCESS_INFORMATION _processInfo;
    private IntPtr _attributeList;
    private bool _disposed;
    private readonly Thread _waitThread;

    public int ProcessId => _processInfo.dwProcessId;
    public IntPtr ProcessHandle => _processInfo.hProcess;

    public event Action? Exited;

    public TerminalProcess(
        PseudoConsole console,
        string? command = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var shellCommand = command ?? DetectShell();

        // 初始化 ConPTY 线程属性列表
        _attributeList = CreateAttributeList(console.Handle);

        // 使用 ConPTY 创建进程
        var startupInfo = new STARTUPINFOEX
        {
            lpAttributeList = _attributeList,
        };
        startupInfo.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();

        var environmentBlock = CreateEnvironmentBlock(environment);
        bool success;
        try
        {
            success = CreateProcess(
                null,
                shellCommand,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT,
                environmentBlock,
                workingDirectory,
                ref startupInfo,
                out _processInfo);
        }
        finally
        {
            if (environmentBlock != IntPtr.Zero)
                Marshal.FreeHGlobal(environmentBlock);
        }

        if (!success)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create process with ConPTY.");

        // 启动后台线程等待进程退出
        _waitThread = new Thread(WaitForExitThread)
        {
            IsBackground = true,
            Name = $"ConPTY-Wait-{_processInfo.dwProcessId}",
        };
        _waitThread.Start();
    }

    /// <summary>
    /// 检测系统上可用的最佳 Shell。
    /// 优先级：pwsh.exe > powershell.exe > cmd.exe
    /// </summary>
    private static string DetectShell()
    {
        // 检测 PowerShell 7+ (pwsh)
        var pwshPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"),
            "pwsh.exe",
        };

        foreach (var path in pwshPaths)
        {
            if (path == "pwsh.exe")
            {
                // 检测是否在 PATH 中
                var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (var dir in pathEnv.Split(Path.PathSeparator))
                {
                    var fullPath = Path.Combine(dir, "pwsh.exe");
                    if (File.Exists(fullPath))
                        return fullPath;
                }
            }
            else if (File.Exists(path))
            {
                return path;
            }
        }

        // 回退到 Windows PowerShell
        var winPowerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (File.Exists(winPowerShell))
            return winPowerShell;

        // 最后手段：从 COMSPEC 获取 cmd.exe
        var comspec = Environment.GetEnvironmentVariable("COMSPEC");
        if (!string.IsNullOrEmpty(comspec) && File.Exists(comspec))
            return comspec;

        return "cmd.exe";
    }

    private static IntPtr CreateEnvironmentBlock(IReadOnlyDictionary<string, string>? environment)
    {
        if (environment == null || environment.Count == 0)
            return IntPtr.Zero;

        var merged = TerminalEnvironmentVariables.MergeWithCurrent(environment);
        var block = new StringBuilder();
        foreach (var (key, value) in merged)
        {
            block.Append(key).Append('=').Append(value).Append('\0');
        }

        block.Append('\0');
        return Marshal.StringToHGlobalUni(block.ToString());
    }

    private static IntPtr CreateAttributeList(IntPtr conPtyHandle)
    {
        // 查询所需大小
        var size = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);

        var attributeList = Marshal.AllocHGlobal(size);

        if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref size))
        {
            Marshal.FreeHGlobal(attributeList);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "InitializeProcThreadAttributeList failed.");
        }

        if (!UpdateProcThreadAttribute(
            attributeList,
            0,
            (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
            conPtyHandle,
            (IntPtr)IntPtr.Size,
            IntPtr.Zero,
            IntPtr.Zero))
        {
            DeleteProcThreadAttributeList(attributeList);
            Marshal.FreeHGlobal(attributeList);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "UpdateProcThreadAttribute failed.");
        }

        return attributeList;
    }

    private void WaitForExitThread()
    {
        WaitForSingleObject(_processInfo.hProcess, INFINITE);
        Exited?.Invoke();
    }

    public void WaitForExit()
    {
        WaitForSingleObject(_processInfo.hProcess, INFINITE);
    }

    public bool HasExited
    {
        get
        {
            if (!GetExitCodeProcess(_processInfo.hProcess, out uint exitCode))
                return true;
            return exitCode != STILL_ACTIVE;
        }
    }

    public void Kill()
    {
        if (!_disposed && !HasExited)
        {
            TerminateProcess(_processInfo.hProcess, 1);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Kill();

        if (_processInfo.hProcess != IntPtr.Zero)
            CloseHandle(_processInfo.hProcess);
        if (_processInfo.hThread != IntPtr.Zero)
            CloseHandle(_processInfo.hThread);

        if (_attributeList != IntPtr.Zero)
        {
            DeleteProcThreadAttributeList(_attributeList);
            Marshal.FreeHGlobal(_attributeList);
            _attributeList = IntPtr.Zero;
        }
    }
}
