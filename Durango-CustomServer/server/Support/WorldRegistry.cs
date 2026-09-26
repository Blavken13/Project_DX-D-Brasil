using System;
using System.Collections.Generic;
using System.IO;
using Durango.Utils;

namespace Durango.Online;

/// <summary>Tipo de uma instância persistente de assentamento.</summary>
public enum SettlementRegionKind
{
    SharedTamed,
    PrivatePlayer,
    Clan
}

/// <summary>
/// Mapeia um RegionId lógico para o terrain/template que ele reutiliza.
/// Cada instância possui seu próprio arquivo .world mesmo quando compartilha o mesmo terrain.
/// </summary>
public sealed class SettlementRegionInstance
{
    public string RegionId;
    public string TemplateId;
    public SettlementRegionKind Kind;
    public string OwnerId;
}

/// <summary>
/// โลกของแต่ละเกาะ — เกาะ 1 ลูก = World 1 ตัว = ไฟล์เซฟ 1 ไฟล์
///
/// ทำไมต้องมี: เซิร์ฟในตัวของเกมมีโลกเดียวเสมอ (Server.BeginServer สร้าง GameServer ตัวเดียว
/// จาก WorldContext ตัวเดียว) แต่ระบบล่องเรือทั้งระบบตั้งอยู่บนสมมติฐานว่ามีหลายเกาะ
/// แล้วเดินทางไปมาได้ ⇒ ต้องถือหลายโลกพร้อมกัน
///
/// โหลดแบบ lazy: สร้างโลกของเกาะเมื่อมีคนไปถึงจริงเท่านั้น ไม่ได้เปิดทั้ง 14 เกาะค้างไว้
/// (แต่ละโลกกิน chunk data ของ terrain เต็มแผ่น)
///
/// ไฟล์เซฟ: <c>offline/&lt;cluster&gt;/regions/&lt;regionId&gt;.world</c>
/// แยกจากไฟล์ <c>0.world</c> ของต้นฉบับที่เป็นสล็อตของโลกเดี่ยว — ของเดิมยังอ่านได้เหมือนเดิม
/// </summary>
public class WorldRegistry
{
    private readonly string _clusterKey;
    private readonly Dictionary<string, World> _worlds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>เกาะตั้งต้นสำหรับผู้เล่นที่ยังไม่เคยไปไหน</summary>
    public string DefaultRegionId { get; }

    public WorldRegistry(string clusterKey, World defaultWorld, string defaultRegionId)
    {
        _clusterKey = clusterKey;
        DefaultRegionId = string.IsNullOrEmpty(defaultRegionId) ? TerrainLoader.DefaultTerrainFile : defaultRegionId;
        // โลกตั้งต้นมาจาก Host (ไฟล์ 0.world ของต้นฉบับ) — ใช้ต่อเลย ไม่สร้างซ้ำ
        if (defaultWorld != null)
        {
            defaultWorld.Registry = this;
            _worlds[DefaultRegionId] = defaultWorld;
        }
    }

    public IEnumerable<KeyValuePair<string, World>> Loaded => _worlds;

    /// <summary>
    /// Instâncias de assentamento que reutilizam um terrain real, mas possuem mundo/save próprios.
    /// Ex.: tamed_01 (pública), personal_xxx (particular) e clan_xxx (clã).
    /// </summary>
    private readonly Dictionary<string, SettlementRegionInstance> _settlementRegions =
        new(StringComparer.OrdinalIgnoreCase);

    public const string DefaultSharedTamedRegionId = "tamed_01";

    private void RegisterSettlementRegion(
        string regionId,
        string templateId,
        SettlementRegionKind kind,
        string ownerId)
    {
        if (string.IsNullOrEmpty(regionId) || string.IsNullOrEmpty(templateId)) return;

        if (_settlementRegions.TryGetValue(regionId, out SettlementRegionInstance existing))
        {
            existing.TemplateId = templateId;
            existing.Kind = kind;
            if (!string.IsNullOrEmpty(ownerId))
            {
                existing.OwnerId = ownerId;
            }
            return;
        }

        _settlementRegions[regionId] = new SettlementRegionInstance
        {
            RegionId = regionId,
            TemplateId = templateId,
            Kind = kind,
            OwnerId = ownerId
        };
    }

    public bool EnsureDefaultSharedTamedRegion()
    {
        if (_settlementRegions.ContainsKey(DefaultSharedTamedRegionId))
        {
            return true;
        }

        string templateId = RegionCatalog.DefaultSettlementTemplateId;
        if (string.IsNullOrEmpty(templateId))
        {
            return false;
        }

        RegisterSettlementRegion(
            DefaultSharedTamedRegionId,
            templateId,
            SettlementRegionKind.SharedTamed,
            ownerId: null);
        return true;
    }

    public void RegisterPersonalRegion(string regionId, string templateId) =>
        RegisterPersonalRegion(regionId, templateId, ownerId: null);

    public void RegisterPersonalRegion(string regionId, string templateId, string ownerId) =>
        RegisterSettlementRegion(regionId, templateId, SettlementRegionKind.PrivatePlayer, ownerId);

