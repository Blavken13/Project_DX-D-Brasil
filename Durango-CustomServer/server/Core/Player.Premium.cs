using System;
using System.Linq;
using Messages;

namespace Durango.Online;

public partial class Player
{
    public static int InventoryCapacity(PlayerContext context) => InventoryMaxSize + (context?.Premium?.InventoryBonus(context.EntityId) ?? 0);
    public int CurrentInventoryCapacity => InventoryCapacity(_context);
    private bool PremiumActive => _context.Premium?.IsActive(EntityId) == true;
    private string _premiumSignature;
    private double _premiumDailyCheckAt;
    internal Func<double> PremiumGatherRoll { get; set; } = Random.Shared.NextDouble;

    public void SyncPremium()
    {
        var store = _context.Premium;
        var subscriptions = store?.Active(EntityId) ?? Array.Empty<PremiumStore.Subscription>();
        string signature = string.Join("|", subscriptions.Select(s => s.PackageId + ":" + s.Until));
        if (_premiumSignature == signature) return;
        _premiumSignature = signature;
        foreach (var package in store?.Packages.Values ?? Enumerable.Empty<PremiumStore.Package>())
        {
            var active = subscriptions.FirstOrDefault(s => s.PackageId == package.Id);
            if (active != null) ApplyTimedStatusEffect(package.EffectId, durationOverride: active.Until - store.Now);
            else ClearTimedStatusEffect(package.EffectId);
        }
        _survival.SetFatigueDisabled(subscriptions.Length > 0);
        _fatigueCheckedStamp = int.MinValue;
        SendStatusEffects();
        SendInventoryInfos();
        SyncFatigueVelocities();
        FlushSurvival();
        SendWalletNow();
    }

    private void UpdatePremium()
    {
        SyncPremium();
        var store = _context.Premium;
        if (store == null || store.Now < _premiumDailyCheckAt) return;
        _premiumDailyCheckAt = store.Now + 30;
        if (_mailStore != null && !store.DeliverDaily(_context, _mailStore, out string error))
            Console.WriteLine("[premium] Entrega diária pendente: " + error);
    }
    private int WithPremiumExp(int amount, string reason) =>
        !PremiumActive || string.Equals(reason, "cheat", StringComparison.OrdinalIgnoreCase) ? amount
        : (int)Math.Min(int.MaxValue, ((long)amount * 3 + Math.Clamp(_context.PremiumExpRemainder, 0, 1)) / 2);
}
