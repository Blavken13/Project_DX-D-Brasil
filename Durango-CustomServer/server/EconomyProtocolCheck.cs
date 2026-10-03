using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Durango.Network;
using Durango.Online;
using Messages;
using Yaml;
using Yaml.Util;

namespace DurangoServerNx;

// Exercita os handlers de Player pela conexão TCP e pelo MessagePack reais.
internal static class EconomyProtocolCheck
{
    public static void Run(string root, string dataDir, ShopCatalog catalog, Action<bool, string> check)
    {
        TerrainLoader.TerrainDir = Path.Combine(dataDir, "terrains");
        var context = new WorldContext { TerrainId = "pe10gr_1" };
        context.Initialize(Path.Combine(root, "protocol.world"));
        var world = new World(context);
        var store = new EconomyStore(Path.Combine(root, "protocol-economy.json"), catalog);
        var seller = MakeContext(root, "protocol-seller");
        var buyer = MakeContext(root, "protocol-buyer");
        var prototype = SingletonDict<string, List<Prototype>>.Instance.Keys.First(id => ItemTradeRules.PrototypeCanTrade(id) &&
            PrototypeYaml.GetItemPrototype(id).Size <= 2 && !id.StartsWith("capsulated_"));
        var item = Cheats.MakeItem(prototype, 10).Value;
        seller.InventoryItems.Add(item);
        using var sellerLink = new Link(seller, world, store);
        using var buyerLink = new Link(buyer, world, store);
        store.ProductBought += sellerLink.Player.NotifyMarketSale;

        var listed = sellerLink.Request<RegisterMultipleProducts, Products>(new RegisterMultipleProducts
        { ItemIds = new[] { item.Id }, EachPrice = 200, Duration = 86400 });
        string productId = listed._Products.Single().Id;
        check(sellerLink.Messages.OfType<InventoryUpdated>().Any(m => m.RemovedItemIds?.Contains(item.Id) == true),
            "TCP anuncio publica remocao do inventario");
        check(buyerLink.Request<SearchProducts, Products>(new SearchProducts { PrototypeId = prototype })._Products.Single().Id == productId,
            "TCP busca retorna anuncio do outro jogador");
        check(buyerLink.Request<AddToFavoriteProducts, Products>(new AddToFavoriteProducts { ProductId = productId })._Products.Single().Id == productId,
            "TCP favorito responde com Products esperado pelo cliente");
        buyerLink.Request<BuyProduct, OK>(new BuyProduct { ProductId = productId });
        check(buyer.InventoryItems.Count(i => i.Id == item.Id) == 1 && buyer.TStone == 4800,
            "TCP compra debita saldo e entrega item");
        sellerLink.PumpUntil(() => sellerLink.Messages.OfType<ProductSold>().Any());
        check(sellerLink.Messages.OfType<MarketCollectablePaymentExists>().Any(m => m.Exists),
            "TCP vendedor recebe notificacao da venda");
        sellerLink.Request<MarketCollectPayment, OK>(new MarketCollectPayment { ProductId = productId });
        check(seller.TStone == 5200, "TCP recebimento da venda atualiza saldo");

        const string commodity = "item_fatigue_drug_01_store";
        check(buyerLink.Request<GetCommodities, Commodities>(default).CommodityInfos.Any(c => c.Id == commodity),
            "TCP catalogo da loja usa Commodities nativo");
        var bought = buyerLink.Request<PurchaseCommodity, Purchased>(new PurchaseCommodity { CommodityId = commodity });
        string receiptId = bought.Purchases.Single().Id;
        check(bought.Purchases.Single().Content is ItemPurchaseContent content && content.Item.Prototype == "fatigue_drug_store",
            "TCP recibo preserva union ItemPurchaseContent");
        buyerLink.Request<AcceptPurchase, OK>(new AcceptPurchase { PurchaseId = receiptId });
        check(buyer.InventoryItems.Any(i => i.Prototype == "fatigue_drug_store") && buyer.WarpGem == 850,
            "TCP recebimento da loja atualiza inventario e carteira");
        buyerLink.Request<AcceptPurchase, Abort>(new AcceptPurchase { PurchaseId = receiptId });
        check(buyer.InventoryItems.Count(i => i.Prototype == "fatigue_drug_store") == 1,
            "TCP repeticao de recebimento rejeitada sem duplicar");
        buyerLink.Request<PurchaseCommodity, Purchased>(new PurchaseCommodity { CommodityId = "item_fatigue_drug_warp_matter" });
        check(buyer.InventoryItems.Any(i => i.Prototype == "fatigue_drug_02_store") && buyer.WarpMatter == 990,
            "TCP entrega automatica envia item e saldo");

        // Cápsulas carregadas do ledger também precisam atravessar o protocolo.
        var capsule = catalog.Definitions.Values.First(d => d.PreviewItem && !d.AutoAccept &&
            ShopCatalog.InPeriod(d, DateTimeOffset.UtcNow.ToUnixTimeSeconds()) &&
            d.Contents["items"]?.Any(i => ((string)i["prototype_id"])?.StartsWith("capsulated_") == true) == true);
        buyer.WarpGem = buyer.DurangoCoin = buyer.ShopMileage = buyer.WarpMatter = 100000;
        check(store.Purchase(buyer, capsule.Id, out var capsulePurchases, out _), "compra de capsula para validar serializacao");
        var reload = new EconomyStore(Path.Combine(root, "protocol-economy.json"), catalog);
        var pending = reload.Purchases(buyer.EntityId);
        // As extensões dos itens em recibos/históricos devem sobreviver ao JSON e ao MessagePack.
        sellerLink.Server.Send(new Purchases { _Purchases = pending });
        sellerLink.PumpUntil(() => sellerLink.Messages.OfType<Purchases>().Any(m =>
            m._Purchases.Any(p => p.Id == capsulePurchases[0].Id)));
        check(sellerLink.Messages.OfType<Purchases>().SelectMany(p => p._Purchases).Any(p =>
            p.Content is ItemPurchaseContent c && c.Item.Ext is ArtifactCapsule),
            "TCP capsula persistida preserva ArtifactCapsule no cliente");
    }

