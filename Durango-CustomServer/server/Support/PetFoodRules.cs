using System;
using System.Collections.Generic;
using System.Linq;
using Messages;

namespace Durango.Online;

/// <summary>The client's food menus and feeding handlers must use the same diet.</summary>
internal static class PetFoodRules
{
    private static readonly string[] HerbivoreTags =
        { "feed_herb", "leaf", "fruit", "vegetable", "grain", "flower", "stem", "herb", "plantable", "seeding", "root",
            "tea_leaf", "cactus", "mushroom", "sugarcane" };
    private static readonly string[] CarnivoreTags =
        { "feed_carni", "meat", "fish", "worm", "egg" };
    private static readonly Dictionary<(string Prototype, int Level), Performance?> Foods = new();

    internal static string[] DietTags(string kind) => kind switch
    {
        "Herbivore" => HerbivoreTags.ToArray(),
        "Carnivore" => CarnivoreTags.ToArray(),
        "Omnivore" => HerbivoreTags.Concat(CarnivoreTags).ToArray(),
        _ => Array.Empty<string>()
    };

    private static Performance? FoodOf(Item item)
    {
        if (string.IsNullOrEmpty(item.Prototype)) return null;
        var key = (item.Prototype, item.Level);
        if (!Foods.TryGetValue(key, out var food))
        {
            var blocks = ItemPerformance.Of(item.Prototype, item.Level);
            int index = blocks.FindIndex(p => p.Id == "pet_food");
            food = index < 0 ? null : blocks[index];
            Foods[key] = food;
        }
        return food;
    }

    internal static bool CanEat(ushort petType, Item item, bool domestication = false)
    {
        Performance? food = FoodOf(item);
        if (food?.Nums == null || food.Value.Nums.GetValueOrDefault("vigor") <= 0) return false;
        string[] tags = Player.PetTables.EatableTagsOf(petType);
        if (tags.Length > 0 && !(item.Tags ?? Array.Empty<Tag>()).Any(t => tags.Contains(t.Id))) return false;
        return !domestication || food.Value.Nums.GetValueOrDefault("decrease_domesticate_time") > 0 ||
            food.Value.Nums.GetValueOrDefault("decrease_domesticate_time_ratio") > 0 ||
            food.Value.Nums.GetValueOrDefault("increase_domesticate_success_rate") > 0;
    }

    // Repair only pet_food: cooked food and crafted stats in other blocks are preserved.
    internal static void RepairItem(ref Item item)
    {
        Performance? native = FoodOf(item);
        if (!native.HasValue) return;
        var blocks = (item.Performance ?? Array.Empty<Performance>()).ToList();
        int index = blocks.FindIndex(p => p.Id == "pet_food");
        Performance repaired = index < 0 ? new Performance { Id = "pet_food" } : blocks[index];
        repaired.Nums = repaired.Nums == null ? new() : new(repaired.Nums);
        repaired.Strs = repaired.Strs == null ? new() : new(repaired.Strs);
        foreach (var pair in native.Value.Nums ?? new Dictionary<string, float>()) repaired.Nums[pair.Key] = pair.Value;
        foreach (var pair in native.Value.Strs ?? new Dictionary<string, string>()) repaired.Strs[pair.Key] = pair.Value;
        if (index < 0) blocks.Add(repaired); else blocks[index] = repaired;
        item.Performance = blocks.ToArray();
    }

    internal static void Normalize(List<Item> items)
    {
        if (items == null) return;
        for (int i = 0; i < items.Count; i++)
        {
            Item item = items[i];
            RepairItem(ref item);
            items[i] = item;
        }
    }

    internal static void RepairPet(ref Messages.Pet pet) =>
        pet.Stat.EatableTags = Player.PetTables.EatableTagsOf(pet.EntityType);

    internal static void RepairCages(ref ArtifactState state)
    {
        if (state.Cage is GrowCage grow && grow.Pets.Data != null)
        {
            for (int i = 0; i < grow.Pets.Data.Length; i++) RepairPet(ref grow.Pets.Data[i]);
            state.Cage = grow;
        }
        if (state.DomesticCage.HasValue)
        {
            DomesticCage domestic = state.DomesticCage.Value;
            if (domestic.Reins != null)
                for (int i = 0; i < domestic.Reins.Length; i++)
                    domestic.Reins[i].EatableTags = Player.PetTables.EatableTagsOf(domestic.Reins[i].PetEntityType);
            state.DomesticCage = domestic;
        }
    }
}
