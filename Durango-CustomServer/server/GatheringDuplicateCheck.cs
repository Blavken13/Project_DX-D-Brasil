using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Durango.Online;
using Durango.Utils;
using Messages;
using Yaml.Util;

namespace DurangoServerNx;

internal static class GatheringDuplicateCheck
{
    private static int _checks;
    private static object Call(object target, string name, params object[] args) => target.GetType()
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    private static int Pending(Player player) => ((IList)typeof(Player)
        .GetField("_pendingCollects", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(player)).Count;
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
        _checks++; Console.WriteLine("[gathering-duplicate-check] OK " + label);
    }

    public static int Run(string dataDir)
    {
        string root = Path.Combine(Path.GetTempPath(), "Durango-gathering-duplicate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Json.DataDir = dataDir; MoCatalog.Load(dataDir); DataStore.Load(dataDir);
            TerrainLoader.TerrainDir = Path.Combine(dataDir, "terrains");
            WorkbenchTags.AssetsDir = Path.Combine(dataDir, "assets"); RegionCatalog.Load(WorkbenchTags.AssetsDir);
            foreach (var mode in new[] { "normal", "premium-extra", "premium-sem-extra" })
                Scenario(root, mode);
            Carcass(root);
            Check(PremiumStore.GatherChance == .25f, "chance premium continua em 25%");
            Check(SafeSave.FlushPending(), "saves temporarios gravados");
            Console.WriteLine($"[gathering-duplicate-check] PASS {_checks} verificacoes");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("[gathering-duplicate-check] FAIL " + ex); return 1; }
        finally { SafeSave.FlushPending(); }
    }

    private static void Scenario(string root, string mode)
    {
        var wc = new WorldContext { TerrainId = "ri35te" }; wc.Initialize(Path.Combine(root, mode + ".world"));
        var world = new World(wc);
        var context = new PlayerContext { RegionId = world.TerrainId,
            PlayerInfo = new Durango.Logic.Clusters.PlayerInfo { PlayerEntityId = mode, PlayerName = mode, PlayerLevel = 10 } };
        context.Initialize(Path.Combine(root, mode + ".player")); context.AppearPlayer.IsAlive = true;
        var store = new PremiumStore(Path.Combine(root, mode + ".premium"), () => 1800000000);
        store.Recover(context);
        bool premium = mode != "normal", extra = mode == "premium-extra";
        if (premium) Check(store.Change(context, "monthly_package_1", 1, "grant", mode, out _, out _, out _), mode + ": premium ativo");
        using var link = new EconomyProtocolCheck.Link(context, world, null);
        context.InventoryItems.Clear(); // Remove login gifts from this gathering fixture.
        int rolls = 0;
        link.Player.PremiumGatherRoll = () => { rolls++; return extra ? .249 : .25; };
        var tile = new Point2(world.EntryPoint.x + 9, world.EntryPoint.y + 9);
        const ushort type = 11070;
        world.AddNatural(tile, type);
        context.AppearPlayer.Move.Movements = new[] { new Movement { MotionName = "Stand", PlaybackRate = 1,
            Path = new[] { new Location { Position = new WorldPosition(tile.x * 200, tile.y * 200), Time = Gauge.CurrentTime } } } };
        var menu = link.Request<Touch, Touched>(new Touch { EntityId = "", EntityType = type, Tile = tile }).Collectible;
        Check(menu.Generators.Single(g => g.Id == "leaf_small").Amount > 1, mode + ": recurso permite coletas sequenciais");
        var request = new Collect { EntityId = "", Tile = tile, GeneratorId = "leaf_small", Level = 1 };
        string key = $"{tile.x},{tile.y}";
        int InventoryMessages() => link.Messages.OfType<Inventory>().Count();
        void RefreshBag()
        {
            int before = InventoryMessages(); link.Send(new GetInventory());
            link.PumpUntil(() => InventoryMessages() > before);
        }
        RefreshBag();
        var timer = link.Request<Collect, Messages.Timer>(request);
        int reserved = world.HarvestedGenerators(key).Count;
        Check(reserved == 1 && Pending(link.Player) == 1 && context.InventoryItems.Count == 0,
            mode + ": primeira coleta reserva uma vez sem entrega antecipada");
        RefreshBag();
        var duplicateTimer = link.Request<Collect, Messages.Timer>(request);
        Check(world.HarvestedGenerators(key).Count == reserved && Pending(link.Player) == 1,
            mode + $": repetir coleta com bolsa aberta conserva uma reserva e um temporizador (reservas={world.HarvestedGenerators(key).Count}, pendentes={Pending(link.Player)})");
        Check(duplicateTimer.Duration <= timer.Duration && duplicateTimer.Duration >= 0,
            mode + ": repeticao informa tempo restante sem reiniciar coleta");
        request.Level = int.MaxValue;
        link.Request<Collect, Messages.Timer>(request);
        Check(Pending(link.Player) == 1 && rolls == 0, mode + ": nivel enviado e repeticoes nao criam entrega nem sorteio extra");
        var other = request; other.GeneratorId = "branch";
        link.Request<Collect, Abort>(other);
        Check(Pending(link.Player) == 1 && world.HarvestedGenerators(key).Count == 1,
            mode + ": outro gerador nao pode iniciar coleta paralela");
        int start = link.Messages.Count;
        Call(link.Player, "UpdatePendingCollects", Gauge.CurrentTime + timer.Duration + .1);
        link.PumpUntil(() => link.Messages.Skip(start).OfType<Collected>().Any());
        RefreshBag();
        var results = link.Messages.Skip(start).OfType<Collected>().ToArray();
        var updates = link.Messages.Skip(start).OfType<InventoryUpdated>().Where(u => u.Items?.Any(i => i.GeneratorId == "leaf_small") == true).ToArray();
        Check(results.Length == 1 && updates.Length == 1 && Pending(link.Player) == 0,
            mode + ": uma conclusao e uma atualizacao de itens");
        var result = results.Single();
        Check(result.Items.Select(i => i.Id).ToHashSet().SetEquals(context.InventoryItems.Select(i => i.Id)) &&
            result.Items.Select(i => i.Id).SequenceEqual(updates.Single().Items.Select(i => i.Id)) &&
            result.Items.Length == context.InventoryItems.Count && context.InventoryItems.Select(i => i.Id).Distinct().Count() == context.InventoryItems.Count,
            mode + ": resultado, bolsa e inventario do servidor concordam sem ids duplicados");
        Check(rolls == (premium ? 1 : 0) && (!extra || result.Items.Length >= 2), mode + ": bonus premium sorteado uma vez e extra preservado");
        int count = context.InventoryItems.Count;
        Call(link.Player, "UpdatePendingCollects", Gauge.CurrentTime + timer.Duration + 1);
        RefreshBag();
        Check(context.InventoryItems.Count == count && link.Messages.Skip(start).OfType<Collected>().Count() == 1,
            mode + ": atualizar bolsa e temporizador novamente nao repete entrega");
        var next = link.Request<Collect, Messages.Timer>(request);
        Check(Pending(link.Player) == 1 && world.HarvestedGenerators(key).Count == 2, mode + ": proxima coleta legitima continua permitida");
        Call(link.Player, "ClearCollectTimers");
        Check(Pending(link.Player) == 0 && world.HarvestedGenerators(key).Count == 1, mode + ": cancelamento devolve somente a reserva pendente");
        link.Request<Collect, Messages.Timer>(request);
        Check(Pending(link.Player) == 1, mode + ": nova tentativa apos cancelar funciona");
        Call(link.Player, "ClearCollectTimers");
    }

