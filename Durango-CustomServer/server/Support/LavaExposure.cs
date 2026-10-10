using System;
using System.Collections.Generic;
using System.Linq;
using Messages;
using Shared.Region;

namespace Durango.Online;

internal static class LavaExposure
{
    // Split movement at tile boundaries: crossing a narrow lava channel must hurt
    // even when both endpoints are on dry ground.
    internal static double Seconds(WorldPosition from, WorldPosition to, double seconds, Func<Point2, Biome> biomeAt)
    {
        if (seconds <= 0 || !float.IsFinite(from.x) || !float.IsFinite(from.y) ||
            !float.IsFinite(to.x) || !float.IsFinite(to.y)) return 0;
        var cuts = new SortedSet<double> { 0, 1 };
        AddCuts(from.x, to.x, cuts);
        AddCuts(from.y, to.y, cuts);
        var points = cuts.ToArray();
        double exposure = 0;
        for (int i = 1; i < points.Length; i++)
        {
            double midpoint = (points[i - 1] + points[i]) / 2;
            var tile = new Point2((int)Math.Floor((from.x + (to.x - from.x) * midpoint) / 200),
                (int)Math.Floor((from.y + (to.y - from.y) * midpoint) / 200));
            if (WorldStatusRules.UnmaskBiome(biomeAt(tile)) == Biome.Lava)
                exposure += (points[i] - points[i - 1]) * seconds;
        }
        return exposure;
    }

    private static void AddCuts(double from, double to, SortedSet<double> cuts)
    {
        if (from == to) return;
        double first = Math.Floor(Math.Min(from, to) / 200) + 1;
        double last = Math.Ceiling(Math.Max(from, to) / 200) - 1;
        // Valid terrain paths fit comfortably within this bound.
        if (last - first > 8192) return;
        for (double tile = first; tile <= last; tile++) cuts.Add((tile * 200 - from) / (to - from));
    }
}
