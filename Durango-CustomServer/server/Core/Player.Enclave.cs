using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Utils;
using Messages;
using Shared.Estate;
using Rights = Shared.Estate.AccessRights;

namespace Durango.Online;

public partial class Player
{
    private bool CommitEnclave(string estateId, EstateRecord after, long cost, Point2 changedCell, uint seq, bool replyOK = false)
    {
        if (!EconomyAvailable(seq)) return false;
        if (!_economy.ChangeClanEstate(_context, _world, LogicalRegionId(), estateId, after, cost, out string error))
        { Send(new Abort { Text = error }, seq); return false; }
        if (after == null || replyOK) Send(default(OK), seq);
        if (after != null) Send(_world.ToLicense(estateId, _world.GetEstate(estateId)), replyOK ? 0u : seq);
        BroadcastEstateGridsAround(changedCell);
        PushClanStateToOnline(CurrentClanId()); OnContextChanged(); return true;
    }
    private void DeclareEnclave(DeclareEstate msg, string clanId, uint seq)
    {
        var clan = ClanStore.Find(clanId);
        if (clan == null || ClanRules.Reward(clan.Level, "max_estate_number") == 0)
        { Send(new Abort { Text = "O território do clã é desbloqueado no nível 5." }, seq); return; }
        if (_world.TryGetEstateIdAtCell(msg.Cell, out _) || _world.EnumerateEstates().Any(e => e.Value.OwnerId == clanId && e.Value.Type == (int)OwnerType.ClanEstate))
        { Send(new Abort { Text = "Este local já está ocupado ou o clã já possui um enclave nesta ilha." }, seq); return; }
        Point2 tile = World.TileFromCell(msg.Cell);
        var record = new EstateRecord { OwnerId = clanId, Type = (int)OwnerType.ClanEstate, RegionId = LogicalRegionId(),
            Size = 1, LargestSize = 1, ActivatedAt = Times.UnixTimeNow(), TileX = tile.x, TileY = tile.y,
            Cells = new() { msg.Cell.x + "," + msg.Cell.y }, AccessForOthers = 0,
            AccessForClanMembers = clan.Roles.Keys.ToDictionary(id => id, id => id <= 1 ? (Rights)63 : (Rights)31) };
        CommitEnclave(Guid.NewGuid().ToString("N"), record, ClanRules.TerritoryCost(0), msg.Cell, seq);
    }
    private bool ClanEstate(EstateRecord estate) => estate?.Type is (int)OwnerType.ClanEstate or (int)OwnerType.ClanWarphole;
    private bool ValidateEnclaveAuthority(EstateRecord estate, uint seq)
    {
        if (!ClanEstate(estate) || estate.OwnerId != CurrentClanId() || !ClanStore.CanManageEstate(EntityId, estate.OwnerId))
        { Send(new Abort { Text = "Você não possui permissão para administrar este território do clã." }, seq); return false; }
        return true;
    }
    private bool EnclaveCellInBounds(Point2 cell)
    {
        var tile = World.TileFromCell(cell);
        return tile.x >= 0 && tile.y >= 0 && tile.x + World.EstateGridSize <= _world.NumChunksX * 16
            && tile.y + World.EstateGridSize <= _world.NumChunksY * 16;
    }
    private void ExpandEnclave(ExpandEstate msg, EstateRecord estate, uint seq)
    {
        if (!ValidateEnclaveAuthority(estate, seq)) return;
        var clan = ClanStore.Find(estate.OwnerId); int maximum = ClanRules.Reward(clan.Level, "max_estate_number");
        if (!EnclaveCellInBounds(msg.Cell) || !IsWithinTiles(World.TileFromCell(msg.Cell), ArtifactReachTiles + World.EstateGridSize)
            || !_world.CanExpandEstate(msg.EstateId, estate.OwnerId, msg.Cell, maximum))
        { Send(new Abort { Text = "Expansão fora de alcance, sem continuidade ou acima do limite do nível do clã." }, seq); return; }
        var after = Json.Read<EstateRecord>(Json.Write(estate));
        after.Cells.Add(msg.Cell.x + "," + msg.Cell.y); after.Size = after.Cells.Count;
        after.LargestSize = Math.Max(after.LargestSize, after.Size);
        CommitEnclave(msg.EstateId, after, estate.Size < estate.LargestSize ? 0 : ClanRules.TerritoryCost(estate.Size), msg.Cell, seq);
    }
    private void ShrinkEnclave(ShrinkEstate msg, EstateRecord estate, uint seq)
    {
        if (!ValidateEnclaveAuthority(estate, seq)) return;
        var after = Json.Read<EstateRecord>(Json.Write(estate));
        if (after.Cells.Count <= 1 || !after.Cells.Remove(msg.Cell.x + "," + msg.Cell.y) || !ConnectedCells(after.Cells))
        { Send(new Abort { Text = "A redução deve manter ao menos uma área e a continuidade do enclave." }, seq); return; }
        after.Size = after.Cells.Count;
        CommitEnclave(msg.EstateId, after, 0, msg.Cell, seq);
    }
    private static bool ConnectedCells(List<string> cells)
    {
        var left = new HashSet<string>(cells); var todo = new Queue<string>();
        todo.Enqueue(cells[0]); left.Remove(cells[0]);
        while (todo.Count > 0)
        {
            string[] p = todo.Dequeue().Split(','); int x = int.Parse(p[0]), y = int.Parse(p[1]);
            foreach (string key in new[] { (x - 1) + "," + y, (x + 1) + "," + y, x + "," + (y - 1), x + "," + (y + 1) })
                if (left.Remove(key)) todo.Enqueue(key);
        }
        return left.Count == 0;
    }
    private void SetEnclaveLicense(SetEstateLicense msg, EstateRecord estate, uint seq)
    {
        if (!ValidateEnclaveAuthority(estate, seq)) return;
        var after = Json.Read<EstateRecord>(Json.Write(estate)); after.SetAccessRights(msg.AccessRights);
        CommitEnclave(msg.EstateId, after, 0, ParseEstateCell(after.Cells[0]), seq, true);
    }
    private static Point2 ParseEstateCell(string cell)
    { string[] parts = cell.Split(','); return new Point2(int.Parse(parts[0]), int.Parse(parts[1])); }
    private void ExtendEnclave(ExtendEstateActivation msg, EstateRecord estate, uint seq)
    {
        if (!ValidateEnclaveAuthority(estate, seq)) return;
        var after = Json.Read<EstateRecord>(Json.Write(estate));
        after.ExpiresAt = Math.Max(Times.UnixTimeNow(), after.ExpiresAt ?? 0) + 7 * 86400;
        CommitEnclave(msg.EstateId, after, 0, ParseEstateCell(after.Cells[0]), seq);
    }
    private void RemoveEnclave(RemoveEstate msg, EstateRecord estate)
    {
        if (!ValidateEnclaveAuthority(estate, 0)) return;
        CommitEnclave(msg.EstateId, null, 0, ParseEstateCell(estate.Cells[0]), 0);
    }
}