    private static void Carcass(string root)
    {
        var wc = new WorldContext { TerrainId = "ri35te" }; wc.Initialize(Path.Combine(root, "carcass.world"));
        var world = new World(wc);
        var context = new PlayerContext { RegionId = world.TerrainId,
            PlayerInfo = new Durango.Logic.Clusters.PlayerInfo { PlayerEntityId = "carcass", PlayerName = "carcass", PlayerLevel = 10 } };
        context.Initialize(Path.Combine(root, "carcass.player")); context.AppearPlayer.IsAlive = true;
        var store = new PremiumStore(Path.Combine(root, "carcass.premium"), () => 1800000000);
        store.Recover(context);
        Check(store.Change(context, "monthly_package_1", 1, "grant", "carcass", out _, out _, out _), "carcaca: personagem premium");
        using var link = new EconomyProtocolCheck.Link(context, world, null);
        link.Player.PremiumGatherRoll = () => throw new InvalidOperationException("Premium gathering roll on carcass");
        var tile = world.EntryPoint;
        var animal = world.AnimalManager.SpawnAt(2010, 28, tile);
        animal.IsAlive = false; animal.Life = 0; animal.DiedAt = Gauge.CurrentTime;
        context.AppearPlayer.Move.Movements = new[] { new Movement { MotionName = "Stand", PlaybackRate = 1,
            Path = new[] { new Location { Position = new WorldPosition(tile.x * 200, tile.y * 200), Time = Gauge.CurrentTime } } } };
        link.Request<Touch, Touched>(new Touch { EntityId = animal.EntityId, EntityType = animal.EntityType, Tile = tile });
        var request = new Collect { EntityId = animal.EntityId, Tile = new Point2(-1, -1), GeneratorId = "meat", Level = 60 };
        int before = context.InventoryItems.Count;
        var timer = link.Request<Collect, Messages.Timer>(request);
        link.Request<Collect, Messages.Timer>(request);
        Check(Pending(link.Player) == 1 && animal.ActiveCollectors == 1 && world.HarvestedGenerators(animal.EntityId).Count == 1,
            "carcaca: repeticao com tile -1 conserva uma reserva e um coletor");
        int start = link.Messages.Count;
        Call(link.Player, "UpdatePendingCollects", Gauge.CurrentTime + timer.Duration + .1);
        link.PumpUntil(() => link.Messages.Skip(start).OfType<Collected>().Any());
        var result = link.Messages.Skip(start).OfType<Collected>().Single();
        Check(context.InventoryItems.Count == before + result.Items.Length && animal.ActiveCollectors == 0 &&
            result.Items.Select(i => i.Id).SequenceEqual(context.InventoryItems.Skip(before).Select(i => i.Id)),
            "carcaca: uma entrega libera protecao e preserva bonus proprio de esfolamento");
        Check(!animal.Butchered && !animal.CorpseDisposed, "carcaca: partes restantes continuam disponiveis");
        link.Request<Collect, Messages.Timer>(request);
        Call(link.Player, "ClearCollectTimers");
        Check(animal.ActiveCollectors == 0 && world.HarvestedGenerators(animal.EntityId).Count == 1,
            "carcaca: cancelar proxima coleta libera protecao sem remover coleta anterior");
    }
}
