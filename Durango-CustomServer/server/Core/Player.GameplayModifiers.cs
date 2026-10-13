using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Utils;
using Messages;
using Shared.Ability;
using Shared.StatusEffect;

namespace Durango.Online;

public partial class Player
{
    private Dictionary<string, float> _runtimeModifiers = new();
    private IEnumerable<EffectDetail> ActiveEffectDetails()
    {
        double now = Times.UnixTimeNow();
        foreach (var effect in _timedStatusEffects.Values)
        {
            if (effect.Until > 0 && effect.Until <= now) continue;
            foreach (var detail in StatusEffectCatalog.PackDetails(effect.Id, effect.Level)) yield return detail;
            foreach (var bonus in effect.Modifiers ?? new())
                yield return new EffectDetail { Type = EffectType.Modifier, Key = bonus.Key, Value = bonus.Value };
        }
        foreach (string id in _toggledStatusEffects.Keys)
            if (!_timedStatusEffects.ContainsKey(id))
                foreach (var detail in StatusEffectCatalog.PackDetails(id, 1)) yield return detail;
    }

    private void CollectRuntimeModifiers(Dictionary<string, float> result)
    {
        foreach (var detail in ActiveEffectDetails())
            if (detail.Type == EffectType.Modifier) Accumulate(result, detail.Key, detail.Value);
        foreach (string id in _context.EquippedItems.Values.Distinct())
        {
            Item item = _context.InventoryItems.FirstOrDefault(i => i.Id == id);
            ItemCraftModifications.Recalculate(ref item);
            foreach (var block in item.Performance ?? Array.Empty<Performance>())
            {
                if (block.Id == "modifiers")
                    foreach (var bonus in block.Nums ?? new()) Accumulate(result, bonus.Key, bonus.Value);
                if (block.Id == "armor")
                    foreach (var field in new[] { "defense", "dodge", "heat_resistant", "cold_resistant",
                        "humid_resistant", "scorching_sun_resistant", "gale_resistant", "snowstorm_resistant", "poison_resistant", "volcanic_heat_resistant" })
                    {
                        float value = block.Nums?.GetValueOrDefault(field) ?? 0;
                        if (float.IsFinite(value)) Accumulate(result, field + "_plus", Math.Max(0, value));
                    }
            }
        }
    }

    private bool SyncModifierRecovery(Dictionary<string, float> modifiers)
    {
        bool changed = _survival.SetMomentum("clan_recovery", null); // Included in the shared modifier source below.
        var velocity = new Dictionary<string, float>();
        foreach (string key in new[] { "life", "health", "stamina", "energy" })
            if (modifiers.GetValueOrDefault(key + "_recovery") is float bonus && bonus != 0)
                velocity[key] = bonus;
        changed |= _survival.SetMomentum("ability_recovery", velocity);
        var increase = new Dictionary<string, float>();
        var decrease = new Dictionary<string, float>();
        foreach (var detail in ActiveEffectDetails())
        {
            var target = detail.Type == EffectType.DecrAmplifier ? decrease :
                detail.Type == EffectType.IncrAmplifier ? increase : null;
            if (target != null) target[detail.Key] = target.GetValueOrDefault(detail.Key) + detail.Value;
        }
        return _survival.SetVelocityAmplifiers(increase, decrease) || changed;
    }

    private float ModifierCostScale(string general, string specific = null)
    {
        var modifiers = CollectModifiers();
        return Math.Clamp(1 - modifiers.GetValueOrDefault(general) -
            (specific == null ? 0 : modifiers.GetValueOrDefault(specific)), .1f, 10f);
    }
}