    private static PlayerContext MakeContext(string root, string id)
    {
        var context = new PlayerContext { PlayerInfo = new Durango.Logic.Clusters.PlayerInfo
            { PlayerEntityId = id, PlayerName = id, PlayerLevel = 10 } };
        context.Initialize(Path.Combine(root, id + ".player"));
        context.TStone = 5000; context.WarpGem = context.WarpMatter = 1000;
        return context;
    }

    internal sealed class Link : IDisposable
    {
        public Connection Server { get; }
        private Connection Client { get; }
        public Player Player { get; }
        public List<object> Messages { get; } = new();
        private readonly List<(object Message, PacketHeader Header)> _replies = new();
        private readonly bool _simulatePlayer;

        public Link(PlayerContext context, World world, EconomyStore store, bool simulatePlayer = false)
        {
            _simulatePlayer = simulatePlayer;
            using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Loopback, 0)); listener.Listen(1);
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Connect(listener.LocalEndPoint);
            Server = new Connection(listener.Accept()); Client = new Connection(socket);
            Receive<Products>(); Receive<Commodities>(); Receive<Purchases>(); Receive<Purchased>();
            Receive<InventoryUpdated>(); Receive<Inventory>(); Receive<WalletUpdated>(); Receive<OK>(); Receive<Abort>();
            Receive<ProductSold>(); Receive<ProductStateUpdated>(); Receive<MarketCollectablePaymentExists>();
            Receive<MarketPaymentReceived>(); Receive<Statistics>(); Receive<AppearPlayer>();
            Receive<Equipments>(); Receive<DefoggedChunks>(); Receive<QuestCategories>();
            Receive<Points>(); Receive<QuestStarted>(); Receive<SetBaseMoveSpeed>(); Receive<Teleported>();
            Receive<CombatInteraction>(); Receive<Move>(); Receive<Damaged>(); Receive<SurvivalUpdated>();
            Receive<StatusEffects>(); Receive<Touched>(); Receive<Collectible>(); Receive<Info>();
            Receive<Messages.Timer>(); Receive<Rewarded>(); Receive<DisappearEntity>(); Receive<EntityDied>();
            Receive<BattleBegun>(); Receive<BattleEnded>(); Receive<AppearAnimal>(); Receive<Weather>();
            Receive<GardenDiff>(); Receive<DisappearEntityOnTile>();
            Receive<Collected>(); Receive<CollectibleChanged>(); Receive<ReplySequenceMark>();
            Receive<ToolNeeded>(); Receive<ArtifactState>(); Receive<ArtifactDisplay>();
            Receive<ExploredPOIs>(); Receive<WarpCosts>(); Receive<RegionMapInfo>();
            Receive<Messages.Region>(); Receive<Routes>();
            Receive<EstateLicense>(); Receive<EstateLicenses>(); Receive<EstateGrids>(); Receive<AppearArtifact>();
            Receive<Warehouse>(); Receive<WarehouseUpdated>(); Receive<SectionItems>();
            Receive<Quests>(); Receive<Messages.QuestState>(); Receive<NotifyQuestProceed>(); Receive<QuestRewardResults>();
            Receive<Recipes>(); Receive<ArtifactBlueprints>(); Receive<Skills>(); Receive<Failed>();
            Receive<Actions>(); Receive<SkillCategoryExperienced>(); Receive<ExpGained>();
            Server.StartReceive(); Client.StartReceive();
            Player = new Player(context.EntityId, Server, world, context, false, store);
            if (simulatePlayer) world.AddPlayer(Player);
            PumpUntil(() => Messages.OfType<AppearPlayer>().Any());
        }

        private void Receive<T>() => Client.Recv<T>((message, header) =>
        { Messages.Add(message); _replies.Add((message, header)); });

        public void Send<T>(T message)
        {
            if (!Client.Send(message)) throw new InvalidOperationException("Nao foi possivel enviar " + typeof(T).Name);
        }

        public TReply Request<T, TReply>(T message)
        {
            int start = _replies.Count;
            if (!Client.Send(message)) throw new InvalidOperationException("Nao foi possivel enviar " + typeof(T).Name);
            PumpUntil(() => _replies.Skip(start).Any(r => r.Header.ReplyOf != 0 && r.Message is TReply));
            return (TReply)_replies.Skip(start).First(r => r.Header.ReplyOf != 0 && r.Message is TReply).Message;
        }

        public void PumpUntil(Func<bool> completed)
        {
            var timer = Stopwatch.StartNew();
            while (!completed() && timer.ElapsedMilliseconds < 5000)
            { Client.Process(); Server.Process(); if (_simulatePlayer) Player?.Process(); Thread.Sleep(2); }
            if (!completed()) throw new TimeoutException("Resposta TCP nao recebida. Ultimas mensagens: " +
                string.Join(",", Messages.TakeLast(8).Select(m => m.GetType().Name)));
        }

        public void Dispose() { Server.Close(); Client.Close(); }
    }
}
