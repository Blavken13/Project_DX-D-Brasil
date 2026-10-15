using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Durango.Online;

public sealed class SeasonPassProgress
{
    [JsonProperty("season")] public string SeasonId = "";
    [JsonProperty("points")] public long Points;
    [JsonProperty("claimed")] public HashSet<int> Claimed = new();
    [JsonProperty("daily_reset")] public long DailyReset;
    [JsonProperty("weekly_reset")] public long WeeklyReset;
    [JsonProperty("daily")] public Dictionary<string, int> Daily = new();
    [JsonProperty("weekly")] public Dictionary<string, int> Weekly = new();
}

internal static class SeasonPassCatalog
{
    public static JObject Current { get; private set; }
    public static string Id => (string)Current?["season"]?["id"] ?? "";
    public static long Since => (long?)Current?["season"]?["since"] ?? 0;
    public static long Until => (long?)Current?["season"]?["until"] ?? 0;
    public static int MaxTier => (int?)Current?["maxTier"] ?? 0;
    public static int PointsPerTier => (int?)Current?["pointsPerTier"] ?? 2000;
    public static bool Active(long now) => Current != null && now >= Since && now < Until;

    public static void Load(string path)
    {
        var catalog = JObject.Parse(System.IO.File.ReadAllText(path));
        if ((catalog["tiers"] as JArray)?.Count != (int?)catalog["maxTier"] ||
            (int?)catalog["pointsPerTier"] <= 0)
            throw new InvalidOperationException("Catálogo do Season Pass inválido: " + path);
        foreach (JToken row in (JArray)catalog["tiers"])
        foreach (string track in new[] { "box", "special" })
        foreach (JToken spec in (JArray)row[track]["items"])
        {
            string id = (string)spec["id"];
            int level = (int?)spec["level"] ?? 60;
            if (string.IsNullOrWhiteSpace(id) || (int?)spec["count"] <= 0)
                throw new InvalidOperationException("Prêmio inválido no Season Pass: " + id);
            try
            {
                ShopCatalog.MakeItem(new JObject { ["prototype_id"] = id, ["level"] = level });
            }
            catch (InvalidOperationException)
            {
                throw new InvalidOperationException("Prêmio inválido no Season Pass: " + id);
            }
        }
        Current = catalog;
    }

    public static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public static long NextDaily(long now) => ((now / 86400) + 1) * 86400;
    public static long NextWeekly(long now)
    {
        var date = DateTimeOffset.FromUnixTimeSeconds(now).UtcDateTime.Date;
        int days = ((int)DayOfWeek.Monday - (int)date.DayOfWeek + 7) % 7;
        if (days == 0) days = 7;
        return new DateTimeOffset(date.AddDays(days), TimeSpan.Zero).ToUnixTimeSeconds();
    }

    public static bool Normalize(SeasonPassProgress state, long now)
    {
        bool changed = false;
        state.Claimed ??= new HashSet<int>();
        state.Daily ??= new Dictionary<string, int>();
        state.Weekly ??= new Dictionary<string, int>();
        if (state.SeasonId != Id)
        {
            state.SeasonId = Id;
            state.Points = 0;
            state.Claimed.Clear();
            state.Daily.Clear();
            state.Weekly.Clear();
            state.DailyReset = 0;
            state.WeeklyReset = 0;
            changed = true;
        }
        if (state.DailyReset <= now)
        {
            state.Daily.Clear();
            state.DailyReset = NextDaily(now);
            changed = true;
        }
        if (state.WeeklyReset <= now)
        {
            state.Weekly.Clear();
            state.WeeklyReset = NextWeekly(now);
            changed = true;
        }
        return changed;
    }

    public static bool ApplyEvent(SeasonPassProgress state, string eventName, int amount, long now)
    {
        if (!Active(now) || amount <= 0) return false;
        bool changed = Normalize(state, now);
        foreach (string period in new[] { "daily", "weekly" })
        foreach (JToken mission in (JArray)Current[period]["missions"])
        {
            if ((string)mission["event"] != eventName) continue;
            string id = (string)mission["id"];
            int goal = (int)mission["count"];
            var map = period == "daily" ? state.Daily : state.Weekly;
            map.TryGetValue(id, out int before);
            int after = Math.Min(goal, before + amount);
            if (after == before) continue;
            map[id] = after;
            if (after == goal) state.Points += (int)mission["points"];
            changed = true;
        }
        return changed;
    }

    public static JObject MissionStatus(SeasonPassProgress state, string period)
    {
        var array = new JArray();
        int max = 0;
        var map = period == "daily" ? state.Daily : state.Weekly;
        foreach (JToken definition in (JArray)Current[period]["missions"])
        {
            string id = (string)definition["id"];
            string eventName = (string)definition["event"];
            int count = (int)definition["count"];
            int points = (int)definition["points"];
            max += points;
            map.TryGetValue(id, out int progress);
            array.Add(new JObject {
                ["id"] = id, ["name"] = MissionName(eventName, count),
                ["event"] = eventName, ["count"] = count, ["points"] = points,
                ["progress"] = progress, ["done"] = progress >= count
            });
        }
        return new JObject {
            ["resetAt"] = period == "daily" ? state.DailyReset : state.WeeklyReset,
            ["maxPoints"] = max, ["missions"] = array
        };
    }

    private static string MissionName(string eventName, int count) => eventName switch
    {
        "login" => "Entrar no jogo hoje",
        "hunt" => $"Caçar {count} animais",
        "collect" => $"Coletar {count} recursos",
        "craft" => $"Criar {count} itens",
        "harvest" => $"Colher {count} plantações",
        "tame" => $"Domar {count} animal",
        _ => eventName
    };
}
