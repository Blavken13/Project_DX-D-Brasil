using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Durango.Online;
using Durango.Utils;
using Messages;
using Yaml;
using Yaml.Util;

namespace DurangoServerNx;

internal static class MobileAnchorArmorCheck
{
    private static object Call(Player player, string method, params object[] args) => typeof(Player)
        .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(player, args);

    internal static void Tutorial(EconomyProtocolCheck.Link link, Action<bool, string> check)
    {
        var context = (PlayerContext)typeof(Player).GetField("_context", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(link.Player);
        // A legacy explored port must not re-enable the tutorial anchor.
        context.ExploredPOIs ??= new Dictionary<string, ExploredPoint>();
        context.ExploredPOIs["legacy-tutorial-port"] = new ExploredPoint
        {
            RegionId = context.RegionId, X = 1, Y = 1,
            Type = (int)Shared.System.PointOfInterest.Port
        };
        var pois = link.Request<GetExploredPOIs, ExploredPOIs>(default);
        check(pois.POIs.All(p => p.Type != Shared.System.PointOfInterest.Port), "Ancora nao publica porto mesmo com descoberta legada");
        link.Request<WarpToPort, Abort>(default);
        check(!link.Messages.OfType<Teleported>().Any(), "Ancora recusa teleporte para jangada pelo TCP");
    }

    internal static void Run(string root, World world, PlayerContext context, EconomyProtocolCheck.Link link, Action<bool, string> check)
    {
        var pois = link.Request<GetExploredPOIs, ExploredPOIs>(default);
        var ports = TerrainLoader.Load(world.TerrainId).Pois.PortPoints;
        check(ports.Count > 0 && ports.All(tile => pois.POIs.Any(p =>
            p.Type == Shared.System.PointOfInterest.Port && p.IsExplored && p.Tile == tile)),
            "mobile recebe portos reais antes de visita-los");
        check(pois.POIs.All(p => p.Type == Shared.System.PointOfInterest.Port), "ancora nao revela crateras nem fendas nao exploradas");
        var first = ports.First();
        context.ExploredPOIs ??= new Dictionary<string, ExploredPoint>();
        context.ExploredPOIs["already-known-port"] = new ExploredPoint
        {
            RegionId = context.RegionId, X = first.x, Y = first.y,
            Type = (int)Shared.System.PointOfInterest.Port
        };
        pois = link.Request<GetExploredPOIs, ExploredPOIs>(default);
        check(pois.POIs.Count(p => p.Type == Shared.System.PointOfInterest.Port && p.Tile == first) == 1,
            "porto ja explorado nao duplica no mapa");

        int armorCount = 0, avatarCount = 0;
        var catalog = new List<string> { "prototype,category,family,min_level,max_level,durability_min_level,durability_max_level,wears_on_defense" };
        string armorId = null, avatarId = null;
        foreach (var entry in SingletonDict<string, List<Prototype>>.Instance)
        foreach (var prototype in entry.Value)
        {
            int level = Math.Clamp(prototype.MinLevel, 1, 70);
            if (ItemDurability.IsArmor(prototype) || prototype.Tags?.ContainsKey("equipment_avatar") == true)
                catalog.Add(string.Join(",", entry.Key, prototype.Category, string.Join(";", prototype.SubCategories ?? Array.Empty<string>()),
                    prototype.MinLevel, prototype.MaxLevel,
                    ItemDurability.Maximum(entry.Key, level).ToString("0.####", CultureInfo.InvariantCulture),
                    ItemDurability.Maximum(entry.Key, prototype.MaxLevel).ToString("0.####", CultureInfo.InvariantCulture),
                    ItemDurability.IsArmor(prototype) ? "true" : "false"));
            if (ItemDurability.IsArmor(prototype))
            {
                check(ItemDurability.Maximum(entry.Key, level) > 1, entry.Key + " tem capacidade funcional no nivel " + level);
                check(ItemDurability.Maximum(entry.Key, prototype.MaxLevel) >= ItemDurability.Maximum(entry.Key, level),
                    entry.Key + " capacidade acompanha nivel");
                armorId ??= entry.Key;
                armorCount++;
            }
            else if (prototype.Tags?.ContainsKey("equipment_avatar") == true)
            {
                check(ItemDurability.Maximum(entry.Key, level) == 1, entry.Key + " traje cosmetico preservado");
                avatarId ??= entry.Key;
                avatarCount++;
            }
        }
        check(armorCount > 100 && avatarCount > 80, "catalogo de roupas e acessorios avaliado por variante");
        File.WriteAllLines(Path.Combine(root, "armor-durability.csv"), catalog);
        Console.WriteLine($"[armor-catalog] {armorCount} variantes funcionais; {avatarCount} cosmeticas; {Path.Combine(root, "armor-durability.csv")}");
        var armor = Cheats.MakeItem(armorId, 60).Value;
        armor.Durability = new Gauge(1, 0, new[] { new GaugeNode(0, .5f) });
        check(ItemDurability.Normalize(ref armor) && Math.Abs(armor.Durability.Get() / armor.Durability.Max() - .5f) < .001,
            "roupa antiga conserva metade da durabilidade ao migrar");
        check(!ItemDurability.Normalize(ref armor), "migracao da roupa e idempotente");
        var unequipped = Cheats.MakeItem(armorId, 60).Value;
        var avatar = Cheats.MakeItem(avatarId, 60).Value;
        context.InventoryItems.AddRange(new[] { armor, unequipped, avatar });
        var savedEquipment = context.EquippedItems;
        context.EquippedItems = new Dictionary<string, string>
        { ["body"] = armor.Id, ["head"] = armor.Id, ["avatar"] = avatar.Id };
        float before = armor.Durability.Get();
        Call(link.Player, "WearEquippedArmor");
        var worn = context.InventoryItems.Single(i => i.Id == armor.Id);
        check(Math.Abs(worn.Durability.Get() - (before - ItemDurability.Delta("defense"))) < .001,
            "defesa desconta delta nativa uma vez por peca equipada");
        check(context.InventoryItems.Single(i => i.Id == unequipped.Id).Durability.Get() == unequipped.Durability.Get() &&
              context.InventoryItems.Single(i => i.Id == avatar.Id).Durability.Get() == avatar.Durability.Get(),
            "roupas na mochila e cosmeticos nao sofrem desgaste de defesa");
        context.Save(); SafeSave.FlushPending();
        var loaded = Json.Read<PlayerContext>(File.ReadAllText(context.Path));
        loaded.Initialize(Path.Combine(root, "armor-reloaded.player"));
        check(Math.Abs(loaded.InventoryItems.Single(i => i.Id == armor.Id).Durability.Get() - worn.Durability.Get()) < .001,
            "reconectar conserva desgaste e capacidade da roupa");
        worn.Durability = new Gauge(worn.Durability.Max(), 0, new[] { new GaugeNode(0, .01f) });
        context.InventoryItems[context.InventoryItems.FindIndex(i => i.Id == armor.Id)] = worn;
        Call(link.Player, "WearEquippedArmor");
        check(context.InventoryItems.Single(i => i.Id == armor.Id).Durability.Get() == 0,
            "roupa quebrada permanece no inventario");
        Call(link.Player, "WearEquippedArmor");
        check(context.InventoryItems.Single(i => i.Id == armor.Id).Durability.Get() == 0,
            "roupa quebrada nao regenera ao receber golpe");
        var kitId = SingletonDict<string, List<Prototype>>.Instance.Keys.First(id =>
            PrototypeYaml.GetItemPrototype(id).Tags?.ContainsKey("tool_repair_kit") == true);
        var kit = Cheats.MakeItem(kitId, 60).Value;
        context.InventoryItems.Add(kit);
        link.Request<RepairItem, Messages.Timer>(new RepairItem { ItemId = armor.Id, KitItemIds = new[] { kit.Id } });
        var repaired = context.InventoryItems.Single(i => i.Id == armor.Id);
        check(repaired.Durability.Get() == repaired.Durability.Max() && repaired.Durability.Max() == armor.Durability.Max(),
            "kit repara roupa e preserva capacidade pelo TCP");
        context.InventoryItems.RemoveAll(i => i.Id == armor.Id || i.Id == avatar.Id || i.Id == unequipped.Id);
        context.EquippedItems = savedEquipment;
    }
}
