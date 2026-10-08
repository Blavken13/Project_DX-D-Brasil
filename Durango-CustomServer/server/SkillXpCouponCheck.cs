using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Durango.Online;
using Durango.Utils;
using Messages;
using Shared.Skill;
using Yaml;
using Yaml.Util;

namespace DurangoServerNx;

internal static class SkillXpCouponCheck
{
    private static int _passed;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _passed++;
        Console.WriteLine("[xp-coupon-check] OK " + message);
    }
    private static T Field<T>(Player player, string name) => (T)typeof(Player)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(player);
    private static void RejectGlobal<T>(EconomyProtocolCheck.Link link, T message)
    {
        int before = link.Messages.OfType<Abort>().Count();
        link.Send(message);
        link.PumpUntil(() => link.Messages.OfType<Abort>().Count() > before);
    }

    public static int Run(string dataDir)
    {
        _passed = 0;
        string root = Path.Combine(Path.GetTempPath(), "Durango-xp-coupons-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Json.DataDir = dataDir; MoCatalog.Load(dataDir); DataStore.Load(dataDir);
            TerrainLoader.TerrainDir = Path.Combine(dataDir, "terrains");
            WorkbenchTags.AssetsDir = Path.Combine(dataDir, "assets");
            RegionCatalog.Load(Path.Combine(dataDir, "assets"));
            var wc = new WorldContext { TerrainId = "pe10gr_1" };
            wc.Initialize(Path.Combine(root, "coupon.world"));
            var world = new World(wc);
            var economy = new EconomyStore(Path.Combine(root, "economy.json"), new ShopCatalog());
            var context = new PlayerContext { PlayerInfo = new Durango.Logic.Clusters.PlayerInfo
                { PlayerEntityId = "xp-coupon-tester", PlayerName = "Cupons", PlayerLevel = 1 } };
            string savePath = Path.Combine(root, "coupon.player");
            context.Initialize(savePath);
            context.TStone = 100000;
            using var link = new EconomyProtocolCheck.Link(context, world, economy);
            link.Player.ContextChanged += context.Save;
            var skills = Field<SkillSave>(link.Player, "_skills");
            var initialItems = context.InventoryItems.Select(i => i.Id).OrderBy(id => id).ToArray();

            var expected = new Dictionary<string, Category>
            {
                ["bonus_xp_sobrevivencia"] = Category.Survival,
                ["bonus_xp_combate_corpo_a_corpo"] = Category.MeleeCombat,
                ["bonus_xp_combate_a_distancia"] = Category.RangedCombat,
                ["bonus_xp_defesa"] = Category.Defense,
                ["bonus_xp_abate"] = Category.Butchery,
                ["bonus_xp_coleta"] = Category.Gathering,
                ["bonus_xp_culinaria"] = Category.Cooking,
                ["bonus_xp_fabricacao_de_armas"] = Category.Weaponcrafting,
                ["bonus_xp_fabricacao_de_armaduras"] = Category.Armorcrafting,
                ["bonus_xp_contrucao"] = Category.Constructing,
                ["bonus_xp_agricultura"] = Category.Farming,
                ["bonus_xp_processamento"] = Category.Process
            };
            Check(SkillXpCoupons.Categories.Count == 12 && expected.All(p =>
                SkillXpCoupons.Categories.GetValueOrDefault(p.Key) == p.Value), "12 referências, uma por habilidade básica");
            foreach (var (id, category) in expected.Where(p => p.Value != Category.Survival))
            {
                var prototype = PrototypeYaml.GetItemPrototype(id, 1);
                Check(prototype != null && prototype.Tags.ContainsKey("usable") && prototype.TradeLocked &&
                    !prototype.DumpLocked && prototype.ImmuneToTime && !prototype.TimeLimited,
                    id + ": uso e exclusão disponíveis, venda bloqueada e sem expiração");
                var coupon = Cheats.MakeItem(id, 1).Value;
                var spare = Cheats.MakeItem(id, 1).Value;
                context.InventoryItems.Add(coupon); context.InventoryItems.Add(spare);
                Check(coupon.Icon == "icon_exp" && coupon.Name.StartsWith("Cupon do Luiz - ") &&
                    !coupon.Tradable && !ItemTradeRules.CanTrade(coupon), id + ": nome, ícone existente e não negociável");
                Check(!coupon.Name.Contains('?') && !coupon.Description.Contains('?'), id + ": texto UTF-8 preservado");
                Check(AdminMailComposer.Parse("{\"target\":\"player\",\"recipient_id\":\"tester\",\"subject\":\"Cupom\",\"message\":\"Teste\",\"items\":[{\"prototype_id\":\"" + id + "\",\"quantity\":1}]}")
                    .Items.Single().Prototype == id, id + ": disponível para anexos administrativos");

                // Mesmo Tradable adulterado não pode contornar o bloqueio do protótipo.
                var spoofed = coupon; spoofed.Tradable = true;
                Check(!ItemTradeRules.CanTrade(spoofed), id + ": flag adulterada não libera venda");
                link.Request<RegisterProduct, Abort>(new RegisterProduct { ItemId = coupon.Id, Price = 100, Duration = 86400 });
                link.Request<PutInItem, Abort>(new PutInItem { EntityId = "container", ItemIds = new[] { coupon.Id } });
                RejectGlobal(link, new AddItemsToWarehouse { EntityId = "warehouse", SectionName = "Itens", ItemIds = new[] { coupon.Id } });
                RejectGlobal(link, new PutInItemsIntoPet { PetId = "pet", ItemIds = new[] { coupon.Id } });
                RejectGlobal(link, new DumpItems { ItemIds = new[] { coupon.Id }, Tile = new Point2(0, 0) });
                Check(context.InventoryItems.Any(i => i.Id == coupon.Id) && context.InventoryItems.Any(i => i.Id == spare.Id),
                    id + ": venda, recipientes, armazéns, animais e chão não retiram o cupom");

                var locked = context.LockedItemIds;
                locked.Add(coupon.Id);
                link.Request<UseItem, Abort>(new UseItem { ItemId = coupon.Id });
                Check(context.InventoryItems.Any(i => i.Id == coupon.Id), id + ": bloqueio manual preserva o cupom");
                locked.Remove(coupon.Id);
                var beforeOthers = skills.Categories.Where(p => p.Key != (int)category)
                    .ToDictionary(p => p.Key, p => Json.Write(p.Value));
                var beforeLearned = skills.Learned.ToDictionary(p => p.Key, p => new Dictionary<string, int>(p.Value));
                int playerExp = skills.Exp;
                var state = skills.Categories[(int)category];
                state.Level = 20; state.Exp = 3;
                if (category == Category.Constructing)
                {
                    state.ResearchStart = Times.UnixTimeNow(); state.ResearchEnd = state.ResearchStart + 3600;
                    state.ResearchSaved = 5;
                }
                int indicators = link.Messages.OfType<SkillCategoryExperienced>().Count();
                link.Request<UseItem, OK>(new UseItem { ItemId = coupon.Id });
                link.PumpUntil(() => link.Messages.OfType<SkillCategoryExperienced>().Count() > indicators);
                var indicator = link.Messages.OfType<SkillCategoryExperienced>().Last();
                Check(indicator.Category == category && indicator.Exp == 10000 && indicator.ResearchReducedTime == 0,
                    id + ": concede 10.000 XP fixos pelo protocolo real");
                Check(state.Level == 60 && state.Exp == 0 && state.ResearchEnd == 0 && state.ResearchStart == 0 && state.ResearchSaved == 0,
                    id + ": chega a 60, conclui pesquisa e descarta excedente");
                Check(skills.Exp == playerExp && beforeOthers.All(p => Json.Write(skills.Categories[p.Key]) == p.Value),
                    id + ": mantém XP do personagem e demais habilidades");
                Check(beforeLearned.All(p => p.Value.All(s => skills.Learned[p.Key].GetValueOrDefault(s.Key) >= s.Value)),
                    id + ": preserva habilidades aprendidas");
                Check(!context.InventoryItems.Any(i => i.Id == coupon.Id) && context.InventoryItems.Any(i => i.Id == spare.Id),
                    id + ": consome exatamente uma unidade");
                Check(Json.Read<SkillSave>(context.Storage[SkillTuning.StorageKey]).Categories[(int)category].Level == 60,
                    id + ": progresso salvo junto com consumo");
                link.Request<UseItem, Abort>(new UseItem { ItemId = coupon.Id });
                link.Request<UseItem, Abort>(new UseItem { ItemId = spare.Id });
                Check(context.InventoryItems.Any(i => i.Id == spare.Id), id + ": repetição e nível máximo não consomem outro cupom");
                link.Send(new DumpItems { ItemIds = new[] { spare.Id } });
                link.PumpUntil(() => !context.InventoryItems.Any(i => i.Id == spare.Id));
                Check(state.Level == 60 && state.Exp == 0, id + ": exclusão sem local funciona e não concede XP");
            }

            // O cupom mantém 10.000 independentemente do limite das ações normais.
            var partialState = skills.Categories[(int)Category.Gathering];
            var table = SkillDataStore.Categories[(int)Category.Gathering];
            int oldNeed = table.ExpNeeded[40];
            try
            {
                table.ExpNeeded[40] = 20000;
                partialState.Level = 40; partialState.Exp = 1;
                var partial = Cheats.MakeItem("bonus_xp_coleta", 1).Value;
                context.InventoryItems.Add(partial);
                link.Request<UseItem, OK>(new UseItem { ItemId = partial.Id });
                Check(partialState.Level == 40 && partialState.Exp == 10001, "ganho abaixo do teto recebe exatamente 10.000 XP");
            }
            finally { table.ExpNeeded[40] = oldNeed; }

            partialState.Level = 40; partialState.Exp = 0;
            link.Player.AddCategoryExp(Category.Gathering, 10000);
            Check(partialState.Level == 40 && partialState.Exp == 12, "XP normal limitado a 12 por ação, independente do cupom");
            partialState.ResearchStart = Times.UnixTimeNow();
            partialState.ResearchEnd = partialState.ResearchStart + 3600;
            partialState.ResearchSaved = 0;
            double researchEnd = partialState.ResearchEnd;
            link.Player.AddCategoryExp(Category.Gathering, 10000);
            Check(partialState.Level == 40 && partialState.Exp == 12 && partialState.ResearchEnd < researchEnd,
                "XP normal mantém redução de pesquisa em vez de pular etapas");

            var premium = new PremiumStore(Path.Combine(root, "premium.json"));
            premium.Recover(context);
            Check(premium.Change(context, "monthly_package_1", 30, "grant", "coupon-premium", out _, out _, out _),
                "premium ativo para verificar XP fixo de Sobrevivência");
            var survival = Cheats.MakeItem("bonus_xp_sobrevivencia", 1).Value;
            context.InventoryItems.Add(survival);
            Check(survival.Icon == "icon_exp" && survival.Name == "Cupon do Luiz - Sobrevivência" && !survival.Tradable &&
                !PrototypeYaml.GetItemPrototype(survival.Prototype).DumpLocked, "Sobrevivência reutiliza ícone e permite somente uso ou exclusão");
            int expBefore = skills.Exp, remainder = context.PremiumExpRemainder;
            var otherCategories = skills.Categories.Where(p => p.Key != 0).ToDictionary(p => p.Key, p => Json.Write(p.Value));
            int gained = link.Messages.OfType<ExpGained>().Count();
            link.Request<UseItem, OK>(new UseItem { ItemId = survival.Id });
            link.PumpUntil(() => link.Messages.OfType<ExpGained>().Count() > gained);
            var xp = link.Messages.OfType<ExpGained>().Last();
            Check(skills.Exp == expBefore + 10000 && xp.Exp == 10000 && xp.BonusExp == 0 &&
                context.PremiumExpRemainder == remainder, "Sobrevivência concede 10.000 XP ao personagem sem multiplicador premium");
            Check(skills.Categories[0].Level == context.PlayerInfo.PlayerLevel &&
                otherCategories.All(p => Json.Write(skills.Categories[p.Key]) == p.Value), "Sobrevivência acompanha personagem e mantém outras habilidades");
            Check(!context.InventoryItems.Any(i => i.Id == survival.Id), "cupom de Sobrevivência é consumido");
            link.Request<UseItem, Abort>(new UseItem { ItemId = survival.Id });
            link.Player.AddExp(2, "regression");
            Check(skills.Exp == expBefore + 10003, "XP normal mantém multiplicador premium de 1,5");

            link.Player.AddExp(int.MaxValue, "cheat");
            var maxSurvival = Cheats.MakeItem("bonus_xp_sobrevivencia", 1).Value;
            context.InventoryItems.Add(maxSurvival);
            link.Request<UseItem, Abort>(new UseItem { ItemId = maxSurvival.Id });
            Check(context.InventoryItems.Any(i => i.Id == maxSurvival.Id), "Sobrevivência no nível 60 não consome cupom");
            link.Send(new DumpItems { ItemIds = new[] { maxSurvival.Id } });
            link.PumpUntil(() => !context.InventoryItems.Any(i => i.Id == maxSurvival.Id));

            Check(SafeSave.FlushPending(), "persistência física concluída");
            var reloaded = PlayerContext.Load(savePath);
            Check(reloaded != null && reloaded.InventoryItems.Select(i => i.Id).OrderBy(id => id).SequenceEqual(initialItems) &&
                Json.Read<SkillSave>(reloaded.Storage[SkillTuning.StorageKey]).Categories[(int)Category.Constructing].Level == 60,
                "reinício preserva consumo, exclusão e progresso da habilidade");
            Console.WriteLine($"[xp-coupon-check] PASS: {_passed} verificações");
            return 0;
        }
        catch (Exception error) { Console.WriteLine("[xp-coupon-check] FAIL " + error); return 1; }
        finally { SafeSave.FlushPending(); }
    }
}
