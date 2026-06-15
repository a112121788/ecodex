using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using static ECodeX.Core.Terminal.ConPtyInterop;

namespace ECodeX.Core.Terminal;

/// <summary>
/// 封装 Windows 伪控制台（ConPTY）句柄。提供用于读取伪控制台输出
/// 和向其写入输入的管道。
/// </summary>
public sealed class PseudoConsole : IDisposable
{
    private IntPtr _handle;
    private bool _disposed;

    /// <summary>调用方从此管道读取终端输出。</summary>
    public SafeFileHandle ReadPipe { get; }

    /// <summary>调用方向此管道写入以向终端发送输入。</summary>
    public SafeFileHandle WritePipe { get; }

    /// <summary>用于创建进程的原始 ConPTY 句柄。</summary>
    public IntPtr Handle => _handle;

    private PseudoConsole(IntPtr handle, SafeFileHandle readPipe, SafeFileHandle writePipe)
    {
        _handle = handle;
        ReadPipe = readPipe;
        WritePipe = writePipe;
    }

    /// <summary>
    /// 按给定尺寸创建新的伪控制台。
    /// </summary>
    public static PseudoConsole Create(short cols, short rows)
    {
        // 为伪控制台的输入创建管道
        var inputSa = new SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            bInheritHandle = true,
        };

        if (!CreatePipe(out var inputReadPipe, out var inputWritePipe, ref inputSa, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create input pipe.");

        // 为伪控制台的输出创建管道
        var outputSa = new SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            bInheritHandle = true,
        };

        if (!CreatePipe(out var outputReadPipe, out var outputWritePipe, ref outputSa, 0))
        {
            inputReadPipe.Dispose();
            inputWritePipe.Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create output pipe.");
        }

        var size = new COORD(cols, rows);
        int hr = CreatePseudoConsole(size, inputReadPipe, outputWritePipe, 0, out var handle);

        if (hr != 0)
        {
            inputReadPipe.Dispose();
            inputWritePipe.Dispose();
            outputReadPipe.Dispose();
            outputWritePipe.Dispose();
            throw new Win32Exception(hr, $"CreatePseudoConsole failed with HRESULT 0x{hr:X8}");
        }

        // 关闭归 ConPTY 所有的管道端
        // ConPTY 现在拥有 inputReadPipe 和 outputWritePipe
        inputReadPipe.Dispose();
        outputWritePipe.Dispose();

        // 调用方向 inputWritePipe 写入（终端输入）
        // 调用方从 outputReadPipe 读取（终端输出）
        return new PseudoConsole(handle, outputReadPipe, inputWritePipe);
    }

    /// <summary>
    /// 调整伪控制台尺寸。
    /// </summary>
    public void Resize(short cols, short rows)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int hr = ResizePseudoConsole(_handle, new COORD(cols, rows));
        if (hr != 0)
            throw new Win32Exception(hr, $"ResizePseudoConsole failed with HRESULT 0x{hr:X8}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        ClosePseudoConsole(_handle);
        _handle = IntPtr.Zero;

        ReadPipe.Dispose();
        WritePipe.Dispose();
    }
}
