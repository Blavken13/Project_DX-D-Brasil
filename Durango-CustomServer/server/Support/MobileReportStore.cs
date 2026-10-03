using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Durango.Online;

// Independent of players, saves and the simulation thread. Objects never escape mutable.
internal sealed class MobileReportStore
{
    internal const int MaxReports = 10000, MaxReportBytes = 49152;
    private const long MaxStorageBytes = 128 * 1024 * 1024;
    internal static readonly string[] Columns = {
        "event_id", "received_at", "occurred_at", "signature", "installation_id", "session_id", "platform",
        "event_type", "version_name", "version_code", "manufacturer", "model", "android_api", "android_version",
        "android_build", "abi", "page_size", "ram_mib", "compatibility", "title_video_disabled", "metadata_available",
        "graphics", "last_stage", "exit_reason", "exit_status", "rss_kib", "exception_class", "java_frames",
        "native_frames", "breadcrumbs", "visual_category" };
    private readonly object _sync = new();
    private readonly string _directory;
    private readonly Dictionary<string, JObject> _reports = new();
    private readonly Dictionary<string, long> _sizes = new();
    private long _storageBytes;
    private readonly Dictionary<string, (DateTimeOffset Start, int Count)> _rates = new();
    private static readonly HashSet<string> Types = new() { "java_crash", "native_crash", "anr", "low_memory", "signal", "startup_failure", "visual" };
    private static readonly Regex JavaFrame = new(@"^[A-Za-z0-9_.$<>]+\([A-Za-z0-9_.$ :+-]*\)$", RegexOptions.CultureInvariant);
    private static readonly Regex NativeFrame = new(@"^#\d+ pc [a-fA-F0-9]+ [A-Za-z0-9_.-]+ .* build=(?:[a-fA-F0-9]{0,128}|\[redigido\])$", RegexOptions.CultureInvariant);

    internal MobileReportStore(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
        foreach (var file in Directory.EnumerateFiles(directory, "*.json").OrderByDescending(File.GetLastWriteTimeUtc))
        {
            if (_reports.Count >= MaxReports || _storageBytes + new FileInfo(file).Length > MaxStorageBytes) { File.Delete(file); continue; }
            try {
                if (new FileInfo(file).Length > MaxReportBytes + 4096) continue;
                using var reader = new JsonTextReader(new StringReader(File.ReadAllText(file))) { DateParseHandling = DateParseHandling.None, MaxDepth = 8 };
                var item = JObject.Load(reader);
                if (Guid.TryParse((string)item["event_id"], out var id) && DateTimeOffset.TryParse((string)item["received_at"], out var received)
                    && received > DateTimeOffset.UtcNow.AddDays(-30)) {
                    string key=id.ToString(); _reports[key] = item; _sizes[key] = new FileInfo(file).Length; _storageBytes += _sizes[key];
                }
                else File.Delete(file);
            } catch (JsonException) { /* A damaged record cannot disable the collector. */ }
        }
    }

