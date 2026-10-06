using System.Collections.Generic;
using System.Collections.ObjectModel;
using Shared.Skill;

namespace Durango.Online;

internal static class SkillXpCoupons
{
    public const int Experience = 10_000;
    public const string Icon = "icon_exp";

    public static bool IsCoupon(string prototype) => prototype != null && Categories.ContainsKey(prototype);

    // Identificadores de item independentes do sprite compartilhado.
    // "contrucao" preserva a referência solicitada para o servidor.
    public static readonly IReadOnlyDictionary<string, Category> Categories =
        new ReadOnlyDictionary<string, Category>(new Dictionary<string, Category>
        {
            ["bonus_xp_sobrevivencia"] = Category.Survival,
            ["bonus_xp_combate_corpo_a_corpo"] = Category.MeleeCombat,
            ["bonus_xp_combate_a_distancia"] = Category.RangedCombat,
            ["bonus_xp_defesa"] = Category.Defense,
            ["bonus_xp_abate"] = Category.Butchery,
            ["bonus_xp_coleta"] = Category.Gathering,
            ["bonus_xp_culinaria"] = Category.Cooking,
            ["bonus_xp_fabricacao_de_armas"] = Category.Weaponcrafting,
            ["bonus_xp_fabricacao_de_armaduras"] = Category.Armorcrafting,
            ["bonus_xp_contrucao"] = Category.Constructing,
            ["bonus_xp_agricultura"] = Category.Farming,
            ["bonus_xp_processamento"] = Category.Process
        });
}
