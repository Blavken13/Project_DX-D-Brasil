using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Durango.Online;
using Durango.Utils;
using Messages;
using Shared.Battle;
using Shared.Item;
using Shared.Skill;
using Yaml;
using Yaml.Util;

namespace DurangoServerNx;

internal static class ProgressionRegressionCheck
{
    private static int _passed;
    private static object Call(object target, string name, params object[] args) => target.GetType()
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    private static T Field<T>(object target, string name) => (T)target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void Check(bool value, string text)
    { if (!value) throw new InvalidOperationException(text); _passed++; Console.WriteLine("[progression-check] OK " + text); }
    private static int DefenseProgress(SkillCategorySave state) => state.Exp +
        SkillDataStore.Categories[(int)Category.Defense].ExpNeeded.Where(p => p.Key < state.Level).Sum(p => p.Value);

    public static int Run(string dataDir)
    {
        string root = Path.Combine(Path.GetTempPath(), "Durango-progression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Json.DataDir = dataDir; MoCatalog.Load(dataDir); DataStore.Load(dataDir);
            WorkbenchTags.AssetsDir = Path.Combine(dataDir, "assets");
            TerrainLoader.TerrainDir = Path.Combine(dataDir, "terrains"); RegionCatalog.Load(Path.Combine(dataDir, "assets"));
            var wc = new WorldContext { TerrainId = "pe10gr_1" }; wc.Initialize(Path.Combine(root, "test.world"));
            var world = new World(wc);
            var context = new PlayerContext { PlayerInfo = new Durango.Logic.Clusters.PlayerInfo
                { PlayerEntityId = "progression-tester", PlayerName = "Tester", PlayerLevel = 1 } };
            string savePath = Path.Combine(root, "tester.player"); context.Initialize(savePath);
            using (var link = new EconomyProtocolCheck.Link(context, world, null))
            {
                var player = link.Player; world.AddPlayer(player); player.ContextChanged += context.Save;
                var skills = Field<SkillSave>(player, "_skills");
                int level60 = (int)typeof(Player).GetMethod("ExpForLevel", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { 60 });
                player.AddExp(level60 - skills.Exp, "cheat");
                var construction = (SkillCategorySave)Call(player, "CategoryState", (int)Category.Constructing);
                var weapons = (SkillCategorySave)Call(player, "CategoryState", (int)Category.Weaponcrafting);
                construction.Level = weapons.Level = 60;
                Call(player, "SaveSkillState");
                Check(player.UnlockedBlueprintIds().Contains("fur_table") && !player.UnlockedBlueprintIds().Contains("fur_table_03"),
                    "nivel alto sozinho nao substitui aprendizado da mesa superior");
                for (int stage = 2; stage <= 4; stage++)
                {
                    int before = link.Messages.OfType<ArtifactBlueprints>().Count();
                    link.Request<LearnSkill, OK>(new LearnSkill { SkillId = "worktable", SubId = "__base__", Level = stage });
                    link.PumpUntil(() => link.Messages.OfType<ArtifactBlueprints>().Count() > before);
                    string id = "fur_table_0" + (stage - 1);
                    Check(link.Messages.OfType<ArtifactBlueprints>().Last().Ids.Contains(id),
                        "aprendizado publica planta imediatamente: " + id);
                }
                string[] harpoons = { "harpoon_wooden_01", "harpoon_bone_01", "harpoon_metal_01" };
                for (int stage = 1; stage <= 3; stage++)
                {
                    int before = link.Messages.OfType<Recipes>().Count();
                    link.Request<LearnSkill, OK>(new LearnSkill { SkillId = "harpoon", SubId = "__base__", Level = stage });
                    link.PumpUntil(() => link.Messages.OfType<Recipes>().Count() > before);
                    Check(link.Messages.OfType<Recipes>().Last().Ids.Contains(harpoons[stage - 1]),
                        "aprendizado publica receita superior: " + harpoons[stage - 1]);
                }
                link.Request<UntrainSkill, OK>(new UntrainSkill { SkillId = "harpoon", SubId = "__base__", Level = 3 });
                link.PumpUntil(() => !link.Messages.OfType<Recipes>().Last().Ids.Contains(harpoons[2]));
                Check(!player.UnlockedRecipeIds().Contains(harpoons[2]), "retirar habilidade tambem atualiza a lista de receitas");
                foreach (var sample in new[] { ("blade_stone", 19), ("blade_sword_stone_01", 39),
                    ("harpoon_wooden_01", 24), ("harpoon_bone_01", 44), ("harpoon_metal_01", 60) })
                {
                    var recipe = CraftRecipeStore.Get(sample.Item1);
                    var materials = new List<Item> { new() { Level = 60, Tags = Array.Empty<Tag>() } };
                    int resultLevel = (int)typeof(Player).GetMethod("ProductLevel", BindingFlags.Static | BindingFlags.NonPublic)
                        .Invoke(null, new object[] { recipe, materials });
                    var products = (Item[])typeof(Player).GetMethod("MakeProducts", BindingFlags.Static | BindingFlags.NonPublic)
                        .Invoke(null, new object[] { recipe, new Dictionary<string, string[]>(), materials, resultLevel });
                    Check(products.Length > 0 && products.All(p => p.Level == sample.Item2),
                        "produto respeita tier original: " + sample.Item1);
                    if (sample.Item1.StartsWith("harpoon"))
                        Check(products.All(p => p.Durability?.Max() > 1), "arpao mantem durabilidade funcional: " + sample.Item1);
                }
                foreach (var sample in new[] { ("fur_table", 19), ("fur_table_01", 39), ("fur_table_02", 59), ("fur_table_03", 60) })
                {
                    var blueprint = BlueprintStore.GetBlueprint(sample.Item1);
                    Check(blueprint.MaxLevel == sample.Item2 && WorkbenchTags.Of(blueprint.EntityType)
                        .Any(t => t.Id == "table" && t.Level == sample.Item2), "bancada carrega capacidade nativa: " + sample.Item1);
                }
                var bench = Cheats.MakeAppearArtifact(new[] { "prop", BlueprintStore.GetBlueprint("fur_table_03").EntityType.ToString() }, out _).Value;
                bench.Tile = world.EntryPoint; bench.FounderEntityId = context.EntityId;
                world.ConstructArtifact(bench, null, context.EntityId);
                object[] arguments = { new CraftRecipeData { workbench_tags = new() { ["table"] = 40 } },
                    new PropKey { EntityId = bench.EntityId, Tile = bench.Tile }, null };
                Check((bool)Call(player, "CheckWorkbench", arguments), "bancada superior aceita requisito acima de nivel 19");

                var defense = (SkillCategorySave)Call(player, "CategoryState", (int)Category.Defense);
                var survival = Field<SurvivalState>(player, "_survival");
                var animal = world.AnimalManager.SpawnAt(2027, 1, world.EntryPoint);
                animal.AggroTargetId = context.EntityId; animal.Attack = 2;
                var position = context.AppearPlayer.Move.Movements[0].Path[0].Position;
                animal.Position = position; animal.AttackTargetPosition = position;
                int initial = DefenseProgress(defense);
                void Impact(double at)
                { animal.AttackHitAt = at; player.ResolveAnimalAttack(animal, at); }
                player.ResolveAnimalAttack(animal, Gauge.CurrentTime);
                Check(DefenseProgress(defense) == initial && skills.DefenseExpRemainder == 0, "ficar em combate sem impacto nao concede Defesa");
                var actions = link.Request<GetActions, Actions>(default);
                Check(actions.BattleActions.Any(a => a.Id == "barehand_dodge"), "esquiva nativa disponivel sem arma");
                link.Send(new UseBattleAction { ActionId = "barehand_dodge", StartAt = 1 });
                link.PumpUntil(() => Field<double>(player, "_defenseActiveUntil") > Gauge.CurrentTime);
                double until = Field<double>(player, "_defenseActiveUntil");
                Check(DefenseProgress(defense) == initial, "usar esquiva sem ataque nao permite farmar experiencia");
                float life = survival.ValueAt(SurvivalState.KeyLife, Gauge.CurrentTime);
                int beforeSkills = link.Messages.OfType<Skills>().Count();
                Impact(Gauge.CurrentTime);
                Check(DefenseProgress(defense) == initial + 2 && survival.ValueAt(SurvivalState.KeyLife, Gauge.CurrentTime) >= life,
                    "esquiva dentro da janela original evita dano e concede fator 2 de Defesa");
                link.PumpUntil(() => link.Messages.OfType<Damaged>().Any(d => d.VictimId == context.EntityId && d.Damage.Result == DamageResult.Dodged));
                link.PumpUntil(() => link.Messages.OfType<Skills>().Count() > beforeSkills);
                Check(link.Messages.OfType<Skills>().Last().Categories[Category.Defense].Exp == defense.Exp &&
                    link.Messages.OfType<Skills>().Last().Categories[Category.Defense].Level == defense.Level,
                    "barra de Defesa recebe snapshot de EXP atualizado durante a sessao");
                int afterDodge = DefenseProgress(defense);
                Impact(Gauge.CurrentTime);
                Check(DefenseProgress(defense) == afterDodge, "multiplos impactos evitados na mesma acao nao repetem recompensa");
                link.Send(new UseBattleAction { ActionId = "barehand_dodge" });
                link.Request<GetActions, Actions>(default);
                Check(Field<double>(player, "_defenseActiveUntil") == until && DefenseProgress(defense) == afterDodge,
                    "cooldown impede renovar protecao ou recompensa por repeticao");
                Impact(until + .01);
                Check(survival.ValueAt(SurvivalState.KeyLife, Gauge.CurrentTime) < life && skills.DefenseExpRemainder > 0,
                    "fora da janela golpe volta a causar dano e acumula fracao de EXP passiva");
                for (int i = 0; i < 9; i++) Impact(until + .02 + i * .01);
                Check(DefenseProgress(defense) == afterDodge + 1 && skills.DefenseExpRemainder < 1e-6,
                    "coeficiente hit_factor 0.1 acumula sem perder fracao ou duplicar impactos");
                Impact(until + 1);
                double fraction = skills.DefenseExpRemainder;
                context.Save(); Check(SafeSave.FlushPending(), "EXP e fracao passiva gravadas no save");
                var persisted = Json.Read<PlayerContext>(File.ReadAllText(savePath)); persisted.Initialize(savePath);
                using (var reconnected = new EconomyProtocolCheck.Link(persisted, world, null))
                {
                    var saved = Field<SkillSave>(reconnected.Player, "_skills");
                    Check(saved.Categories[(int)Category.Defense].Exp == defense.Exp && Math.Abs(saved.DefenseExpRemainder - fraction) < 1e-6,
                        "reconexao preserva experiencia e progresso fracionado de Defesa");
                    Check(Field<double>(reconnected.Player, "_defenseActiveUntil") == 0, "reconexao nao restaura protecao temporaria da esquiva");
                }
                defense.Level = 20; defense.ResearchStart = Gauge.CurrentTime; defense.ResearchEnd = defense.ResearchStart + 100;
                int researchExp = defense.Exp; double end = defense.ResearchEnd;
                Call(player, "RecordDefenseExperience", 2d);
                Check(defense.ResearchEnd < end && defense.Exp == researchExp,
                    "EXP de Defesa durante pesquisa reduz tempo segundo regras normais de habilidades");
                Check(link.Messages.OfType<SkillCategoryExperienced>().Any(e => e.Category == Category.Defense),
                    "cliente recebe notificacao nativa de experiencia de Defesa");
            }
            Console.WriteLine($"[progression-check] PASS {_passed} verificacoes"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { SafeSave.FlushPending(); Directory.Delete(root, true); }
    }
}
