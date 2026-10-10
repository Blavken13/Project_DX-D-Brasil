using System.Collections;
using System.Reflection;
using Durango.Online;
using Durango.Terrain;
using Durango.Utils;
using Messages;
using Shared.Region;
using Yaml;
using Yaml.Util;

namespace DurangoServerNx;

internal static class VolcanoCraterCheck
{
    private static int _checks;
    private static void Check(bool value, string text)
    { if (!value) throw new InvalidOperationException(text); _checks++; Console.WriteLine("[volcano-crater] OK " + text); }
    private static object Call(object obj, string name, params object[] args) => obj.GetType()
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(obj, args);
    private static T Field<T>(object obj, string name) => (T)obj.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(obj);
    private static void Place(PlayerContext context, Point2 tile, double time) =>
        context.AppearPlayer.Move.Movements = new[] { new Movement { PlaybackRate = 1, Path = new[] {
            new Location { Position = new WorldPosition(tile.x * 200 + 100, tile.y * 200 + 100), Time = time } } } };
    private static World MakeWorld(string root, string id, out WorldContext context)
    {
        context = new WorldContext { TerrainId = id }; context.Initialize(Path.Combine(root, id + ".world"));
        return new World(context);
    }
    private static PlayerContext MakePlayer(string root, string id, World world)
    {
        var context = new PlayerContext { PlayerInfo = new Durango.Logic.Clusters.PlayerInfo {
            PlayerEntityId = id, PlayerName = id, PlayerLevel = 60 } };
        context.Initialize(Path.Combine(root, id + ".player")); context.AppearPlayer.IsAlive = true;
        Place(context, world.EntryPoint, Gauge.CurrentTime);
        return context;
    }
    private static NaturalInfo[] Spots(World world)
    {
        var result = new List<NaturalInfo>();
        for (int y = 0; y < world.NumChunksY; y++)
        for (int x = 0; x < world.NumChunksX; x++)
            result.AddRange(NaturalInfo.FromBytes(((Chunk)Call(world,"CreateChunk",x,y)).Garden));
        return result.ToArray();
    }
    private static bool Near(NaturalInfo n, Point2 tile) => Math.Max(Math.Abs(n.X - tile.x), Math.Abs(n.Y - tile.y)) <= 24;

