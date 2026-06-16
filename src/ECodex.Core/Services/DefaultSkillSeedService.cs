namespace ECodex.Core.Services;

public sealed record DefaultSkillSeedResult(
    string SourceDirectory,
    string TargetDirectory,
    IReadOnlyList<string> CopiedSkills,
    IReadOnlyList<string> SkippedSkills,
    IReadOnlyList<string> Errors);

public sealed class DefaultSkillSeedService
{
    public const string BundledSkillsDirectoryName = "default-skills";

    public static string GetDefaultTargetDirectory()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".agents", "skills");
    }

    public DefaultSkillSeedResult Seed(string sourceDirectory, string targetDirectory)
    {
        var copied = new List<string>();
        var skipped = new List<string>();
        var errors = new List<string>();

        if (!Directory.Exists(sourceDirectory))
            return new DefaultSkillSeedResult(sourceDirectory, targetDirectory, copied, skipped, errors);

        Directory.CreateDirectory(targetDirectory);

        foreach (var skillDirectory in Directory.GetDirectories(sourceDirectory).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var skillName = Path.GetFileName(skillDirectory);
            if (string.IsNullOrWhiteSpace(skillName))
                continue;

            var targetSkillDirectory = Path.Combine(targetDirectory, skillName);
            if (Directory.Exists(targetSkillDirectory))
            {
                skipped.Add(skillName);
                continue;
            }

            try
            {
                CopyDirectory(skillDirectory, targetSkillDirectory);
                copied.Add(skillName);
            }
            catch (Exception ex)
            {
                errors.Add($"{skillName}: {ex.Message}");
            }
        }

        return new DefaultSkillSeedResult(sourceDirectory, targetDirectory, copied, skipped, errors);
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        foreach (var file in Directory.GetFiles(sourceDirectory))
        {
            var targetFile = Path.Combine(targetDirectory, Path.GetFileName(file));
            File.Copy(file, targetFile, overwrite: false);
        }

        foreach (var childDirectory in Directory.GetDirectories(sourceDirectory))
        {
            var targetChild = Path.Combine(targetDirectory, Path.GetFileName(childDirectory));
            CopyDirectory(childDirectory, targetChild);
        }
    }
}
