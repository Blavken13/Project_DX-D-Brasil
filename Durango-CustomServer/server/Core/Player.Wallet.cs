using System;
using System.Collections.Generic;
using Durango.Network;
using Messages;
using Shared.Economy;

namespace Durango.Online;

/// <summary>
/// Wallet persistente por personagem.
///
/// Etapa 02:
/// - T-Stone -> Currency.TStone.
/// - Warp Gem -> Currency.Gem.
/// - Durango Coin -> um único saldo publicado como MobileCoin e PcCoin.
///
/// O Alpha não possui pagamento com dinheiro real; todos os saldos ficam em UnpaidBalances.
/// </summary>
public partial class Player
{
    private const long MaxCurrencyBalance = 99_999_999L;

    public long TStone => _context?.TStone ?? 0L;
    public long WarpGem => _context?.WarpGem ?? 0L;
    public long DurangoCoin => _context?.DurangoCoin ?? 0L;

    public Wallet BuildWallet()
    {
        long coin = DurangoCoin;
        return new Wallet
        {
            PaidBalances = new Dictionary<Currency, long>(),
            UnpaidBalances = new Dictionary<Currency, long>
            {
                { Currency.TStone, TStone },
                { Currency.Gem, WarpGem },
                { Currency.MobileCoin, coin },
                { Currency.PcCoin, coin },
                { Currency.CashshopMileage, _context?.ShopMileage ?? 0 },
                { Currency.WarpMatter, _context?.WarpMatter ?? 0 }
            },
            Vouchers = Array.Empty<VoucherInfo>()
        };
    }

    public long GetCurrencyBalance(Currency currency)
    {
        return NormalizeCurrency(currency) switch
        {
            WalletCurrency.TStone => TStone,
            WalletCurrency.WarpGem => WarpGem,
            WalletCurrency.DurangoCoin => DurangoCoin,
            WalletCurrency.ShopMileage => _context?.ShopMileage ?? 0,
            WalletCurrency.WarpMatter => _context?.WarpMatter ?? 0,
            _ => 0L
        };
    }

    public bool AddCurrency(Currency currency, long amount, string reason)
    {
        if (_context == null || amount <= 0) return false;

        WalletCurrency normalized = NormalizeCurrency(currency);
        if (normalized == WalletCurrency.Unsupported) return false;

        long before = BalanceOf(normalized);
        long after = amount >= MaxCurrencyBalance - before
            ? MaxCurrencyBalance
            : before + amount;

        SetBalance(normalized, after);
        Console.WriteLine(
            $"[economia] {ShortEntityId()} +{amount:N0} {CurrencyName(normalized)} " +
            $"→ {after:N0} ({reason ?? "sem motivo"})");
        PushWallet();
        return true;
    }

    public bool TrySpendCurrency(Currency currency, long amount, string reason)
    {
        if (_context == null) return false;

        WalletCurrency normalized = NormalizeCurrency(currency);
        if (normalized == WalletCurrency.Unsupported) return false;
        if (amount <= 0) return true;

        long before = BalanceOf(normalized);
        if (before < amount) return false;

        long after = before - amount;
        SetBalance(normalized, after);
        Console.WriteLine(
            $"[economia] {ShortEntityId()} -{amount:N0} {CurrencyName(normalized)} " +
            $"→ {after:N0} ({reason ?? "sem motivo"})");
        PushWallet();
        return true;
    }

    // Compatibilidade com os sistemas já existentes que usam T-Stone.
    public void AddTStone(long amount, string reason)
        => AddCurrency(Currency.TStone, amount, reason);

    public bool TrySpendTStone(long amount, string reason)
        => TrySpendCurrency(Currency.TStone, amount, reason);

    public void SendWalletNow() => PushWallet();

    private void PushWallet()
    {
        Send(new WalletUpdated
        {
            EntityId = EntityId,
            Wallet = BuildWallet()
        });
        OnContextChanged();
    }

    private enum WalletCurrency
    {
        Unsupported = 0,
        TStone = 1,
        WarpGem = 2,
        DurangoCoin = 3,
        ShopMileage = 4,
        WarpMatter = 5
    }

    private static WalletCurrency NormalizeCurrency(Currency currency)
    {
        return currency switch
        {
            Currency.TStone => WalletCurrency.TStone,
            Currency.Gem => WalletCurrency.WarpGem,
            Currency.Coin => WalletCurrency.DurangoCoin,
            Currency.MobileCoin => WalletCurrency.DurangoCoin,
            Currency.PcCoin => WalletCurrency.DurangoCoin,
            Currency.CashshopMileage => WalletCurrency.ShopMileage,
            Currency.WarpMatter => WalletCurrency.WarpMatter,
            _ => WalletCurrency.Unsupported
        };
    }

    private long BalanceOf(WalletCurrency currency)
    {
        return currency switch
        {
            WalletCurrency.TStone => _context.TStone,
            WalletCurrency.WarpGem => _context.WarpGem,
            WalletCurrency.DurangoCoin => _context.DurangoCoin,
            WalletCurrency.ShopMileage => _context.ShopMileage,
            WalletCurrency.WarpMatter => _context.WarpMatter,
            _ => 0L
        };
    }

    private void SetBalance(WalletCurrency currency, long value)
    {
        value = Math.Clamp(value, 0L, MaxCurrencyBalance);
        switch (currency)
        {
            case WalletCurrency.TStone:
                _context.TStone = value;
                break;
            case WalletCurrency.WarpGem:
                _context.WarpGem = value;
                break;
            case WalletCurrency.DurangoCoin:
                _context.DurangoCoin = value;
                break;
            case WalletCurrency.ShopMileage: _context.ShopMileage = value; break;
            case WalletCurrency.WarpMatter: _context.WarpMatter = value; break;
        }
    }

    private static string CurrencyName(WalletCurrency currency)
    {
        return currency switch
        {
            WalletCurrency.TStone => "T-Stone",
            WalletCurrency.WarpGem => "Warp Gem",
            WalletCurrency.DurangoCoin => "Durango Coin",
            WalletCurrency.ShopMileage => "Pontos da loja",
            WalletCurrency.WarpMatter => "Warp Matter",
            _ => "moeda desconhecida"
        };
    }

    private string ShortEntityId()
        => string.IsNullOrEmpty(EntityId) ? "?" : EntityId[..Math.Min(8, EntityId.Length)];
}
