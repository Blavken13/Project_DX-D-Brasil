using System.Linq;
using Durango.Utils;
namespace Durango.Online;

public partial class World
{
    internal void ApplyClanEstate(ClanEstateOperation operation, long sequence)
    {
        if (sequence <= _context.ClanEconomySequence) return;
        foreach (var key in _context.EstateCells.Where(p => p.Value == operation.EstateId).Select(p => p.Key).ToArray())
            _context.EstateCells.Remove(key);
        _context.Estates.Remove(operation.EstateId);
        if (operation.Record != null)
        {
            var record = Json.Read<EstateRecord>(Json.Write(operation.Record));
            _context.Estates[operation.EstateId] = record;
            foreach (string cell in record.Cells) _context.EstateCells[cell] = operation.EstateId;
        }
        _context.ClanEconomySequence = sequence;
        Save();
    }
}

public sealed partial class EconomyStore
{
    internal void RecoverClanWorld(World world, string regionId)
    {
        lock (_sync)
            foreach (var effect in _state.Effects.Where(e => e.ClanEstate?.RegionId == regionId))
                world.ApplyClanEstate(effect.ClanEstate, effect.Sequence);
    }
    internal bool ChangeClanEstate(PlayerContext context, World world, string regionId, string estateId, EstateRecord after, long cost, out string error)
    {
        lock (_sync)
        {
            string transactionError = null;
            bool ok = ClanStore.TryEconomic(context, "estate", null, cost, null, (op, debit) =>
                Commit(Next(), context, new EconomyEffect {
                    ClanOperation = op,
                    ClanEstate = new ClanEstateOperation { RegionId = regionId, EstateId = estateId,
                        Record = after == null ? null : Json.Read<EstateRecord>(Json.Write(after)), RuntimeWorld = world }
                }, out transactionError), out _, out error);
            error ??= transactionError; return ok;
        }
    }
}
