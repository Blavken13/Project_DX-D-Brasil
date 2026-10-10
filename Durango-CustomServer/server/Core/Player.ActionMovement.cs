using System;
using Messages;

namespace Durango.Online;

public partial class Player
{
    private double PendingActionStartedAt()
    {
        double startedAt = double.PositiveInfinity;
        foreach (var pending in _pendingCollects) startedAt = Math.Min(startedAt, pending.StartedAt);
        foreach (var pending in _pendingBuilds) startedAt = Math.Min(startedAt, pending.StartedAt);
        foreach (var pending in _pendingReturnWarps) startedAt = Math.Min(startedAt, pending.StartedAt);
        if (_pendingCraft != null) startedAt = Math.Min(startedAt, _pendingCraft.StartedAt);
        if (_pendingTaming != null) startedAt = Math.Min(startedAt, _pendingTaming.StartedAt);
        if (_pendingCrater != null) startedAt = Math.Min(startedAt, _pendingCrater.StartedAt);
        if (_pendingTravelWarp != null) startedAt = Math.Min(startedAt, _pendingTravelWarp.StartedAt);
        return startedAt;
    }

    private static bool PositionChanged(WorldPosition from, WorldPosition to) =>
        !UnityEngine.Mathf.Approximately(from.x, to.x) || !UnityEngine.Mathf.Approximately(from.y, to.y);

    private bool MovedDuringPendingAction(Movement[] movements, WorldPosition before)
    {
        double startedAt = PendingActionStartedAt();
        if (double.IsPositiveInfinity(startedAt)) return false;
        var known = MovementPath();
        Location? previous = known?.Length > 0 && known[^1].Path?.Length > 0 ? known[^1].Path[^1] : null;
        foreach (var movement in movements)
        {
            // Native clients batch old walking samples with the first action pose.
            // An absolute pose correction is not evidence of a new displacement.
            string motion = movement.MotionName ?? "";
            bool locomotion = motion.Contains("Walk", StringComparison.OrdinalIgnoreCase) ||
                motion.Contains("Run", StringComparison.OrdinalIgnoreCase) ||
                motion.Contains("Swim", StringComparison.OrdinalIgnoreCase) ||
                motion.Contains("Dash", StringComparison.OrdinalIgnoreCase);
            foreach (var location in movement.Path)
            {
                // CompactMovement can repeat the preceding sample. Never interpret
                // a replay or an out-of-order location as a new displacement.
                if (previous.HasValue && location.Time <= previous.Value.Time)
                {
                    previous = location;
                    continue;
                }
                if (location.Time >= startedAt && PositionChanged(previous?.Position ?? before, location.Position) &&
                    (locomotion || previous.HasValue && previous.Value.Time >= startedAt)) return true;
                previous = location;
            }
        }
        return false;
    }

    private void InterruptActionsForMovement()
    {
        CancelPendingCraftAndNotify(true);
        ClearCollectTimers();
        ClearBuildTimers();
        CancelPendingTaming();
        InterruptCraterInvestment();
        ClearWarpTimers();
    }
}
