using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Durango.Online;
using Durango.Utils;
using Messages;
using Newtonsoft.Json.Linq;
using Shared.Display;
using Shared.Animal;
using Shared.Pet;
using Shared.Region;

namespace DurangoServerNx;

internal static class MapTravelMountCheck
{
    private static object Call(object target, string name, params object[] args) => target.GetType()
        .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args);
    private static PlayerContext Context(string root, string id) {
        var context = new PlayerContext { RegionId = "ri35te", PlayerInfo = new Durango.Logic.Clusters.PlayerInfo {
            PlayerEntityId = id, PlayerName = id, PlayerLevel = 60 } };
        context.Initialize(Path.Combine(root, id + ".player")); return context;
    }
    internal static void Run(string root, Action<bool, string> check)
    {
        var wc = new WorldContext { TerrainId = "ri35te" }; wc.Initialize(Path.Combine(root, "map-mount.world"));
        var world = new World(wc); var context = Context(root, "map-mount");
        var catalog = new ShopCatalog();
        var economy = new EconomyStore(Path.Combine(root, "free-ticket-economy.json"), catalog);
        using var link = new EconomyProtocolCheck.Link(context, world, economy, true);
        check(context.ExploredChunks[context.RegionId].Count is > 0 and <= 9,
            "entrada revela somente entorno, preservando nevoa");
        var untouched = link.Request<GetRegionMapInfo, RegionMapInfo>(new GetRegionMapInfo { RegionId = "ri55tu" });
        check(untouched.DefoggedChunks.Chunks.Length == 0, "preview de ilha nao visitada fica sob nevoa");
        int count = context.ExploredChunks[context.RegionId].Count;
        var initial = context.ExploredChunks[context.RegionId].ToHashSet();
        int far = world.NumChunksX > 7 ? 6 : world.NumChunksX - 2;
        var movement = new Movement { MotionName = "Walk", PlaybackRate = 1,
            Path = new[] { new Location { Position = new WorldPosition(far * 3200, far * 3200), Time = Gauge.CurrentTime } } };
        link.Send(new Move { EntityId = context.EntityId, Movements = new[] { movement } });
        link.Request<GetRegionMapInfo, RegionMapInfo>(new GetRegionMapInfo { RegionId = context.RegionId });
        check(context.ExploredChunks[context.RegionId].Count > count && initial.IsSubsetOf(context.ExploredChunks[context.RegionId]),
            "movimento revela novos chunks sem esquecer os anteriores");
        var revealed = context.ExploredChunks[context.RegionId].ToHashSet();
        context.Save(); SafeSave.FlushPending();
        var persisted = Json.Read<PlayerContext>(File.ReadAllText(context.Path)); persisted.Initialize(context.Path);
        check(persisted.ExploredChunks[context.RegionId].SetEquals(revealed), "exploracao sobrevive ao save");
        var other = Context(root, "other-explorer");
        using (var otherLink = new EconomyProtocolCheck.Link(other, world, null, true))
            check(!other.ExploredChunks[other.RegionId].SetEquals(revealed), "exploracao e individual por jogador");
        context.ExploredChunks["other-instance"] = new HashSet<int> { 0 };
        check(context.ExploredChunks[context.RegionId].SetEquals(revealed), "ilhas e instancias mantem historicos separados");

        var level = typeof(Player).GetField("_skillLevel", BindingFlags.Instance | BindingFlags.NonPublic);
        level.SetValue(link.Player, 60);
        var routes = link.Request<GetRoutes, Routes>(default);
        var regionDefinitions = Json.ReadFromFile<JObject>("region_templates");
        foreach (var outpost in regionDefinitions.Properties().Where(p => (bool?)p.Value["active"] == true && (int?)p.Value["role"] == (int)Role.Outpost))
            check(routes._Routes.TryGetValue(Role.Outpost, out var outposts) && outposts.GetValueOrDefault(outpost.Name)?.Length > 0,
                "posto avancado visivel tem ilha no modal: " + outpost.Name);
        foreach (var arch in routes.ArchipelagoRoutes)
        {
            var info = link.Request<GetArchipelago, Archipelago>(new GetArchipelago { ArchipelagoId = arch.ArchipelagoId });
            check(arch.IncludedRoutes.Length > 0 && arch.IncludedRoutes.All(r => info.IncludedRegions.Any(i => i.Id == r.RegionId)),
                "modal recebe todas as ilhas do grupo " + arch.ArchipelagoId);
        }
        var areas = Json.ReadFromFile<JObject>("archipelago_templates");
        foreach (var area in areas.Properties().Where(a => (bool?)a.Value["active"] == true)
            .GroupBy(a => ((int)a.Value["level"], (int)a.Value["biome"])).Select(g => g.First()))
        {
            int islandLevel = (int)area.Value["level"], biome = (int)area.Value["biome"];
            check(routes.ArchipelagoRoutes.Any(a => a.Level == islandLevel && (int)a.Biome == biome && a.IncludedRoutes.Length > 0),
                $"zona visivel nivel {islandLevel} bioma {biome} tem ilha navegavel");
        }
        foreach (var region in RegionCatalog.Others(world.TerrainId))
        {
            var template = RegionCatalog.GetTemplate(region.TemplateId);
            int required = Math.Max(1, template.AvailableLevel);
            level.SetValue(link.Player, required);
            routes = link.Request<GetRoutes, Routes>(default);
            check(routes._Routes.Values.SelectMany(x => x.Values).SelectMany(x => x).Any(r => r.RegionId == region.Id),
                region.Id + " aparece no nivel de acesso do cliente: " + required);
            level.SetValue(link.Player, required - 1);
            routes = link.Request<GetRoutes, Routes>(default);
            check(!routes._Routes.Values.SelectMany(x => x.Values).SelectMany(x => x).Any(r => r.RegionId == region.Id),
                region.Id + " permanece bloqueada abaixo do nivel correto");
        }
        level.SetValue(link.Player, 60);

        const string ticket = "item_skill_reset_ticket_store";
        var native = Json.ReadFromFile<JObject>("purchaser/commodities")["posted_commodities"];
        check(catalog.Definitions[ticket].Price == 0 && catalog.Definitions.Values.Where(d => d.Id != ticket)
            .All(d => d.Price == (long)native[d.Id]["price_amount"]), "apenas bilhete de reset muda de preco");
        context.WarpGem = 0;
        var bought = link.Request<PurchaseCommodity, Purchased>(new PurchaseCommodity { CommodityId = ticket });
        link.Request<AcceptPurchase, OK>(new AcceptPurchase { PurchaseId = bought.Purchases.Single().Id });
        check(context.WarpGem == 0 && context.InventoryItems.Any(i => i.Prototype == "skill_reset_ticket"),
            "bilhete comprado e recebido por TCP com saldo zero");

        var skillSave = (SkillSave)typeof(Player).GetField("_skills", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(link.Player);
        skillSave.Learned["worktable"] = new Dictionary<string, int> { ["__base__"] = 4 };
        skillSave.UntrainedCount = 999;
        var category = (SkillCategorySave)Call(link.Player, "CategoryState", 0);
        category.Level = 20; category.Exp = 3;
        int exp = skillSave.Exp;
        check((int)Call(link.Player, "UsedSkillPoints") > 0, "reset parte de personagem com pontos gastos");
        string resetId = context.InventoryItems.First(i => i.Prototype == "skill_reset_ticket").Id;
        int skillMessages = link.Messages.OfType<Skills>().Count();
        link.Request<UseItem, OK>(new UseItem { ItemId = resetId });
        link.PumpUntil(() => link.Messages.OfType<Skills>().Count() > skillMessages);
        check((int)Call(link.Player, "UsedSkillPoints") == 0 &&
            (int)Call(link.Player, "RemainSkillPoints") == (int)Call(link.Player, "TotalSkillPoints"),
            "bilhete devolve todos os pontos sem exigir saldo nem limite de resets");
        check(skillSave.Exp == exp && category.Level == 20 && category.Exp == 3,
            "reset preserva experiencia do personagem e progresso das categorias");
        check(!context.InventoryItems.Any(i => i.Id == resetId) && !link.Player.UnlockedBlueprintIds().Contains("fur_table_03"),
            "reset consome um bilhete e remove desbloqueios das skills pagas");
        var savedSkills = Json.Read<SkillSave>(context.Storage[SkillTuning.StorageKey]);
        check(savedSkills.UntrainedCount == 0 && savedSkills.Learned["worktable"]["__base__"] == 1,
            "reset salvo mantem apenas a habilidade gratuita da bancada");
        link.Request<UseItem, Abort>(new UseItem { ItemId = resetId });
        var beginnerContext = Context(root, "reset-beginner");
        using (var beginnerLink = new EconomyProtocolCheck.Link(beginnerContext, world, null, true))
        {
            typeof(Player).GetField("_skillLevel", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(beginnerLink.Player, 1);
            var beginnerTicket = Cheats.MakeItem("skill_reset_ticket", 1).Value;
            beginnerContext.InventoryItems.Add(beginnerTicket);
            beginnerLink.Request<UseItem, OK>(new UseItem { ItemId = beginnerTicket.Id });
            check(!beginnerContext.InventoryItems.Any(i => i.Id == beginnerTicket.Id),
                "personagem nivel 1 sem skills pagas tambem pode usar o bilhete");
        }

        var petData = Json.ReadFromFile<JObject>("pet/pets_for_client");
        ushort type = ushort.Parse(petData.Properties().First(p => (bool?)p.Value["is_ridable"] == true).Name);
        var pet = Player.PetFactory.Build(type, (PetRank)0, 20, context.EntityId).Value;
        var entry = new Player.PetStore.Entry { Pet = pet, LifeMax = pet.Stat.Life.Max(Gauge.CurrentTime),
            HungryMax = pet.Stat.Hungry.Max(Gauge.CurrentTime) };
        Player.PetStore.Add(context.EntityId, entry);
        link.Request<SpawnPet, OK>(new SpawnPet { PetId = pet.EntityId });
        link.Send(default(Mount));
        link.PumpUntil(() => link.Messages.OfType<PlayerDisplay>().Any(p => p.BoardingOn == BoardingOn.Pet));
        check(entry.Pet.IsBoarding && context.AppearPlayer.Display.VehicleEntityId == pet.EntityId,
            "montar publica estado do personagem e vincula o dino correto");
        link.Send(default(Unmount));
        link.PumpUntil(() => !entry.Pet.IsBoarding);
        check(context.AppearPlayer.Display.BoardingOn == BoardingOn.None && context.AppearPlayer.Display.VehicleEntityId == "",
            "desmontar limpa o vinculo de montaria");
        link.Send(default(Mount)); link.PumpUntil(() => entry.Pet.IsBoarding);
        link.Request<ReturnPet, OK>(new ReturnPet { PetId = pet.EntityId });
        check(!entry.Pet.IsSpawned && context.AppearPlayer.Display.BoardingOn == BoardingOn.None,
            "recolher dino montado desmonta o personagem");
        string target = "ri20te_alpha";
        var destination = RegionCatalog.GetTemplate("ri20te190710");
        level.SetValue(link.Player, destination.AvailableLevel - 1);
        link.Request<TravelByRegion, Abort>(new TravelByRegion { RegionId = target });
        check(context.RegionId != target, "viagem direta tambem bloqueia jogador abaixo do nivel de acesso");
        level.SetValue(link.Player, destination.AvailableLevel);
        link.Request<TravelByRegion, OK>(new TravelByRegion { RegionId = target });
        link.PumpUntil(() => link.Messages.OfType<Emigrated>().Any());
        check(context.RegionId == target && context.AppearPlayer.Move.Movements == null,
            "viagem para nova ilha no nivel correto prepara chegada e reconexao");
    }
}
