using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Durango.Online;
using Durango.Utils;
using Messages;
using Yaml.Util;

namespace DurangoServerNx;

internal static class SurvivalContinuityCheck
{
    private static int _checks;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        _checks++; Console.WriteLine("[survival-continuity-check] OK " + label);
    }
    internal static SurvivalState State(Player player) => (SurvivalState)typeof(Player)
        .GetField("_survival", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(player);
    private static float Initial(Gauge gauge) => gauge.Get(gauge.Determination[0].Time);
    internal static float[] Snapshot(PlayerContext context) => new[]
    {
        Initial(context.AppearPlayer.Survival.Life),
        Initial(context.AppearPlayer.Survival.Gauges[SurvivalState.KeyHealth]),
        Initial(context.AppearPlayer.Survival.Gauges[SurvivalState.KeyStamina]),
        Initial(context.AppearPlayer.Survival.Gauges[SurvivalState.KeyEnergy]),
        context.AppearPlayer.Survival.Gauges[SurvivalState.KeyHealth].Max(),
        context.AppearPlayer.Survival.Gauges[SurvivalState.KeyEnergy].Max()
    };
    internal static float[] Prepare(Player player, PlayerContext context)
    {
        SurvivalState state = State(player); double now = Gauge.CurrentTime;
        float health = state.MaxOf(SurvivalState.KeyHealth, now);
        float energy = state.MaxOf(SurvivalState.KeyEnergy, now);
        state.Set(SurvivalState.KeyHealth, health - 10);
        state.Set(SurvivalState.KeyLife, health - 40);
        state.Set(SurvivalState.KeyEnergy, energy - 10);
        state.Set(SurvivalState.KeyStamina, energy - 30);
        state.Freeze(now);
        return Snapshot(context);
    }
    internal static bool Matches(PlayerContext context, float[] values) => Snapshot(context)
        .Zip(values, (actual, expected) => Math.Abs(actual - expected) < .05f).All(v => v);

    private static World World(string root, string terrain)
    {
        var wc = new WorldContext { TerrainId = terrain };
        wc.Initialize(Path.Combine(root, terrain + ".world")); return new World(wc);
    }
    private static PlayerContext Context(string root)
    {
        var context = new PlayerContext { RegionId = "ri35te", PlayerInfo = new Durango.Logic.Clusters.PlayerInfo
            { PlayerEntityId = "continuity", PlayerName = "continuity", PlayerLevel = 60 } };
        context.Initialize(Path.Combine(root, "continuity.player"));
        context.AppearPlayer.IsAlive = true;
        SkillDataStore.EnsureLoaded();
        int exp = (int)typeof(Player).GetMethod("ExpForLevel", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { 60 });
        context.Storage[SkillTuning.StorageKey] = Json.WriteToBytes(new SkillSave { Exp = exp });
        return context;
    }
    private static void CheckGaugeRestoration(string root)
    {
        var fresh = new PlayerContext(); fresh.Initialize(Path.Combine(root, "fresh.player"));
        Check(fresh.AppearPlayer.Survival.Life.Get() == 300 &&
              fresh.AppearPlayer.Survival.Gauges[SurvivalState.KeyStamina].Get() == 100,
            "personagem novo mantem os valores iniciais do jogo");
        var state = new SurvivalState(fresh, live: false); double now = Gauge.CurrentTime;
        state.SetMaxBonus(SurvivalState.KeyHealth, 400, now);
        state.SetMaxBonus(SurvivalState.KeyEnergy, 200, now);
        state.Set(SurvivalState.KeyHealth, 480); state.Set(SurvivalState.KeyLife, 420);
        state.Set(SurvivalState.KeyEnergy, 250); state.Set(SurvivalState.KeyStamina, 180);
        state.Flush(now);
        var before = Snapshot(fresh);
        var reloaded = Json.Read<PlayerContext>(Json.Write(fresh)); reloaded.Initialize(fresh.Path);
        Check(Matches(reloaded, before), "save antigo com limites ampliados recupera bonus sem novos campos");
        state = new SurvivalState(reloaded, live: false);
        Check(!state.SetMaxBonus(SurvivalState.KeyHealth, 400, now) && Matches(reloaded, before),
            "reaplicar o mesmo bonus nao cura nem duplica os valores");
        Check(state.SetMaxBonus(SurvivalState.KeyHealth, 420, now) &&
              Math.Abs(state.ValueAt(SurvivalState.KeyHealth, now) - 500) < .01f,
            "aumento real do bonus acrescenta somente a diferenca");
        state.Set(SurvivalState.KeyLife, 173); state.Set(SurvivalState.KeyHealth, 231);
        state.Set(SurvivalState.KeyStamina, 21); state.Set(SurvivalState.KeyEnergy, 64); state.Flush(now);
        before = Snapshot(reloaded); SurvivalState.Reset(reloaded);
        Check(Matches(reloaded, before), "barras danificadas abaixo de 300 e 100 nao recebem cura na recarga");
        reloaded.AppearPlayer.IsAlive = false;
        state = new SurvivalState(reloaded, live: false); state.Set(SurvivalState.KeyLife, 0); state.Flush(now);
        var dead = Json.Read<PlayerContext>(Json.Write(reloaded)); dead.Initialize(reloaded.Path);
        Check(!dead.AppearPlayer.IsAlive && dead.AppearPlayer.Survival.Life.Get() == 0,
            "recarga de personagem morto nao revive nem preenche vida");
    }
    internal static int Run(string dataDir)
    {
        string root = Path.Combine(Path.GetTempPath(), "Durango-survival-continuity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Json.DataDir = dataDir; MoCatalog.Load(dataDir); DataStore.Load(dataDir);
            TerrainLoader.TerrainDir = Path.Combine(dataDir, "terrains");
            RegionCatalog.Load(Path.Combine(dataDir, "assets"));
            CheckGaugeRestoration(root);
            var world = World(root, "ri35te"); var context = Context(root); float[] before;
            using (var link = new EconomyProtocolCheck.Link(context, world, null))
            {
                before = Prepare(link.Player, context);
                Console.WriteLine($"[survival-continuity-check] antes: vida={before[0]}, saude={before[1]}, vitalidade={before[2]}, energia={before[3]}");
                Check(before[0] > 300 && before[2] > 100, "cenario acima dos limites basicos");
            }
            using (var link = new EconomyProtocolCheck.Link(context, world, null))
            {
                Console.WriteLine("[survival-continuity-check] depois: " + string.Join(", ", Snapshot(context)));
                Check(Matches(context, before), "reconexao em memoria preserva valores e limites");
                var packet = link.Messages.OfType<AppearPlayer>().First();
                Check(Math.Abs(Initial(packet.Survival.Life) - before[0]) < .01f &&
                      Math.Abs(Initial(packet.Survival.Gauges[SurvivalState.KeyStamina]) - before[2]) < .01f,
                    "primeiro AppearPlayer entrega vida e vitalidade preservadas pelo TCP");
                Prepare(link.Player, context);
            }
            before = Snapshot(context); context.Save(); SafeSave.FlushPending();
            var loaded = PlayerContext.Load(context.Path);
            Check(Matches(loaded, before), "recarga do save preserva valores antes de criar conexao");
            using (var link = new EconomyProtocolCheck.Link(loaded, world, null))
            {
                Check(Matches(loaded, before), "reconexao apos recarga preserva valores");
                before = Prepare(link.Player, loaded);
                link.Request<TravelByRegionInArchipelago, OK>(new TravelByRegionInArchipelago { RegionId = "ri55tu" });
                link.PumpUntil(() => link.Messages.OfType<Emigrated>().Any());
                Check(loaded.RegionId == "ri55tu" && Matches(loaded, before), "partida para outra ilha preserva barras");
            }
            using (var link = new EconomyProtocolCheck.Link(loaded, World(root, "ri55tu"), null))
                Check(Matches(loaded, before), "entrada na ilha de destino preserva barras");
            Check(SafeSave.FlushPending(), "saves temporarios concluidos");
            Console.WriteLine($"[survival-continuity-check] PASS {_checks} verificacoes"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { SafeSave.FlushPending(); Directory.Delete(root, true); }
    }
}
