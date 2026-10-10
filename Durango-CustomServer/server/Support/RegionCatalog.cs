using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Messages;
using Newtonsoft.Json.Linq;
using Shared.Region;
using Shared.Survival;

namespace Durango.Online;

/// <summary>
/// สารบัญเกาะของเซิร์ฟ — สร้างจาก terrain zip ที่วางอยู่จริง ผูกกับ region template ของเกม
///
/// ทำไมต้องมี: ระบบล่องเรือถามเซิร์ฟว่า "จากท่าเรือนี้ไปไหนได้บ้าง" (GetRoutes) แล้วขอ
/// รายละเอียดปลายทางต่อ (GetRegion / GetArchipelago) ⇒ เซิร์ฟต้องมีสารบัญก่อน
/// เซิร์ฟในตัวของเกมไม่มีเพราะมันมีโลกเดียวเสมอ
///
/// เกาะ 1 ลูก = terrain zip 1 ไฟล์ · RegionId ใช้ชื่อไฟล์ (เช่น "ri35te") ให้ผูกกับไฟล์เซฟตรง ๆ
///
/// **template สำคัญกว่าที่คิด** — ตัวเกมอ่าน level/role/biome จาก region_templates.json ของตัวเอง
/// แล้วใช้จัดหน้า UI ทั้งหมด (client/Durango.UI/WorldRoutesUnstableArea.cs:236 จับคู่โซนด้วย
/// Role + Level + MajorBiome ของ template) ⇒ Routes ที่เราส่งต้องใช้ค่าเดียวกันเป๊ะ
/// ไม่งั้นเกาะจะไม่ไปโผล่ในโซนไหนเลย
/// </summary>
public static class RegionCatalog
{
    /// <summary>ข้อมูล template ที่ UI ใช้จัดโซน — อ่านจาก data/assets/region_templates.json</summary>
    public class TemplateInfo
    {
        public string Id;
        public int Level;
        public bool Active;
        public int AvailableLevel;
        public Role Role = Role.Rural;
        public HashSet<string> Tags = new(StringComparer.Ordinal);
        public bool AllowsPvp => Role == Role.Outpost || Role == Role.Instance && Tags.Contains("pvpisland");
        public Biome Biome = Biome.Invalid;
        public double ExpiresIn;
        public Dictionary<ushort, int> CollectibleLevels = new();

        /// <summary>ฝูงสัตว์ที่เกิดบนเกาะแบบนี้ · ชื่อกลุ่ม (land/beach/…) → รายการฝูง</summary>
        public Dictionary<string, List<HerdSpawn>> Herds = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// ไบโอมบนเกาะแบบนี้ให้ความเหนื่อยหมวดไหน — จาก <c>region_templates.json → biome_effects</c>
        /// (ครบทั้ง 267 แม่แบบ) เช่น <c>volcanic → [volcanic_heat]</c> · <c>swamp_mud → [humid]</c>
        /// คู่ไบโอม↔หมวดตรงกับ <c>constants.json → resistance.types_by_biome</c> ที่ NEXON
        /// แม็ปเป็น <c>Dictionary&lt;Biome, Derived&gt;</c> เองอยู่แล้ว (client/Yaml/Resistance.cs)
        /// </summary>
        public Dictionary<Biome, FatigueCategory[]> BiomeEffects = new();

        /// <summary>
        /// ชื่อสถานการณ์สภาพอากาศของเกาะแบบนี้ — ค่าจริงจาก <c>region_templates.json → weather</c>
        /// เช่น <c>ending_climate_snowy</c> · <c>volcanic_normal</c> · <c>always_volcanic_ash</c>
        /// (null = ไม่ระบุ) แปลงเป็นลำดับสภาพอากาศจริงที่ <see cref="WeatherTuning"/>
        /// </summary>
        public string Weather;
    }

    /// <summary>
    /// Uma entrada de herd de region_templates.json.
    ///
    /// O quociente packed / 100 continua sendo usado como EntityType.
    /// O sufixo packed % 100 é preservado como metadata (PackedSuffix), mas NÃO é
    /// tratado como nível de combate da fauna selvagem.
    ///
    /// CombatLevelExplicit existe apenas para spawns criados por código/cheat,
    /// onde o chamador fornece deliberadamente um nível.
    /// </summary>
    public readonly struct HerdSpawn
    {
        public readonly ushort EntityType;
        public readonly int PackedSuffix;
        public readonly int CombatLevelExplicit;

        public HerdSpawn(ushort entityType, int combatLevel)
        {
            EntityType = entityType;
            PackedSuffix = 0;
            CombatLevelExplicit = combatLevel;
        }

        private HerdSpawn(ushort entityType, int packedSuffix, bool fromPacked)
        {
            EntityType = entityType;
            PackedSuffix = packedSuffix;
            CombatLevelExplicit = 0;
        }

        public static HerdSpawn FromPacked(int packed) =>
            new((ushort)(packed / 100), packed % 100, fromPacked: true);
    }

