using Messages;
using Shared.Skill;
using System;
using System.Linq;

namespace Durango.Online;

public partial class Player
{
    private bool RejectSkillXpCouponTransfer(string[] itemIds, uint seq = 0)
    {
        if (!_context.InventoryItems.Any(item =>
                (itemIds ?? Array.Empty<string>()).Contains(item.Id) && SkillXpCoupons.IsCoupon(item.Prototype)))
            return false;
        Send(new Abort { Text = "Os Cupons do Luiz não podem ser transferidos. Você pode usá-los ou apagá-los." }, seq);
        return true;
    }

    private bool TryUseSkillXpCoupon(Item item, int inventoryIndex, uint seq)
    {
        if (!SkillXpCoupons.Categories.TryGetValue(item.Prototype, out Category category)) return false;

        if (_skills == null || !SkillDataStore.Categories.ContainsKey((int)category))
        {
            Send(new Abort { Text = "Não foi possível conceder a experiência deste cupom." }, seq);
            return true;
        }
        int level = category == Category.Survival ? _skillLevel : CategoryState((int)category).Level;
        if (level >= SkillDataStore.MaxPlayerLevel)
        {
            Send(new Abort { Text = "Esta habilidade já está no nível máximo. O cupom não foi consumido." }, seq);
            return true;
        }

        // Remoção e XP são persistidos pelo mesmo SaveSkillState.
        _context.InventoryItems.RemoveAt(inventoryIndex);
        _lockedItemIds.Remove(item.Id);
        if (category == Category.Survival)
        {
            // Sobrevivência acompanha o nível do personagem no jogo original.
            AddExp(SkillXpCoupons.Experience, item.Prototype, fixedGrant: true);
        }
        else
        {
            AddCategoryExp(category, SkillXpCoupons.Experience, fixedGrant: true);
        }
        Send(new InventoryUpdated { EntityId = EntityId, RemovedItemIds = new[] { item.Id } });
        Send(default(OK), seq);
        return true;
    }
}
