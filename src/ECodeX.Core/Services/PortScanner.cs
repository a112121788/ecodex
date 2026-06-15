using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;

namespace ECodeX.Core.Services;

/// <summary>
/// 扫描终端面板的子进程正在监听的 TCP 端口。
/// 用于在侧边栏显示正在监听的端口（类似 ecodex 的 PortScanner.swift）。
/// </summary>
public static class PortScanner
{
    /// <summary>
    /// 获取指定进程及其子进程中所有处于 LISTEN 状态的 TCP 端口。
    /// </summary>
    public static List<int> GetListeningPorts(int processId)
    {
        var ports = new HashSet<int>();
        var processIds = GetProcessTree(processId);

        try
        {
            var properties = IPGlobalProperties.GetIPGlobalProperties();
            var listeners = properties.GetActiveTcpListeners();

            // netstat 方法：通过 PowerShell 匹配端口与 PID
            // 比遍历所有连接更快
            var pidPorts = GetPidPortMap();

            foreach (var (pid, port) in pidPorts)
            {
                if (processIds.Contains(pid) && port > 0)
                    ports.Add(port);
            }
        }
        catch
        {
            // 端口扫描是尽力而为的
        }

        return ports.OrderBy(p => p).ToList();
    }

    /// <summary>
    /// 获取进程树中的所有进程 ID（父进程 + 所有后代）。
    /// </summary>
    private static HashSet<int> GetProcessTree(int rootPid)
    {
        var tree = new HashSet<int> { rootPid };
        try
        {
            // 使用 WMI 构建父->子映射
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT ProcessId, ParentProcessId FROM Win32_Process");
            var parentMap = new Dictionary<int, List<int>>();
            foreach (var obj in searcher.Get())
            {
                int pid = Convert.ToInt32(obj["ProcessId"]);
                int ppid = Convert.ToInt32(obj["ParentProcessId"]);
                if (!parentMap.ContainsKey(ppid))
                    parentMap[ppid] = [];
                parentMap[ppid].Add(pid);
            }
            // BFS 查找所有后代
            var queue = new Queue<int>();
            queue.Enqueue(rootPid);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (parentMap.TryGetValue(current, out var children))
                {
                    foreach (var child in children)
                    {
                        if (tree.Add(child))
                            queue.Enqueue(child);
                    }
                }
            }
        }
        catch
        {
            // 尽力而为 — WMI 可能不可用
        }
        return tree;
    }

    /// <summary>
    /// 使用 netstat 获取 PID 到监听 TCP 端口的映射。
    /// </summary>
    private static List<(int pid, int port)> GetPidPortMap()
    {
        var results = new List<(int pid, int port)>();

        try
        {
            var psi = new ProcessStartInfo("netstat", "-ano -p TCP")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process == null) return results;

            while (!process.StandardOutput.EndOfStream)
            {
                var line = process.StandardOutput.ReadLine();
                if (line == null) continue;

                // 解析行如：TCP    0.0.0.0:3000    0.0.0.0:0    LISTENING    12345
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5 &&
                    parts[0] == "TCP" &&
                    parts[3] == "LISTENING" &&
                    int.TryParse(parts[4], out int pid))
                {
                    var localAddr = parts[1];
                    int colonIndex = localAddr.LastIndexOf(':');
                    if (colonIndex >= 0 && int.TryParse(localAddr[(colonIndex + 1)..], out int port))
                    {
                        results.Add((pid, port));
                    }
                }
            }

            process.WaitForExit(3000);
        }
        catch
        {
            // 尽力而为
        }

        return results;
    }
}
