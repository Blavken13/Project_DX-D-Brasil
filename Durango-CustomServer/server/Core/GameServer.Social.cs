using System.Linq;
using Messages;
namespace Durango.Online;

public partial class GameServer
{
    private double _partyStatusAt;
    private void NotifyClanChanged(string entityId)
    {
        foreach (var pair in _radiotowerConnections.Where(p => p.Value == entityId).ToArray())
        {
            pair.Key.Send(default(ClanInfoUpdated));
            pair.Key.Send(default(ClanStatusEffectsUpdated));
            pair.Key.Send(default(ClanRewardsUpdated));
        }
    }
    private void PublishPartyStatus()
    {
        if (Gauge.CurrentTime < _partyStatusAt) return;
        _partyStatusAt = Gauge.CurrentTime + 2;
        foreach (var target in _radiotowerConnections.ToArray())
        {
            var team = PartyStore.Snapshot(target.Value);
            if (team?.Members[target.Value].Accepted != true) continue;
            foreach (var member in team.Members.Where(m => m.Value.Accepted))
            {
                var status = Player.OnlinePartierStatus(member.Key) ?? new PartierStatus { EntityId = member.Key, IsOnline = false };
                target.Key.Send(status);
            }
        }
    }
}