    public static int Run(string dataDir)
    {
        string root = Path.Combine(Path.GetTempPath(), "Durango-volcano-crater-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string prior = AppData.BasePath;
        try
        {
            AppData.BasePath = root; Json.DataDir = dataDir; MoCatalog.Load(dataDir); DataStore.Load(dataDir);
            WorkbenchTags.AssetsDir = Path.Combine(dataDir,"assets"); TerrainLoader.TerrainDir = Path.Combine(dataDir,"terrains");
            RegionCatalog.Load(WorkbenchTags.AssetsDir);
            Check(LavaExposure.Seconds(new(100,100), new(500,100), 2,
                t => t.x == 1 ? Biome.Lava : Biome.Volcanic) == 1, "travessia de canal entre dois pontos secos conta exatamente um segundo de lava");
            Check(LavaExposure.Seconds(new(500,100), new(100,100), 2,
                t => t.x == 1 ? (Biome)(0xC0 | (int)Biome.Lava) : Biome.Volcanic) == 1, "lava com flags e movimento reverso mantem dano correto");
            Check(LavaExposure.Seconds(new(300,100), new(300,100), 3, _ => Biome.Lava) == 3 &&
                LavaExposure.Seconds(new(100,100), new(500,100), 3, _ => Biome.Volcanic) == 0, "parado na lava sofre dano continuo; terra seca nao causa dano");
            Check(CollectibleTable.AllSpecs(13048).Any(s => s.Id == "obsidian" && s.PrototypeId == "stone_obsidian") &&
                !CollectibleTable.AllSpecs(13048).Any(s => s.PrototypeId == "stone"), "obsidiana usa item e gerador nativos sem fallback para pedra");
            foreach (ushort type in new ushort[] { 13026,13022,13023,13024,13028,13030,16018,16019,16014,16016 })
                Check(CollectibleTable.GeneratorCount(type) > 0 &&
                    !CollectibleTable.AllSpecs(type).Any(s => s.PrototypeId == "ore_iron" && type != 13019), "mineral " + type + " tem loot sem ferro indevido");

            var volcano = MakeWorld(root,"ua60vol",out _);
            var ctx = MakePlayer(root,"survivor",volcano);
            using var link = new EconomyProtocolCheck.Link(ctx,volcano,null); volcano.AddPlayer(link.Player);
            var survival = Field<SurvivalState>(link.Player,"_survival");
            var lava = Enumerable.Range(0,volcano.Biomes.Length).First(i => WorldStatusRules.UnmaskBiome(volcano.Biomes[i]) == Biome.Lava);
            var lavaTile = new Point2(lava % volcano.NumTilesX,lava / volcano.NumTilesX);
            double now = Gauge.CurrentTime;
            int left = lavaTile.x, right = lavaTile.x;
            while (left > 0 && WorldStatusRules.UnmaskBiome(volcano.BiomeAt(new(left,lavaTile.y))) == Biome.Lava) left--;
            while (right < volcano.NumTilesX-1 && WorldStatusRules.UnmaskBiome(volcano.BiomeAt(new(right,lavaTile.y))) == Biome.Lava) right++;
            var from = new WorldPosition(left*200+100,lavaTile.y*200+100);
            var to = new WorldPosition(right*200+100,lavaTile.y*200+100);
            Place(ctx,new(left,lavaTile.y),now-1);
            Call(link.Player,"UpdateLavaExposure",now-1);
            Call(link.Player,"UpdateLavaExposure",now); // server ticks before the client's 0.5s batch arrives
            link.Send(new Move { Movements=new[] { new Movement { PlaybackRate=1,Path=new[] {
                new Location { Position=from,Time=now-.5 },new Location { Position=to,Time=now } } } } });
            link.Request<GetActions,Actions>(default);
            float crossingBefore=survival.ValueAt(SurvivalState.KeyLife,Gauge.CurrentTime);
            Call(link.Player,"UpdateLavaExposure",now);
            Check(survival.ValueAt(SurvivalState.KeyLife,Gauge.CurrentTime)<crossingBefore-1,
                "pacote TCP cruzando lava entre extremos secos aplica dano pelo trajeto preservado");
            Check(Field<Movement[]>(link.Player,"_receivedMovements")[0].Path[0].Position.x==from.x,
                "armazenamento do ultimo ponto nao sobrescreve inicio do pacote recebido");
            crossingBefore=survival.ValueAt(SurvivalState.KeyLife,Gauge.CurrentTime);
            link.Send(new Move { Movements=Field<Movement[]>(link.Player,"_receivedMovements") });
            link.Request<GetActions,Actions>(default); Call(link.Player,"UpdateLavaExposure",now);
            Check(survival.ValueAt(SurvivalState.KeyLife,Gauge.CurrentTime)>=crossingBefore-.01,
                "repetir pacote de movimento nao reaplica dano de lava");
            survival.Set(SurvivalState.KeyLife,300); Call(link.Player,"FlushSurvival");
            now=Gauge.CurrentTime;
            Place(ctx,lavaTile,now-2); Call(link.Player,"UpdateLavaExposure",now-1);
            float before = survival.ValueAt(SurvivalState.KeyLife,now);
            Call(link.Player,"UpdateLavaExposure",now);
            Check(survival.ValueAt(SurvivalState.KeyLife,now) < before-69 && ctx.AppearPlayer.IsAlive,
                "lava do mapa vulcanico aplica 70 de dano por segundo");
            var boots = Cheats.MakeItem("shoes_lavaproof",60).Value; ctx.InventoryItems.Add(boots); ctx.EquippedItems["feet"] = boots.Id;
            before = survival.ValueAt(SurvivalState.KeyLife,now);
            Call(link.Player,"UpdateLavaExposure",now+1);
            Check(survival.ValueAt(SurvivalState.KeyLife,now) >= before-.01, "botas nativas protegem da lava");
            ctx.EquippedItems.Clear();
            survival.Set(SurvivalState.KeyLife,20); Call(link.Player,"FlushSurvival");
            Call(link.Player,"UpdateLavaExposure",now+2);
            Check(!ctx.AppearPlayer.IsAlive && survival.ValueAt(SurvivalState.KeyLife,Gauge.CurrentTime) == 0,
                "lava letal mata e congela a vida em zero");
            int deaths = Field<int>(link.Player,"_deathCount"); Call(link.Player,"UpdateSurvival");
            Check(Field<int>(link.Player,"_deathCount") == deaths, "personagem morto nao recebe morte duplicada");
            Call(link.Player,"HandleReviveMsg",false);
            Check(ctx.AppearPlayer.IsAlive && survival.ValueAt(SurvivalState.KeyLife,Gauge.CurrentTime) > 0, "renascimento recupera vida");
            Place(ctx,volcano.EntryPoint,Gauge.CurrentTime);
            survival.Set(SurvivalState.KeyLife,0);
            link.Request<Collect,Abort>(new Collect { Tile=volcano.EntryPoint, GeneratorId="stone" });
            Check(!ctx.AppearPlayer.IsAlive, "coleta com vida zero dispara morte e e bloqueada");
            Call(link.Player,"HandleReviveMsg",false);
            survival.Set(SurvivalState.KeyHealth,0); Call(link.Player,"FlushSurvival");
            Check(!ctx.AppearPlayer.IsAlive, "saude zero limita a vida a zero e mata imediatamente");
            Call(link.Player,"HandleReviveMsg",false);
            survival.Set(SurvivalState.KeyLife,20);
            survival.SetMomentum("fatal-test",new Dictionary<string,float> { [SurvivalState.KeyLife] = -100 });
            survival.Flush(Gauge.CurrentTime-1); Call(link.Player,"UpdateSurvival");
            Check(!ctx.AppearPlayer.IsAlive, "efeito continuo mata mesmo antes de Tick reenviar barras");
            survival.SetMomentum("fatal-test",null);

            foreach (var region in RegionCatalog.All)
            {
                var world = MakeWorld(root,region.TerrainId,out var state);
                var craters = world.ArtifactManager.Enumerable(a => a.EntityType == 7037).ToArray();
                if (craters.Length == 0) continue;
                var spots = Spots(world);
                foreach (var crater in craters)
                {
                    var types = (ushort[])Call(world,"CraterMineralTypes",crater.Tile);
                    Check(types.Length > 0 && types.All(type => spots.Any(n => Near(n,crater.Tile) &&
                        DataHelper.GetBiomeSpriteInfo(world.GatheringTypeAt(new(n.X,n.Y),n.EntityType))?.CollectibleId ==
                        DataHelper.GetBiomeSpriteInfo(type).CollectibleId)), region.TerrainId + ": crateras possuem spots dos minerais nativos");
                    Check(world.ActivateCrater(crater.EntityId,Gauge.CurrentTime), region.TerrainId + ": inducao funcional");
                    Check(state.CraterResources[crater.EntityId].All(n => types.Contains(n.EntityType)),
                        region.TerrainId + ": inducao produz apenas os minerais da cratera");
                }
                int count = state.AddedNatural.Count;
                SafeSave.FlushPending();
                var restart = new World(WorldContext.Load(state.Path));
                Check(Field<WorldContext>(restart,"_context").AddedNatural.Count == count, region.TerrainId + ": restart nao duplica os spots");
            }
            foreach (var pair in new[] { ("ri35de","stone_obsidian"), ("ri40tr","ore_tin"), ("ua60vol","ore_iron_black") })
                CollectMineral(root,pair.Item1,pair.Item2);
            Check(SafeSave.FlushPending(),"saves temporarios gravados");
            Console.WriteLine($"[volcano-crater] PASS {_checks} verificacoes. Saves temporarios: {root}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { SafeSave.FlushPending(); AppData.BasePath=prior; }
    }

    private static void CollectMineral(string root,string terrain,string prototype)
    {
        var world = MakeWorld(root,terrain,out var state);
        var context = MakePlayer(root,"miner-"+terrain,world);
        using var link = new EconomyProtocolCheck.Link(context,world,null); world.AddPlayer(link.Player);
        var learned = Field<SkillSave>(link.Player,"_skills");
        foreach (var category in SkillDataStore.Skills)
        {
            ((SkillCategorySave)Call(link.Player,"CategoryState",category.Key)).Level=60;
            foreach (var skill in category.Value) learned.Learned[skill.Key] = skill.Value.ToDictionary(p=>p.Key,p=>p.Value.Length);
        }
        var craters = world.ArtifactManager.Enumerable(a=>a.EntityType==7037).ToArray();
        var spot = Spots(world).First(n=>craters.Any(c=>Near(n,c.Tile)) &&
            CollectibleTable.AllSpecs(world.GatheringTypeAt(new(n.X,n.Y),n.EntityType)).Any(s=>s.PrototypeId==prototype));
        var tile = new Point2(spot.X,spot.Y); Place(context,tile,Gauge.CurrentTime);
        string generator = CollectibleTable.AllSpecs(world.GatheringTypeAt(tile,spot.EntityType)).First(s=>s.PrototypeId==prototype).Id;
        var direct = link.Request<GetCollectible,Collectible>(new GetCollectible { Tile=tile });
        Check(direct.Generators.Any(g=>g.Id==generator && g.Enabled),terrain+": consulta direta oferece mineral coletavel");
        var menu = link.Request<Touch,Touched>(new Touch { Tile=tile, EntityType=spot.EntityType,EntityId="mineral" });
        Check(menu.Collectible.Generators.Any(g=>g.Id==generator && g.Enabled),terrain+": clique oferece loot correto");
        string pickaxeId=SingletonDict<string,List<Prototype>>.Instance.Keys.First(id=>PrototypeYaml.GetItemPrototype(id).Tags?.ContainsKey("pickaxe")==true);
        var pickaxe=Cheats.MakeItem(pickaxeId,60).Value; context.InventoryItems.Add(pickaxe);
        var collect=new Collect { Tile=tile,EntityId="mineral",GeneratorId=generator,ToolItemId=pickaxe.Id };
        int before=context.InventoryItems.Count;
        var timer=link.Request<Collect,Messages.Timer>(collect);
        int aborts=link.Messages.OfType<Abort>().Count();
        ReportedGameplayCheck.BufferedArrival(link,context,"Pickaxe_Collect_A");
        Check(Field<IList>(link.Player,"_pendingCollects").Count==1 &&
            link.Messages.OfType<Abort>().Count()==aborts && context.InventoryItems.Count==before,
            terrain+": primeira coleta de mineral nao cancela com chegada atrasada e animacao");
        Call(link.Player,"UpdatePendingCollects",Gauge.CurrentTime+timer.Duration+.1);
        link.PumpUntil(()=>context.InventoryItems.Count>before);
        Check(context.InventoryItems.Skip(before).Any(i=>i.Prototype==prototype && i.Level==world.RegionLevel), terrain+": coleta entrega mineral no nivel da ilha");
        Call(world,"ProcessRegrow",Gauge.CurrentTime+1000);
        Check(world.NaturalTypeAt(tile)==spot.EntityType && world.HarvestedGenerators($"{tile.x},{tile.y}").Count==0,
            terrain+": mineral regenera com o tipo e loot originais");
        timer=link.Request<Collect,Messages.Timer>(collect); before=context.InventoryItems.Count;
        string toolBefore=Json.Write(context.InventoryItems.First(i=>i.Id==pickaxe.Id));
        link.Send(default(Depart)); link.Request<GetSkills,Skills>(default);
        Call(link.Player,"UpdatePendingCollects",Gauge.CurrentTime+timer.Duration+1);
        Check(Field<IList>(link.Player,"_pendingCollects").Count==0 && context.InventoryItems.Count==before &&
            Json.Write(context.InventoryItems.First(i=>i.Id==pickaxe.Id))==toolBefore &&
            world.HarvestedGenerators($"{tile.x},{tile.y}").Count==0,
            terrain+": movimento manual cancela sem gastar ferramenta ou esgotar mineral");
        timer=link.Request<Collect,Messages.Timer>(collect);
        Field<SurvivalState>(link.Player,"_survival").Set(SurvivalState.KeyLife,0); Call(link.Player,"UpdateSurvival");
        Call(link.Player,"UpdatePendingCollects",Gauge.CurrentTime+timer.Duration+1);
        Check(!context.AppearPlayer.IsAlive && Field<IList>(link.Player,"_pendingCollects").Count==0 && context.InventoryItems.Count==before,
            terrain+": morte cancela coleta pendente sem entregar item");
    }
}
