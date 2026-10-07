using System;
using System.IO;
using System.Linq;
using Durango.Online;
using Durango.Terrain;
using Durango.Utils;
using Messages;
using Shared.Estate;
using Yaml.Util;

namespace DurangoServerNx;

internal static class EstateReturnCheck
{
    private static int _passed;
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
        _passed++;
        Console.WriteLine("[estate-return-check] OK " + label);
    }
    private static PlayerContext Context(string root, string name, string region)
    {
        var context = new PlayerContext { RegionId = region, PlayerInfo = new Durango.Logic.Clusters.PlayerInfo
            { PlayerEntityId = name, PlayerName = name, PlayerLevel = 60 } };
        context.Initialize(Path.Combine(root, name + ".player"));
        context.AppearPlayer.IsAlive = true;
        return context;
    }
    private static Point2 Position(AppearPlayer player)
    {
        WorldPosition position = player.Move.Movements[0].Path[0].Position;
        return new Point2((int)(position.x / 200), (int)(position.y / 200));
    }
    private static PlayerContext ReturnFromHunt(PlayerContext context, World hunt, OwnerType type, string estateId)
    {
        using (var link = new EconomyProtocolCheck.Link(context, hunt, null, true))
        {
            link.Player.ContextChanged += context.Save;
            var important = context.InventoryItems.FirstOrDefault(i => context.LockedItemIds.Contains(i.Id));
            if (important.Id == null)
            {
                important = Cheats.MakeItem("axe_onehand_loose_stone", 30).Value;
                context.InventoryItems.Add(important);
            }
            link.Send(new LockOrUnlockItems { Lock = true, ItemIds = new[] { important.Id } });
            link.PumpUntil(() => link.Messages.OfType<InventoryInfos>().Any(i =>
                i.LockedItemIds?.Contains(important.Id) == true));
            Check(context.LockedItemIds.Contains(important.Id), "bloqueio pertence ao save do personagem");
            link.Request<ReturnToEstate, Messages.Timer>(new ReturnToEstate { OwnerType = type });
            link.PumpUntil(() => link.Messages.OfType<Emigrated>().Any());
            Check(context.PendingEstateArrival?.EstateId == estateId, "retorno grava somente o dominio do dono: " + context.EntityId);
        }
        SafeSave.FlushPending();
        var loaded = PlayerContext.Load(context.Path);
        Check(loaded.LockedItemIds.SetEquals(context.LockedItemIds) && loaded.LockedItemIds.Count > 0,
            "viagem e recarga do save preservam itens bloqueados: " + context.EntityId);
        Check(loaded.RegionId == context.RegionId && loaded.PendingEstateArrival?.EstateId == estateId,
            "destino e chegada sobrevivem a reconexao: " + context.EntityId);
        return loaded;
    }
    private static void Arrival(PlayerContext context, World world, string estateId)
    {
        using var link = new EconomyProtocolCheck.Link(context, world, null, true);
        link.PumpUntil(() => link.Messages.OfType<Inventory>().Any());
        Check(link.Messages.OfType<Inventory>().Last().InventoryInfos.LockedItemIds.ToHashSet()
            .SetEquals(context.LockedItemIds), "inventario inicial restaura bloqueios na ilha de destino");
        var locked = context.InventoryItems.FirstOrDefault(i => context.LockedItemIds.Contains(i.Id));
        // As duas variantes do protocolo representam dropar no chão e destruir.
        var dump = typeof(Player).GetMethod("DumpItemsToGround", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
        if (locked.Id != null)
        {
            dump.Invoke(link.Player, new object[] { new DumpItems { ItemIds = new[] { locked.Id }, Tile = world.EntryPoint } });
            dump.Invoke(link.Player, new object[] { new DumpItems { ItemIds = new[] { locked.Id } } });
            Check(context.InventoryItems.Any(i => i.Id == locked.Id), "item bloqueado nao pode ser dropado ou destruido apos viagem");
        }
        Point2 tile = Position(link.Messages.OfType<AppearPlayer>().First());
        if (estateId == null)
            Check(tile.Equals(world.EntryPoint), "jogador sem dominio chega ao bote: " + context.EntityId);
        else
            Check(world.TryGetEstateIdAtCell(World.CellFromTile(tile), out string arrived) && arrived == estateId &&
                !tile.Equals(world.EntryPoint), "primeiro AppearPlayer nasce no proprio dominio: " + context.EntityId);
        Check(context.PendingEstateArrival == null, "chegada e consumida uma unica vez: " + context.EntityId);
    }
    public static int Run(string dataDir)
    {
        string root = Path.Combine(Path.GetTempPath(), "Durango-estate-return-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string priorAppData = AppData.BasePath;
        try
        {
            AppData.BasePath = root;
            Json.DataDir = dataDir; MoCatalog.Load(dataDir); DataStore.Load(dataDir);
            WorkbenchTags.AssetsDir = Path.Combine(dataDir, "assets");
            TerrainLoader.TerrainDir = Path.Combine(dataDir, "terrains");
            RegionCatalog.Load(Path.Combine(dataDir, "assets"));
            const string huntId = "ri30td01";
            World Hunt(string filename)
            {
                var wc = new WorldContext { TerrainId = huntId };
                wc.Initialize(Path.Combine(root, filename + ".world"));
                return new World(wc);
            }
            var hunt = Hunt("hunt");
            var registry = new WorldRegistry("estate-return", hunt, huntId);
            Check(registry.EnsureDefaultSharedTamedRegion(), "Ilha Domada registrada");
            var tamed = registry.GetOrCreate(WorldRegistry.DefaultSharedTamedRegionId);
            var terrain = TerrainLoader.Load(tamed.TerrainId);
            var candidateCells = TerrainEcology.LandGrid(terrain, World.EstateGridSize)
                .Where(t => Math.Abs(t.x - tamed.EntryPoint.x) > 12 || Math.Abs(t.y - tamed.EntryPoint.y) > 12)
                .Where(t => Enumerable.Range(0, 16).All(i => TerrainEcology.IsLand(terrain, new Point2(t.x + i % 4, t.y + i / 4))))
                .Select(World.CellFromTile).ToHashSet();
            Point2[] Neighbors(Point2 c) => new[] { new Point2(c.x + 1, c.y), new Point2(c.x - 1, c.y),
                new Point2(c.x, c.y + 1), new Point2(c.x, c.y - 1) };
            Point2 originalCell = candidateCells.First(c => Neighbors(c).Any(candidateCells.Contains));
            Point2 expansionCell = Neighbors(originalCell).First(candidateCells.Contains);
            var cells = new[] { originalCell, candidateCells.First(c => !c.Equals(originalCell) && !c.Equals(expansionCell)), expansionCell };
            Check(cells.Length == 3, "dominios de teste em terra firme longe do bote");
            string firstId = tamed.DeclareEstate("return-owner-one", OwnerType.Player, cells[0], WorldRegistry.DefaultSharedTamedRegionId).Value.EstateId;
            string secondId = tamed.DeclareEstate("return-owner-two", OwnerType.Player, cells[1], WorldRegistry.DefaultSharedTamedRegionId).Value.EstateId;
            registry.SaveAll(); SafeSave.FlushPending();

            // A new registry must discover the estate from the saved destination island.
            hunt = Hunt("hunt-after-restart");
            registry = new WorldRegistry("estate-return", hunt, huntId);
            Check(!registry.Loaded.Any(p => p.Key == WorldRegistry.DefaultSharedTamedRegionId), "mundo de destino ainda nao carregado apos reinicio");
            var first = Context(root, "return-owner-one", huntId);
            using (var menu = new EconomyProtocolCheck.Link(first, hunt, null, true))
            {
                var licenses = menu.Request<GetEstateLicenses, EstateLicenses>(default);
                Check(licenses.UrbanEstate?.EstateId == firstId, "menu nas ilhas de caca identifica dominio persistido do jogador");
            }
            tamed = registry.GetOrCreate(WorldRegistry.DefaultSharedTamedRegionId);
            Check(tamed.GetEstate(firstId)?.OwnerId == first.EntityId, "dominio localizado no save da Ilha Domada");
            first = ReturnFromHunt(first, hunt, OwnerType.Player, firstId);
            Check(first.RegionId == WorldRegistry.DefaultSharedTamedRegionId, "retorno muda para a Ilha Domada");
            Arrival(first, tamed, firstId);

            // The personal shortcut uses the same public estate when no private island exists.
            var second = Context(root, "return-owner-two", huntId);
            second = ReturnFromHunt(second, hunt, OwnerType.PersonalPlayer, secondId);
            Arrival(second, tamed, secondId);
            var homeless = Context(root, "return-no-estate", huntId);
            homeless = ReturnFromHunt(homeless, hunt, OwnerType.Player, null);
            Arrival(homeless, tamed, null);
            var noPrivate = Context(root, "return-no-private", huntId);
            noPrivate = ReturnFromHunt(noPrivate, hunt, OwnerType.PersonalPlayer, null);
            Arrival(noPrivate, tamed, null);

            using (var local = new EconomyProtocolCheck.Link(first, tamed, null, true))
            {
                local.Request<ReturnToEstate, Messages.Timer>(new ReturnToEstate { OwnerType = OwnerType.Player });
                local.PumpUntil(() => local.Messages.OfType<Teleported>().Any());
                Point2 tile = local.Messages.OfType<Teleported>().Last().Tile;
                Check(tamed.TryGetEstateIdAtCell(World.CellFromTile(tile), out string arrived) && arrived == firstId &&
                    !local.Messages.OfType<Emigrated>().Any(), "retorno na mesma ilha teleporta para o dominio sem trocar de mundo");
            }
            var foreign = Context(root, "return-foreign", WorldRegistry.DefaultSharedTamedRegionId);
            foreign.PendingEstateArrival = new EstateArrival { RegionId = foreign.RegionId, EstateId = firstId };
            Arrival(foreign, tamed, null);
            var stale = Context(root, "return-stale", WorldRegistry.DefaultSharedTamedRegionId);
            stale.PendingEstateArrival = new EstateArrival { RegionId = huntId, EstateId = firstId };
            Arrival(stale, tamed, null);

            Point2 anchor = World.TileFromCell(cells[0]);
            var obstruction = new AppearArtifact { EntityId = "return-obstruction", Tile = anchor, Size = new Point2(1, 1) };
            tamed.ArtifactManager.AddArtifact(obstruction);
            Check(tamed.TryEstateArrivalTile(firstId, out Point2 free) && !free.Equals(anchor) &&
                tamed.TryGetEstateIdAtCell(World.CellFromTile(free), out string freeEstate) && freeEstate == firstId,
                "chegada prefere um ponto livre dentro do proprio dominio");
            // Removing the original anchor must not send the owner to the old lot.
            Point2 neighbor = expansionCell;
            Check(tamed.ExpandEstate(firstId, first.EntityId, neighbor, 8).HasValue &&
                tamed.ShrinkEstate(firstId, first.EntityId, cells[0]).HasValue &&
                tamed.TryEstateArrivalTile(firstId, out Point2 shrunk) && World.CellFromTile(shrunk).Equals(neighbor),
                "dominio reduzido usa um lote que continua pertencendo ao jogador");

            const string privateRegion = "personal_return";
            registry.RegisterPersonalRegion(privateRegion, "pe10gr_1", first.EntityId);
            var privateWorld = registry.GetOrCreate(privateRegion);
            var privateTerrain = TerrainLoader.Load(privateWorld.TerrainId);
            Point2 privateCell = TerrainEcology.LandGrid(privateTerrain, 4).First(t =>
                Enumerable.Range(0, 16).All(i => TerrainEcology.IsLand(privateTerrain, new Point2(t.x + i % 4, t.y + i / 4))));
            string privateId = privateWorld.DeclareEstate(first.EntityId, OwnerType.PersonalPlayer, World.CellFromTile(privateCell), privateRegion).Value.EstateId;
            first.RegionId = huntId; first.PersonalRegionId = privateRegion; first.PersonalRegionTemplateId = "pe10gr_1";
            first = ReturnFromHunt(first, hunt, OwnerType.PersonalPlayer, privateId);
            Check(first.RegionId == privateRegion, "dono de Ilha Particular mantem o destino particular");
            Arrival(first, privateWorld, privateId);
            first.RegionId = huntId;
            first = ReturnFromHunt(first, hunt, OwnerType.Player, firstId);
            Arrival(first, tamed, firstId);
            second.RegionId = huntId;
            second = ReturnFromHunt(second, hunt, OwnerType.Player, secondId);
            Check(tamed.RemoveEstate(secondId, second.EntityId), "dominio removido enquanto o jogador troca de ilha");
            Arrival(second, tamed, null);
            Console.WriteLine($"[estate-return-check] PASS {_passed} verificacoes");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("[estate-return-check] FAIL " + ex); return 1; }
        finally
        {
            SafeSave.FlushPending();
            AppData.BasePath = priorAppData;
            Directory.Delete(root, true);
        }
    }
}
