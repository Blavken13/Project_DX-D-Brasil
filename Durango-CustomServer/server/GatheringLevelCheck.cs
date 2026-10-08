using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Durango.Online;
using Durango.Utils;
using Messages;
using Yaml;
using Yaml.Util;

namespace DurangoServerNx;

internal static class GatheringLevelCheck
{
    private const ushort ResourceType = 11070; // tree_sandalwood: leaf_small e outros generators.
    private const string GeneratorId = "leaf_small";

    private static object Call(object target, string method, params object[] args) => target.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);

    private static PlayerContext Context(string root, string id, World world)
    {
        var context = new PlayerContext { RegionId = world.TerrainId, PlayerInfo = new Durango.Logic.Clusters.PlayerInfo
            { PlayerEntityId = id, PlayerName = id, PlayerLevel = 60 } };
        context.Initialize(Path.Combine(root, id + ".player"));
        context.AppearPlayer.Level = 60;
        context.AppearPlayer.IsAlive = true;
        return context;
    }

    private static void Place(PlayerContext context, Point2 tile) => context.AppearPlayer.Move.Movements = new[]
    { new Movement { MotionName = "Stand", PlaybackRate = 1, Path = new[]
        { new Location { Position = new WorldPosition(tile.x * 200, tile.y * 200), Time = Gauge.CurrentTime } } } };

    private static SkillCategorySave Skill(EconomyProtocolCheck.Link link) =>
        (SkillCategorySave)Call(link.Player, "CategoryState", (int)Shared.Skill.Category.Gathering);

    private static Collectible Touch(EconomyProtocolCheck.Link link, Point2 tile) => link.Request<Touch, Touched>(
        new Touch { EntityId = "", EntityType = ResourceType, Tile = tile }).Collectible;

    private static Generator Leaf(Collectible menu) => menu.Generators.Single(g => g.Id == GeneratorId);

    private static void Collect(EconomyProtocolCheck.Link link, PlayerContext context, Point2 tile,
        int clientLevel, int expectedLevel, int actionLevel, Action<bool, string> check)
    {
        int before = context.InventoryItems.Count;
        int replies = link.Messages.OfType<Collected>().Count();
        var timer = link.Request<Collect, Messages.Timer>(new Collect
            { EntityId = "", Tile = tile, GeneratorId = GeneratorId, Level = clientLevel });
        Call(link.Player, "UpdatePendingCollects", Gauge.CurrentTime + timer.Duration + .1);
        link.PumpUntil(() => link.Messages.OfType<Collected>().Count() > replies);
        var items = context.InventoryItems.Skip(before).ToArray();
        var reply = link.Messages.OfType<Collected>().Last();
        check(items.Length > 0 && items.All(i => i.Level == expectedLevel &&
            GatheredPropertiesCheck.Levels(i, expectedLevel)) && reply.Items.All(i => i.Level == expectedLevel),
            $"coleta TCP entrega itens e tags nivel {expectedLevel} mesmo com cliente enviando {clientLevel}");
        check(reply.ActionInfo.ActionLevel == actionLevel && reply.ActionInfo.PotentialLevel == expectedLevel &&
            reply.ActionInfo.RelatedCategory == Shared.Skill.Category.Gathering,
            "resultado distingue nivel do recurso e nivel obtido pela habilidade");
    }

    public static void Run(string root, World highWorld, World lowWorld, Action<bool, string> check)
    {
        var tile = new Point2(highWorld.EntryPoint.x + 9, highWorld.EntryPoint.y + 9);
        highWorld.AddNatural(tile, ResourceType);
        var beginner = Context(root, "gather-beginner", highWorld);
        var expert = Context(root, "gather-expert", highWorld);
        using var low = new EconomyProtocolCheck.Link(beginner, highWorld, null, true);
        using var high = new EconomyProtocolCheck.Link(expert, highWorld, null, true);
        Place(beginner, tile);
        Place(expert, tile);
        Skill(low).Level = 10;
        Skill(high).Level = 60;
        int resourceLevel = (int)Call(high.Player, "CurrentGatheringLevel", ResourceType);
        var lowMenu = Touch(low, tile);
        var highMenu = Touch(high, tile);
        check(Leaf(lowMenu).Level == 10 && Leaf(highMenu).Level == resourceLevel,
            "mesmo recurso mostra niveis diferentes conforme habilidade, sem cache entre jogadores");
        check(lowMenu.Generators.All(g => {
            var other = highMenu.Generators.Single(h => h.Id == g.Id);
            return g.Amount == other.Amount && g.Duration == other.Duration && g.Effort == other.Effort &&
                g.ToolRequirements.Count == other.ToolRequirements.Count &&
                g.ToolRequirements.All(t => other.ToolRequirements.TryGetValue(t.Key, out int level) && level == t.Value);
        }), "habilidade nao altera quantidade compartilhada, tempo base ou requisitos de ferramenta");

        Collect(low, beginner, tile, 60, 10, resourceLevel, check);
        var refreshed = low.Request<GetCollectible, Collectible>(new GetCollectible { EntityId = "", Tile = tile });
        var expertRefresh = Touch(high, tile);
        check(Leaf(refreshed).Level == Math.Min(resourceLevel, Skill(low).Level) && Leaf(refreshed).Amount == Leaf(lowMenu).Amount - 1 &&
            Leaf(expertRefresh).Amount == Leaf(refreshed).Amount,
            "GetCollectible acompanha habilidade após XP e ambos observam a mesma quantidade restante");
        Collect(high, expert, tile, 1, resourceLevel, resourceLevel, check);
        Skill(low).Level = 20;
        check(Leaf(low.Request<GetCollectible, Collectible>(new GetCollectible { EntityId = "", Tile = tile })).Level == 20,
            "progredir a habilidade atualiza o nivel sem relogar ou mudar de ilha");
        Collect(low, beginner, tile, int.MaxValue, 20, resourceLevel, check);
        for (int taken = 3; taken < Leaf(lowMenu).Amount; taken++)
        {
            Skill(low).Level = 1;
            Skill(low).Exp = 0;
            Collect(low, beginner, tile, 60, 1, resourceLevel, check);
        }
        check(!Touch(high, tile).Generators.Any(g => g.Id == GeneratorId) &&
            !Touch(low, tile).Generators.Any(g => g.Id == GeneratorId) && highWorld.NaturalTypeAt(tile) == ResourceType,
            "esgotamento e compartilhado e nao remove o recurso com outros generators restantes");

        var lowTile = new Point2(lowWorld.EntryPoint.x + 9, lowWorld.EntryPoint.y + 9);
        lowWorld.AddNatural(lowTile, ResourceType);
        var lowIslandContext = Context(root, "gather-low-island", lowWorld);
        using var lowIsland = new EconomyProtocolCheck.Link(lowIslandContext, lowWorld, null, true);
        Place(lowIslandContext, lowTile);
        Skill(lowIsland).Level = 60;
        check(Leaf(Touch(lowIsland, lowTile)).Level == 10, "habilidade alta conserva teto da ilha baixa");
        Collect(lowIsland, lowIslandContext, lowTile, 60, 10, 10, check);

        // Limites do prototype tambem se aplicam quando os dados introduzem itens com minimo maior.
        var prototype = PrototypeYaml.GetItemPrototype(GeneratorId);
        int originalMin = prototype.MinLevel;
        int originalMax = prototype.MaxLevel;
        try
        {
            prototype.MinLevel = 15;
            Skill(low).Level = 10;
            highWorld.AddNatural(tile, ResourceType);
            highWorld.ForgetHarvests($"{tile.x},{tile.y}");
            check(!Leaf(Touch(low, tile)).Enabled, "menu desabilita item com minimo acima da habilidade");
            int before = beginner.InventoryItems.Count;
            var abort = low.Request<Collect, Abort>(new Collect { EntityId = "", Tile = tile, GeneratorId = GeneratorId, Level = 60 });
            check(abort.Text.Contains("15") && beginner.InventoryItems.Count == before &&
                highWorld.HarvestedGenerators($"{tile.x},{tile.y}").Count == 0,
                "servidor rejeita minimo acima da habilidade sem dar itens nem reservar recurso");
            Skill(low).Level = 15;
            check(Leaf(Touch(low, tile)).Enabled, "atingir nivel minimo habilita o recurso");
            Collect(low, beginner, tile, 60, 15, resourceLevel, check);

            prototype.MinLevel = originalMin;
            prototype.MaxLevel = 7;
            check(Leaf(Touch(high, tile)).Level == 7, "menu respeita maximo do prototype abaixo da ilha e habilidade");
            Collect(high, expert, tile, 60, 7, 7, check);
        }
        finally
        {
            prototype.MinLevel = originalMin;
            prototype.MaxLevel = originalMax;
        }
        CheckLevel60(root, check);
    }

    private static void CheckLevel60(string root, Action<bool, string> check)
    {
        string island = RegionCatalog.All.First(r => RegionCatalog.GetTemplate(r.TemplateId) is
            { Role: Shared.Region.Role.Risky, Level: 60 }).Id;
        var worldContext = new WorldContext { TerrainId = island };
        worldContext.Initialize(Path.Combine(root, "gather-60.world"));
        var world = new World(worldContext);
        var context = Context(root, "gather-level-matrix", world);
        using var link = new EconomyProtocolCheck.Link(context, world, null);
        var skills = (SkillSave)typeof(Player).GetField("_skills", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(link.Player);
        int characterExp = (int)typeof(Player).GetMethod("ExpForLevel", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { 60 });
        link.Player.AddExp(characterExp - skills.Exp, "cheat");
        // Unlock the tested resource families independently of their quality cap.
        foreach (var (id, subs) in SkillDataStore.Skills[(int)Shared.Skill.Category.Gathering])
            foreach (var (sub, nodes) in subs)
            {
                if (!skills.Learned.TryGetValue(id, out var learned)) skills.Learned[id] = learned = new();
                learned[sub] = nodes.Length;
            }
        var tile = new Point2(world.EntryPoint.x + 9, world.EntryPoint.y + 9);
        Place(context, tile);
        var tool = Cheats.MakeItem("axe_onehand_loose_stone", 60).Value;
        context.InventoryItems.Add(tool);
        void Gather(ushort type, string generator, int skillLevel, int? deliverySkill = null)
        {
            Skill(link).Level = skillLevel; Skill(link).Exp = 0;
            Skill(link).ResearchStart = Skill(link).ResearchEnd = 0;
            world.AddNatural(tile, type); world.ForgetHarvests($"{tile.x},{tile.y}");
            var menu = TouchType(link, type, tile);
            check(menu.Generators.Single(g => g.Id == generator).Level == skillLevel,
                $"ilha 60 / Coleta {skillLevel}: prévia de {generator} respeita habilidade");
            int before = context.InventoryItems.Count;
            int replies = link.Messages.OfType<Collected>().Count();
            var timer = link.Request<Collect, Messages.Timer>(new Collect { EntityId = "", Tile = tile,
                GeneratorId = generator, Level = int.MaxValue, ToolItemId = tool.Id });
            if (deliverySkill.HasValue) Skill(link).Level = deliverySkill.Value;
            Call(link.Player, "UpdatePendingCollects", Gauge.CurrentTime + timer.Duration + 1);
            link.PumpUntil(() => link.Messages.OfType<Collected>().Count() > replies);
            int expected = deliverySkill ?? skillLevel;
            var items = context.InventoryItems.Skip(before).ToArray();
            var reply = link.Messages.OfType<Collected>().Last();
            check(context.AppearPlayer.Level == 60 && items.Length > 0 && items.All(i => i.Level == expected && i.Tags.All(t => t.Level <= expected)) &&
                reply.Items.All(i => i.Level == expected) && reply.ActionInfo.PotentialLevel == expected,
                $"personagem 60 coleta {generator} nível {expected}, com tags e pacote concordando");
            var updated = link.Request<GetSkills, Skills>(default);
            check(updated.Categories[Shared.Skill.Category.Gathering].Level == Skill(link).Level,
                "barra da habilidade recebe estado real após os 12 pontos da coleta");
        }
        foreach (int level in new[] { 1, 10, 20, 60 })
        {
            Gather(11070, "leaf_small", level);
            Gather(11070, "wood_log", level);
            Gather(12000, "stone", level);
        }
        var premium = new PremiumStore(Path.Combine(root, "gather-premium.json"));
        premium.Recover(context);
        check(premium.Change(context, "monthly_package_1", 1, "grant", "gather-level-premium", out _, out _, out _),
            "premium ativado para verificar nível da coleta adicional");
        link.Player.SyncPremium(); link.Player.PremiumGatherRoll = () => 0;
        Gather(11070, "leaf_small", 40, deliverySkill: 10);
        var premiumItems = link.Messages.OfType<Collected>().Last().Items;
        check(premiumItems.Length >= 2 && premiumItems.All(i => i.Level == 10 && i.GeneratorId == "leaf_small") &&
            premiumItems.Select(i => i.Id).Distinct().Count() == premiumItems.Length,
            "item extra do premium herda limite final e mantém origem e identidade próprias");
        check(premium.Change(context, "monthly_package_1", 0, "revoke", "gather-level-premium-end", out _, out _, out _),
            "premium removido antes do teste de reconexão");
        link.Player.SyncPremium();
        Skill(link).Level = 10; Skill(link).Exp = 0;
        Call(link.Player, "SaveSkillState"); context.Save(); check(SafeSave.FlushPending(), "habilidade de Coleta salva para teste de reconexão");
        var persisted = PlayerContext.Load(context.Path);
        using var reconnect = new EconomyProtocolCheck.Link(persisted, world, null);
        check(reconnect.Request<GetSkills, Skills>(default).Categories[Shared.Skill.Category.Gathering].Level == 10,
            "reconexão preserva Coleta 10 apesar do personagem estar no nível 60");
        Place(persisted, tile); world.AddNatural(tile, ResourceType); world.ForgetHarvests($"{tile.x},{tile.y}");
        check(Leaf(Touch(reconnect, tile)).Level == 10, "prévia após reconexão conserva o limite da habilidade");
        Collect(reconnect, persisted, tile, 60, 10, 60, check);
        var template = RegionCatalog.GetTemplate(world.TerrainInfo.region_template);
        bool hadOverride = template.CollectibleLevels.TryGetValue(ResourceType, out int original);
        try
        {
            template.CollectibleLevels[ResourceType] = 80;
            check((int)Call(reconnect.Player, "CurrentGatheringLevel", ResourceType) == 60,
                "override de recurso não ultrapassa nível 60 da ilha");
        }
        finally { if (hadOverride) template.CollectibleLevels[ResourceType] = original; else template.CollectibleLevels.Remove(ResourceType); }
    }

    private static Collectible TouchType(EconomyProtocolCheck.Link link, ushort type, Point2 tile) =>
        link.Request<Touch, Touched>(new Touch { EntityId = "", EntityType = type, Tile = tile }).Collectible;
}
