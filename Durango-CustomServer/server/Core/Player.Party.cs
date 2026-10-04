using System;
using System.Linq;
using Durango.Network;
using Messages;
using UnityEngine;

namespace Durango.Online;

public partial class Player
{
    private double _partyRefreshAt;
    private bool _hadParty;
    internal static PartierStatus? OnlinePartierStatus(string id) => FindClanOnlinePlayer(id)?.BuildPartierStatus();
    internal PartierStatus BuildPartierStatus()
    {
        double now = Gauge.CurrentTime;
        var pos = PlayerWorldPosition();
        return new PartierStatus
        {
            EntityId = EntityId, RegionId = LogicalRegionId(), Tile = new Point2((int)(pos.x / 200), (int)(pos.y / 200)),
            Health = new Vector2(_survival.ValueAt(SurvivalState.KeyLife, now), _survival.MaxOf(SurvivalState.KeyLife, now)),
            Energy = new Vector2(_survival.ValueAt(SurvivalState.KeyEnergy, now), _survival.MaxOf(SurvivalState.KeyEnergy, now)),
            Level = _skillLevel, IsOnline = true, ExpiresAt = now + 10
        };
    }
    private static PartierStatus PartyStatus(string id, PartyStore.Member member)
    {
        var online = OnlinePartierStatus(id); if (online.HasValue) return online.Value;
        var status = member.Status; status.EntityId = id; status.IsOnline = false;
        status.Level = Math.Max(1, status.Level); return status;
    }
    private void SendParty(uint seq = 0)
    {
        var team = PartyStore.Snapshot(EntityId);
        _hadParty = team != null;
        if (team == null) { Send(new Party { Id = "", Info = null }, seq); return; }
        Player leader = FindClanOnlinePlayer(team.Leader);
        Send(new Party
        {
            Id = team.Id,
            Info = new PartyInfo
            {
                LeaderRadioId = new RadioId { Name = leader?._context.AppearPlayer.Name ?? team.Members[team.Leader].Name ?? team.Leader, Freq = leader?._context.AppearPlayer.Freq ?? 0 },
                LeaderStatus = PartyStatus(team.Leader, team.Members[team.Leader]),
                MemberStatus = team.Members.Where(m => m.Key != team.Leader)
                    .Select(m => new Pair<PartierStatus, bool>(PartyStatus(m.Key, m.Value), m.Value.Accepted)).ToArray()
            }
        }, seq);
    }
    private void ChangeParty(string action, string target, uint seq)
    {
        if (action == "invite" && FindClanOnlinePlayer(target) == null)
        { Send(new Abort { Text = "O jogador precisa estar online para receber o convite." }, seq); return; }
        if (!PartyStore.Change(EntityId, action, target, out string[] affected, out string error))
        { Send(new Abort { Text = error }, seq); return; }
        foreach (string id in affected) FindClanOnlinePlayer(id)?.SendParty();
    }
    private void UpdateParty()
    {
        if (Gauge.CurrentTime < _partyRefreshAt) return;
        _partyRefreshAt = Gauge.CurrentTime + 2;
        PartyStore.UpdateProfile(EntityId, _context.AppearPlayer.Name, BuildPartierStatus());
        if (PartyStore.Snapshot(EntityId) != null || _hadParty) SendParty();
    }
    private void RegisterPartyHandlers()
    {
        PartyStore.Load(_context.Path);
        PartyStore.UpdateProfile(EntityId, _context.AppearPlayer.Name, BuildPartierStatus());
        _connection.ConnetionClosed += () =>
        {
            var status = BuildPartierStatus(); status.IsOnline = false;
            PartyStore.UpdateProfile(EntityId, _context.AppearPlayer.Name, status, true);
        };
        _connection.Recv(delegate(GetParty msg, PacketHeader h) { SendParty(h.Seq); });
        _connection.Recv(delegate(MakeParty msg, PacketHeader h) { ChangeParty("make", null, h.Seq); });
        _connection.Recv(delegate(InviteIntoParty msg, PacketHeader h) { ChangeParty("invite", msg.InviteeEntityId, h.Seq); });
        _connection.Recv(delegate(JoinIntoParty msg, PacketHeader h) { ChangeParty("join", null, h.Seq); });
        _connection.Recv(delegate(RejectPartyInvitation msg, PacketHeader h) { ChangeParty("reject", msg.InviteeEntityId, h.Seq); });
        _connection.Recv(delegate(LeaveParty msg, PacketHeader h) { ChangeParty("leave", null, h.Seq); });
        _connection.Recv(delegate(KickPartyMember msg, PacketHeader h) { ChangeParty("kick", msg.MemberEntityId, h.Seq); });
        _connection.Recv(delegate(ElectPartyLeader msg, PacketHeader h) { ChangeParty("elect", msg.MemberEntityId, h.Seq); });
        _connection.Recv(delegate(ResubscribePartyChannel msg, PacketHeader h) { SendParty(); });
        _connection.Recv(delegate(GetRoutesOfParty msg, PacketHeader h)
        {
            if (!PartyStore.Accepted(EntityId)) { Send(new Abort { Text = "Você não pertence a um grupo." }, h.Seq); return; }
            HandleGetRoutesMsg(h.Seq);
        });
    }
}
