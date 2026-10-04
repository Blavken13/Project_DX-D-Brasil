using Durango.Network;
using Messages;
namespace Durango.Online;
public partial class Player
{
    private void ChangeAlly(string target, string action, uint seq)
    {
        if (!ClanStore.ChangeAlly(EntityId, target, action, out string error))
        { Send(new Abort { Text = error }, seq); return; }
        PushClanStateToOnline(CurrentClanId()); PushClanStateToOnline(target);
        Send(default(OK), seq);
    }
    private void RegisterAllyHandlers()
    {
        _connection.Recv(delegate(GetAllySlots m, PacketHeader h) { Send(new AllySlots { Slots = ClanStore.AllySlots(EntityId) }, h.Seq); });
        _connection.Recv(delegate(SuggestAlly m, PacketHeader h) { ChangeAlly(m.ClanId, "suggest", h.Seq); });
        _connection.Recv(delegate(SuggestBreak m, PacketHeader h) { ChangeAlly(m.ClanId, "suggestBreak", h.Seq); });
        _connection.Recv(delegate(AcceptSuggestion m, PacketHeader h) { ChangeAlly(m.ClanId, "accept", h.Seq); });
        _connection.Recv(delegate(RefuseSuggestion m, PacketHeader h) { ChangeAlly(m.ClanId, "refuse", h.Seq); });
        _connection.Recv(delegate(BreakAlly m, PacketHeader h) { ChangeAlly(m.ClanId, "break", h.Seq); });
    }
}
