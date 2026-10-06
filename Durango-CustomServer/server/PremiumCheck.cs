using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Durango.Online;
using Durango.Utils;
using Messages;
using Shared.Economy;
using Shared.Mailing;
using Yaml;
using Yaml.Util;

namespace DurangoServerNx;

internal static class PremiumCheck
{
    private static int _checks;
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        _checks++; Console.WriteLine("[premium-check] OK " + message);
    }
    private static object Call(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);

    public static int Run(string dataDir)
    {
        string root = Path.Combine(Path.GetTempPath(), "Durango-premium-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Json.DataDir = dataDir; MoCatalog.Load(dataDir); DataStore.Load(dataDir);
            TerrainLoader.TerrainDir = Path.Combine(dataDir, "terrains");
            WorkbenchTags.AssetsDir = Path.Combine(dataDir, "assets");
            double now = 1800000000;
            string path = Path.Combine(root, "premium.json");
            var store = new PremiumStore(path, () => now);
            var alice = Context(root, "alice");
            var bob = Context(root, "bob");
            store.Recover(alice); store.Recover(bob);
            Check(store.Packages.Count == 3 && store.Packages["monthly_package_1"].InventoryBonus == 40
                && store.Packages["day_package_1"].DailyGems == 10, "pacotes originais carregados");
            long initialGems = alice.WarpGem;
            string oldSnapshot = Json.Write(alice);
            Check(store.Change(alice, "monthly_package_1", 30, "grant", "grant-1", out var first, out _, out _), "concessão offline salva");
            Check(store.IsActive(alice.EntityId) && alice.WarpGem == initialGems + 300 && !store.IsActive(bob.EntityId), "bônus imediato e direito por personagem");
            Check(store.Change(alice, "monthly_package_1", 30, "grant", "grant-1", out _, out bool duplicate, out _) && duplicate
                && alice.WarpGem == initialGems + 300, "pedido repetido não duplica prazo nem gems");
            Check(!store.Change(alice, "monthly_package_1", 7, "grant", "grant-1", out _, out _, out _), "ID conflitante recusado");
            Check(!store.Change(alice, "monthly_package_1", 0, "grant", "invalid", out _, out _, out _)
                && !store.Change(alice, "missing", 1, "grant", "invalid-2", out _, out _, out _), "duração e pacote inválidos recusados");
            Check(store.Change(alice, "monthly_package_1", 7, "grant", "renew-1", out var renewal, out _, out _)
                && renewal.Until == first.Until + 7 * 86400, "renovação soma prazo");
            Check(store.Change(alice, "day_package_2", 15, "grant", "grant-2", out _, out _, out _), "pacote diferente ativado");
            Check(Player.InventoryCapacity(alice) == Player.InventoryMaxSize + 80, "capacidade de pacotes diferentes soma");
            var reloaded = new PremiumStore(path, () => now);
            var stale = Json.Read<PlayerContext>(oldSnapshot);
            reloaded.Recover(stale);
            Check(stale.WarpGem == initialGems + 750 && stale.PremiumSequence == alice.PremiumSequence, "journal recupera gems após crash com save antigo");
            reloaded.Recover(stale);
            Check(stale.WarpGem == initialGems + 750, "recuperação não duplica crédito");
            Check(reloaded.IsActive(alice.EntityId) && reloaded.InventoryBonus(alice.EntityId) == 80, "direitos sobrevivem ao reinício");
            var mail = new MailStore(Path.Combine(root, "mail.json"), () => now);
            Check(store.DeliverDaily(alice, mail, out _), "entregas diárias dos dois pacotes");
            var inbox = mail.Inbox(alice.EntityId);
            Check(inbox.Length == 2 && inbox.Sum(m => m.Money.GetValueOrDefault(Currency.Gem)) == 40
                && inbox.Sum(m => m.AttachedItems.Length) == 6, "correio contém gems e comida nativos");
            Check(store.DeliverDaily(alice, mail, out _) && mail.Inbox(alice.EntityId).Length == 2, "não repete entrega no mesmo dia");
            long beforeClaim = alice.WarpGem;
            string beforeClaimSnapshot = Json.Write(alice);
            Check(!mail.Delete(alice.EntityId, inbox.Select(m => m.Id).ToArray(), out _), "anexos pendentes protegidos");
            Check(mail.Accept(alice, inbox.Select(m => m.Id).ToArray(), out var items, out _, out _)
                && alice.WarpGem == beforeClaim + 40 && items.Length == 6, "resgate concede gems e itens juntos");
            Check(mail.Accept(alice, inbox.Select(m => m.Id).ToArray(), out items, out _, out _)
                && alice.WarpGem == beforeClaim + 40 && items.Length == 0, "resgate repetido não duplica moeda");
            var staleClaim = Json.Read<PlayerContext>(beforeClaimSnapshot);
            mail.Recover(staleClaim);
            Check(staleClaim.WarpGem == beforeClaim + 40 && staleClaim.InventoryItems.Count == alice.InventoryItems.Count, "journal do correio recupera moeda e itens após crash");
            var reopenedMail = new MailStore(Path.Combine(root, "mail.json"), () => now);
            Check(store.DeliverDaily(alice, reopenedMail, out _) && reopenedMail.Inbox(alice.EntityId).Length == 2, "restart não repete diária resgatada");
            now += 3 * 86400;
            Check(store.DeliverDaily(alice, mail, out _) && mail.Inbox(alice.EntityId).Length == 4, "novo dia concede uma diária sem retroativo offline");
            var wc = new WorldContext { TerrainId = "pe10gr_1" }; wc.Initialize(Path.Combine(root, "test.world"));
            var world = new World(wc);
            using var link = new EconomyProtocolCheck.Link(alice, world, null, simulatePlayer: true, mailStore: mail);
            link.Player.SyncPremium();
            Check(link.Player.CurrentInventoryCapacity == Player.InventoryMaxSize + 80, "capacidade online corresponde ao premium");
            int exp = (int)Call(link.Player, "WithPremiumExp", 100, "Caçar");
            Check(exp == 150 && (int)Call(link.Player, "WithPremiumExp", 100, "cheat") == 100, "bônus XP 50%, sem multiplicar ajuste administrativo");
            link.Player.AddExp(100, "premium-test");
            link.PumpUntil(() => link.Messages.OfType<ExpGained>().Any());
            Check(link.Messages.OfType<ExpGained>().Last().BonusExp == 50, "protocolo mostra bônus de XP");
            var skills = (SkillSave)link.Player.GetType().GetField("_skills", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(link.Player);
            int beforeSmallXp = skills.Exp;
            link.Player.AddExp(1, "small-premium"); link.Player.AddExp(1, "small-premium");
            Check(skills.Exp == beforeSmallXp + 3 && alice.PremiumExpRemainder == 0,
                "ganhos pequenos preservam a fração de 50% entre ações");
            var survival = (SurvivalState)link.Player.GetType().GetField("_survival", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(link.Player);
            survival.Set(SurvivalState.KeyFatigue, 80); survival.Add(SurvivalState.KeyFatigue, 15);
            survival.SetMomentum("test-fatigue", new() { [SurvivalState.KeyFatigue] = 3 });
            survival.Flush(Gauge.CurrentTime);
            Check(survival.ValueAt(SurvivalState.KeyFatigue, Gauge.CurrentTime + 10) == 0, "fadiga zero por ações e curvas ambientais");
            alice.InventoryItems.Clear();
            link.Player.PremiumGatherRoll = () => 0.249;
            var berry = Cheats.MakeItem("wildberry", 1).Value;
            berry.GeneratorId = "premium-test";
            Call(link.Player, "CompleteCollectAfterDelay", default(Collected), new List<Item> { berry }, 100u,
                "premium-test", new Point2(0, 0), "premium-test", false, false, null);
            link.PumpUntil(() => link.Messages.OfType<Collected>().Any());
            Check(alice.InventoryItems.Count == 2 && link.Messages.OfType<Collected>().Last().Items.Length == 2,
                "coleta real concede extra e protocolo publica os mesmos itens");
            alice.InventoryItems.Clear();
            link.Player.PremiumGatherRoll = () => 0.25;
            Call(link.Player, "CompleteCollectAfterDelay", default(Collected), new List<Item> { berry }, 101u,
                "premium-test", new Point2(0, 0), "premium-test", false, false, null);
            Check(alice.InventoryItems.Count == 1, "sorteio no limite de 25% não concede extra");
            alice.InventoryItems.Clear();
            link.Player.PremiumGatherRoll = () => 0;
            Call(link.Player, "CompleteCollectAfterDelay", default(Collected), new List<Item> { berry }, 102u,
                "premium-test", new Point2(0, 0), "premium-test", true, false, null);
            Check(alice.InventoryItems.Count == 1, "esfolamento mantém sua regra própria de bônus");
            alice.InventoryItems.Clear();
            var ballast = berry; ballast.Id = "full"; ballast.Size = link.Player.CurrentInventoryCapacity;
            alice.InventoryItems.Add(ballast);
            Call(link.Player, "CompleteCollectAfterDelay", default(Collected), new List<Item> { berry }, 103u,
                "premium-test", new Point2(0, 0), "premium-test", false, false, null);
            Check(alice.InventoryItems.Count == 1, "mochila cheia não recebe itens fora da capacidade");
            alice.InventoryItems.Clear();
            Check(store.Change(alice, "day_package_2", 0, "revoke", "revoke-1", out _, out _, out _), "revogação individual");
            link.Player.SyncPremium();
            Check(store.IsActive(alice.EntityId) && link.Player.CurrentInventoryCapacity == Player.InventoryMaxSize + 40, "revogar um pacote mantém o outro");
            now = renewal.Until;
            link.Player.SyncPremium();
            Check(!store.IsActive(alice.EntityId) && link.Player.CurrentInventoryCapacity == Player.InventoryMaxSize,
                "expiração restaura capacidade e remove premium durante sessão");
            survival.Add(SurvivalState.KeyFatigue, 10); survival.Flush(Gauge.CurrentTime);
            Check(survival.ValueAt(SurvivalState.KeyFatigue, Gauge.CurrentTime) >= 9.9f, "fadiga normal retorna após expiração");
            Check(store.Change(alice, "monthly_package_1", 1, "grant", "after-expiry", out var expiredRenew, out _, out _)
                && expiredRenew.Until == now + 86400, "renovação vencida começa agora");
            Check(PremiumStore.GatherChance == 0.25f, "chance premium definida em 25%");
            alice.InventoryItems.Clear(); alice.EquippedItems.Clear();
            link.Player.SyncPremium();
            var gloves = Cheats.MakeItem("s02_gloves_work_01", 60).Value;
            var bag = Cheats.MakeItem("event_suitcase", 60).Value;
            ItemCraftModifications.Initialize(ref gloves);
            Check(ItemCraftModifications.Apply(ref gloves, "reform_pocket", CraftRecipeStore.Get("reform_pocket"),
                new Dictionary<string, Item[]>(), 0, out _), "luvas recebem bolso pela melhoria real");
            int pocket = (int)gloves.Performance.Single(p => p.Id == "armor").Nums["bag_size"];
            alice.InventoryItems.Add(gloves); alice.InventoryItems.Add(bag);
            Check(Player.InventoryCapacity(alice) == Player.InventoryMaxSize + 40, "peças na mochila não concedem armazenamento");
            int infos = link.Messages.OfType<InventoryInfos>().Count();
            link.Request<Equip, Equipments>(new Equip { Action = "equip", SlotName = "gloves", ItemId = gloves.Id });
            link.PumpUntil(() => link.Messages.OfType<InventoryInfos>().Count() > infos);
            Check(Player.InventoryCapacity(alice) == Player.InventoryMaxSize + 40 + pocket &&
                link.Messages.OfType<InventoryInfos>().Last().MaxSize == Player.InventoryCapacity(alice),
                "bolso soma com premium e publica capacidade ao equipar");
            link.Request<Equip, Equipments>(new Equip { Action = "equip", SlotName = "precious", ItemId = bag.Id });
            Check(Player.InventoryCapacity(alice) == Player.InventoryMaxSize + 40 + pocket + 20,
                "acessório soma carry_capacity com armadura e premium");
            var naturalPocket = Cheats.MakeItem("s02_gloves_work_01", 60).Value;
            naturalPocket.Tags = naturalPocket.Tags.Append(new Tag { Id = "pocket", Level = 2 }).ToArray();
            alice.InventoryItems.Add(naturalPocket); alice.EquippedItems["gloves"] = naturalPocket.Id;
            Check(Player.InventoryCapacity(alice) == Player.InventoryMaxSize + 40 + 20 + 1 + 12,
                "bolsos naturais também concedem armazenamento pela fórmula do atributo");
            alice.EquippedItems["duplicate"] = naturalPocket.Id;
            Check(Player.InventoryCapacity(alice) == Player.InventoryMaxSize + 40 + 20 + 1 + 12,
                "referência repetida no save não duplica armazenamento");
            alice.EquippedItems.Remove("duplicate"); alice.EquippedItems["gloves"] = gloves.Id;
            alice.InventoryItems.RemoveAll(i => i.Id == naturalPocket.Id);
            link.PumpUntil(() => link.Messages.OfType<Statistics>().Last().DerivedsAbilities[Shared.Ability.Derived.InventoryCapacity]
                == Player.InventoryCapacity(alice));
            Check(link.Messages.OfType<Statistics>().Last().DerivedsAbilities[Shared.Ability.Derived.InventoryCapacity]
                == Player.InventoryCapacity(alice), "estatísticas e inventário usam a mesma capacidade");
            link.Request<Equip, Abort>(new Equip { Action = "equip", SlotName = "head", ItemId = bag.Id });
            Check(alice.EquippedItems.Count == 2, "item não pode multiplicar bônus em espaços incompatíveis");
            var full = berry; full.Id = "capacity-ballast"; full.Size = Player.InventoryCapacity(alice) - 2;
            alice.InventoryItems.Add(full);
            bool CanReceive(Item[] items) => (bool)typeof(EconomyStore).GetMethod("CanReceive", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { alice, items });
            Check(CanReceive(Array.Empty<Item>()) && !CanReceive(new[] { berry }),
                "capacidade com equipamentos é aplicada ao limite real de entrada de itens");
            var capacityReload = Json.Read<PlayerContext>(Json.Write(alice)); capacityReload.Initialize(Path.Combine(root, "capacity.player"));
            store.Recover(capacityReload);
            Check(Player.InventoryCapacity(capacityReload) == Player.InventoryCapacity(alice),
                "reconexão preserva bônus do bolso e acessório sem duplicação");
            link.Request<Equip, Equipments>(new Equip { Action = "unequip", SlotName = "gloves" });
            Check(Player.InventoryCapacity(alice) == Player.InventoryMaxSize + 40 + 20 && alice.InventoryItems.Count == 3,
                "remover peça reduz capacidade e preserva itens já guardados");
            Check(store.Change(alice, "monthly_package_1", 0, "revoke", "capacity-revoke", out _, out _, out _), "premium removido no teste de armazenamento");
            link.Player.SyncPremium();
            Check(Player.InventoryCapacity(alice) == Player.InventoryMaxSize + 20, "expiração do premium preserva armazenamento do acessório");
            link.Request<Equip, Equipments>(new Equip { Action = "unequip", SlotName = "precious" });
            Check(Player.InventoryCapacity(alice) == Player.InventoryMaxSize, "retirar última peça restaura capacidade base");
            var blocked = Path.Combine(root, "blocked"); Directory.CreateDirectory(blocked);
            var brokenStore = new PremiumStore(blocked, () => now);
            long beforeFailure = bob.WarpGem;
            Check(!brokenStore.Change(bob, "monthly_package_1", 1, "grant", "fail", out _, out _, out _)
                && !brokenStore.IsActive(bob.EntityId) && bob.WarpGem == beforeFailure, "falha de gravação não ativa direito nem credita gems");
            Check(SafeSave.FlushPending(), "snapshots de testes salvos");
            Console.WriteLine($"[premium-check] PASS {_checks} verificações");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("[premium-check] FAIL " + error); return 1; }
        finally { SafeSave.FlushPending(); Directory.Delete(root, true); }
    }
    private static PlayerContext Context(string root, string id)
    {
        var context = new PlayerContext { OwnerKey = "test-" + id,
            PlayerInfo = new Durango.Logic.Clusters.PlayerInfo { PlayerEntityId = id, PlayerName = id, PlayerLevel = 10 } };
        context.Initialize(Path.Combine(root, id + ".player"));
        return context;
    }
}
