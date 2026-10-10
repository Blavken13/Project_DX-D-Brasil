using System;
using System.Collections.Generic;
using Durango.Network;
using Durango.Utils;
using Messages;
using Newtonsoft.Json.Linq;
using Shared.Building;
using Yaml.Util;

namespace Durango.Online;

public partial class Player
{
    private sealed record PendingCrater(string EntityId, int Amount, uint Sequence, double DueAt);
    private PendingCrater _pendingCrater;

    public int InductionStones => Math.Max(0, _context.Vouchers?.GetValueOrDefault(CrackTuning.VoucherId) ?? 0);

    public void AddInductionStones(int amount, string reason = "Recompensa")
    {
        if (amount <= 0) return;
        int max = InductionRewardTuning.Maximum;
        _context.Vouchers ??= new Dictionary<string, int>();
        int before = InductionStones;
        _context.Vouchers[CrackTuning.VoucherId] = (int)Math.Min(max, (long)before + amount);
        Console.WriteLine($"[pedras-portal] {ShortEntityId()} +{InductionStones - before} → {InductionStones} ({reason})");
        PushWallet();
    }

    private void RegisterCraterHandlers()
    {
        _connection.Recv(delegate(InvestToCrack msg, PacketHeader header)
        {
            var artifact = _world.ArtifactManager.Get(msg.EntityId);
            if (_pendingCrater != null || !CanInvestInCrater(artifact, msg.Amount, Gauge.CurrentTime) ||
                !_world.ReserveCrater(msg.EntityId))
            {
                Console.WriteLine($"[cratera] rejeitado {msg.EntityId}: solicitado={msg.Amount}, custo={artifact?.States.Crack?.RequiredInvestment}, saldo={InductionStones}, pendente={_pendingCrater != null}");
                Send(new Abort { Text = "Cratera indisponível, fora de alcance ou pedras de indução insuficientes." }, header.Seq);
                return;
            }
            _pendingCrater = new PendingCrater(msg.EntityId, msg.Amount, header.Seq,
                Gauge.CurrentTime + CrackTuning.InvestmentDuration);
            Send(default(ReplySequenceMark), header.Seq);
            Send(new Messages.Timer { Duration = CrackTuning.InvestmentDuration }, header.Seq);
        });
        _connection.ConnetionClosed += CancelCraterInvestment;
    }

    private bool CanInvestInCrater(AppearArtifact? artifact, int amount, double now) =>
        _context.AppearPlayer.IsAlive && artifact is { } a && a.States.BuildingState == BuildingState.Completed &&
        a.States.Crack is { } crack &&
        (!crack.ActivatedUntil.HasValue || crack.ActivatedUntil <= now) &&
        amount > 0 && amount == crack.RequiredInvestment && InductionStones >= amount &&
        IsWithinTiles(a.Tile, ArtifactReachTiles + Math.Max(a.Size.x, a.Size.y));

    private void CancelCraterInvestment()
    {
        if (_pendingCrater is not { } pending) return;
        _pendingCrater = null;
        _world.ReleaseCrater(pending.EntityId);
    }

    private void InterruptCraterInvestment()
    {
        if (_pendingCrater is not { } pending) return;
        CancelCraterInvestment();
        Send(new Abort { Text = "Indução interrompida pelo movimento. As pedras foram preservadas." }, pending.Sequence);
        Send(default(ReplySequenceMark), pending.Sequence);
    }

    private void UpdateCraterInvestment(double now)
    {
        if (_pendingCrater is not { } pending || now < pending.DueAt) return;
        CancelCraterInvestment();
        if (!CanInvestInCrater(_world.ArtifactManager.Get(pending.EntityId), pending.Amount, now) ||
            !_world.ActivateCrater(pending.EntityId, now))
            Send(new Abort { Text = "A indução foi cancelada. As pedras foram preservadas." }, pending.Sequence);
        else
        {
            _context.Vouchers[CrackTuning.VoucherId] = InductionStones - pending.Amount;
            PushWallet();
            Send(default(OK), pending.Sequence);
        }
        Send(default(ReplySequenceMark), pending.Sequence);
    }
}
