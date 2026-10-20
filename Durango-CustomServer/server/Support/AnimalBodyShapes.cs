using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Utils;

namespace Durango.Online;

// Measured visual footprints in runtime world units: do not multiply by BaseScale again.
public static class AnimalBodyShapes
{
    public sealed class Circle { public float x, z, radius; }
    public sealed class Shape { public float attack_padding; public Circle[] circles; }
    private static readonly Dictionary<string, Shape> Shapes =
        Json.ReadFromFile<Dictionary<string, Shape>>("animal_body_shapes") ?? new();

    public static Shape Get(ushort type) => Shapes.GetValueOrDefault(type.ToString());
    public static IEnumerable<(WorldPosition center, float radius)> At(AnimalManager.Animal animal, double now)
    {
        var position = animal.PositionAt(now);
        var shape = Get(animal.EntityType);
        if (shape?.circles?.Length > 0)
        {
            float radians = animal.Yaw * MathF.PI / 180f;
            float sin = MathF.Sin(radians), cos = MathF.Cos(radians);
            foreach (var c in shape.circles)
                yield return (new WorldPosition(position.x + c.x * cos + c.z * sin,
                    position.y - c.x * sin + c.z * cos), c.radius);
        }
        else
        {
            var info = AnimalTypes.Get(animal.EntityType);
            yield return (position, (info?.BoundRadius ?? 0) * (info?.BaseScale ?? 1));
        }
    }
    public static float Radius(AnimalManager.Animal animal) => Get(animal.EntityType)?.circles is { Length: > 0 } circles
        ? circles.Max(c => MathF.Sqrt(c.x * c.x + c.z * c.z) + c.radius)
        : (AnimalTypes.Get(animal.EntityType)?.BoundRadius ?? 0) * (AnimalTypes.Get(animal.EntityType)?.BaseScale ?? 1);

    public static bool Within(AnimalManager.Animal animal, WorldPosition point, float padding, double now) =>
        At(animal, now).Any(c => MathF.Pow(point.x - c.center.x, 2) + MathF.Pow(point.y - c.center.y, 2)
            <= MathF.Pow(c.radius + Math.Max(0, padding), 2));

    public static float SurfaceDistance(AnimalManager.Animal animal, WorldPosition point, double now) =>
        Math.Max(0, At(animal, now).Min(c => MathF.Sqrt(MathF.Pow(point.x - c.center.x, 2) +
            MathF.Pow(point.y - c.center.y, 2)) - c.radius));
}
