using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Durango.Utils;
using Messages;
using Newtonsoft.Json;
using Shared.Economy;
using Shared.Market;

namespace Durango.Online;

// Um ledger por cluster, compartilhado por todas as ilhas. O arquivo do ledger é
// confirmado antes de alterar inventários/saldos. Após crash, efeitos posteriores
// ao checkpoint do personagem são reaplicados uma única vez, inclusive com vendedor offline.
public sealed partial class EconomyStore
{
    public const long BalanceLimit = 99_999_999;
    private readonly object _sync = new();
    private readonly string _path;
    private readonly Func<double> _clock;
    private EconomyState _state;
    public ShopCatalog Catalog { get; }
    public event Action<MarketListing> ProductBought;

    public EconomyStore(string path, ShopCatalog catalog, Func<double> clock = null)
    {
        _path = path;
        _clock = clock ?? Times.UnixTimeNow;
        Catalog = catalog;
        // Não voltar silenciosamente a um ledger anterior: personagens podem já
        // conter operações posteriores. Preservar os arquivos para recuperação.
        if (!File.Exists(path))
        {
            if (File.Exists(path + ".bak")) throw new InvalidDataException("Ledger ausente, mas existe backup: " + path);
            _state = new EconomyState();
        }
        else
        {
            _state = Json.Read<EconomyState>(File.ReadAllBytes(path));
            if (_state == null || _state.Version != 1 || _state.Sequence < 0 ||
                _state.Listings == null || _state.Receipts == null || _state.Effects == null)
                throw new InvalidDataException("Ledger da economia invalido: " + path);
            foreach (var listing in _state.Listings) NormalizeItems(listing.Product.Items);
            foreach (var receipt in _state.Receipts) NormalizeItems(receipt.Items);
            foreach (var effect in _state.Effects) NormalizeItems(effect.AddItems);
        }
    }

    private static void NormalizeItems(Item[] items)
    {
        if (items == null) return;
        var list = items.ToList();
        ItemExtRepair.Normalize(list, "economia");
        list.CopyTo(items);
    }

    public void Recover(PlayerContext context)
    {
        lock (_sync)
        {
            ClanStore.EnsureLoaded(context.Path);
            foreach (var effect in _state.Effects.Where(e => e.ClanOperation != null))
                ClanStore.ApplyEconomic(effect.ClanOperation, effect.Sequence);
            if (context.EconomySequence > _state.Sequence)
                throw new InvalidDataException("Personagem possui transacoes posteriores ao ledger: " + context.EntityId);
            foreach (var effect in _state.Effects.Where(e => e.EntityId == context.EntityId && e.Sequence > context.EconomySequence))
                Apply(context, effect);
        }
    }

    private bool Commit(EconomyState next, PlayerContext context, EconomyEffect effect, out string error)
    {
        error = null;
        effect.EntityId = context.EntityId;
        effect.Sequence = next.Sequence = _state.Sequence + 1;
        next.Effects.Add(effect);
        if (!SafeSave.WriteAtomic(_path, Json.WriteToBytes(next), "economia"))
        {
            error = "Nao foi possivel salvar a transacao. Nenhum item ou saldo foi alterado.";
            return false;
        }
        _state = next;
        Apply(context, effect);
        return true;
    }

    private EconomyState Next() => new()
    {
        Sequence = _state.Sequence,
        Listings = new List<MarketListing>(_state.Listings),
        Receipts = new List<ShopReceipt>(_state.Receipts),
        Effects = new List<EconomyEffect>(_state.Effects)
    };

    public bool Spend(PlayerContext context, Currency currency, long amount, out string error)
    {
        lock (_sync)
        {
            error = null;
            if (amount < 0 || !SupportsCurrency(currency)) { error = "Custo inválido."; return false; }
            long balance = Balance(context, currency);
            if (balance < amount) { error = "Saldo insuficiente para expandir o acampamento."; return false; }
            if (amount == 0) return true;
            return Commit(Next(), context, new EconomyEffect { Balances = new() { [currency] = balance - amount } }, out error);
        }
    }

