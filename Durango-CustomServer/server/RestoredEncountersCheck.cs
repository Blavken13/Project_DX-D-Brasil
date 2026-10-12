using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Durango.Online;
using Durango.Utils;
using Messages;
using Yaml.Util;

namespace DurangoServerNx;

internal static class RestoredEncountersCheck
{
    private static int _checks;
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
        _checks++; Console.WriteLine("[restored-encounters-check] OK " + label);
    }

    public static int Run(string dataDir)
    {
        try
        {
            Json.DataDir = dataDir; MoCatalog.Load(dataDir); DataStore.Load(dataDir);
            WorkbenchTags.AssetsDir = Path.Combine(dataDir, "assets");
            TerrainLoader.TerrainDir = Path.Combine(dataDir, "terrains");
            RegionCatalog.Load(WorkbenchTags.AssetsDir); AnimalTuning.Reload();
            var expected = new Dictionary<string, ushort[]> {
                ["op60te_alpha"] = new ushort[] { 2108, 2112 },
                ["ua60de_alpha"] = new ushort[] { 2125 },
                ["ua60sn_alpha"] = new ushort[] { 2109, 2113 },
                ["ua60tr_alpha"] = new ushort[] { 2115 }
            };
            foreach (var pair in expected)
            {
                var terrain = TerrainLoader.Load(pair.Key);
                var template = RegionCatalog.GetTemplate(terrain.Info.region_template);
                Check(template.RestoredEncounters.Select(e => e.EntityType).ToHashSet().SetEquals(pair.Value),
                    pair.Key + ": catalogo completo");
                // Repeat generation to catch population shuffling accidentally duplicating/omitting elites.
                for (int run = 0; run < 3; run++)
                {
                    var manager = new AnimalManager(terrain, template);
                    Check(manager.Count == AnimalTuning.TargetAnimalsPerRegion &&
                        manager.All.Select(a => a.Tile).Distinct().Count() == manager.Count &&
                        manager.All.All(a => TerrainEcology.IsLand(terrain, a.Tile)), pair.Key + ": populacao e terreno seguros");
                    Check(manager.All.All(a => !(terrain.Pois?.PortPoints.Any(p =>
                        Math.Abs(p.x - a.Tile.x) < 8 && Math.Abs(p.y - a.Tile.y) < 8) ?? false)),
                        pair.Key + ": fora dos portos");
                    foreach (ushort type in pair.Value)
                    {
                        var animal = manager.All.Single(a => a.EntityType == type);
                        var info = AnimalTypes.Get(type);
                        var message = animal.ToMessage();
                        Check(animal.IsRestoredEncounter && animal.CombatLevel == 60 && message.Level == 60 &&
                            message.EntityType == type && !info.Tamable, type + ": alfa nativo nivel 60 sem doma");
                        var motions = AnimalMotions.Of(type);
                        Check(message.Display.BaseScale == info.BaseScale && !string.IsNullOrEmpty(motions?.Stand) &&
                            !string.IsNullOrEmpty(motions?.Dead) && !string.IsNullOrEmpty(motions?.PickAttack(null)),
                            type + ": escala e animacoes originais");
                        float hp = type is 2113 or 2115 ? 200000 : (float)StatFormula.EvalOr(info.LifeMax,
                            new Dictionary<string, double> { ["combat_level"] = 60, ["unstable_factor"] = 1 }, 1);
                        Check(Math.Abs(animal.LifeMax - hp) < .01f && animal.Life == hp &&
                            message.Survival.Life.Max() == hp && message.Survival.Life.Get(Gauge.CurrentTime) == hp,
                            type + ": vida correta no servidor e no pacote");
                        Check(CollectibleTable.AllSpecs(type).Count > 0, type + ": carcaca coletavel");
                        animal.IsAlive = false; animal.Life = 0; animal.DiedAt = 1;
                        AnimalManager.Animal revived = null;
                        manager.Process(1 + AnimalManager.CorpseDisposeDelay, _ => { }, _ => { });
                        Check(animal.CorpseDisposed && !animal.IsAlive, type + ": descarte da carcaca");
                        manager.Process(1 + AnimalManager.RespawnDelay, _ => { }, a => revived = a);
                        Check(revived == animal && animal.IsAlive && animal.Life == hp && animal.IsRestoredEncounter,
                            type + ": respawn conserva atributos");
                    }
                }
            }
            var tropical = TerrainLoader.Load("op60tr_alpha");
            Check(new AnimalManager(tropical, RegionCatalog.GetTemplate(tropical.Info.region_template)).All.Any(a => a.EntityType == 2004),
                "Braquiossauro comum continua disponivel");
            foreach (string path in Directory.GetFiles(TerrainLoader.TerrainDir, "*.zip"))
            {
                string id = Path.GetFileNameWithoutExtension(path);
                if (expected.ContainsKey(id)) continue;
                var terrain = TerrainLoader.Load(id);
                Check(RegionCatalog.GetTemplate(terrain.Info.region_template).RestoredEncounters.Count == 0,
                    id + ": sem encontros adicionais");
            }
            Check(AnimalTypes.Get(2108).BaseScale == .4f, "Compsognathus usa variante Alfa existente");
            Console.WriteLine($"[restored-encounters-check] PASS {_checks} verificacoes");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("[restored-encounters-check] FAIL " + ex); return 1; }
    }
}
