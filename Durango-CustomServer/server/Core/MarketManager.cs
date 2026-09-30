using System;
using System.Collections.Generic;
using System.Linq;
using Messages;
using Shared.Market;
using Yaml;

namespace Durango.Online;

// Filtros usados pelo cliente original. Somente anúncios reais entram na busca.
public static class MarketManager
{
    public static Product[] Search(IEnumerable<Product> products, SearchProducts search)
    {
        products = products.Where(p => p.Items?.Length > 0 &&
            (!search.Price.HasValue || (p.Currency == search.Price.Value.Currency &&
                (!search.Price.Value.Min.HasValue || p.Price >= search.Price.Value.Min.Value) &&
                (!search.Price.Value.Max.HasValue || p.Price <= search.Price.Value.Max.Value))) &&
            (!search.Level.HasValue ||
                ((!search.Level.Value.Min.HasValue || p.Level >= search.Level.Value.Min.Value) &&
                 (!search.Level.Value.Max.HasValue || p.Level <= search.Level.Value.Max.Value))) &&
            p.Items.Any(i => Matches(i, search)));
        return Page(products, search.Sort, search.Skip);
    }

    private static bool Matches(Item item, SearchProducts search)
    {
        var prototype = PrototypeYaml.GetItemPrototype(item.Prototype);
        if (prototype == null) return false;
        if (!string.IsNullOrWhiteSpace(search.ItemName) &&
            (item.Name ?? "").IndexOf(search.ItemName, StringComparison.OrdinalIgnoreCase) < 0) return false;
        if (!string.IsNullOrEmpty(search.PrototypeId) && item.Prototype != search.PrototypeId) return false;
        if (!string.IsNullOrEmpty(search.Category) && prototype.Category != search.Category) return false;
        if (search.SubCategories?.Length > 0 && !search.SubCategories.Any(s => prototype.SubCategories?.Contains(s) == true)) return false;
        if (search.NestedTags?.Length > 0 && !search.NestedTags.All(group => group?.Length > 0 &&
            group.Any(tag => item.Tags?.Any(t => t.Id == tag) == true))) return false;
        return true;
    }

    public static Product[] Page(IEnumerable<Product> products, SortCondition? condition, int skip, int limit = 20)
    {
        var sort = condition ?? new SortCondition { Field = ProductSortField.RegisteredAt, Ascending = false };
        Func<Product, IComparable> key = sort.Field switch
        {
            ProductSortField.Price => p => p.Price,
            ProductSortField.ExpiresAt => p => p.ExpiresAt,
            ProductSortField.PurchasedAt => p => p.PurchasedAt ?? 0,
            ProductSortField.Level => p => p.Level,
            ProductSortField.Durability => p => p.Durability,
            ProductSortField.State => p => (int)p.State,
            _ => p => p.ListedAt
        };
        var ordered = sort.Ascending ? products.OrderBy(key) : products.OrderByDescending(key);
        return ordered.ThenBy(p => p.Id, StringComparer.Ordinal).Skip(Math.Max(0, skip)).Take(Math.Clamp(limit, 1, 100)).ToArray();
    }
}
