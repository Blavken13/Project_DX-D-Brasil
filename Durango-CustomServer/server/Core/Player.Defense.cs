using System;
using Shared.Skill;

namespace Durango.Online;

public partial class Player
{
    private double _defenseActiveFrom;
    private double _defenseActiveUntil;
    private double _defenseExpFactor;
    private bool _defenseRewarded;

    private void BeginDefenseAction(BattleActionData action, double now)
    {
        ClearDefenseAction();
        var defense = action.defense_info;
        if (defense == null || !float.IsFinite(defense.active_time) || defense.active_time <= 0 ||
            !float.IsFinite(defense.dodge_force) || defense.dodge_force <= 0) return;
        _defenseActiveFrom = now + Math.Max(0, defense.stand_by_time);
        _defenseActiveUntil = _defenseActiveFrom + defense.active_time;
        _defenseExpFactor = Math.Max(0, action.meta.exp_factor);
    }

    private bool HasActiveDefense(double now) => _context.AppearPlayer.IsAlive &&
        now >= _defenseActiveFrom && now < _defenseActiveUntil;

    private void ClearDefenseAction()
    {
        _defenseActiveFrom = _defenseActiveUntil = _defenseExpFactor = 0;
        _defenseRewarded = false;
    }

    private void RecordSuccessfulDefense(bool active)
    {
        if (!active)
        {
            RecordDefenseExperience(BattleDataStore.DamageableExp.auto_defense_factor);
            return;
        }
        // A ação pode evitar vários impactos, mas seu fator de EXP é aplicado uma vez.
        if (_defenseRewarded) return;
        _defenseRewarded = true;
        RecordDefenseExperience(_defenseExpFactor);
    }

    private void RecordDefenseExperience(double factor)
    {
        if (_skills == null || !double.IsFinite(factor) || factor <= 0) return;
        double remainder = double.IsFinite(_skills.DefenseExpRemainder)
            ? Math.Clamp(_skills.DefenseExpRemainder, 0, 1) : 0;
        double total = remainder + Math.Min(factor * SkillTuning.CategoryExpPerAction, SkillDataStore.CategoryExpLimit);
        int points = (int)Math.Floor(total + 1e-7);
        _skills.DefenseExpRemainder = Math.Max(0, total - points);
        if (points > 0) AddCategoryExp(Category.Defense, points, save: false);
        SaveSkillState();
    }
}
