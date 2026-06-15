using System.Diagnostics;

namespace ECodeX.Core.Services;

/// <summary>
/// 提取工作目录的 git 信息（分支、远程、PR 状态）。
/// </summary>
public static class GitService
{
    /// <summary>
    /// 获取指定目录的当前 git 分支名称。
    /// 若非 git 仓库则返回 null。
    /// </summary>
    public static string? GetBranch(string? workingDirectory)
    {
        if (string.IsNullOrEmpty(workingDirectory)) return null;

        // 快速路径：直接读取 .git/HEAD
        var gitHeadPath = FindGitHead(workingDirectory);
        if (gitHeadPath != null && File.Exists(gitHeadPath))
        {
            try
            {
                var content = File.ReadAllText(gitHeadPath).Trim();
                const string refPrefix = "ref: refs/heads/";
                if (content.StartsWith(refPrefix))
                    return content[refPrefix.Length..];

                // 分离 HEAD — 返回短 SHA
                if (content.Length >= 7)
                    return content[..7];
            }
            catch
            {
                // 回退到 git 命令
            }
        }

        return RunGit("rev-parse --abbrev-ref HEAD", workingDirectory);
    }

    /// <summary>
    /// 获取指定目录的 git 远程 URL。
    /// </summary>
    public static string? GetRemoteUrl(string? workingDirectory)
    {
        if (string.IsNullOrEmpty(workingDirectory)) return null;
        return RunGit("config --get remote.origin.url", workingDirectory);
    }

    private static string? FindGitHead(string directory)
    {
        var current = directory;
        while (current != null)
        {
            var gitDir = Path.Combine(current, ".git");
            if (Directory.Exists(gitDir))
                return Path.Combine(gitDir, "HEAD");

            // 处理 .git 文件（worktree）
            if (File.Exists(gitDir))
            {
                try
                {
                    var content = File.ReadAllText(gitDir).Trim();
                    if (content.StartsWith("gitdir: "))
                    {
                        var gitDirPath = content["gitdir: ".Length..];
                        if (!Path.IsPathRooted(gitDirPath))
                            gitDirPath = Path.GetFullPath(Path.Combine(current, gitDirPath));
                        return Path.Combine(gitDirPath, "HEAD");
                    }
                }
                catch
                {
                    // 继续
                }
            }

            current = Path.GetDirectoryName(current);
        }
        return null;
    }

    private static string? RunGit(string arguments, string workingDirectory)
    {
        try
        {
            var psi = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(3000);

            return process.ExitCode == 0 && !string.IsNullOrEmpty(output) ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
