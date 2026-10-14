using System;
using System.Linq;
using Durango.Utils;
using Messages;
using Shared.StatusEffect;

namespace Durango.Online;

public partial class Player
{
    public static int InventoryCapacity(PlayerContext context) => (int)Math.Min(int.MaxValue,
        (long)InventoryMaxSize + (context?.Premium?.InventoryBonus(context.EntityId) ?? 0) + EquipmentInventoryBonus(context));
    public int CurrentInventoryCapacity
    {
        get
        {
            float equippedCarry = _context.EquippedItems.Values.Distinct().Sum(id => EquipmentStats.Value(
                _context.InventoryItems.FirstOrDefault(i => i.Id == id), "modifiers", "carry_capacity"));
            float abilityCarry = _runtimeModifiers.GetValueOrDefault("carry_capacity") - equippedCarry - _runtimePremiumCarry;
            return (int)Math.Clamp((long)InventoryCapacity(_context) + (long)Math.Floor(abilityCarry), 1, int.MaxValue);
        }
    }
    private float _runtimePremiumCarry;
    private float PremiumCarryInModifiers()
    {
        if (_context.Premium == null) return 0;
        var effectIds = _context.Premium.Packages.Values.Select(p => p.EffectId).ToHashSet();
        double now = Times.UnixTimeNow();
        return _timedStatusEffects.Values.Where(e => effectIds.Contains(e.Id) && (e.Until <= 0 || e.Until > now))
            .Sum(e => StatusEffectCatalog.PackDetails(e.Id, e.Level)
                .Where(d => d.Type == EffectType.Modifier && d.Key == "carry_capacity").Sum(d => d.Value)
                + (e.Modifiers?.GetValueOrDefault("carry_capacity") ?? 0));
    }
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
        if (_skills != null) SendFullStatistics();
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
