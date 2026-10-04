using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Durango.Utils;
using Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared.Clan;

namespace Durango.Online;

internal sealed class ClanRoleRecord
{
    [JsonProperty("id")] public int Id;
    [JsonProperty("name")] public string Name;
    [JsonProperty("grade")] public int Grade;
    [JsonProperty("permissions")] public Permissions Permissions;
    [JsonProperty("user_type")] public UserType UserType;
}
internal sealed class ClanAllyRecord
{
    public string Proposer;
    public bool IsAlly;
    public bool Breaking;
    public double Since;
    public double Until;
    public bool Locked;
}
internal sealed class ClanResearchRecord
{
    public string Id;
    public string Lab;
    public double Since;
    public double Until;
    public double CooltimeUntil;
}
internal sealed class ClanEconomyOperation
{
    public string ClanId;
    public ClanRecord Created;
    public long FundDelta;
    public ClanResearchRecord Research;
}

internal sealed class ClanEstateOperation
{
    public string RegionId;
    public string EstateId;
    public EstateRecord Record;
    [JsonIgnore] public World RuntimeWorld;
}

internal static class ClanRules
{
    private static string _dataDir;
    private static JObject _table;
    private static JObject _constants;
    private static JObject _research;
    private static void Load()
    {
        if (_dataDir == Json.DataDir) return;
        _table = JObject.Parse(File.ReadAllText(Path.Combine(Json.DataDir, "assets", "clan.json")));
        _constants = JObject.Parse(File.ReadAllText(Path.Combine(Json.DataDir, "assets", "constants.json")));
        _research = JObject.Parse(File.ReadAllText(Path.Combine(Json.DataDir, "assets", "clan_research.json")));
        _dataDir = Json.DataDir;
    }
    internal static long CreationCost { get { Load(); return (long)_constants["clan_creation_costs"]["amount"]; } }
    internal static int Level(long xp) { Load(); return Math.Min(25, 1 + ((JArray)_table["level_thresholds"]).Count(t => xp >= (long)t)); }
    internal static int Reward(int level, string key)
    {
        Load(); return Enumerable.Range(1, Math.Clamp(level, 1, 25)).Select(n => (int?)_table["level_rewards"][n.ToString()][key] ?? 0).Max();
    }
    internal static string[] Blueprints(int level)
    {
        Load(); return Enumerable.Range(1, Math.Clamp(level, 1, 25)).Select(n => (string)_table["level_rewards"][n.ToString()]["blueprint_id"]).Where(s => !string.IsNullOrEmpty(s)).ToArray();
    }
    internal static int AllyCapacity(int level)
    { Load(); return Math.Min((int)_constants["ally"]["max_slot_count"], (int)_constants["ally"]["default_slot_count"] + ((JArray)_constants["ally"]["slot_opens_at"]).Count(t => level >= (int)t)); }
    internal static long TerritoryCost(int count)
    {
        Load();
        if (!StatFormula.TryEval((string)_constants["clan_territory_costs"]["amount"], new Dictionary<string, double> { ["territory_count"] = count }, out double cost) || cost < 0 || cost > EconomyStore.BalanceLimit)
            throw new InvalidOperationException("Custo de território inválido.");
        return (long)cost;
    }
    internal static JObject Research(string id) { Load(); return _research[id ?? ""] as JObject; }
    internal static string[] ResearchIds { get { Load(); return _research.Properties().Select(p => p.Name).ToArray(); } }
    internal static int ResearchLevel(int category) => category switch { 0 => 11, 1 => 15, 2 => 12, 3 => 13, _ => 16 };
    internal static string LabBlueprint(int category) => category switch
    { 0 => "clan_collect_lab", 1 => "clan_craft_lab", 2 => "clan_adventure_lab", 3 => "clan_battle_lab", _ => "clan_advanced_lab" };
    internal static int GrowthBuff(int level)
    {
        Load(); return Enumerable.Range(1, Math.Clamp(level, 1, 25)).Select(n => (int?)(_table["level_rewards"][n.ToString()]["status_effect"] as JObject)?["clan_growth_buff"] ?? 0).Max();
    }
    internal static Dictionary<int, ClanRoleRecord> DefaultRoles() => new()
    {
        [0] = new ClanRoleRecord { Id = 0, Name = "Líder", Grade = 0, Permissions = (Permissions)31, UserType = UserType.Root },
        [1] = new ClanRoleRecord { Id = 1, Name = "Oficial", Grade = 1, Permissions = Permissions.ApproveMember | Permissions.EditClanInfo | Permissions.Research | Permissions.OccupyWarphole, UserType = UserType.Normal },
        [2] = new ClanRoleRecord { Id = 2, Name = "Membro", Grade = 2, UserType = UserType.Normal }
    };
}
