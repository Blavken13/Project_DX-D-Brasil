using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Durango.Utils;
using Messages;
using Newtonsoft.Json.Linq;
using Shared.Economy;
using Yaml;

namespace Durango.Online;

// Preços nativos. Somente produtos cujo conteúdo completo pode ser entregue.
public sealed class ShopCatalog
{
    private readonly Dictionary<string, Definition> _definitions = new(StringComparer.Ordinal);
    private readonly decimal _listingRate, _lowSalesRate, _highSalesRate;
    private readonly long _threshold;
    public IReadOnlyDictionary<string, Definition> Definitions => _definitions;
    public Dictionary<string, string> Unavailable { get; } = new();

    public sealed class Definition
    {
        public string Id;
        public JObject Data;
        public Currency Currency;
        public long Price;
        public int MaxCount;
        public int PeriodDays;
        public int PeriodCount;
        public bool WeeklyReset;
        public int Mileage;
        public JObject Contents;
        public int Copies = 1;
        public bool PreviewItem;
        public bool AutoAccept;
    }

    public ShopCatalog()
    {
        var root = Json.ReadFromFile<JObject>("purchaser/commodities");
        var rows = root?["posted_commodities"] as JObject;
        if (rows != null)
            foreach (var row in rows.Properties())
            {
                var def = Parse(row.Name, row.Value as JObject, rows, out string reason);
                if (def != null) _definitions.Add(def.Id, def); else Unavailable[row.Name] = reason;
            }
        var market = Json.ReadFromFile<JObject>("constants")?["market"];
        _listingRate = Rate(market?["listing_fee_rate"]);
        _lowSalesRate = Rate(market?["sales_fee_rates"]?["under_threshold"]);
        _highSalesRate = Rate(market?["sales_fee_rates"]?["over_threshold"]);
        _threshold = (long?)market?["sales_fee_threshold"] ?? 1000;
        Console.WriteLine($"[loja] catalogo original: {_definitions.Count} produtos suportados; {Unavailable.Count} indisponiveis");
    }

    private static decimal Rate(JToken token) => decimal.TryParse((string)token, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate)
        ? Math.Clamp(rate, 0, 1) : 0;
    public long ListingFee(long price) => (long)Math.Max(0, price * _listingRate);
    public long SalesFee(long price)
    {
        long lower = Math.Min(price, _threshold);
        return (long)Math.Max(0, lower * _lowSalesRate + (price - lower) * _highSalesRate);
    }

    private static Definition Parse(string id, JObject data, JObject rows, out string reason)
    {
        reason = "Produto oculto, com pagamento externo ou mecanismo ainda nao suportado.";
        if (data == null || (bool?)data["invisible"] == true || !string.IsNullOrEmpty((string)data["iap_product_id"]) ||
            data["special_deal_duration"] != null || data["purchase_condition"] != null) return null;
        int type = (int?)data["type"] ?? -1;
        var currency = (Currency)((int?)data["price_currency"] ?? -1);
        long price = (long?)data["price_amount"] ?? -1;
        if (!EconomyStore.SupportsCurrency(currency) || price <= 0 || price > EconomyStore.BalanceLimit ||
            type is not (1 or 2 or 4 or 6 or 7)) return null;
        var contents = data["contents"] as JObject;
        int copies = 1;
        if (type == 1)
        {
            long amount = (long?)data["gem_amount"] ?? 0;
            if (amount <= 0) return null;
            contents = new JObject { ["money"] = new JArray(new JObject { ["currency"] = 1, ["amount"] = amount }) };
        }
        if (type == 6)
        {
            string sourceId = (string)data["source_commodity_id"];
            contents = sourceId == null ? null : rows[sourceId]?["contents"] as JObject;
            copies = (int?)data["count"] ?? 0;
            if (copies < 1 || copies > 100) return null;
        }
        if (contents == null || !contents.Properties().Any() ||
            contents.Properties().Any(p => p.Name is not ("items" or "money" or "weighted_items" or "capsulated_modulars"))) return null;
        if (!ValidateContents(contents)) { reason = "Conteudo incompleto ou item/blueprint nao disponivel."; return null; }
        return new Definition
        {
            Id = id, Data = data, Currency = currency, Price = price, Contents = contents, Copies = copies,
            PreviewItem = type is 4 or 6 or 7,
            AutoAccept = (data["tags"] as JArray)?.Any(t => ((int)t & 16) != 0) == true,
            MaxCount = Math.Max(0, (int?)data["purchase_limit"]?["max_count"] ?? 0),
            PeriodDays = Math.Max(0, (int?)data["purchase_limit"]?["periodic_counts_limit"]?["days"] ??
                (int?)data["purchase_limit"]?["periodic_limit"]?["days"] ?? 0),
            PeriodCount = Math.Max(0, (int?)data["purchase_limit"]?["periodic_counts_limit"]?["counts"] ??
                (data["purchase_limit"]?["periodic_limit"] != null ? 1 : 0)),
            WeeklyReset = (string)data["purchase_limit"]?["periodic_counts_limit"]?["reset_type"] == "weekly",
            Mileage = Math.Max(0, (int?)data["bonus_mileage"] ?? 0)
        };
    }

