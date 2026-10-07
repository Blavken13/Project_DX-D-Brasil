using System.Reflection;
using Durango.Online;
using Durango.Utils;
using Messages;
using Yaml.Util;

namespace DurangoServerNx;

internal static class SkillPointsCheck
{
    private static int _checks;
    private static void Check(bool pass, string description)
    {
        if (!pass) throw new InvalidOperationException(description);
        _checks++;
        Console.WriteLine("[sp-check] OK " + description);
    }
    private static object Call(Player player, string name, params object[] args) => typeof(Player)
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player, args);

    internal static int Run(string dataDir)
    {
        string root = Path.Combine(Path.GetTempPath(), "Durango-sp-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Json.DataDir = dataDir; MoCatalog.Load(dataDir); DataStore.Load(dataDir);
            SkillDataStore.EnsureLoaded();
            TerrainLoader.TerrainDir = Path.Combine(dataDir, "terrains");
            RegionCatalog.Load(Path.Combine(dataDir, "assets"));
            var rows = new (int First, int Last, int Base, int Alpha)[]
            {
                (1, 1, 15, 23), (2, 8, 6, 9), (9, 9, 7, 11), (10, 12, 8, 12),
                (13, 18, 9, 14), (19, 19, 10, 15), (20, 22, 11, 17), (23, 28, 12, 18),
                (29, 29, 13, 20), (30, 32, 14, 21), (33, 38, 15, 23), (39, 39, 16, 24),
                (40, 42, 17, 26), (43, 44, 18, 27), (45, 48, 18, 27), (49, 49, 19, 29),
                (50, 52, 20, 30), (53, 58, 21, 32), (59, 59, 22, 33), (60, 60, 23, 35)
            };
            int accumulated = 0;
            foreach (var row in rows)
                for (int level = row.First; level <= row.Last; level++)
                {
                    accumulated += row.Alpha;
                    Check(SkillTuning.OriginalSkillPointsForLevel(level) == row.Base &&
                        SkillTuning.SkillPointsForLevel(level) == row.Alpha &&
                        SkillTuning.TotalSkillPointsForLevel(level) == accumulated,
                        $"nível {level}: base {row.Base}, Alpha {row.Alpha}, total {accumulated}");
                }
            Check(SkillTuning.TotalSkillPointsForLevel(60) > 305, "nível 60 ganha retroativo acima dos 305 SP anteriores");

            var wc = new WorldContext { TerrainId = "pe10gr_1" }; wc.Initialize(Path.Combine(root, "test.world"));
            var world = new World(wc);
            foreach (int level in new[] { 1, 9, 20, 40, 60 })
            {
                string path = Path.Combine(root, "level-" + level + ".player");
                var context = new PlayerContext { PlayerInfo = new Durango.Logic.Clusters.PlayerInfo
                    { PlayerEntityId = "sp-" + level, PlayerName = "Tester", PlayerLevel = level } };
                context.Initialize(path);
                int exp = (int)typeof(Player).GetMethod("ExpForLevel", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { level });
                var legacy = new SkillSave { Exp = exp, UntrainedCount = 3 };
                context.Storage[SkillTuning.StorageKey] = System.Text.Encoding.UTF8.GetBytes(Json.Write(legacy));
                int spent;
                string learned;
                using (var link = new EconomyProtocolCheck.Link(context, world, null))
                {
                    world.AddPlayer(link.Player);
                    var reply = link.Request<GetSkills, Skills>(default);
                    Check(reply.SkillPoint == SkillTuning.TotalSkillPointsForLevel(level),
                        $"personagem antigo nível {level} recebe SP atualizados pelo TCP");
                    if (level == 60)
                    {
                        var category = (SkillCategorySave)Call(link.Player, "CategoryState", (int)Shared.Skill.Category.Constructing);
                        category.Level = 60;
                        link.Request<LearnSkill, OK>(new LearnSkill { SkillId = "cage_domestication", SubId = "__base__", Level = 1 });
                        Call(link.Player, "GrantFreeSkills", true);
                    }
                    spent = (int)Call(link.Player, "UsedSkillPoints");
                    Check((int)Call(link.Player, "RemainSkillPoints") == reply.SkillPoint - spent,
                        "skills aprendidas continuam descontadas do saldo");
                    var save = (SkillSave)typeof(Player).GetField("_skills", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(link.Player);
                    learned = Json.Write(save.Learned);
                    Check(save.Exp == exp && save.UntrainedCount == 3, "retroativo preserva EXP e histórico de desaprendizado");
                    Call(link.Player, "SaveSkillState"); context.Save();
                }
                Check(SafeSave.FlushPending(), "save temporário concluído");
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    var persisted = PlayerContext.Load(path);
                    using var reconnect = new EconomyProtocolCheck.Link(persisted, world, null);
                    var reply = reconnect.Request<GetSkills, Skills>(default);
                    Check(reply.SkillPoint == SkillTuning.TotalSkillPointsForLevel(level) &&
                        (int)Call(reconnect.Player, "RemainSkillPoints") == reply.SkillPoint - spent,
                        "reconexão mantém saldo sem duplicar retroativo");
                    var save = (SkillSave)typeof(Player).GetField("_skills", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(reconnect.Player);
                    Check(Json.Write(save.Learned) == learned && save.Exp == exp && save.UntrainedCount == 3,
                        "reconexão preserva aprendizado, EXP e histórico");
                }
            }
            Console.WriteLine($"[sp-check] PASS {_checks} verificações");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { SafeSave.FlushPending(); Directory.Delete(root, true); }
    }
}
