using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Utils;
using Newtonsoft.Json.Linq;
using Shared.Ability;

namespace Durango.Online;

/// <summary>Advanced recipes unlocked by crafting proficiency, from the original reward tables.</summary>
internal static class CraftingAbilityRecipes
{
    internal record Unlock(Derived Ability, float RequiredValue, string RecipeId);
    private static Unlock[] _unlocks;
    internal static IReadOnlyList<Unlock> All
    {
        get
        {
            if (_unlocks != null) return _unlocks;
            var thresholds = Json.ReadFromFile<JObject>("crafting_rewards_datas");
            var rewards = Json.ReadFromFile<JObject>("crafting_rewards");
            var list = new List<Unlock>();
            foreach (JProperty ability in thresholds.Properties())
                foreach (JProperty row in ((JObject)ability.Value).Properties())
                {
                    var reward = rewards[(string)row.Value["reward_id"]];
                    if ((int?)reward?["type"] != 19 || (string)reward["recipe_id"] is not { Length: > 0 } recipe) continue;
                    list.Add(new Unlock((Derived)int.Parse(ability.Name), (float)row.Value["required_value"], recipe));
                }
            return _unlocks = list.ToArray();
        }
    }
    internal static IEnumerable<string> Unlocked(Func<Derived, float> value)
    {
        foreach (var group in All.GroupBy(u => u.Ability))
        {
            float actual = value(group.Key);
            foreach (var unlock in group) if (actual >= unlock.RequiredValue) yield return unlock.RecipeId;
        }
    }
}
