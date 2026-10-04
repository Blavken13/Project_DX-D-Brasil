using System;
using System.Collections.Generic;
using System.Linq;
using Messages;

namespace Durango.Online;

public partial class Player
{
    private void DumpItemsToGround(DumpItems msg)
    {
        List<Item> source = _context.InventoryItems;
        PetStore.Entry pet = null;
        string warehouse = msg.SourceProp?.EntityId;
        if (warehouse != null)
        {
            if (!MayTouchArtifact(warehouse, "Descartar itens", Shared.Estate.AccessRights.Take)) return;
            source = WarehouseStore.Items(warehouse, msg.SectionName, false);
        }
        else if (!string.IsNullOrEmpty(msg.SourcePetEntityId))
        {
            pet = PetStore.Find(EntityId, msg.SourcePetEntityId);
            source = pet?.Bag;
        }
        var ids = msg.ItemIds ?? Array.Empty<string>();
        if (source == null || ids.Length == 0 || ids.Any(id => string.IsNullOrEmpty(id) || _lockedItemIds.Contains(id))
            || ids.Distinct().Count() != ids.Length) return;
        var items = source.Where(i => ids.Contains(i.Id)).ToList();
        if (items.Count != ids.Length) return;
        if (msg.Tile.HasValue && (!IsWithinTiles(msg.Tile.Value, ArtifactReachTiles)
            || !_world.DropItems(msg.Tile.Value, ClampFloor(msg.Floor), items)))
        {
            Send(new Abort { Text = "Não é possível colocar os itens neste local." });
            return;
        }
        source.RemoveAll(i => ids.Contains(i.Id));
        if (warehouse != null) { SendWarehouseUpdated(warehouse, msg.SectionName, null, ids); _world.Save(); }
        else if (pet != null) SyncPetBag(pet);
        else Send(new InventoryUpdated { EntityId = EntityId, RemovedItemIds = ids });
        OnContextChanged();
    }

    private Collectible GroundCollectible(string id, List<Item> items) => new()
    {
        EntityId = id, CollectibleId = "package", Size = "small",
        Generators = items.Select(i => new Generator { Id = i.Id, Name = i.Name ?? "Item",
            Icon = i.Icon, Amount = 1, Level = i.Level, Enabled = true,
            ToolRequirements = new Dictionary<string, int>() }).ToArray()
    };

    private bool TrySendGroundCollectible(string id, uint seq)
    {
        var items = _world.GroundItems(id);
        if (items == null) return false;
        Send(GroundCollectible(id, items), seq);
        return true;
    }

    private bool TryTouchGroundPackage(string id, uint seq)
    {
        var items = _world.GroundItems(id);
        if (items == null) return false;
        Send(new Touched { EntityId = id, EntityName = "Itens no chão",
            Interactions = new[] { (int)Shared.System.Interaction.Collect }, Collectible = GroundCollectible(id, items) }, seq);
        return true;
    }

    private bool TryCollectGroundPackage(Collect msg, uint seq)
    {
        var items = _world.GroundItems(msg.EntityId);
        if (items == null) return false;
        var artifact = _world.ArtifactManager.Get(msg.EntityId);
        int index = items.FindIndex(i => i.Id == msg.GeneratorId);
        if (!artifact.HasValue || !IsWithinTiles(artifact.Value.Tile, ArtifactReachTiles) || index < 0)
        { RejectCollect(seq, "Este item não está disponível para coleta.", msg); return true; }
        var item = items[index];
        if (_context.InventoryItems.Sum(i => (long)Math.Max(1, i.Size)) + Math.Max(1, item.Size) > CurrentInventoryCapacity)
        { RejectCollect(seq, "Não há espaço na mochila.", msg); return true; }
        items.RemoveAt(index);
        AddItems(new List<Item> { item });
        Send(default(ReplySequenceMark), seq);
        Send(new Messages.Timer { Duration = 0 }, seq);
        Send(new InventoryUpdated { EntityId = EntityId, Items = new[] { item } });
        bool empty = items.Count == 0;
        if (empty) _world.DestructArtifact(msg.EntityId); else _world.Save();
        OnContextChanged();
        FinishCollect(new Collected { Items = new[] { item }, Result = Shared.Item.Result.Success,
            RanOut = empty, ActionInfo = new ActionInfo { ActionLevel = item.Level, PotentialLevel = item.Level,
                RelatedCategory = Shared.Skill.Category.Invalid, RelatedAbility = Shared.Ability.Derived.Invalid, SuccessRatio = 1 } }, seq);
        return true;
    }
}
