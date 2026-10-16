using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Durango.Online;
using Durango.Utils;
using Messages;
using Newtonsoft.Json.Linq;
using Shared.Region;
using Yaml.Util;

namespace DurangoServerNx;

internal static class AridBasinCheck
{
    public static int Run(string dataDir)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "Durango-arid-" + Guid.NewGuid().ToString("N"));
        string previous = AppData.BasePath;
        int passed = 0;
        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            passed++;
            Console.WriteLine("[arid-check] OK " + description);
        }
        Directory.CreateDirectory(temporary);
        try
        {
            AppData.BasePath = temporary;
            Json.DataDir = dataDir;
            MoCatalog.Load(dataDir); DataStore.Load(dataDir);
            WorkbenchTags.AssetsDir = Path.Combine(dataDir, "assets");
            TerrainLoader.TerrainDir = Path.Combine(dataDir, "terrains");
            RegionCatalog.Load(WorkbenchTags.AssetsDir);
            Check(RegionCatalog.TryGet(AridBasinContent.TerrainId, out var region), "Arid Basin is registered by its unique terrain ID");
            Check(region.Name == "Arid Basin" && region.TerrainId == AridBasinContent.TerrainId, "route carries the custom terrain and display name");
            Check(RegionCatalog.All.Count(r => AridBasinContent.IsTerrain(r.Id)) == 1, "only one Arid Basin route is advertised");
            var template = RegionCatalog.GetTemplate(region.TemplateId);
            Check(template.Id == "ua60deMain01" && template.Level == 60 && template.Biome == Biome.Desert && template.Role == Role.Risky,
                "client-known desert template groups Arid Basin in the level-60 archipelago");
            var context = new WorldContext { TerrainId = region.TerrainId };
            context.Initialize(Path.Combine(temporary, "arid.world"));
            var world = new World(context);
            Check(world.TerrainInfo.global_landmarks?.Length == 1 && world.TerrainInfo.global_landmarks[0].Id == AridBasinContent.MeshLandmarkId,
                "World preserves the visual mesh landmark instead of clearing it");
            Check(world.TerrainInfo.landmarks.Any(l => l.id == AridBasinContent.MeshLandmarkId && l.prefab == AridBasinContent.MeshPrefab),
                "terrain metadata links the actual Unity prefab");
            var wireInfo = JObject.Parse(Json.Write(world.TerrainInfo));
            Check(wireInfo["global_landmarks"] is JArray array && array.Count == 1,
                "HTTP terrain metadata serializes the visual landmark");
            var animals = world.AnimalManager.All;
            Check(animals.Count == 1, "native desert herds are suppressed on this terrain");
            var giga = animals.Single();
            Check(giga.EntityType == 2213 && giga.CombatLevel == 60 && giga.Tile.x == 128 && giga.Tile.y == 128,
                "exactly one level-60 Giganotosaurus spawns at tile 128,128 with the aliased client template");
            Check(Math.Abs(giga.ToMessage().Display.BaseScale - 4f) < .0001 && AnimalTypes.Get(2213).BoundRadius == 240f,
                "spawn scale is four times the T-Rex and its server hit radius scales with the model");
            var arid = TerrainLoader.Load(AridBasinContent.TerrainId);
            Check(arid.Pois.PortPoints.Count == 1 && arid.Pois.PortPoints[0] == new Point2(128, 27),
                "the inline port coordinate is available for arrival and the raft map");
            Check(arid.Pois.Craters.Count == 0 && !world.ArtifactManager.Enumerable(a => a.States.Crack.HasValue).Any(),
                "Arid Basin has no crater POIs or crater structures");
            var other = TerrainLoader.Load("ua60de_alpha");
            Check(!arid.Biomes.SequenceEqual(other.Biomes) && !arid.Ocean.SequenceEqual(other.Ocean),
                "Arid Basin coastline and map layers differ from the existing desert island");
            double now = Gauge.CurrentTime;
            var center = giga.Position;
            var walk = world.AnimalManager.BuildChase(giga, new WorldPosition(center.x + 1280, center.y), 0, now);
            Check(Math.Abs(walk.Movements[0].Path[1].Time - now - 1) < .001,
                "Giga follows its measured 320 cm/s stride at display scale 4");
            var appeared = giga.ToMessage().Move.Movements[0];
            Check(appeared.MotionName == "Giganotosaurus-Move-Fwd" && appeared.Path.Length == 2 &&
                appeared.Path[0].Position.x < appeared.Path[1].Position.x,
                "joining during a walk receives the current position and remaining moving path");
            giga.MakeMotion(AnimalMotions.StandOf(2213), 0, now + 1);
            Check(giga.StopWalkingAt == 0 && giga.WalkEndAt == 0,
                "interrupting a walk clears its outstanding stop timer");
            giga.Position = center;
            var raptor = world.AnimalManager.SpawnAt(2001, 1, new Point2(128, 128));
            float nativeSpeed = AnimalMotions.Of(2001).MoveSpeed * AnimalMotions.Of(2001).MovePlaybackRate * AnimalTypes.Get(2001).BaseScale;
            var nativeWalk = world.AnimalManager.BuildChase(raptor, new WorldPosition(center.x + nativeSpeed * 2, center.y), 0, now);
            Check(Math.Abs(nativeWalk.Movements[0].Path[1].Time - now - 2) < .001,
                "the same native speed/cadence rule applies to the existing raptor");

            var playerContext = new PlayerContext { PlayerInfo = new Durango.Logic.Clusters.PlayerInfo
                { PlayerEntityId = "giga-combat-check", PlayerName = "giga-combat-check", PlayerLevel = 60 } };
            playerContext.Initialize(Path.Combine(temporary, "giga.player"));
            playerContext.AppearPlayer.IsAlive = true;
            void Place(float fromX, float toX, double start, double end) => playerContext.AppearPlayer.Move.Movements = new[] {
                new Movement { MotionName = "Walk", PlaybackRate = 1, Path = new[] {
                    new Location { Position = new WorldPosition(fromX, center.y), Time = start },
                    new Location { Position = new WorldPosition(toX, center.y), Time = end }
                } }
            };
            using var link = new EconomyProtocolCheck.Link(playerContext, world, null, true);
            var player = link.Player;
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var turn = typeof(Player).GetMethod("AnimalTurn", flags);
            var survival = (SurvivalState)typeof(Player).GetField("_survival", flags).GetValue(player);
            giga.AggroTargetId = player.EntityId;
            giga.Attack = 25;
            Place(center.x + 600, center.x + 600, now - 1, now);
            float life = survival.ValueAt(SurvivalState.KeyLife, Gauge.CurrentTime);
            turn.Invoke(player, new object[] { giga, now });
            Check(giga.AttackAt == now + Player.AnimalAttackWarningSeconds,
                "Giga starts its bite at the visible body edge, beyond the former one-tile limit");
            player.ResolveAnimalAttack(giga, now + 2);
            Check(Math.Abs(giga.StandAt - (now + 3.25)) < .001 && Math.Abs(giga.AttackHitAt - (now + 2.65)) < .001,
                "Giga bite impact and recovery follow its actual 1.25-second clip");
            player.ResolveAnimalAttack(giga, now + 2.7);
            Check(survival.ValueAt(SurvivalState.KeyLife, Gauge.CurrentTime) < life,
                "Giga bite really removes player life at the body edge");
            float afterHit = survival.ValueAt(SurvivalState.KeyLife, Gauge.CurrentTime);
            player.ResolveAnimalAttack(giga, now + 3);
            Check(survival.ValueAt(SurvivalState.KeyLife, Gauge.CurrentTime) >= afterHit,
                "a resolved bite cannot apply damage twice");
            Place(center.x + 760, center.x + 760, now + 2.7, now + 2.8);
            turn.Invoke(player, new object[] { giga, now + 2.8 });
            Check(giga.StopWalkingAt == 0 && giga.StandAt > now + 2.8,
                "chasing cannot overwrite the bite clip during its recovery");
            giga.NextAttackAt = giga.StandAt = 0;
            Place(center.x + 1600, center.x + 600, now + 3, now + 4);
            turn.Invoke(player, new object[] { giga, now + 4 });
            Check(giga.AttackAt == now + 6,
                "attack range uses the player's interpolated location rather than the old path origin");
            Place(center.x + 600, center.x + 1200, now + 4, now + 5);
            player.ResolveAnimalAttack(giga, now + 6);
            float beforeMiss = survival.ValueAt(SurvivalState.KeyLife, Gauge.CurrentTime);
            player.ResolveAnimalAttack(giga, now + 6.7);
            Check(survival.ValueAt(SurvivalState.KeyLife, Gauge.CurrentTime) >= beforeMiss,
                "leaving the bite's area during the warning still avoids damage");
            var obsoleteCrater = Cheats.MakeAppearArtifact(new[] { "prop", "7037" }, out _).Value;
            obsoleteCrater.EntityId = "poi_crater_0";
            obsoleteCrater.Tile = new Point2(80, 80);
            world.ArtifactManager.AddArtifact(obsoleteCrater);
            var restored = new World(context);
            Check(!restored.ArtifactManager.Enumerable(a => a.EntityId == "poi_crater_0").Any(),
                "loading an older Arid Basin save removes its obsolete crater");
            Console.WriteLine($"[arid-check] PASS {passed} checks; production saves untouched");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("[arid-check] FAIL " + error);
            return 1;
        }
        finally
        {
            AppData.BasePath = previous;
            Directory.Delete(temporary, true);
        }
    }
}
