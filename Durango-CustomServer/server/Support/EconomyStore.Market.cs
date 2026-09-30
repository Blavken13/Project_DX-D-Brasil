using System;
using System.Collections.Generic;
using System.Linq;
using Messages;
using Shared.Economy;
using Shared.Market;

namespace Durango.Online;

public sealed partial class EconomyStore
{
    public bool Register(PlayerContext seller, string regionId, string[] ids, long eachPrice, float duration,
        out Product[] products, out string error)
    {
        lock (_sync)
        {
            Recover(seller);
            products = Array.Empty<Product>();
            error = "Selecione itens validos e um preco positivo.";
            if (ids == null || ids.Length == 0 || ids.Length > 100 || ids.Any(string.IsNullOrEmpty) ||
                ids.Distinct().Count() != ids.Length || eachPrice <= 0 || eachPrice > BalanceLimit ||
                !float.IsFinite(duration) || duration <= 0 || duration > 86400) return false;
            var items = ids.Select(id => seller.InventoryItems.FirstOrDefault(i => i.Id == id)).ToArray();
            if (items.Any(i => string.IsNullOrEmpty(i.Id) || !ItemTradeRules.CanTrade(i) ||
                    seller.EquippedItems.ContainsValue(i.Id) ||
                    i.Durability == null || i.Durability.Max() <= 0 || i.Durability.Get() / i.Durability.Max() < .9f))
            {
                error = "Itens equipados, nao negociaveis ou com durabilidade abaixo de 90% nao podem ser anunciados.";
                return false;
            }
            long fee = Catalog.ListingFee(eachPrice);
            long totalFee = checked(fee * ids.Length);
            if (seller.TStone < totalFee) { error = "T-Stones insuficientes para a taxa de anuncio."; return false; }
            var next = Next();
            double now = _clock();
            products = items.Select(i => new Product
            {
                Id = Guid.NewGuid().ToString("N"), RegionId = regionId ?? "1", ListedAt = now,
                ExpiresAt = now + duration, DeletesAt = 253402300799d, Price = eachPrice,
                Fee = Catalog.SalesFee(eachPrice), Currency = Currency.TStone,
                Items = new[] { i }, Level = i.Level, Durability = i.Durability.Get(), State = ProductState.Registered
            }).ToArray();
            foreach (var product in products) next.Listings.Add(new MarketListing { SellerId = seller.EntityId, Product = product, ListingFee = fee });
            return Commit(next, seller, new EconomyEffect
            {
                RemoveItemIds = ids, Balances = new() { [Currency.TStone] = seller.TStone - totalFee }
            }, out error);
        }
    }

    public bool Buy(PlayerContext buyer, string productId, out MarketListing bought, out string error)
    {
        lock (_sync)
        {
            Recover(buyer);
            bought = null;
            var listing = _state.Listings.FirstOrDefault(l => l.Product.Id == productId);
            error = "Este anuncio nao esta mais disponivel.";
            if (listing == null || CurrentProduct(listing).State != ProductState.Registered) return false;
            if (listing.SellerId == buyer.EntityId) { error = "Voce nao pode comprar seu proprio anuncio."; return false; }
            if (buyer.TStone < listing.Product.Price) { error = "T-Stones insuficientes."; return false; }
            if (!CanReceive(buyer, listing.Product.Items)) { error = "Inventario cheio. Libere espaco antes de comprar."; return false; }
            var product = listing.Product;
            product.State = ProductState.PaymentPending;
            product.PurchasedAt = _clock();
            bought = listing with { BuyerId = buyer.EntityId, Product = product };
            var next = Next();
            next.Listings[next.Listings.IndexOf(listing)] = bought;
            bool committed = Commit(next, buyer, new EconomyEffect
            {
                AddItems = product.Items, Balances = new() { [Currency.TStone] = buyer.TStone - product.Price }
            }, out error);
            if (committed) ProductBought?.Invoke(bought);
            return committed;
        }
    }