    public string RegisterClanRegion(string clanId, string templateId = null)
    {
        if (string.IsNullOrWhiteSpace(clanId)) return null;

        templateId ??= RegionCatalog.DefaultSettlementTemplateId;
        if (string.IsNullOrEmpty(templateId)) return null;

        string regionId = ClanRegionIdFor(clanId);
        RegisterSettlementRegion(regionId, templateId, SettlementRegionKind.Clan, clanId);
        return regionId;
    }

    public static string ClanRegionIdFor(string clanId) =>
        string.IsNullOrWhiteSpace(clanId) ? null : "clan_" + clanId;

    public bool TryGetSettlementRegion(string regionId, out SettlementRegionInstance instance) =>
        _settlementRegions.TryGetValue(regionId ?? "", out instance);

    public bool TryGetPersonalTemplate(string regionId, out string templateId)
    {
        templateId = null;
        if (!TryGetSettlementRegion(regionId, out SettlementRegionInstance instance) ||
            instance.Kind != SettlementRegionKind.PrivatePlayer)
        {
            return false;
        }

        templateId = instance.TemplateId;
        return true;
    }

    public bool IsSettlementRegion(string regionId) =>
        !string.IsNullOrEmpty(regionId) && _settlementRegions.ContainsKey(regionId);

    public bool IsPersonalRegion(string regionId) =>
        TryGetSettlementRegion(regionId, out SettlementRegionInstance instance) &&
        instance.Kind == SettlementRegionKind.PrivatePlayer;

    public bool IsSharedTamedRegion(string regionId) =>
        TryGetSettlementRegion(regionId, out SettlementRegionInstance instance) &&
        instance.Kind == SettlementRegionKind.SharedTamed;

    public bool IsClanRegion(string regionId) =>
        TryGetSettlementRegion(regionId, out SettlementRegionInstance instance) &&
        instance.Kind == SettlementRegionKind.Clan;

    public IEnumerable<string> PersonalRegionIds
    {
        get
        {
            foreach (SettlementRegionInstance instance in _settlementRegions.Values)
            {
                if (instance.Kind == SettlementRegionKind.PrivatePlayer)
                {
                    yield return instance.RegionId;
                }
            }
        }
    }

    public World GetOrCreate(string regionId)
    {
        string terrainFile = null;
        if (string.IsNullOrEmpty(regionId))
        {
            regionId = DefaultRegionId;
        }
        else if (RegionCatalog.TryGet(regionId, out _))
        {
            terrainFile = regionId; // catalog ใช้ชื่อไฟล์ terrain เป็น region id
        }
        else
        {
            if (string.Equals(regionId, DefaultSharedTamedRegionId, StringComparison.OrdinalIgnoreCase))
            {
                EnsureDefaultSharedTamedRegion();
            }

            if (_settlementRegions.TryGetValue(regionId, out SettlementRegionInstance settlement))
            {
                terrainFile = settlement.TemplateId;
            }
            else
            {
                throw new InvalidOperationException($"Unknown region '{regionId}'.");
            }
        }

        if (_worlds.TryGetValue(regionId, out World existing))
        {
            return existing;
        }

        string path = MakeRegionPath(_clusterKey, regionId);
        // WorldContext.Save เขียนไฟล์ตรง ๆ ไม่สร้างโฟลเดอร์ให้ (ต้นฉบับเขียนลง offline/<cluster>/
        // ที่มีอยู่แล้วเสมอ) — โฟลเดอร์ regions/ เป็นของใหม่ จึงต้องสร้างเองก่อน
        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        WorldContext context = WorldContext.Load(path);
        if (context == null)
        {
            context = new WorldContext();
            context.Initialize(path);
            Console.WriteLine($"[world] สร้างโลกใหม่ของเกาะ {regionId}" +
                              (terrainFile != null && terrainFile != regionId ? $" (template {terrainFile})" : ""));
        }
        // โลกใช้ไฟล์ terrain จริง (pe10gr_*) แต่จำ region id ของตัวเองแยกได้ผ่าน registry key
        context.TerrainId = terrainFile ?? regionId;

        var world = new World(context);
        world.Registry = this;
        _worlds[regionId] = world;
        return world;
    }

    public void ProcessAll()
    {
        // Process() pode materializar uma nova Personal Region e modificar _worlds.
        // Iteramos sobre um snapshot para não invalidar o enumerador do Dictionary.
        foreach (World world in new List<World>(_worlds.Values))
        {
            world.Process();
        }
    }

    public void SaveAll()
    {
        foreach (World world in new List<World>(_worlds.Values))
        {
            world.Save();
        }
    }

    public void StopAll()
    {
        foreach (World world in new List<World>(_worlds.Values))
        {
            world.Stop();
        }
    }

    private static string MakeRegionPath(string clusterKey, string regionId) =>
        Path.Combine(AppData.CombinePath(WorldContext.GetBasePath(clusterKey)), "regions", regionId + ".world");
}