    internal static JObject Validate(JObject input)
    {
        var output = new JObject();
        foreach (string field in new[] { "event_id", "installation_id", "session_id" }) {
            if (!Guid.TryParse((string)input[field], out var id)) throw new ArgumentException("Identificador inválido: " + field);
            output[field] = id.ToString();
        }
        if ((string)input["platform"] != "Android" || !Types.Contains((string)input["event_type"] ?? ""))
            throw new ArgumentException("Somente relatórios Android são aceitos.");
        output["platform"] = "Android"; output["event_type"] = input["event_type"].DeepClone();
        if (input["occurred_at"]?.Type != JTokenType.Integer) throw new ArgumentException("Data inválida.");
        long timestamp = (long)input["occurred_at"];
        if (timestamp < 1577836800000L || timestamp > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 86400000L)
            throw new ArgumentException("Data fora do intervalo.");
        output["occurred_at"] = timestamp;
        foreach (string field in new[] { "version_name", "manufacturer", "model", "android_version", "android_build", "abi", "exception_class", "last_stage" }) {
            string value = Text(input[field], 160);
            if (field == "last_stage" && !Regex.IsMatch(value, @"^[A-Z0-9_]*$")) value = "";
            if (field == "exception_class" && !Regex.IsMatch(value, @"^[A-Za-z0-9_.$]*$")) value = "";
            output[field] = value;
        }
        foreach (string field in new[] { "version_code", "android_api", "page_size", "ram_mib", "exit_reason", "exit_status", "rss_kib" }) {
            var token = input[field];
            if (token != null && token.Type != JTokenType.Integer) throw new ArgumentException("Número inválido: " + field);
            long value = token == null ? 0 : (long)token;
            if (value < 0 || value > 100000000000L) throw new ArgumentException("Número fora do intervalo.");
            output[field] = value;
        }
        foreach (string field in new[] { "compatibility", "title_video_disabled", "metadata_available" })
            output[field] = input[field]?.Type == JTokenType.Boolean && (bool)input[field];
        string category = Text(input["visual_category"], 40);
        if (category != "" && category != "black_character" && category != "missing_texture" && category != "other_visual")
            throw new ArgumentException("Categoria visual inválida.");
        output["visual_category"] = category;
        output["graphics"] = Lines(input["graphics"], 8, 256, null);
        output["java_frames"] = Lines(input["java_frames"], 128, 256, JavaFrame);
        output["native_frames"] = Lines(input["native_frames"], 64, 512, NativeFrame);
        output["breadcrumbs"] = Lines(input["breadcrumbs"], 64, 96, new Regex(@"^[0-9 ]*[A-Z][A-Z0-9_]*$"));
        string frames = string.Join("\n", ((JArray)output["native_frames"]).Take(8).Select(x => {
            // Group by library, relative PC and BuildID; ignore absolute paths and symbolic names.
            var m = Regex.Match((string)x, @"pc ([a-fA-F0-9]+) ([A-Za-z0-9_.-]+).* build=(.*)$");
            return m.Success ? m.Groups[2].Value + ":" + m.Groups[1].Value.ToLowerInvariant() + ":" + m.Groups[3].Value : "";
        }));
        if (frames.Length == 0) frames = string.Join("\n", ((JArray)output["java_frames"]).Take(8));
        string key = output["event_type"] + "|" + output["exception_class"] + "|" + output["exit_reason"] + "|" + output["visual_category"] + "|" + frames;
        if (frames.Length == 0) key += "|" + output["last_stage"] + "|" + output["version_name"];
        output["signature"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant()[..24];
        if (Encoding.UTF8.GetByteCount(output.ToString(Formatting.None)) > MaxReportBytes) throw new ArgumentException("Relatório muito grande.");
        return output;
    }

    private static string Text(JToken token, int limit) {
        if (token == null) return "";
        if (token.Type != JTokenType.String) throw new ArgumentException("Texto inválido.");
        string value = (string)token;
        if (value.Length > limit) throw new ArgumentException("Texto excede o limite.");
        return new string(value.Where(c => !char.IsControl(c)).ToArray());
    }
    private static JArray Lines(JToken token, int count, int length, Regex pattern) {
        if (token == null) return new JArray();
        if (token is not JArray array || array.Count > count) throw new ArgumentException("Lista excede o limite.");
        var result = new JArray();
        foreach (var entry in array) { string value = Text(entry, length); if (pattern == null || pattern.IsMatch(value)) result.Add(value); }
        return result;
    }

    // 200 only after fsync + atomic rename. A retry with the same UUID is safe.
    internal int Accept(JObject item, string address)
    {
        lock (_sync) {
            string id = (string)item["event_id"];
            if (_reports.TryGetValue(id, out var existing)) {
                var comparable = (JObject)existing.DeepClone(); comparable.Remove("received_at");
                return JToken.DeepEquals(comparable, item) ? 200 : 409;
            }
            var now = DateTimeOffset.UtcNow;
            foreach (var key in _rates.Where(x => now - x.Value.Start >= TimeSpan.FromMinutes(1)).Select(x => x.Key).ToArray()) _rates.Remove(key);
            string[] keys = { "global", "ip:" + address, "install:" + item["installation_id"] };
            int[] limits = { 600, 60, 10 };
            if (_rates.Count > 4096) return 429;
            for (int i = 0; i < keys.Length; i++) if (_rates.TryGetValue(keys[i], out var rate) && rate.Count >= limits[i]) return 429;
            foreach (var key in keys) { var rate = _rates.GetValueOrDefault(key, (now, 0)); _rates[key] = (rate.Item1, rate.Item2 + 1); }
            var saved = (JObject)item.DeepClone(); saved["received_at"] = now.ToString("O");
            string destination = Path.Combine(_directory, id + ".json"), temporary = destination + ".tmp";
            byte[] bytes = Encoding.UTF8.GetBytes(saved.ToString(Formatting.None));
            try {
                using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) { file.Write(bytes); file.Flush(true); }
                File.Move(temporary, destination, true);
                _reports[id] = saved;
                _sizes[id] = bytes.Length; _storageBytes += bytes.Length;
                while (_storageBytes > MaxStorageBytes) Remove((string)_reports.Values.OrderBy(x => (string)x["received_at"]).First()["event_id"]);
                foreach (var old in _reports.Values.OrderByDescending(x => (string)x["received_at"]).Select((x, i) => (x, i))
                    .Where(x => x.i >= MaxReports || DateTimeOffset.Parse((string)x.x["received_at"], CultureInfo.InvariantCulture) < now.AddDays(-30)).Select(x => (string)x.x["event_id"]).ToArray()) {
                    Remove(old);
                }
                return 200;
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    private void Remove(string id) { File.Delete(Path.Combine(_directory,id+".json")); _storageBytes -= _sizes[id]; _sizes.Remove(id); _reports.Remove(id); }
    internal JObject[] Snapshot() {
        lock (_sync) {
            foreach(var id in _reports.Values.Where(x => DateTimeOffset.Parse((string)x["received_at"],CultureInfo.InvariantCulture)<DateTimeOffset.UtcNow.AddDays(-30)).Select(x=>(string)x["event_id"]).ToArray()) Remove(id);
            return _reports.Values.Select(x => (JObject)x.DeepClone()).OrderByDescending(x => (string)x["received_at"]).ToArray();
        }
    }
    internal static string CsvCell(JToken value) {
        string text = value is JContainer ? value.ToString(Formatting.None) : Convert.ToString((value as JValue)?.Value, CultureInfo.InvariantCulture) ?? "";
        if (text.TrimStart().StartsWith("=") || text.TrimStart().StartsWith("+") || text.TrimStart().StartsWith("-") || text.TrimStart().StartsWith("@")) text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
