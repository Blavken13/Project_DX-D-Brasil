using System;

namespace Durango.Online;

public static class StaminaTuning
{
    public const float ActionCostScale = 0.5f;
    // Actions carries integer costs: use the same rounding for display and debit.
    public static int Cost(float original) => (int)Math.Ceiling(Math.Max(0, original) * ActionCostScale);
}