    private static readonly List<Region> _regions = new();
    private static readonly Dictionary<string, Region> _byId = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, TemplateInfo> _templates = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<Region> All => _regions;

    public static void Load(string assetsDir)
    {
        LoadTemplates(assetsDir);
        LoadRegions();
    }

    /// <summary>
    /// อ่าน region_templates.json — เอาเฉพาะ level / role / biome ที่ UI ใช้จัดโซน
    /// biome มาจากคีย์แรกของ <c>biome_effects</c> (เช่น "temperate_forest" → Biome.TemperateForest)
    /// ซึ่งเป็นทางเดียวกับที่ตัวเกมหา MajorBiome (client/Yaml/RegionTemplate.cs:105-120)
    /// </summary>
    private static void LoadTemplates(string assetsDir)
    {
        _templates.Clear();
        string path = Path.Combine(assetsDir ?? "", "region_templates.json");
        if (!File.Exists(path))
        {
            Console.WriteLine($"[region] ⚠️ ไม่พบ {path} — เกาะจะไม่มีข้อมูล level/biome");
            return;
        }
        try
        {
            var root = JObject.Parse(File.ReadAllText(path));
            foreach (KeyValuePair<string, JToken> kv in root)
            {
                var info = new TemplateInfo { Id = kv.Key };
                if (kv.Value is JObject o)
                {
                    info.Level = (int?)o["level"] ?? 0;
                    info.Active = (bool?)o["active"] ?? false;
                    info.AvailableLevel = info.Level;
                    info.ExpiresIn = (double?)o["expires_in"] ?? 0;
                    if (o["collectible_levels"] is JObject levels)
                        foreach (var level in levels.Properties())
                            if (ushort.TryParse(level.Name, out var type) && (int?)level.Value > 0)
                                info.CollectibleLevels[type] = (int)level.Value;
                    info.Weather = (string)o["weather"];
                    if (o["tags"] is JArray tags)
                        info.Tags.UnionWith(tags.Values<string>().Where(t => t != null));
                    if ((int?)o["role"] is { } roleValue && Enum.IsDefined(typeof(Role), roleValue))
                    {
                        info.Role = (Role)roleValue;
                    }
                    if (o["biome_effects"] is JObject effects)
                    {
                        info.Biome = effects.Properties().Select(p => ParseBiome(p.Name))
                            .FirstOrDefault(b => b is >= Biome.TemperateForest and <= Biome.Volcanic, Biome.Invalid);
                        foreach (JProperty be in effects.Properties())
                        {
                            Biome biome = ParseBiome(be.Name);
                            if (biome == Biome.Invalid || be.Value is not JArray cats) continue;
                            FatigueCategory[] list = cats
                                .Select(x => FatigueTuning.ParseCategory(x?.ToString()))
                                .Where(c => c != FatigueCategory.Invalid)
                                .ToArray();
                            if (list.Length > 0) info.BiomeEffects[biome] = list;
                        }
                    }
                    if (o["herds"] is JObject herds)
                    {
                        foreach (JProperty group in herds.Properties())
                        {
                            if (group.Value["spawns"] is not JArray spawns || spawns.Count == 0) continue;
                            var list = new List<HerdSpawn>(spawns.Count);
                            foreach (JToken packed in spawns)
                            {
                                if ((int?)packed is { } value) list.Add(HerdSpawn.FromPacked(value));
                            }
                            if (list.Count > 0) info.Herds[group.Name] = list;
                        }
                    }
                }
                _templates[kv.Key] = info;
            }
            // Match RegionTemplateDict.OnInitalized on the client: an active
            // level band opens at the preceding active band's level + 1.
            int previous = 0, current = 0;
            foreach (var info in _templates.Values.Where(t => t.Active).OrderBy(t => t.Level))
            {
                if (current < info.Level) { previous = current; current = info.Level; }
                info.AvailableLevel = previous + 1;
            }
            foreach (var info in _templates.Values.Where(t => !t.Active && t.Role == Role.Risky))
            {
                var area = _templates.Values.FirstOrDefault(t => t.Active && t.Role == info.Role &&
                    t.Level == info.Level && t.Biome == info.Biome);
                if (area != null) info.AvailableLevel = area.AvailableLevel;
            }
            Console.WriteLine($"[region] อ่าน region template {_templates.Count} รายการ");
        }
        catch (Exception e)
        {
            Console.WriteLine($"[region] อ่าน region_templates.json ไม่สำเร็จ: {e.Message}");
        }
    }

