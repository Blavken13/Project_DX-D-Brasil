using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Durango.Online;

internal static class AndroidAssetBundleCheck
{
    public const string IndexName = "Info.5.2.1.json";

    public static bool IsBundleName(string name)
    {
        if (!name.EndsWith(".bundle", StringComparison.Ordinal)) return false;
        string stem = name[..^7];
        int dot = stem.LastIndexOf('.');
        return dot > 0 && IsHash(stem[(dot + 1)..]);
    }

    private static bool IsHash(string text) => text?.Length == 32 &&
        text.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    public static int Run(string directory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new InvalidDataException("Diretório Android não configurado. Use --assetbundles-android <pasta>.");
            var index = JObject.Parse(File.ReadAllText(Path.Combine(directory, IndexName)));
            var files = index["FileList"] as JArray ?? throw new InvalidDataException("FileList ausente.");
            var items = index["ItemList"] as JArray ?? throw new InvalidDataException("ItemList ausente.");
            if (!IsHash((string)index["PreloadCrc"]) || !IsHash((string)index["PreloadHash"]))
                throw new InvalidDataException("CRC/hash de preload inválido.");

            var entries = new Dictionary<string, JObject>(StringComparer.Ordinal);
            foreach (JToken token in files)
            {
                var entry = token as JObject ?? throw new InvalidDataException("Entrada de FileList não é um objeto.");
                string name = (string)entry["Name"];
                if (string.IsNullOrEmpty(name) || !name.EndsWith(".bundle", StringComparison.Ordinal) ||
                    name.Contains("..") || name.IndexOfAny(new[] { '/', '\\', ':', '\0' }) >= 0 ||
                    !IsHash((string)entry["Crc"]) || !IsHash((string)entry["Hash"]))
                    throw new InvalidDataException("Entrada inválida no catálogo: " + name);
                if (!entries.TryAdd(name, entry))
                    throw new InvalidDataException("Entrada duplicada no catálogo: " + name);
            }
            entries.Add("preload.bundle", new JObject
            {
                ["Name"] = "preload.bundle", ["Crc"] = index["PreloadCrc"], ["Priority"] = 9999
            });

            var missing = new JArray();
            var missingNames = new HashSet<string>(StringComparer.Ordinal);
            var invalidHeaders = new JArray();
            var undeclaredDependencies = new HashSet<string>(StringComparer.Ordinal);
            long payloadBytes = 0;
            foreach (var pair in entries)
            {
                JObject entry = pair.Value;
                string filename = pair.Key[..^7] + "." + (string)entry["Crc"] + ".bundle";
                string path = Path.Combine(directory, filename);
                if (!File.Exists(path))
                {
                    missingNames.Add(pair.Key);
                    missing.Add(filename);
                }
                else
                {
                    using var stream = File.OpenRead(path);
                    payloadBytes += stream.Length;
                    byte[] header = new byte[96];
                    int count = stream.Read(header, 0, header.Length);
                    string signature = Encoding.ASCII.GetString(header, 0, count);
                    if (!signature.StartsWith("UnityFS\0", StringComparison.Ordinal) ||
                        !signature.Contains("2017.4.34f1\0", StringComparison.Ordinal))
                        invalidHeaders.Add(filename);
                }
                foreach (string dependency in entry["Dependencies"]?.Values<string>() ?? Enumerable.Empty<string>())
                    if (!entries.ContainsKey(dependency)) undeclaredDependencies.Add(dependency);
            }

            // O cliente exige prioridade > 500 e também as dependências transitivas desses arquivos.
            var prerequisiteNames = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>(entries.Where(p => (int?)p.Value["Priority"] > 500).Select(p => p.Key));
            while (pending.TryPop(out string name))
            {
                if (!prerequisiteNames.Add(name) || !entries.TryGetValue(name, out JObject entry)) continue;
                foreach (string dependency in entry["Dependencies"]?.Values<string>() ?? Enumerable.Empty<string>())
                    pending.Push(dependency);
            }
            string[] prerequisiteMissing = prerequisiteNames.Where(missingNames.Contains).OrderBy(n => n).ToArray();
            bool complete = missing.Count == 0 && invalidHeaders.Count == 0 && undeclaredDependencies.Count == 0;
            Console.WriteLine(new JObject
            {
                ["complete"] = complete,
                ["expected"] = entries.Count,
                ["available"] = entries.Count - missing.Count,
                ["missing"] = missing.Count,
                ["items"] = items.Count,
                ["payload_bytes"] = payloadBytes,
                ["prerequisites_ready"] = prerequisiteMissing.Length == 0 && invalidHeaders.Count == 0 && undeclaredDependencies.Count == 0,
                ["prerequisite_dependencies_missing"] = new JArray(prerequisiteMissing),
                ["invalid_bundle_headers"] = invalidHeaders,
                ["undeclared_dependencies"] = new JArray(undeclaredDependencies.OrderBy(n => n)),
                ["missing_files"] = missing
            }.ToString());
            return complete ? 0 : 3;
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or
            Newtonsoft.Json.JsonException or InvalidCastException or ArgumentException or FormatException or OverflowException)
        {
            Console.WriteLine(new JObject { ["complete"] = false, ["error"] = error.Message }.ToString());
            return 2;
        }
    }
}