    private static bool ValidateContents(JObject contents)
    {
        long units = 0;
        foreach (var item in (contents["items"] as JArray ?? new JArray()).Concat(contents["weighted_items"] as JArray ?? new JArray()))
        {
            var id = (string)item["prototype_id"];
            int level = (int?)item["level"] ?? 1;
            if (string.IsNullOrEmpty(id) || PrototypeYaml.GetItemPrototype(id, level) == null ||
                (int?)item["count"] is <= 0 || (double?)item["weight"] is <= 0 || !CanBuildItem(id)) return false;
            units += (int?)item["count"] ?? 1;
        }
        foreach (var modular in contents["capsulated_modulars"] as JArray ?? new JArray())
        {
            if (PrototypeYaml.GetItemPrototype((string)modular["prototype_id"]) == null ||
                BlueprintStore.GetBlueprint((string)modular["artifact_id"]) == null ||
                (int?)modular["size_x"] is not (> 0 and <= 16) || (int?)modular["size_y"] is not (> 0 and <= 16)) return false;
            units++;
        }
        foreach (var money in contents["money"] as JArray ?? new JArray())
            if (!EconomyStore.SupportsCurrency((Currency)((int?)money["currency"] ?? -1)) ||
                (long?)money["amount"] is not (> 0 and <= EconomyStore.BalanceLimit)) return false;
        return units <= 200 && (units > 0 || (contents["money"] as JArray)?.Count > 0);
    }

    private static bool CanBuildItem(string id) => !id.StartsWith("capsulated_", StringComparison.Ordinal) ||
        BlueprintStore.GetBlueprint(id["capsulated_".Length..]) != null;

    public static bool InPeriod(Definition definition, double now)
    {
        var periods = definition.Data["purchase_limit"]?["purchasable_times"] as JArray;
        if (periods == null || periods.Count == 0) return true;
        var date = DateTimeOffset.FromUnixTimeSeconds((long)now);
        return periods.Any(p =>
            (!DateTimeOffset.TryParse((string)p["purchase_starts_at"], out var start) || date >= start) &&
            (!DateTimeOffset.TryParse((string)p["purchase_ends_at"], out var end) || date < end));
    }

    public ShopReceiptContent[] Generate(Definition definition)
    {
        var results = new List<ShopReceiptContent>();
        for (int copy = 0; copy < definition.Copies; copy++)
        {
            var result = new ShopReceiptContent();
            foreach (var spec in definition.Contents["items"] as JArray ?? new JArray())
                for (int i = 0; i < ((int?)spec["count"] ?? 1); i++) result.Items.Add(MakeItem(spec));
            var weighted = definition.Contents["weighted_items"] as JArray;
            if (weighted?.Count > 0)
            {
                double total = weighted.Sum(s => (double?)s["weight"] ?? 1);
                double roll = System.Random.Shared.NextDouble() * total;
                JToken selected = weighted.Last;
                foreach (var spec in weighted) { roll -= (double?)spec["weight"] ?? 1; if (roll < 0) { selected = spec; break; } }
                result.Items.Add(MakeItem(selected));
            }
            foreach (var spec in definition.Contents["capsulated_modulars"] as JArray ?? new JArray()) result.Items.Add(MakeModular(spec));
            foreach (var money in definition.Contents["money"] as JArray ?? new JArray())
            {
                var currency = EconomyStore.NormalizeCurrency((Currency)(int)money["currency"]);
                result.Money[currency] = checked(result.Money.GetValueOrDefault(currency) + (long)money["amount"]);
            }
            results.Add(result);
        }
        return results.ToArray();
    }

    internal static Item MakeItem(JToken spec)
    {
        string id = (string)spec["prototype_id"];
        var item = Cheats.MakeItem(id, (int?)spec["level"] ?? 1) ?? throw new InvalidOperationException("Item ausente: " + id);
        var colors = spec["colors"] as JArray;
        if (colors?.Count > 0) item.ColorR = (string)colors[0];
        if (colors?.Count > 1) item.ColorG = (string)colors[1];
        if (colors?.Count > 2) item.ColorB = (string)colors[2];
        if (id.StartsWith("capsulated_", StringComparison.Ordinal)) item = AddCapsule(item, id["capsulated_".Length..], null);
        return item;
    }

    private static Item MakeModular(JToken spec) => AddCapsule(
        Cheats.MakeItem((string)spec["prototype_id"], (int?)spec["level"] ?? 1) ?? throw new InvalidOperationException("Capsula ausente."),
        (string)spec["artifact_id"], spec);

    private static Item AddCapsule(Item item, string blueprintId, JToken modular)
    {
        var blueprint = BlueprintStore.GetBlueprint(blueprintId) ?? throw new InvalidOperationException("Blueprint ausente: " + blueprintId);
        var artifact = Cheats.MakeAppearArtifact(new[] { "artifact", blueprint.EntityType.ToString(CultureInfo.InvariantCulture) }, out _) ??
            throw new InvalidOperationException("Nao foi possivel gerar a capsula.");
        var size = modular == null ? blueprint.Size : new Point2((int)modular["size_x"], (int)modular["size_y"]);
        if (modular != null)
        {
            artifact.Display.Parts = modular["overridden_parts"]?.ToObject<Dictionary<string, string>>() ?? artifact.Display.Parts;
            artifact.Display.Textures = modular["overridden_textures"]?.ToObject<Dictionary<string, string>>() ?? artifact.Display.Textures;
        }
        artifact.States.Level = checked((byte)item.Level);
        item.Ext = new ArtifactCapsule
        {
            EntityId = artifact.EntityId, BlueprintId = blueprintId, ArtifactLevel = item.Level,
            Display = artifact.Display, State = artifact.States, OccupySize = size,
            Tags = artifact.Tags._Tags ?? Array.Empty<Tag>(), Performance = Array.Empty<Performance>(), LookNames = new()
        };
        return item;
    }

    public sealed class ShopReceiptContent
    {
        public List<Item> Items = new();
        public Dictionary<Currency, long> Money = new();
    }
}
