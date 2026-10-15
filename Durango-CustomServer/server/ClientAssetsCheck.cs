using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DurangoServerNx;

// HTTP 200 does not guarantee that the client can load a table: duplicate keys
// are accepted by the server's dictionaries but reject the client's data load.
internal static class ClientAssetsCheck
{
    public static int Run(string dataDir)
    {
        string assets = Path.Combine(dataDir, "assets");
        if (!Directory.Exists(assets))
        {
            Console.Error.WriteLine($"[client-assets-check] FAIL: diretório ausente: {assets}");
            return 1;
        }

        string[] files = Directory.GetFiles(assets, "*.json", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            Console.Error.WriteLine("[client-assets-check] FAIL: nenhum catálogo JSON encontrado");
            return 1;
        }

        int failures = 0;
        var settings = new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error };
        foreach (string file in files)
        {
            try
            {
                JToken.Parse(File.ReadAllText(file), settings);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                failures++;
                Console.Error.WriteLine($"[client-assets-check] FAIL {Path.GetRelativePath(assets, file)}: {ex.Message}");
            }
        }

        Console.WriteLine($"[client-assets-check] {(failures == 0 ? "PASS" : "FAIL")}: {files.Length} catálogos, {failures} inválidos");
        return failures == 0 ? 0 : 1;
    }
}
