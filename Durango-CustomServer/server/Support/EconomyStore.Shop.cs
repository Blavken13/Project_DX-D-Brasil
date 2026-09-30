using System;
using System.Collections.Generic;
using System.Linq;
using Messages;
using Shared.Economy;

namespace Durango.Online;

public sealed partial class EconomyStore
{
    // No Alpha, o reset semanal acontece na segunda-feira, 00:00 UTC.
    private double PeriodStart(ShopCatalog.Definition definition) => definition.WeeklyReset
        ? 345600d + Math.Floor((_clock() - 345600d) / 604800d) * 604800d
        : _clock() - definition.PeriodDays * 86400d;

    private double[] Orders(string entityId, string commodityId) => _state.Receipts
        .Where(r => r.EntityId == entityId && r.CommodityId == commodityId)
        .GroupBy(r => r.OrderId).Select(g => g.First().PurchasedAt).OrderBy(t => t).ToArray();

    private CommodityInfo Commodity(string entityId, ShopCatalog.Definition definition)
    {
        var times = Orders(entityId, definition.Id);
        var info = new CommodityInfo { Id = definition.Id,
            MaxPurchasableCount = definition.MaxCount > 0 ? Math.Max(0, definition.MaxCount - times.Length) : null };
        if (definition.PeriodCount > 0 && definition.PeriodDays > 0)
        {
            var recent = times.Where(t => t >= PeriodStart(definition)).ToArray();
            info.PeriodicPurchasableCount = Math.Max(0, definition.PeriodCount - recent.Length);
            if (recent.Length >= definition.PeriodCount)
                info.PeriodicPurchasableAt = definition.WeeklyReset ? PeriodStart(definition) + 604800d :
                    recent[recent.Length - definition.PeriodCount] + definition.PeriodDays * 86400d;
        }
        return info;
    }

    public CommodityInfo[] Commodities(string entityId)
    {
        lock (_sync) return Catalog.Definitions.Values.Where(d => ShopCatalog.InPeriod(d, _clock()))
            .Select(d => Commodity(entityId, d)).ToArray();
    }

    public Purchase[] Purchases(string entityId)
    {
        lock (_sync) return _state.Receipts.Where(r => r.EntityId == entityId && !r.AcceptedAt.HasValue).Select(r => r.Message()).ToArray();
    }

    public UserFirstPurchase[] FirstPurchases(string entityId)
    {
        lock (_sync) return _state.Receipts.Where(r => r.EntityId == entityId).GroupBy(r => r.CommodityId)
            .Select(g => new UserFirstPurchase { CommodityId = g.Key, PurchaseId = g.OrderBy(r => r.PurchasedAt).First().Id }).ToArray();
    }

    public bool Purchase(PlayerContext buyer, string commodityId, out Messages.Purchase[] purchases, out string error)
        => Purchase(buyer, commodityId, out purchases, out _, out error);

    public bool Purchase(PlayerContext buyer, string commodityId, out Messages.Purchase[] purchases, out Item[] delivered, out string error)
    {
        lock (_sync)
        {
            Recover(buyer);
            purchases = Array.Empty<Messages.Purchase>();
            delivered = Array.Empty<Item>();
            error = "Este produto nao esta disponivel na loja.";
            if (commodityId == null || !Catalog.Definitions.TryGetValue(commodityId, out var definition) ||
                !ShopCatalog.InPeriod(definition, _clock())) return false;
            var available = Commodity(buyer.EntityId, definition);
            if (available.MaxPurchasableCount == 0 || available.PeriodicPurchasableCount == 0)
            { error = "Limite de compras deste produto atingido."; return false; }
            long balance = Balance(buyer, definition.Currency);
            if (balance < definition.Price) { error = "Saldo insuficiente para comprar este produto."; return false; }
            var currency = NormalizeCurrency(definition.Currency);
            var balances = new Dictionary<Currency, long> { [currency] = balance - definition.Price };
            if (definition.Mileage > 0)
            {
                long mileage = balances.GetValueOrDefault(Currency.CashshopMileage, buyer.ShopMileage);
                if (mileage > BalanceLimit - definition.Mileage) { error = "Saldo de pontos da loja no limite."; return false; }
                balances[Currency.CashshopMileage] = mileage + definition.Mileage;
            }
            ShopCatalog.ShopReceiptContent[] contents;
            try { contents = Catalog.Generate(definition); }
            catch (Exception ex)
            {
                Console.WriteLine($"[loja] produto {commodityId} nao pode ser gerado: {ex.Message}");
                error = "O conteudo deste produto nao pode ser entregue. Nenhum saldo foi debitado.";
                return false;
            }
            string orderId = Guid.NewGuid().ToString("N");
            var receipts = contents.Select(c => new ShopReceipt
            {
                Id = Guid.NewGuid().ToString("N"), OrderId = orderId, CommodityId = commodityId,
                EntityId = buyer.EntityId, PurchasedAt = _clock(), PreviewItem = definition.PreviewItem,
                Items = c.Items.ToArray(), Money = c.Money
            }).ToArray();
            if (definition.AutoAccept)
            {
                delivered = receipts.SelectMany(r => r.Items).ToArray();
                if (!CanReceive(buyer, delivered)) { error = "Inventario cheio. Libere espaco antes de comprar."; return false; }
                foreach (var pair in receipts.SelectMany(r => r.Money))
                {
                    long current = balances.GetValueOrDefault(pair.Key, Balance(buyer, pair.Key));
                    if (pair.Value > BalanceLimit - current) { error = "Saldo de moedas no limite."; return false; }
                    balances[pair.Key] = current + pair.Value;
                }
                receipts = receipts.Select(r => r with { AcceptedAt = _clock() }).ToArray();
            }
            var next = Next();
            next.Receipts.AddRange(receipts);
            if (!Commit(next, buyer, new EconomyEffect { Balances = balances, AddItems = delivered }, out error)) return false;
            purchases = receipts.Select(r => r.Message()).ToArray();
            return true;
        }
    }

    public bool Accept(PlayerContext buyer, string purchaseId, out Item[] items, out string error)
    {
        lock (_sync)
        {
            Recover(buyer);
            items = Array.Empty<Item>();
            var receipts = _state.Receipts.Where(r => r.EntityId == buyer.EntityId && !r.AcceptedAt.HasValue &&
                (purchaseId == null || r.Id == purchaseId)).ToArray();
            error = "Compra inexistente, ja recebida ou de outro jogador.";
            if (receipts.Length == 0) return false;
            items = receipts.SelectMany(r => r.Items).ToArray();
            if (!CanReceive(buyer, items)) { error = "Inventario cheio. A compra continua disponivel para receber na loja."; return false; }
            var balances = new Dictionary<Currency, long>();
            foreach (var pair in receipts.SelectMany(r => r.Money))
            {
                long current = balances.GetValueOrDefault(pair.Key, Balance(buyer, pair.Key));
                if (pair.Value > BalanceLimit - current) { error = "Saldo de moedas no limite. Use parte do saldo antes de receber."; return false; }
                balances[pair.Key] = current + pair.Value;
            }
            var next = Next();
            foreach (var receipt in receipts) next.Receipts[next.Receipts.IndexOf(receipt)] = receipt with { AcceptedAt = _clock() };
            return Commit(next, buyer, new EconomyEffect { AddItems = items, Balances = balances }, out error);
        }
    }
}
