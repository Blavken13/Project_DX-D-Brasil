using System;
using System.Linq;
using Durango.Network;
using Messages;
using Shared.Market;

namespace Durango.Online;

public partial class Player
{
    private void RegisterMarketHandlers()
    {
        _connection.Recv(delegate(GetRegisteredProducts m, PacketHeader h) { MarketHistory("registered", m.Sort, m.Skip, h.Seq); });
        _connection.Recv(delegate(GetSoldProducts m, PacketHeader h) { MarketHistory("sold", m.Sort, m.Skip, h.Seq); });
        _connection.Recv(delegate(GetPurchasedProducts m, PacketHeader h) { MarketHistory("purchased", m.Sort, m.Skip, h.Seq); });
        _connection.Recv(delegate(GetExpiredProducts m, PacketHeader h) { MarketHistory("expired", m.Sort, m.Skip, h.Seq); });
        _connection.Recv(delegate(GetPersonalProducts m, PacketHeader h) { MarketHistory("personal", m.Sort, m.Skip, h.Seq); });
        _connection.Recv(delegate(GetSimilarProducts m, PacketHeader h)
        {
            var products = _economy?.Search(new SearchProducts { PrototypeId = m.PrototypeId, Level = new RangePredicate { Min = m.Level, Max = m.Level } }) ?? Array.Empty<Product>();
            Send(new Products { _Products = products.Take(Math.Clamp(m.Limit ?? 20, 1, 20)).ToArray() }, h.Seq);
        });
        _connection.Recv(delegate(RegisterMultipleProducts m, PacketHeader h) { RegisterMarketProducts(m.ItemIds, m.EachPrice, m.Duration, h.Seq); });
        _connection.Recv(delegate(RegisterProduct m, PacketHeader h) { RegisterMarketProducts(new[] { m.ItemId }, m.Price, m.Duration, h.Seq); });
        _connection.Recv(delegate(UnregisterProduct m, PacketHeader h)
        {
            if (!EconomyAvailable(h.Seq)) return;
            if (!_economy.Unregister(_context, m.ProductId, out string error)) { Send(new Abort { Text = error }, h.Seq); return; }
            Send(new ProductStateUpdated { ProductId = m.ProductId, State = ProductState.Unregistered });
            Send(default(OK), h.Seq);
        });
        _connection.Recv(delegate(WithdrawProduct m, PacketHeader h)
        {
            if (!EconomyAvailable(h.Seq)) return;
            if (!_economy.Withdraw(_context, m.ProductId, out var items, out string error)) { Send(new Abort { Text = error }, h.Seq); return; }
            SendEconomyInventory(items);
            Send(new ProductStateUpdated { ProductId = m.ProductId, State = ProductState.Withdrawn });
            Send(default(OK), h.Seq);
        });
        _connection.Recv(delegate(AddToFavoriteProducts m, PacketHeader h)
        {
            if (_economy?.Favorites(new[] { m.ProductId }).Length > 0 && _context.MarketFavorites.Count < 1000) _context.MarketFavorites.Add(m.ProductId);
            OnContextChanged();
            Send(BuildFavoriteProducts(), h.Seq);
        });
        _connection.Recv(delegate(RemoveFromFavoriteProducts m, PacketHeader h)
        {
            _context.MarketFavorites.Remove(m.ProductId ?? "");
            OnContextChanged();
            Send(BuildFavoriteProducts(), h.Seq);
        });
        _connection.Recv(delegate(MarketCollectPayment m, PacketHeader h) { CollectMarketPayments(m.ProductId ?? "", h.Seq, false); });
        _connection.Recv(delegate(MarketCollectAllPayments m, PacketHeader h) { CollectMarketPayments(null, h.Seq, true); });
        Send(new MarketCollectablePaymentExists { Exists = _economy?.HasPayments(EntityId) == true });
    }

    private bool EconomyAvailable(uint seq)
    {
        if (_economy != null) return true;
        Send(new Abort { Text = "Economia indisponivel neste servidor." }, seq);
        return false;
    }

    private void MarketHistory(string kind, SortCondition? sort, int skip, uint seq) =>
        Send(new Products { _Products = _economy?.History(EntityId, kind, sort, skip) ?? Array.Empty<Product>() }, seq);

    private Products BuildFavoriteProducts() => new() { _Products = _economy?.Favorites(_context.MarketFavorites) ?? Array.Empty<Product>() };

    private void RegisterMarketProducts(string[] ids, long price, float duration, uint seq)
    {
        if (!EconomyAvailable(seq)) return;
        if (!_economy.Register(_context, _world.TerrainId, ids, price, duration, out var products, out string error))
        { Send(new Abort { Text = error }, seq); return; }
        Send(new InventoryUpdated { EntityId = EntityId, RemovedItemIds = ids });
        SendWalletNow();
        Send(new Products { _Products = products }, seq);
    }

    private void BuyMarketProduct(string id, uint seq)
    {
        if (!EconomyAvailable(seq)) return;
        if (!_economy.Buy(_context, id, out var listing, out string error)) { Send(new Abort { Text = error }, seq); return; }
        SendEconomyInventory(listing.Product.Items);
        SendWalletNow();
        Send(default(OK), seq);
    }

    private void CollectMarketPayments(string id, uint seq, bool all)
    {
        if (!EconomyAvailable(seq)) return;
        if (!_economy.Collect(_context, id, out var products, out long total, out string error))
        {
            if (all && !_economy.HasPayments(EntityId)) Send(new Products { _Products = Array.Empty<Product>() }, seq);
            else Send(new Abort { Text = error }, seq);
            return;
        }
        SendWalletNow();
        foreach (var product in products) Send(new ProductStateUpdated { ProductId = product.Id, State = ProductState.PaymentReceived });
        Send(new MarketPaymentReceived { FirstItemName = products[0].Items[0].Name, ItemCount = products.Length, TotalPrice = total });
        Send(new MarketCollectablePaymentExists { Exists = _economy.HasPayments(EntityId) });
        if (all) Send(new Products { _Products = products }, seq); else Send(default(OK), seq);
    }

    public void NotifyMarketSale(EconomyStore.MarketListing listing)
    {
        Send(new ProductSold { Item = listing.Product.Items[0], Price = listing.Product.Price });
        Send(new ProductStateUpdated { ProductId = listing.Product.Id, State = ProductState.PaymentPending });
        Send(new MarketCollectablePaymentExists { Exists = true });
    }

    private void SendEconomyInventory(Item[] items) => Send(new InventoryUpdated { EntityId = EntityId, Items = items });
}
