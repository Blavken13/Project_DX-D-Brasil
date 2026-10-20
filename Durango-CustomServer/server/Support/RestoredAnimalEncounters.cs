using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace Durango.Online;

/// <summary>Server encounter placement; original client animal definitions remain intact.</summary>
public static class RestoredAnimalEncounters
{
    public sealed class Encounter
    {
        public ushort EntityType;
        public float? LifeMax;
        public int? TileX;
        public int? TileY;
        public int CoordinateWidth;
        public bool AllowOcean;
    }

    public static void Load(string assetsDir, IDictionary<string, RegionCatalog.TemplateInfo> templates)
    {
        string path = Path.Combine(assetsDir, "..", "restored-animal-encounters.json");
        if (!File.Exists(path)) return;
        var root = JObject.Parse(File.ReadAllText(path));
        foreach (var property in root.Properties())
        {
            if (!templates.TryGetValue(property.Name, out var template))
                throw new InvalidDataException("Unknown encounter template: " + property.Name);
            var entries = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in (JArray)property.Value)
            {
                int? tileX = (int?)item["tile_x"];
                int? tileY = (int?)item["tile_y"];
                var encounter = new Encounter {
                    EntityType = (ushort)(int)item["entity_type"],
                    LifeMax = (float?)item["life_max"],
                    TileX = tileX,
                    TileY = tileY,
                    CoordinateWidth = (int?)item["coordinate_width"] ?? 0,
                    AllowOcean = (bool?)item["allow_ocean"] ?? false
                };
                bool hasPosition = tileX.HasValue && tileY.HasValue;
                bool partialPosition = tileX.HasValue != tileY.HasValue;
                string uniqueKey = hasPosition
                    ? $"{encounter.EntityType}@{tileX},{tileY}"
                    : encounter.EntityType.ToString();
                if (AnimalTypes.Get(encounter.EntityType) == null || partialPosition ||
                    tileX is < 0 or > 511 || tileY is < 0 or > 511 || !entries.Add(uniqueKey) ||
                    encounter.CoordinateWidth < 0 || encounter.CoordinateWidth > 512 ||
                    encounter.LifeMax is { } hp && (!float.IsFinite(hp) || hp <= 0))
                    throw new InvalidDataException("Invalid animal encounter: " + item);
                template.RestoredEncounters.Add(encounter);
            }
        }
    }
}
