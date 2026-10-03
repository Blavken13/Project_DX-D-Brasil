using System;
using System.IO;

namespace Durango.Online;

// Docker keeps administrator edits in the existing persistent state volume.
// Only configuration files are restored; game tables still come from each new image.
internal static class AdminConfigStore
{
    private static string DirectoryPath => Environment.GetEnvironmentVariable("DURANGO_ADMIN_CONFIG_DIR");

    private static bool IsConfig(string relative) => relative == "config.json" || relative == "islands.json"
        || relative == "whitelist.txt"
        || System.Text.RegularExpressions.Regex.IsMatch(relative, "^islands/[A-Za-z0-9_-]+/config\\.json$");

    public static void Restore(string dataDir)
    {
        string source = DirectoryPath;
        if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source)) return;
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file).Replace('\\', '/');
            if (!IsConfig(relative)) continue;
            string target = Path.Combine(dataDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(file, target, overwrite: true);
        }
    }

    public static void Write(string dataDir, string path, string content)
    {
        string relative = Path.GetRelativePath(dataDir, path).Replace('\\', '/');
        if (!IsConfig(relative)) throw new InvalidOperationException("Arquivo de configuração inválido.");
        AtomicWrite(path, content);
        if (!string.IsNullOrWhiteSpace(DirectoryPath)) AtomicWrite(Path.Combine(DirectoryPath, relative), content);
    }

    private static void AtomicWrite(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, content);
        File.Move(temporary, path, overwrite: true);
    }
}