    private static void Apply(PlayerContext context, EconomyEffect effect)
    {
        ClanStore.ApplyEconomic(effect.ClanOperation, effect.Sequence);
        effect.ClanEstate?.RuntimeWorld?.ApplyClanEstate(effect.ClanEstate, effect.Sequence);
        if (context.EconomySequence >= effect.Sequence) return;
        if (effect.RemoveItemIds != null)
            context.InventoryItems.RemoveAll(i => effect.RemoveItemIds.Contains(i.Id));
        if (effect.AddItems != null)
            // Gauges e extensões são referências mutáveis: o inventário não deve
            // alterar o histórico confirmado quando o jogador usa/repara o item.
            foreach (var item in CopyItems(effect.AddItems))
                if (!context.InventoryItems.Any(i => i.Id == item.Id)) context.InventoryItems.Add(item);
        if (effect.Balances != null)
            foreach (var pair in effect.Balances) SetBalance(context, pair.Key, pair.Value);
        context.EconomySequence = effect.Sequence;
        context.Save();
    }

    private static Item[] CopyItems(Item[] items)
    {
        var copy = Json.Read<Item[]>(Json.Write(items));
        NormalizeItems(copy);
        return copy;
    }

    public static Currency NormalizeCurrency(Currency currency) => currency switch
    {
        Currency.MobileCoin or Currency.PcCoin => Currency.Coin,
        _ => currency
    };

    public static bool SupportsCurrency(Currency currency) => NormalizeCurrency(currency) is
        Currency.TStone or Currency.Gem or Currency.Coin or Currency.CashshopMileage or Currency.WarpMatter;

    public static long Balance(PlayerContext context, Currency currency) => NormalizeCurrency(currency) switch
    {
        Currency.TStone => context.TStone,
        Currency.Gem => context.WarpGem,
        Currency.Coin => context.DurangoCoin,
        Currency.CashshopMileage => context.ShopMileage,
        Currency.WarpMatter => context.WarpMatter,
        _ => 0
    };

    private static void SetBalance(PlayerContext context, Currency currency, long value)
    {
        switch (NormalizeCurrency(currency))
        {
            case Currency.TStone: context.TStone = value; break;
            case Currency.Gem: context.WarpGem = value; break;
            case Currency.Coin: context.DurangoCoin = value; break;
            case Currency.CashshopMileage: context.ShopMileage = value; break;
            case Currency.WarpMatter: context.WarpMatter = value; break;
        }
    }

    private static bool CanReceive(PlayerContext context, Item[] items) =>
        context.InventoryItems.Sum(i => (long)Math.Max(1, i.Size)) + items.Sum(i => (long)Math.Max(1, i.Size)) <= Player.InventoryCapacity(context);

    public sealed class EconomyState
    {
        public int Version = 1;
        public long Sequence;
        public List<MarketListing> Listings = new();
        public List<ShopReceipt> Receipts = new();
        public List<EconomyEffect> Effects = new();
    }

    public sealed class EconomyEffect
    {
        [JsonProperty] internal ClanEconomyOperation ClanOperation;
        [JsonProperty] internal ClanEstateOperation ClanEstate;
        public long Sequence;
        public string EntityId;
        public string[] RemoveItemIds;
        public Item[] AddItems;
        // Valores finais, em vez de somas: uma recuperação não soma dinheiro novamente.
        public Dictionary<Currency, long> Balances;
    }

    public sealed record MarketListing
    {
        public string SellerId;
        public string BuyerId;
        public Product Product;
        public long ListingFee;
    }

    public sealed record ShopReceipt
    {
        public string Id;
        public string EntityId;
        public string CommodityId;
        public string OrderId;
        public double PurchasedAt;
        public double? AcceptedAt;
        public bool PreviewItem;
        public Item[] Items = Array.Empty<Item>();
        public Dictionary<Currency, long> Money = new();

        public Purchase Message() => new()
        {
            Id = Id, CommodityId = CommodityId, PurchasedAt = PurchasedAt,
            AcceptedAt = AcceptedAt, ExpiresAt = 253402300799d,
            Content = PreviewItem && Items.Length == 1 ? new ItemPurchaseContent { Item = Items[0] } : null
        };
    }
}
