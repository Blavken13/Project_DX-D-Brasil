using System;
using System.Collections.Generic;
using System.Linq;

namespace Durango.Online;

internal static class CombatDamage
{
    // Server balance curve: armor attenuates damage continuously instead of
    // subtracting a level-scaled defense until every weapon hits for one.
    internal static int Calculate(float power, BattleAttackInfo hit, float defense,
        Dictionary<string, float> resistances = null, float scale = 1)
    {
        if (!float.IsFinite(power) || power <= 0 || hit == null) return 0;
        float weight = hit.atk_ratio?.Values.Where(v => v > 0).Sum() ?? 0;
        float resistance = weight > 0 ? hit.atk_ratio.Where(p => p.Value > 0)
            .Sum(p => p.Value * Math.Max(0, resistances?.GetValueOrDefault(p.Key, 1) ?? 1)) / weight : 1;
        float armor = Math.Max(0, defense) * resistance * (1 - Math.Clamp(hit.armor_penetration, 0, 1));
        double value = power * (double)power / (power + armor) * Math.Max(0, hit.damage_bonus) * Math.Max(0, scale);
        if (!double.IsFinite(value) || value <= 0) return 0;
        return (int)Math.Clamp(Math.Round(value), CombatTuning.MinDamage, int.MaxValue);
    }

    internal static double ImpactDelay(BattleActionData action, BattleAttackInfo hit)
    {
        float rate = action.meta.playback_rate.GetValueOrDefault(1);
        if (!float.IsFinite(rate) || rate <= 0) rate = 1;
        float time = hit.damage_time > 0 ? hit.damage_time : hit.attack_time > 0 ? hit.attack_time : action.meta.action_length;
        return Math.Max(.001, time / rate);
    }
}
