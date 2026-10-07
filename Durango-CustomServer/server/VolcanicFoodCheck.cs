using System.Reflection;
using Durango.Online;
using Durango.Utils;
using Messages;
using Yaml.Util;

namespace DurangoServerNx;

internal static class VolcanicFoodCheck
{
    private static int _checks;
    private static void Check(bool pass, string text)
    {
        if (!pass) throw new InvalidOperationException(text);
        _checks++;
        Console.WriteLine("[volcanic-food-check] OK " + text);
    }

    internal static int Run(string dataDir)
    {
        string root = Path.Combine(Path.GetTempPath(), "Durango-volcanic-food-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Json.DataDir = dataDir; MoCatalog.Load(dataDir); DataStore.Load(dataDir);
            TerrainLoader.TerrainDir = Path.Combine(dataDir, "terrains");
            RegionCatalog.Load(Path.Combine(dataDir, "assets"));
            CheckCurves(root);
            foreach (string terrain in new[] { "ua60vol", "ua60de_alpha", "op60te_alpha", "pe10gr_1" })
                CheckFood(root, terrain);
            Check(SafeSave.FlushPending(), "saves temporários concluídos");
            Console.WriteLine($"[volcanic-food-check] PASS {_checks} verificações");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { SafeSave.FlushPending(); Directory.Delete(root, true); }
    }

    private static PlayerContext Context(string root, string id)
    {
        var context = new PlayerContext { PlayerInfo = new Durango.Logic.Clusters.PlayerInfo
            { PlayerEntityId = id, PlayerName = id, PlayerLevel = 60 } };
        context.Initialize(Path.Combine(root, id + ".player"));
        context.AppearPlayer.Level = 60; context.AppearPlayer.IsAlive = true;
        return context;
    }

    private static void Reset(SurvivalState state, double now, float life = 300, float health = 300)
    {
        state.Set(SurvivalState.KeyLife, life); state.Set(SurvivalState.KeyHealth, health);
        state.Set(SurvivalState.KeyEnergy, 10); state.Set(SurvivalState.KeyStamina, 10);
        state.Flush(now);
    }

    private static void CheckCurves(string root)
    {
        var context = Context(root, "curves");
        var state = new SurvivalState(context, live: true);
        double now = Gauge.CurrentTime;
        state.SetMomentum("storm-test", StatusEffectCatalog.Get("volcanic_storm").GetType1Velocities(1));
        Reset(state, now);
        float health = state.ValueAt(SurvivalState.KeyHealth, now + 10);
        float before = state.ValueAt(SurvivalState.KeyLife, now + 10);
        state.Flush(now + 10); // Comer/recalcular não pode causar outro dano neste instante.
        float after = state.ValueAt(SurvivalState.KeyLife, now + 10);
        Console.WriteLine($"[volcanic-food-check] tempestade após 10s: health={health:0.000}, life antes={before:0.000}, depois={after:0.000}");
        Check(Math.Abs(before - after) < .01f, "recalcular barras durante tempestade preserva vida no mesmo instante");
        Check(Math.Abs(before - health) < .01f, "vida acompanha saúde máxima durante tempestade sem exceder o teto");

        Reset(state, now, life: 100);
        Check(Math.Abs(state.ValueAt(SurvivalState.KeyLife, now + 10) - 110) < .01f,
            "vida abaixo do teto mantém regeneração base antes de alcançar saúde decrescente");
        Check(Math.Abs(state.ValueAt(SurvivalState.KeyLife, now + 25) - state.ValueAt(SurvivalState.KeyHealth, now + 25)) < .01f,
            "após encontro com o teto, vida acompanha redução real da saúde");
        Check(state.ValueAt(SurvivalState.KeyLife, now + 40) == 0, "tempestade mantém dano original até saúde esgotar");

        var baseline = new SurvivalState(Context(root, "baseline"), live: true);
        baseline.SetMomentum("storm-test", StatusEffectCatalog.Get("volcanic_storm").GetType1Velocities(1));
        Reset(baseline, now); Reset(state, now);
        foreach (double seconds in new[] { 1d, 5, 10, 15, 25, 35 })
        {
            state.Add(SurvivalState.KeyEnergy, 20); state.Flush(now + seconds);
            Check(Math.Abs(state.ValueAt(SurvivalState.KeyLife, now + seconds) - baseline.ValueAt(SurvivalState.KeyLife, now + seconds)) < .02f,
                $"restaurar energia aos {seconds}s não acrescenta dano à tempestade");
        }

        state.SetMomentum("storm-test", null);
        state.SetMomentum("recovery-test", new() { [SurvivalState.KeyHealth] = 10 });
        Reset(state, now, life: 100, health: 100);
        Check(Math.Abs(state.ValueAt(SurvivalState.KeyLife, now + 60) - 160) < .01f,
            "recuperação de saúde não acelera a regeneração base da vida");
        state.SetMomentum("recovery-test", new() { [SurvivalState.KeyHealth] = 5, [SurvivalState.KeyLife] = 20 });
        Reset(state, now, life: 80, health: 100);
        Check(Math.Abs(state.ValueAt(SurvivalState.KeyLife, now + 10) - state.ValueAt(SurvivalState.KeyHealth, now + 10)) < .01f,
            "descanso acompanha teto crescente depois de alcançá-lo");
        Check(state.ValueAt(SurvivalState.KeyLife, now + 60) == 300, "descanso continua até teto estático após saúde recuperar");

        state.SetMomentum("recovery-test", null);
        state.SetMomentum("damage-test", new() { [SurvivalState.KeyLife] = -11 });
        Reset(state, now, life: 50);
        Check(state.ValueAt(SurvivalState.KeyLife, now + 10) == 0, "dano direto continua limitado pelo piso de vida");
        state.SetMomentum("damage-test", null);
        state.SetMomentum("energy-test", new() { [SurvivalState.KeyEnergy] = -10 });
        Reset(state, now);
        Check(state.ValueAt(SurvivalState.KeyStamina, now + 2) == 0,
            "stamina também acompanha energia decrescente até o piso");
        float stamina = state.ValueAt(SurvivalState.KeyStamina, now + .5);
        state.Flush(now + .5);
        Check(Math.Abs(state.ValueAt(SurvivalState.KeyStamina, now + .5) - stamina) < .01f,
            "recalcular energia não causa salto na stamina");
        baseline.Freeze(now + 10);
        Check(Math.Abs(baseline.ValueAt(SurvivalState.KeyLife, now + 10) - baseline.ValueAt(SurvivalState.KeyLife, now + 100)) < .01f,
            "desconexão congela vida e saúde durante tempestade");
    }

    private static void CheckFood(string root, string terrain)
    {
        var savedWorld = new WorldContext { TerrainId = terrain };
        savedWorld.Initialize(Path.Combine(root, terrain + ".world"));
        var world = new World(savedWorld);
        var context = Context(root, terrain); context.RegionId = terrain;
        using var link = new EconomyProtocolCheck.Link(context, world, null);
        var state = (SurvivalState)typeof(Player).GetField("_survival", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(link.Player);
        foreach (string weather in terrain == "ua60vol"
            ? new[] { "volcanic_ash", "volcanic_sign", "volcanic_storm" } : new[] { "sunny" })
        {
            link.Player.SyncWeatherStatusEffects(weather);
            // Dez segundos de exposição antes de comer: exercita pelo TCP o
            // salto original quando life já deveria acompanhar health.
            Reset(state, Gauge.CurrentTime - 10);
            foreach (string prototype in new[] { "meatball_01", "meatball_01", "meatball_01" })
            {
                var food = Cheats.MakeItem(prototype, 60).Value;
                context.InventoryItems.Add(food);
                double start = Gauge.CurrentTime;
                var curveBefore = context.AppearPlayer.Survival.Life;
                float energy = state.ValueAt(SurvivalState.KeyEnergy, start);
                link.Request<UseItem, OK>(new UseItem { ItemId = food.Id, Accept = true });
                double end = Gauge.CurrentTime;
                Check(Math.Abs(state.ValueAt(SurvivalState.KeyLife, end) - curveBefore.Get(end)) < .1f,
                    terrain + "/" + weather + ": comer bolinho não acrescenta dano à vida");
                Check(state.ValueAt(SurvivalState.KeyEnergy, end) >= energy && context.InventoryItems.All(i => i.Id != food.Id),
                    terrain + "/" + weather + ": comida restaura energia e é consumida uma vez");
                Check(Enumerable.Range(0, 61).All(seconds =>
                    state.ValueAt(SurvivalState.KeyLife, end + seconds) <= state.MaxOf(SurvivalState.KeyLife, end + seconds) + .01f),
                    terrain + "/" + weather + ": vida respeita saúde máxima ao longo dos próximos 60s");
            }
        }
    }
}