    public bool Unregister(PlayerContext seller, string productId, out string error)
    {
        lock (_sync)
        {
            Recover(seller);
            var listing = _state.Listings.FirstOrDefault(l => l.Product.Id == productId && l.SellerId == seller.EntityId);
            error = "Anuncio inexistente, vendido ou de outro jogador.";
            if (listing == null || CurrentProduct(listing).State != ProductState.Registered) return false;
            var product = listing.Product;
            product.State = ProductState.Unregistered;
            var next = Next();
            next.Listings[next.Listings.IndexOf(listing)] = listing with { Product = product };
            return Commit(next, seller, new EconomyEffect(), out error);
        }
    }

    public bool Withdraw(PlayerContext seller, string productId, out Item[] items, out string error)
    {
        lock (_sync)
        {
            Recover(seller);
            items = Array.Empty<Item>();
            var listing = _state.Listings.FirstOrDefault(l => l.Product.Id == productId && l.SellerId == seller.EntityId);
            error = "Este anuncio nao possui itens disponiveis para retirada.";
            if (listing == null || CurrentProduct(listing).State is not (ProductState.Expired or ProductState.Unregistered)) return false;
            items = listing.Product.Items;
            if (!CanReceive(seller, items)) { error = "Inventario cheio."; return false; }
            var product = listing.Product;
            product.State = ProductState.Withdrawn;
            var next = Next();
            next.Listings[next.Listings.IndexOf(listing)] = listing with { Product = product };
            return Commit(next, seller, new EconomyEffect { AddItems = items }, out error);
        }
    }

    public bool Collect(PlayerContext seller, string productId, out Product[] collected, out long total, out string error)
    {
        lock (_sync)
        {
            Recover(seller);
            var listings = _state.Listings.Where(l => l.SellerId == seller.EntityId &&
                l.Product.State == ProductState.PaymentPending && (productId == null || l.Product.Id == productId)).ToArray();
            collected = Array.Empty<Product>();
            total = listings.Sum(l => l.Product.Price - l.Product.Fee);
            error = "Nenhum pagamento disponivel para receber.";
            if (listings.Length == 0) return false;
            if (total > BalanceLimit - seller.TStone) { error = "Saldo de T-Stones no limite. Receba as vendas separadamente ou use parte do saldo."; return false; }
            var next = Next();
            collected = listings.Select(l => { var p = l.Product; p.State = ProductState.PaymentReceived; return p; }).ToArray();
            for (int i = 0; i < listings.Length; i++) next.Listings[next.Listings.IndexOf(listings[i])] = listings[i] with { Product = collected[i] };
            return Commit(next, seller, new EconomyEffect { Balances = new() { [Currency.TStone] = seller.TStone + total } }, out error);
        }
    }

    private Product CurrentProduct(MarketListing listing)
    {
        var product = listing.Product;
        if (product.State == ProductState.Registered && product.ExpiresAt <= _clock()) product.State = ProductState.Expired;
        return product;
    }

    public Product[] Search(SearchProducts search)
    {
        lock (_sync) return MarketManager.Search(_state.Listings.Select(CurrentProduct).Where(p => p.State == ProductState.Registered), search);
    }

    public Product[] History(string entityId, string kind, SortCondition? sort, int skip)
    {
        lock (_sync)
        {
            IEnumerable<MarketListing> rows = kind == "purchased"
                ? _state.Listings.Where(l => l.BuyerId == entityId)
                : _state.Listings.Where(l => l.SellerId == entityId);
            var products = rows.Select(CurrentProduct);
            products = kind switch
            {
                "registered" => products.Where(p => p.State == ProductState.Registered),
                "sold" => products.Where(p => p.State is ProductState.PaymentPending or ProductState.PaymentReceived),
                "expired" => products.Where(p => p.State is ProductState.Expired or ProductState.Unregistered),
                "purchased" => products.Select(p => { p.State = ProductState.Sold; return p; }),
                _ => products
            };
            return MarketManager.Page(products, sort, skip);
        }
    }

    public Product[] Favorites(IEnumerable<string> ids)
    {
        lock (_sync)
        {
            var favoriteIds = new HashSet<string>(ids ?? Array.Empty<string>());
            return _state.Listings.Select(CurrentProduct).Where(p => favoriteIds.Contains(p.Id) && p.State == ProductState.Registered).ToArray();
        }
    }

    public bool HasPayments(string entityId)
    {
        lock (_sync) return _state.Listings.Any(l => l.SellerId == entityId && l.Product.State == ProductState.PaymentPending);
    }
}
