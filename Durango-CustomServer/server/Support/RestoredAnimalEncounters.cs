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
    }

    public static void Load(string assetsDir, IDictionary<string, RegionCatalog.TemplateInfo> templates)
    {
        string path = Path.Combine(assetsDir, "..", "restored-animal-encounters.json");
        if (!File.Exists(path)) return;
        var root = JObject.Parse(File.ReadAllText(path));
        foreach (var property in root.Properties())
        {
            if (!templates.TryGetValue(property.Name, out var template) || template.Level != 60)
                throw new InvalidDataException("Invalid level-60 encounter template: " + property.Name);
            var types = new HashSet<ushort>();
            foreach (var item in (JArray)property.Value)
            {
                var encounter = new Encounter {
                    EntityType = (ushort)(int)item["entity_type"],
                    LifeMax = (float?)item["life_max"]
                };
                if (AnimalTypes.Get(encounter.EntityType) == null || !types.Add(encounter.EntityType) ||
                    encounter.LifeMax is { } hp && (!float.IsFinite(hp) || hp <= 0))
                    throw new InvalidDataException("Invalid animal encounter: " + item);
                template.RestoredEncounters.Add(encounter);
            }
        }
    }
}
