using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Durango.Online;
using Durango.Utils;
using Messages;
using Newtonsoft.Json.Linq;
using Shared.Skill;
using Yaml;
using Yaml.Util;

namespace DurangoServerNx;

internal static class CombatSkillsCheck
{
    private static object Call(Player player, string name, params object[] args) => typeof(Player)
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player, args);

    internal static void Run(string root, World world, Action<bool, string> check)
    {
        var context = new PlayerContext { PlayerInfo = new Durango.Logic.Clusters.PlayerInfo
            { PlayerEntityId = "combat-skills-tester", PlayerName = "Combat tester", PlayerLevel = 60 } };
        context.Initialize(Path.Combine(root, "combat-skills.player"));
        context.AppearPlayer.IsAlive = true;
        using (var link = new EconomyProtocolCheck.Link(context, world, null))
        {
            var player = link.Player;
            var save = (SkillSave)typeof(Player).GetField("_skills", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(player);
            save.Exp = (int)typeof(Player).GetMethod("ExpForLevel", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { 60 });
            foreach (var category in new[] { Category.MeleeCombat, Category.RangedCombat, Category.Defense })
                ((SkillCategorySave)Call(player, "CategoryState", (int)category)).Level = 60;
            Call(player, "SaveSkillState");

            HashSet<string> Actions() => link.Request<GetActions, Actions>(default).BattleActions
                .Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
            void Equip(Item? item)
            {
                string slot = item.HasValue ? PerformanceYaml.GetWeapon(item.Value.Prototype).Slot : "main";
                bool changed = item.HasValue ? context.EquippedItems.GetValueOrDefault(slot) != item.Value.Id
                    : context.EquippedItems.ContainsKey(slot);
                int before = link.Messages.OfType<Actions>().Count();
                link.Request<Equip, Equipments>(new Equip { Action = item.HasValue ? "equip" : "unequip",
                    SlotName = slot, ItemId = item?.Id });
                if (changed) link.PumpUntil(() => link.Messages.OfType<Actions>().Count() > before);
            }
            var allowed = Json.ReadFromFile<JObject>("tag_allow_actions");
            var weapons = new Dictionary<string, Item>();
            foreach (var pair in allowed.Properties().Where(p => p.Name != "bare_hands"))
            {
                string prototype = SingletonDict<string, List<Prototype>>.Instance.Keys.First(k =>
                    PrototypeYaml.GetItemPrototype(k).Tags?.ContainsKey(pair.Name) == true && PerformanceYaml.GetWeapon(k) != null);
                var weapon = Cheats.MakeItem(prototype, 60).Value;
                context.InventoryItems.Add(weapon); weapons[pair.Name] = weapon;
                Equip(weapon);
                var actions = link.Messages.OfType<Actions>().Last().BattleActions.Select(a => a.Id).ToHashSet();
                check(pair.Value["default_actions"].Values<string>().All(actions.Contains), pair.Name + ": ataques basicos disponiveis");
                check(!actions.Any(a => pair.Value["skill_actions"].Values<string>().Contains(a) &&
                    a != "onehand_dodge" && a != "twohand_dodge"), pair.Name + ": habilidades pagas nao aprendidas bloqueadas");
            }
            Equip(weapons["sword_onehand"]);
            void Learn(string id, int level = 1)
            {
                int before = link.Messages.OfType<Actions>().Count();
                link.Request<LearnSkill, OK>(new LearnSkill { SkillId = id, SubId = "__base__", Level = level });
                // Algumas habilidades de outras armas nao alteram a lista atual.
                if (id == "onehanded_smash" && level == 1)
                    link.PumpUntil(() => link.Messages.OfType<Actions>().Count() > before);
            }
            double stamina = ((SurvivalState)typeof(Player).GetField("_survival", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(player)).ValueAt(SurvivalState.KeyStamina, Gauge.CurrentTime);
            Call(player, "HandleUseBattleActionMsg", new UseBattleAction { ActionId = "onehand_smash" });
            var cooldowns = (Dictionary<string, double>)typeof(Player).GetField("_battleActionReadyAt", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(player);
            check(!cooldowns.ContainsKey("onehand_smash") && !((bool)typeof(Player).GetField("_inBattle", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(player)), "pedido forjado de habilidade nao aprendida nao inicia combate nem cooldown");
            var survival = (SurvivalState)typeof(Player).GetField("_survival", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(player);
            check(Math.Abs(survival.ValueAt(SurvivalState.KeyStamina, Gauge.CurrentTime) - stamina) < .01,
                "habilidade nao aprendida nao consome stamina");
            Learn("onehanded_smash");
            check(link.Messages.OfType<Actions>().Last().BattleActions.Any(a => a.Id == "onehand_smash") &&
                !Actions().Contains("twohand_smash"), "aprender atualiza barra imediatamente apenas para arma compativel");
            Learn("onehanded_smash", 2);
            link.Request<UntrainSkill, OK>(new UntrainSkill { SkillId = "onehanded_smash", SubId = "__base__", Level = 2 });
            check(Actions().Contains("onehand_smash"), "remover melhoria conserva acao do primeiro nivel");
            Learn("twohanded_smash"); Learn("quick_shot"); Learn("lance_strike"); Learn("tackle");
            Equip(weapons["axe_twohand"]);
            var twohand = link.Messages.OfType<Actions>().Last().BattleActions.Select(a => a.Id).ToHashSet();
            check(twohand.Contains("twohand_smash_axe") && twohand.Contains("melee_tackle") && !twohand.Contains("onehand_smash"),
                "trocar arma substitui imediatamente a variante aprendida e conserva tackle universal: " + string.Join(",", twohand));
            Call(player, "HandleUseBattleActionMsg", new UseBattleAction { ActionId = "onehand_smash" });
            check(!cooldowns.ContainsKey("onehand_smash"), "acao da arma anterior rejeitada mesmo com pedido antigo");
            Equip(weapons["bow"]);
            check(Actions().Contains("ranged_bow_quickshot") && !Actions().Contains("ranged_bow_aimedshot"),
                "arco recebe tiro aprendido mas nao tiro avancado sem aprendizado");
            Equip(weapons["crossbow"]);
            check(Actions().Contains("ranged_crossbow_quickshot") && !Actions().Contains("ranged_bow_quickshot"),
                "besta usa variante propria da mesma recompensa de tiro");
            Equip(weapons["lance_twohand"]);
            check(Actions().Contains("twohand_lance_strike") && !Actions().Contains("twohand_smash"),
                "lanca recebe apenas as acoes compativeis aprendidas");
            Equip(weapons["sword_onehand"]);
            Call(player, "HandleUseBattleActionMsg", new UseBattleAction { ActionId = "onehand_smash" });
            check(cooldowns.ContainsKey("onehand_smash"), "acao aprendida e compativel pode ser executada");
            double ready = cooldowns["onehand_smash"];
            Equip(weapons["axe_onehand"]);
            Equip(weapons["sword_onehand"]);
            check(cooldowns["onehand_smash"] == ready, "trocar arma conserva cooldown existente");
            int updates = link.Messages.OfType<Actions>().Count();
            link.Request<UntrainSkill, OK>(new UntrainSkill { SkillId = "onehanded_smash", SubId = "__base__", Level = 1 });
            link.PumpUntil(() => link.Messages.OfType<Actions>().Count() > updates);
            check(!link.Messages.OfType<Actions>().Last().BattleActions.Any(a => a.Id == "onehand_smash"),
                "desaprender remove acao da barra imediatamente");
            cooldowns.Remove("onehand_smash");
            Call(player, "HandleUseBattleActionMsg", new UseBattleAction { ActionId = "onehand_smash" });
            check(!cooldowns.ContainsKey("onehand_smash"), "desaprender revoga execucao no servidor");
            foreach (string id in new[] { "onehanded_smash", "onehanded_flurry", "onehanded_stab", "twohanded_sweeping",
                "twohanded_strike", "lance_dash", "aimed_shot" }) Learn(id);
            foreach (var pair in weapons)
            {
                Equip(pair.Value);
                var expected = allowed[pair.Key]["default_actions"].Values<string>()
                    .Concat(allowed[pair.Key]["skill_actions"].Values<string>()).ToHashSet();
                check(Actions().SetEquals(expected), pair.Key + ": todas as habilidades aprendidas usam somente variantes compativeis");
            }
            Equip(weapons["sword_onehand"]);
            link.Request<UntrainSkill, OK>(new UntrainSkill { SkillId = "onehanded_smash", SubId = "__base__", Level = 1 });
            context.EquippedItems["sub"] = weapons["blunt_onehand"].Id;
            int inventoryCount = context.InventoryItems.Count;
            Equip(weapons["axe_twohand"]);
            check(!context.EquippedItems.ContainsKey("main") && !context.EquippedItems.ContainsKey("sub") &&
                context.InventoryItems.Count == inventoryCount, "arma de duas maos desocupa ambas as maos sem perder itens");
            Equip(weapons["sword_onehand"]);
            Equip(null);
            check(Actions().Contains("barehand_default_a") && !Actions().Contains("barehand_combination"),
                "sem arma conserva ataque basico mas nao concede combo avancado");
            context.EquippedItems["main"] = weapons["sword_onehand"].Id;
            context.EquippedItems["both"] = weapons["axe_twohand"].Id;
        }
        using (var reconnected = new EconomyProtocolCheck.Link(context, world, null))
        {
            var actions = reconnected.Request<GetActions, Actions>(default).BattleActions;
            check(actions.Any(a => a.Id == "melee_tackle") && !actions.Any(a => a.Id == "onehand_smash"),
                "reconectar restaura habilidades salvas sem conceder habilidade desaprendida");
            check(context.EquippedItems.ContainsKey("main") && !context.EquippedItems.ContainsKey("both") &&
                !actions.Any(a => a.Id.StartsWith("twohand_", StringComparison.Ordinal)),
                "save antigo com armas conflitantes conserva main como cliente original sem misturar acoes");
        }
    }
}
