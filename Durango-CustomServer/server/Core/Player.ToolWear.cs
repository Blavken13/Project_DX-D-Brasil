using System;
using System.Linq;
using Messages;

namespace Durango.Online;

public static class ToolWearTuning
{
    public const int UsesBase = 40;
    public const int UsesPerTier = 40;
    public const float WarnBelow = .2f;
}

public partial class Player
{
    private void WearTool(string itemId, string action = "collect")
    {
        if (string.IsNullOrEmpty(itemId)) return;
        int index = _context.InventoryItems.FindIndex(i => i.Id == itemId);
        if (index < 0) return;
        var item = _context.InventoryItems[index];
        if (ItemDurability.Maximum(item.Prototype, item.Level) <= 1) return;
        ItemDurability.Normalize(ref item);
        float maximum = item.Durability.Max();
        float before = item.Durability.Get();
        float after = Math.Max(0, before - ItemDurability.Delta(action));
        if (before <= 0) return;
        item.Durability = new Gauge(maximum, 0f, new[] { new GaugeNode(0, after) });
        _context.InventoryItems[index] = item;
        // Itens quebrados continuam na mochila para que possam ser reparados.
        Send(new InventoryUpdated { EntityId = EntityId, Items = new[] { item } });
        if (after <= 0)
            Send(new Info { Text = $"{item.Name} quebrou. Use um kit de reparo para restaurá-lo." });
        else if (after / maximum < ToolWearTuning.WarnBelow && before / maximum >= ToolWearTuning.WarnBelow)
            Send(new Info { Text = $"{item.Name} está quase quebrando (restam {after / maximum:P0})." });
        OnContextChanged();
    }

    private void WearEquippedWeapon()
    {
        Item? weapon = null;
        float strongest = 0;
        foreach (var id in _context.EquippedItems.Values)
        {
            int index = _context.InventoryItems.FindIndex(i => i.Id == id);
            if (index < 0) continue;
            var candidate = _context.InventoryItems[index];
            if (candidate.Durability?.Get() <= 0) continue;
            float attack = BattleDataStore.WeaponAttack(candidate.Prototype, candidate.Level);
            if (attack <= strongest) continue;
            weapon = candidate;
            strongest = attack;
        }
        if (weapon.HasValue) WearTool(weapon.Value.Id, "attack");
    }

    private void WearEquippedArmor()
    {
        foreach (var id in _context.EquippedItems.Values.Distinct().ToArray())
        {
            int index = _context.InventoryItems.FindIndex(i => i.Id == id);
            if (index < 0) continue;
            var item = _context.InventoryItems[index];
            if (ItemDurability.IsArmor(item.Prototype, item.Level)) WearTool(id, "defense");
        }
    }
}
