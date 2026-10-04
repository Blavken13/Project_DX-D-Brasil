using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Utils;
using Messages;
using Shared.Estate;

namespace Durango.Online;

public partial class Player
{
    private void InitializeClanArtifactAccess()
    {
        if (_world.Registry?.IsClanRegion(LogicalRegionId()) != true) return;
        foreach (var artifact in _world.ArtifactManager.Enumerable(a => !a.States.Access.HasValue).ToArray())
            if (_world.TryGetEstateIdAtCell(World.CellFromTile(artifact.Tile), out string estateId) && _world.GetEstate(estateId)?.OwnerId == CurrentClanId())
                _world.ArtifactManager.SetAccess(artifact.EntityId, DefaultClanArtifactAccess());
    }
    private ArtifactAccess DefaultClanArtifactAccess() => new()
    {
        Others = false, Friends = new(),
        ClanMembers = (ClanStore.Find(CurrentClanId())?.Roles.Keys ?? Enumerable.Empty<int>()).ToDictionary(id => id, _ => true),
        InventoryAccess = new InventoryAccess { Others = 0, Friends = new(),
            ClanMembers = (ClanStore.Find(CurrentClanId())?.Roles.Keys ?? Enumerable.Empty<int>()).ToDictionary(id => id, _ => -1), TakenCounts = new() }
    };
    private void SetArtifactPermissions(SetArtifactAccess msg, uint seq)
    {
        var artifact = _world.ArtifactManager.Get(msg.EntityId);
        if (!artifact.HasValue || artifact.Value.Tile.x != msg.Tile.x || artifact.Value.Tile.y != msg.Tile.y
            || !IsWithinTiles(msg.Tile, ArtifactReachTiles + Math.Max(artifact.Value.Size.x, artifact.Value.Size.y))
            || !_world.TryGetEstateIdAtCell(World.CellFromTile(msg.Tile), out string estateId)
            || string.IsNullOrEmpty(EstateAuthorityOwner(_world.GetEstate(estateId))))
        { Send(new Abort { Text = "Você não possui autoridade sobre esta estrutura ou está fora de alcance." }, seq); return; }
        var access = msg.Access;
        access.Friends ??= new(); access.ClanMembers ??= new();
        if (access.InventoryAccess.HasValue)
        {
            var inventory = access.InventoryAccess.Value;
            if (inventory.Others < -1 || inventory.Friends?.Values.Any(n => n < -1) == true || inventory.ClanMembers?.Values.Any(n => n < -1) == true)
            { Send(new Abort { Text = "Limite de retirada inválido." }, seq); return; }
            inventory.TakenCounts = artifact.Value.States.Access?.InventoryAccess?.TakenCounts ?? new();
            inventory.TakenCountsValidUntil = artifact.Value.States.Access?.InventoryAccess?.TakenCountsValidUntil;
            inventory.Friends ??= new(); inventory.ClanMembers ??= new(); access.InventoryAccess = inventory;
        }
        _world.ArtifactManager.SetAccess(msg.EntityId, access);
        Send(_world.ArtifactManager.Get(msg.EntityId).Value.States); Send(default(OK), seq);
    }
    private bool ClanArtifactAllows(AppearArtifact artifact, EstateRecord estate, Shared.Estate.AccessRights required)
    {
        if (!estate.AllowsClan(EntityId, required)) return false;
        if (ClanStore.CanManageEstate(EntityId, estate.OwnerId) || !artifact.States.Access.HasValue) return true;
        var access = artifact.States.Access.Value;
        int role = ClanStore.MemberRole(EntityId, estate.OwnerId);
        return access.ClanMembers?.GetValueOrDefault(role) == true;
    }
    private bool CanWithdrawArtifact(string entityId, int count, out string error)
    {
        error = null;
        var artifact = _world.ArtifactManager.Get(entityId);
        var inventory = artifact?.States.Access?.InventoryAccess;
        if (!inventory.HasValue) return true;
        if (!_world.TryGetEstateIdAtCell(World.CellFromTile(artifact.Value.Tile), out string estateId))
        { error = "Esta estrutura não pertence a um domínio autorizado."; return false; }
        var estate = _world.GetEstate(estateId);
        if (!string.IsNullOrEmpty(EstateAuthorityOwner(estate))) return true;
        var access = inventory.Value; int limit;
        if (ClanEstate(estate)) limit = access.ClanMembers?.GetValueOrDefault(ClanStore.MemberRole(EntityId, estate.OwnerId)) ?? 0;
        else
        {
            var friend = FriendStore.GetFriendType(estate.OwnerId, EntityId);
            limit = friend == Shared.Player.FriendType.Invalid ? access.Others : access.Friends?.GetValueOrDefault(friend) ?? 0;
        }
        int taken = access.TakenCountsValidUntil > Times.UnixTimeNow() ? access.TakenCounts?.GetValueOrDefault(EntityId) ?? 0 : 0;
        if (limit == -1 || count <= limit - taken) return true;
        error = "O limite diário de retirada desta estrutura foi atingido."; return false;
    }
    private void RecordArtifactWithdrawal(string entityId, int count)
    {
        var artifact = _world.ArtifactManager.Get(entityId);
        if (artifact?.States.Access?.InventoryAccess is not InventoryAccess inventory) return;
        var access = artifact.Value.States.Access.Value;
        double now = Times.UnixTimeNow();
        if (!inventory.TakenCountsValidUntil.HasValue || inventory.TakenCountsValidUntil <= now)
        { inventory.TakenCounts = new(); inventory.TakenCountsValidUntil = (Math.Floor(now / 86400) + 1) * 86400; }
        inventory.TakenCounts ??= new();
        inventory.TakenCounts[EntityId] = checked(inventory.TakenCounts.GetValueOrDefault(EntityId) + count);
        access.InventoryAccess = inventory; _world.ArtifactManager.SetAccess(entityId, access);
    }
}
