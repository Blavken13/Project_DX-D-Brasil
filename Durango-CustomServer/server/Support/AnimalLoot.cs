using System;
using System.Collections.Generic;
using System.Linq;

namespace Durango.Online;

/// <summary>
/// Reconstructed carcass materials. Native recipe sources are retained by CollectibleTable;
/// these families fill the incomplete client catalogue using existing raw-item prototypes.
/// </summary>
internal static class AnimalLoot
{
    private static readonly string[] Flesh = { "meat", "fat", "organ", "tendon" };
    private static readonly string[] Mammals = { "phenacodus", "skunkodus", "rat", "elephantulus", "macrauchenia", "megaloceros", "direwolf", "sabertooth", "retriever", "andrewsarchus", "mammoth", "deinotherium", "elephant" };
    private static readonly string[] Birds = { "oviraptor", "ornithomimus", "coelophysis", "gallimimus", "dodophysis", "dodo", "gastornis", "magpie_gastornis", "pavomimus", "tarbosaurus_snowy" };
    private static readonly string[] Armored = { "ankylosaurus", "euoplocephalus", "tortoise_ankylosaurus" };
    private static readonly string[] Plates = { "stegosaurus", "tuojiangosaurus", "kentrosaurus" };
    private static readonly string[] Horns = { "triceratops", "chasmosaurus", "styracosaurus", "centrosaurus", "protoceratops", "zebraceratops", "bonusaurus", "megaloceros" };
    private static readonly string[] Ivory = { "mammoth", "deinotherium", "elephant" };
    private static readonly string[] HairlessMammals = { "deinotherium", "elephant" };

    private static bool Matches(string name, IEnumerable<string> families) =>
        families.Any(f => name == f || name.StartsWith(f + "_", StringComparison.Ordinal));

    public static IReadOnlyList<string> For(AnimalTypes.Info animal)
    {
        string name = animal?.Name ?? "";
        if (name == "dummy" || name.StartsWith("recruiter_", StringComparison.Ordinal) ||
            name == "watermelon" || name.Contains("_paper", StringComparison.Ordinal))
            return Array.Empty<string>();
        if (name == "horseshoecrab") return new[] { "meat", "crab_shell" };

        bool mammal = Matches(name, Mammals);
        bool armored = Matches(name, Armored);
        bool carnivore = animal.IsAggressive && !mammal;
        bool large = animal.SizeLevel >= 4 && !name.Contains("_baby", StringComparison.Ordinal);
        var result = new List<string> { "meat" };
        result.Add(armored ? "leather_raw_armored" : mammal && !Matches(name, HairlessMammals) ? "leather_raw_fur" : "leather_raw");
        result.Add(large ? "bone_leg_thick" : carnivore ? "bone_leg_carni" : "bone_leg");
        result.Add(large ? carnivore ? "bone_head_carni" : "bone_head_big" : "bone_head");
        result.Add(carnivore ? "bone_rib_carni" : "bone_rib");
        result.AddRange(Flesh.Skip(1));
        if (large) result.Add("meat_serloin");
        if (mammal && animal.SizeLevel >= 3) result.Add("meat_belley");
        if (Matches(name, Birds)) result.Add("meat_breast");
        if (Matches(name, Birds)) result.Add("feather");
        if (armored || Matches(name, Plates)) result.Add("bone_board");
        bool youngOrFemaleDeer = name.StartsWith("megaloceros", StringComparison.Ordinal) &&
            (name.Contains("_baby", StringComparison.Ordinal) || name.Contains("_female", StringComparison.Ordinal));
        if (Matches(name, Horns) && !youngOrFemaleDeer) result.Add("bone_horn");
        if (Matches(name, Ivory)) result.Add("bone_ivory");
        if (animal.IsAggressive && !name.StartsWith("compsognathus", StringComparison.Ordinal)) result.Add("bone_tooth");
        if (name.StartsWith("lizard_poisonous", StringComparison.Ordinal)) result.Add("poison_sac");
        return result;
    }

    // Explicit rewards matter: bone_03 is taught BEFORE bone_02, and meat_04 before meat_03.
    // Comparing only the largest category rank would bypass unlearned nodes.
    public static string RewardFor(string prototype) => prototype switch
    {
        "meat" => "meat_01",
        "meat_serloin" or "meat_belley" or "meat_breast" or "meat_tenderloin" => "meat_03",
        "fat" => "meat_02",
        "organ" or "poison_sac" => "meat_04",
        "tendon" => "meat_05",
        "leather_raw_armored" => "leather_02",
        "leather_raw" or "leather_raw_fur" or "leather_raw_fur_narrow" or "leather_raw_fur_wide" or "leather_raw_hard" => "leather_01",
        "bone_leg" or "bone_leg_normal" or "bone_leg_carni" or "bone_leg_herv" => "bone_01",
        "bone_leg_thick" => "bone_03",
        "bone_rib" or "bone_rib_carni" or "bone_rib_herv" or "bone_rib_warp_acc_allo" or "bone_rib_warp_acc_allo_02" => "bone_02",
        "bone_head" => "skull_02",
        "bone_head_big" or "bone_head_carni" or "bone_head_herv" => "skull_03",
        "bone_board" => "boneplate_02",
        "bone_horn" or "bone_horn_small" => "horn_02",
        "bone_tooth" => "tooth_01",
        "bone_ivory" => "tooth_02",
        "feather" => "feather_01",
        _ => null
    };
}