    /// <summary>"temperate_forest" → Biome.TemperateForest (ชื่อใน json เป็น snake_case)</summary>
    private static Biome ParseBiome(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return Biome.Invalid;
        }
        string pascal = string.Concat(name.Split('_').Select(p =>
            p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p.Substring(1)));
        return Enum.TryParse(pascal, ignoreCase: true, out Biome biome) ? biome : Biome.Invalid;
    }

    private static void LoadRegions()
    {
        _regions.Clear();
        _byId.Clear();

        string dir = TerrainLoader.TerrainDir;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            Console.WriteLine($"[region] ⚠️ ไม่พบโฟลเดอร์ terrain: {dir}");
            return;
        }

        foreach (string path in Directory.GetFiles(dir, "*.zip").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            string id = Path.GetFileNameWithoutExtension(path);
            TerrainData data;
            try
            {
                data = TerrainLoader.Load(id);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[region] ข้าม {id}: {e.Message}");
                continue;
            }

            string templateId = data?.Info?.region_template ?? id;
            TemplateInfo template = GetTemplate(templateId);
            if (template == null)
            {
                // template ที่เกมไม่รู้จัก ⇒ client ข้ามทิ้งทั้งกลุ่ม (client/ExploreSystem.cs:307)
                Console.WriteLine($"[region] ⚠️ ข้าม {id}: เกมไม่รู้จัก template '{templateId}'");
                continue;
            }

            var region = new Region
            {
                Id = id,
                TerrainId = id,
                TemplateId = templateId,
                Role = template.Role,
                Name = DisplayName(template),
                CreatedAt = Durango.Utils.Times.UnixTimeNow()
            };
            _byId[id] = region;

            // Tutorial, Safehouse e templates de Personal Region precisam ser reconhecidos
            // por TryGet/GetOrCreate, mas não devem aparecer na lista pública de navegação.
            if (template.Role != Role.Tutorial &&
                template.Role != Role.Safehouse &&
                template.Role != Role.Personal)
            {
                _regions.Add(region);
            }
        }

        Console.WriteLine($"[region] สารบัญเกาะ {_regions.Count} ลูก: " +
                          string.Join(", ", _regions.Select(r => $"{r.Id}(lv{GetTemplate(r.TemplateId)?.Level})")));
    }

    public static bool TryGet(string regionId, out Region region)
    {
        if (!_byId.TryGetValue(regionId ?? "", out region)) return false;
        // As ilhas instaladas são persistentes. Não anunciar um prazo vencido desde 1970.
        region.CreatedAt = Durango.Utils.Times.UnixTimeNow();
        return true;
    }

    public static string DisplayName(TemplateInfo template)
    {
        if (template?.Role == Role.Safehouse) return "Refúgio Tropical";
        if (template?.Role == Role.Tutorial) return "Ilha de Ancora";
        if (template?.Role == Role.Personal) return "Ilha Domada";
        string biome = template?.Biome switch
        {
            Biome.TemperateForest => "Floresta Temperada", Biome.TropicalForest => "Floresta Tropical",
            Biome.Desert => "Deserto", Biome.Tundra => "Tundra", Biome.SnowField => "Campos Nevados",
            Biome.Grassland => "Savana", Biome.SwampMud => "Pântano", Biome.Volcanic => "Ilha Vulcânica",
            _ => "Ilha"
        };
        return template?.Level > 0 ? $"{biome} — Nível {template.Level}" : biome;
    }

    public static TemplateInfo GetTemplate(string templateId) =>
        templateId != null && _templates.TryGetValue(templateId, out TemplateInfo info) ? info : null;

    /// <summary>
    /// Primeiro template de assentamento que possui terrain real instalado.
    /// As instâncias públicas, particulares e de clã reutilizam esse terrain como base,
    /// mas persistem em arquivos .world separados.
    /// </summary>
    public static string DefaultSettlementTemplateId =>
        _byId.Values
            .Where(region => GetTemplate(region.TemplateId)?.Role == Role.Personal)
            .Select(region => region.TemplateId)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    public static string DefaultPersonalTemplateId => DefaultSettlementTemplateId;

    /// <summary>เกาะอื่นทั้งหมดที่ไม่ใช่เกาะที่ยืนอยู่ตอนนี้ — ปลายทางของเส้นทางเดินเรือ</summary>
    public static IEnumerable<Region> Others(string currentRegionId) =>
        _regions.Where(r => !string.Equals(r.Id, currentRegionId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// id ของหมู่เกาะที่รวมเกาะระดับ+ไบโอมเดียวกันไว้ — ตั้งเองเพราะเซิร์ฟจริงของ NEXON
    /// เป็นคนสร้างหมู่เกาะแบบไดนามิก (มีอายุ หมดแล้วสร้างใหม่) ซึ่งเรายังไม่ได้ทำ
    /// </summary>
    public static string ArchipelagoIdOf(TemplateInfo template) =>
        template == null ? null : $"arch_{(int)template.Role}_{template.Level}_{(int)template.Biome}";
}
