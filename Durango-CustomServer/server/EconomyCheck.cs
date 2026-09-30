using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Durango.Online;
using Durango.Utils;
using Messages;
using Shared.Economy;
using Shared.Market;
using Yaml;
using Yaml.Util;

namespace DurangoServerNx;

// Exercita a implementação real em arquivos temporários, sem tocar no cluster de jogo.
internal static class EconomyCheck
{
    private static int _passed;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _passed++;
        Console.WriteLine("[economy-check] OK " + message);
    }

    public static int Run(string dataDir)
    {
        string testRoot = Path.Combine(Path.GetTempPath(), "Durango-economy-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        try
        {
            Json.DataDir = dataDir;
            MoCatalog.Load(dataDir);
            DataStore.Load(dataDir);
            var catalog = new ShopCatalog();
            string ledgerPath = Path.Combine(testRoot, "economy.json");
            double now = Times.UnixTimeNow();
            var store = new EconomyStore(ledgerPath, catalog, () => now);
            var seller = Context(testRoot, "seller", 1000);
            var buyer = Context(testRoot, "buyer", 5000);
            buyer.WarpGem = 100000;
            buyer.DurangoCoin = 100000;
            buyer.WarpMatter = 100000;
            buyer.ShopMileage = 100000;
            var prototype = SingletonDict<string, List<Prototype>>.Instance.Keys.First(id => ItemTradeRules.PrototypeCanTrade(id) &&
                PrototypeYaml.GetItemPrototype(id).Size <= 2 && !id.StartsWith("capsulated_"));
            var item = Cheats.MakeItem(prototype, 10).Value;
            item.Name = "Teste-Market";
            item.ModifiedCount = 3;
            item.ColorR = "ABCDEF";
            seller.InventoryItems.Add(item);
            string sellerBefore = Json.Write(seller), buyerBefore = Json.Write(buyer);

            Check(!store.Register(seller, "island-a", new[] { item.Id, item.Id }, 300, 86400, out _, out _), "IDs duplicados recusados");
            Check(!store.Register(seller, "island-a", new[] { item.Id }, -1, 86400, out _, out _), "preco negativo recusado");
            Check(!store.Register(seller, "island-a", new[] { item.Id }, 300, float.NaN, out _, out _), "duracao NaN recusada");
            seller.EquippedItems["test"] = item.Id;
            Check(!store.Register(seller, "island-a", new[] { item.Id }, 300, 86400, out _, out _), "item equipado recusado");
            seller.EquippedItems.Clear();
            var worn = item;
            worn.Durability = new Gauge(1, 0, new[] { new GaugeNode(0, .5f) });
            seller.InventoryItems[0] = worn;
            Check(!store.Register(seller, "island-a", new[] { item.Id }, 300, 86400, out _, out _), "durabilidade abaixo de 90% recusada");
            seller.InventoryItems[0] = item;
            Check(store.Register(seller, "island-a", new[] { item.Id }, 300, 86400, out var listed, out _), "anuncio confirmado");
            string id = listed.Single().Id;
            Check(seller.InventoryItems.Count == 0, "item removido do vendedor para custodia");
            Check(store.History(seller.EntityId, "registered", null, 0).Length == 1, "historico de anuncios");
            Check(store.Search(new SearchProducts { ItemName = "teste-market" }).Single().Id == id, "busca por nome sem diferenca de caixa");
            Check(store.Search(new SearchProducts { PrototypeId = "invalid" }).Length == 0, "filtro por prototype");
            Check(store.Search(new SearchProducts { Price = new PriceRangePredicate { Min = 301, Currency = Currency.TStone } }).Length == 0, "filtro por preco");
            Check(store.Search(new SearchProducts { Level = new RangePredicate { Min = 11 } }).Length == 0, "filtro por nivel");
            Check(store.Favorites(new[] { id }).Length == 1, "favoritos retornam anuncio real");
            Check(!store.Buy(seller, id, out _, out _), "compra do proprio anuncio recusada");
            var poor = Context(testRoot, "poor", 0);
            Check(!store.Buy(poor, id, out _, out _), "compra sem saldo recusada");
            var full = Context(testRoot, "full", 1000);
            var ballast = item; ballast.Id = "ballast"; ballast.Size = Player.InventoryMaxSize;
            full.InventoryItems.Add(ballast);
            Check(!store.Buy(full, id, out _, out _), "compra com inventario cheio recusada");
            Check(full.TStone == 1000 && store.Search(default).Length == 1, "falha de compra preserva saldo e anuncio");
            Check(!store.Unregister(buyer, id, out _), "cancelamento por outro jogador recusado");
            Check(!store.Withdraw(buyer, id, out _, out _), "retirada por outro jogador recusada");

            // Dois compradores, de ilhas/conexões diferentes, disputam o mesmo item.
            var rival = Context(testRoot, "rival", 5000);
            string rivalBefore = Json.Write(rival);
            bool boughtA = false, boughtB = false;
            Parallel.Invoke(() => boughtA = store.Buy(buyer, id, out _, out _), () => boughtB = store.Buy(rival, id, out _, out _));
            Check(boughtA != boughtB, "somente um comprador vence a disputa");
            var winner = boughtA ? buyer : rival;
            Check(winner.InventoryItems.Single().Id == item.Id && winner.InventoryItems[0].ModifiedCount == 3 &&
                winner.InventoryItems[0].ColorR == "ABCDEF", "ID, modificacoes e cores preservados na venda");
            Check(winner.TStone == 4700 && seller.TStone == 1000, "comprador debitado e vendedor aguarda recebimento");
            Check(!store.Buy(buyer, id, out _, out _) && !store.Buy(rival, id, out _, out _), "compra repetida nao duplica item");
            Check(store.HasPayments(seller.EntityId), "venda offline possui pagamento pendente");
            Check(store.History(winner.EntityId, "purchased", null, 0).Single().State == ProductState.Sold, "historico do comprador");
            Check(!store.Collect(winner, id, out _, out _, out _), "outro jogador nao recebe receita do vendedor");
            Check(store.Collect(seller, id, out _, out long received, out _), "vendedor recebe pagamento");
            Check(received == 300 - catalog.SalesFee(300) && seller.TStone == 1000 + received, "valor e taxa de venda corretos");
            Check(!store.Collect(seller, id, out _, out _, out _) && !store.HasPayments(seller.EntityId), "pagamento recebido uma unica vez");

            // Recomeçar do snapshot ANTERIOR ao anúncio, simulando crash antes do autosave.
            var recoveredSeller = Restore(sellerBefore, Path.Combine(testRoot, "recovered-seller.player"));
            var reloaded = new EconomyStore(ledgerPath, catalog, () => now);
            reloaded.Recover(recoveredSeller);
            Check(recoveredSeller.InventoryItems.Count == 0 && recoveredSeller.TStone == seller.TStone, "ledger recupera custodia e receita apos crash");
            reloaded.Recover(recoveredSeller);
            Check(recoveredSeller.TStone == seller.TStone, "replay idempotente");
            Check(reloaded.History(seller.EntityId, "sold", null, 0).Single().State == ProductState.PaymentReceived, "historico persistido no restart");
            {
                var recoveredBuyer = Restore(boughtA ? buyerBefore : rivalBefore, Path.Combine(testRoot, "recovered-buyer.player"));
                reloaded.Recover(recoveredBuyer);
                Check(recoveredBuyer.TStone == 4700 && recoveredBuyer.InventoryItems.Single().Id == item.Id, "replay recupera debito e entrega do comprador");
            }

            var expiring = Cheats.MakeItem(prototype, 10).Value;
            seller.InventoryItems.Add(expiring);
            Check(store.Register(seller, "island-b", new[] { expiring.Id }, 200, 1, out var expiredRows, out _), "anuncio de outra ilha");
            now += 2;
            Check(!store.Buy(buyer, expiredRows[0].Id, out _, out _), "anuncio expirado nao pode ser comprado");
            Check(store.History(seller.EntityId, "expired", null, 0).Length == 1, "anuncios expirados listados");
            Check(store.Withdraw(seller, expiredRows[0].Id, out _, out _), "retirada de item expirado");
            Check(!store.Withdraw(seller, expiredRows[0].Id, out _, out _), "retirada repetida nao duplica item");
            Check(store.Register(seller, "island-b", new[] { expiring.Id }, 200, 86400, out var cancelRows, out _), "item retirado pode ser anunciado novamente");
            Check(store.Unregister(seller, cancelRows[0].Id, out _) && store.Withdraw(seller, cancelRows[0].Id, out _, out _), "cancelamento e devolucao de item");

            Check(!ItemTradeRules.PrototypeCanTrade("trade_locked_artifact_capsule") &&
                !ItemTradeRules.PrototypeCanTrade("hat_special_builder_avatar"), "capsulas vinculadas e avatar permanecem bloqueados");
            var legacy = Context(testRoot, "legacy", 0);
            var legacyItem = item; legacyItem.Tradable = false;
            legacy.InventoryItems.Add(legacyItem); legacy.MarketTradabilityVersion = 0;
            ItemTradeRules.Migrate(legacy);
            Check(legacy.InventoryItems[0].Tradable, "itens antigos recebem migracao de negociabilidade");
            var legacyWarehouse = new WorldContext { Warehouses = new()
                { ["test"] = new Player.WarehouseStore.Box { Order = new(), Sections = new()
                    { ["inventory"] = new List<Item> { legacyItem } } } } };
            ItemTradeRules.Migrate(legacyWarehouse);
            Check(legacyWarehouse.Warehouses["test"].Sections["inventory"][0].Tradable,
                "itens antigos de armazens recebem migracao de negociabilidade");

            const string commodityId = "item_fatigue_drug_01_store";
            Check(catalog.Definitions.TryGetValue(commodityId, out var definition), "produto original disponivel");
            Check(definition.Price == 150 && definition.Currency == Currency.Gem, "preco original: 150 Warp Gems");
            Check(store.Commodities(buyer.EntityId).Any(c => c.Id == commodityId), "catalogo publico inclui produto suportado");
            buyer.WarpGem = 1000;
            string beforeShop = Json.Write(buyer);
            Check(!store.Purchase(poor, commodityId, out _, out _), "loja recusa saldo insuficiente");
            Check(!store.Purchase(buyer, "invalid", out _, out _), "loja recusa produto inexistente");
            Check(store.Purchase(buyer, commodityId, out var purchases, out _), "compra na loja cria recibo");
            Check(buyer.WarpGem == 850 && store.Purchases(buyer.EntityId).Any(p => p.Id == purchases[0].Id), "debito correto e compra fica pendente de recebimento");
            Check(!store.Accept(seller, purchases[0].Id, out _, out _), "recibo nao pode ser recebido por outra conta");
            int beforeItems = buyer.InventoryItems.Count;
            Check(store.Accept(buyer, purchases[0].Id, out var delivered, out _), "recebimento de item da loja");
            Check(delivered.Single().Prototype == "fatigue_drug_store" && buyer.InventoryItems.Count == beforeItems + 1, "item correto entregue");
            Check(!store.Accept(buyer, purchases[0].Id, out _, out _), "recebimento repetido nao duplica item");
            var shopRecovered = Restore(beforeShop, Path.Combine(testRoot, "shop-recovered.player"));
            var shopReload = new EconomyStore(ledgerPath, catalog, () => now);
            shopReload.Recover(shopRecovered);
            Check(shopRecovered.WarpGem == 850 && shopRecovered.InventoryItems.Any(i => i.Id == delivered[0].Id), "loja recupera debito e entrega apos crash");
            Check(!shopReload.Accept(shopRecovered, purchases[0].Id, out _, out _), "recibo consumido permanece consumido apos restart");
            Check(store.FirstPurchases(buyer.EntityId).Any(p => p.CommodityId == commodityId), "historico real de primeira compra");

            full.WarpGem = 1000;
            Check(store.Purchase(full, commodityId, out var fullPurchase, out _), "compra pode aguardar espaco no inventario");
            Check(!store.Accept(full, fullPurchase[0].Id, out _, out _) && store.Purchases(full.EntityId).Length == 1, "inventario cheio preserva recibo pendente");
            full.InventoryItems.Clear();
            Check(store.Accept(full, fullPurchase[0].Id, out _, out _), "recebimento funciona depois de liberar espaco");

            const string automaticId = "item_fatigue_drug_warp_matter";
            var autoBuyer = Context(testRoot, "automatic", 0);
            autoBuyer.WarpMatter = 1000;
            Check(catalog.Definitions[automaticId].AutoAccept, "tag original de entrega automatica reconhecida");
            var autoDefinition = catalog.Definitions[automaticId];
            for (int i = 0; i < autoDefinition.PeriodCount; i++)
                Check(store.Purchase(autoBuyer, automaticId, out var autoPurchases, out var autoItems, out _) &&
                    autoPurchases.Single().AcceptedAt.HasValue && autoItems.Length == 1, "entrega automatica atomica " + (i + 1));
            Check(autoBuyer.InventoryItems.Count == autoDefinition.PeriodCount && store.Purchases(autoBuyer.EntityId).Length == 0,
                "itens automaticos entregues sem recibos pendentes");
            Check(!store.Purchase(autoBuyer, automaticId, out _, out _) &&
                store.Commodities(autoBuyer.EntityId).Single(c => c.Id == automaticId).PeriodicPurchasableAt > now,
                "limite semanal aplicado e comunicado ao cliente");
            var autoReload = new EconomyStore(ledgerPath, catalog, () => now);
            Check(!autoReload.Purchase(autoBuyer, automaticId, out _, out _), "limite semanal persiste no restart");
            double originalNow = now;
            now = store.Commodities(autoBuyer.EntityId).Single(c => c.Id == automaticId).PeriodicPurchasableAt.Value + 1;
            Check(store.Purchase(autoBuyer, automaticId, out _, out _), "limite semanal renova na data indicada");
            now = originalNow;
            full.InventoryItems.Clear(); full.InventoryItems.Add(ballast); full.WarpMatter = 1000;
            Check(!store.Purchase(full, automaticId, out _, out _) && full.WarpMatter == 1000,
                "entrega automatica sem espaco recusa antes de debitar");

            buyer.DurangoCoin = 1000;
            long beforeGems = buyer.WarpGem;
            Check(store.Purchase(buyer, "gem_900", out var gemPurchase, out _) && buyer.DurangoCoin == 700,
                "conversao de moeda usa preco original de 300 Coins");
            Check(store.Accept(buyer, gemPurchase[0].Id, out _, out _) && buyer.WarpGem == beforeGems + 900,
                "recebimento credita exatamente 900 Warp Gems");
            Check(!store.Accept(buyer, gemPurchase[0].Id, out _, out _), "conversao nao pode ser recebida duas vezes");

            foreach (var d in catalog.Definitions.Values.Where(d => ShopCatalog.InPeriod(d, now)))
            {
                var content = catalog.Generate(d);
                Check(content.Length == d.Copies && content.All(c => c.Items.Count > 0 || c.Money.Count > 0), "conteudo completo: " + d.Id);
                foreach (var capsule in content.SelectMany(c => c.Items).Where(i => i.Prototype.StartsWith("capsulated_") || i.Prototype == "trade_locked_artifact_capsule"))
                    Check(capsule.Ext is ArtifactCapsule, "capsula utilizavel: " + capsule.Prototype);
            }

            var random = catalog.Definitions.Values.FirstOrDefault(d => ShopCatalog.InPeriod(d, now) && !d.AutoAccept && d.Contents["weighted_items"] != null);
            if (random != null)
            {
                buyer.WarpGem = buyer.DurangoCoin = buyer.ShopMileage = buyer.WarpMatter = 100000;
                Check(store.Purchase(buyer, random.Id, out var boxes, out _), "compra de caixa aleatoria");
                var boxReload = new EconomyStore(ledgerPath, catalog, () => now);
                var pending = boxReload.Purchases(buyer.EntityId).Single(p => p.Id == boxes[0].Id);
                Check(((ItemPurchaseContent)pending.Content).Item.Id == ((ItemPurchaseContent)boxes[0].Content).Item.Id, "resultado aleatorio nao muda no restart");
            }

            string blockedPath = Path.Combine(testRoot, "blocked-ledger");
            Directory.CreateDirectory(blockedPath);
            var failedStore = new EconomyStore(blockedPath, catalog);
            var failedSeller = Context(testRoot, "failed", 1000);
            failedSeller.InventoryItems.Add(item);
            Check(!failedStore.Register(failedSeller, "1", new[] { item.Id }, 300, 86400, out _, out _), "erro de disco rejeita transacao");
            Check(failedSeller.InventoryItems.Count == 1 && failedSeller.TStone == 1000 && failedStore.Search(default).Length == 0, "erro de disco nao perde item, saldo ou cria anuncio");
            EconomyProtocolCheck.Run(testRoot, dataDir, catalog, Check);
            Console.WriteLine($"[economy-check] PASS {_passed} verificacoes. Arquivos temporarios: {testRoot}");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("[economy-check] FAIL " + ex); return 1; }
        finally { SafeSave.FlushPending(); }
    }

    private static PlayerContext Context(string folder, string id, long stones)
    {
        var context = new PlayerContext { PlayerInfo = new Durango.Logic.Clusters.PlayerInfo { PlayerEntityId = id, PlayerName = id, PlayerLevel = 10 } };
        context.Initialize(Path.Combine(folder, id + ".player"));
        context.TStone = stones;
        return context;
    }

    private static PlayerContext Restore(string snapshot, string path)
    {
        var context = Json.Read<PlayerContext>(snapshot);
        context.Initialize(path);
        return context;
    }
}
