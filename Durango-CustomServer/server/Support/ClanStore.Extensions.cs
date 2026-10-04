using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Utils;
using Messages;
using Shared.Clan;

namespace Durango.Online;

internal static partial class ClanStore
{
    internal static bool HasPermission(ClanRecord clan, string actor, Permissions permission) =>
        clan != null && clan.Members.TryGetValue(actor, out var m) && clan.Roles.TryGetValue(m.RoleId, out var role)
        && (role.UserType == UserType.Root || (role.Permissions & permission) == permission);

    private static bool Leader(string actor, out ClanRecord clan, out string error)
    {
        clan = FindByMemberLocked(actor); error = null;
        if (clan?.Members[actor].RoleId == 0) return true;
        error = "Somente o líder pode administrar os cargos e as alianças do clã."; return false;
    }
    internal static int MemberRole(string actor, string clanId)
    {
        lock (Gate) return _state.Clans.TryGetValue(clanId ?? "", out var clan) && clan.Members.TryGetValue(actor, out var member) ? member.RoleId : -1;
    }
    internal static bool SetRoleInfo(string actor, int id, MemberRole info, out string error)
    {
        lock (Gate)
        {
            if (!Leader(actor, out var clan, out error)) return false;
            string name = info.Name?.Trim();
            if (id == 0 || id < -1 || id > 1000 || string.IsNullOrEmpty(name) || name.Length > 24 || ((int)info.Permissions & ~31) != 0)
            { error = "Cargo ou permissões inválidos. O cargo de líder é protegido."; return false; }
            if (id == -1) id = Enumerable.Range(1, 1000).FirstOrDefault(n => !clan.Roles.ContainsKey(n));
            if (id == 0 || (!clan.Roles.ContainsKey(id) && clan.Roles.Count >= 10))
            { error = "O limite é de dez cargos por clã."; return false; }
            int grade = clan.Roles.TryGetValue(id, out var old) ? old.Grade : clan.Roles.Values.Max(r => r.Grade) + 1;
            clan.Roles[id] = new ClanRoleRecord { Id = id, Name = name, Grade = grade, Permissions = info.Permissions, UserType = UserType.Normal };
            SaveLocked(); return true;
        }
    }
    internal static bool SetRoleGrades(string actor, RoleOrder[] orders, out string error)
    {
        lock (Gate)
        {
            if (!Leader(actor, out var clan, out error)) return false;
            if (orders == null || orders.Length != clan.Roles.Count || orders.Select(o => o.RoleId).Distinct().Count() != orders.Length
                || orders.Select(o => o.Grade).Distinct().Count() != orders.Length
                || orders.Any(o => !clan.Roles.ContainsKey(o.RoleId) || o.Grade < 0 || (o.RoleId == 0 ? o.Grade != 0 : o.Grade == 0)))
            { error = "Informe a ordenação completa dos cargos, com o líder na primeira posição."; return false; }
            foreach (var order in orders) clan.Roles[order.RoleId].Grade = order.Grade;
            SaveLocked(); return true;
        }
    }
    internal static bool RemoveRole(string actor, int id, int moveTo, out string error)
    {
        lock (Gate)
        {
            if (!Leader(actor, out var clan, out error)) return false;
            if (id <= 2 || moveTo == 0 || moveTo == id || !clan.Roles.ContainsKey(id) || !clan.Roles.ContainsKey(moveTo))
            { error = "Os três cargos básicos são protegidos. Escolha outro cargo para os integrantes."; return false; }
            foreach (var member in clan.Members.Values.Where(m => m.RoleId == id)) member.RoleId = moveTo;
            clan.Roles.Remove(id); SaveLocked(); return true;
        }
    }

    internal static bool TryEconomic(PlayerContext context, string action, string id, long amount, ClanResearchRecord research,
        Func<ClanEconomyOperation, long, bool> commit, out ClanRecord result, out string error)
    {
        lock (Gate)
        {
            result = null; error = null;
            var clan = FindByMemberLocked(context.EntityId);
            var op = new ClanEconomyOperation(); long debit = 0;
            switch (action)
            {
                case "create":
                    string name = id?.Trim();
                    if (clan != null || FindByApplierLocked(context.EntityId) != null || string.IsNullOrEmpty(name) || name.Length < 2 || name.Length > 24
                        || _state.Clans.Values.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
                    { error = "Nome de clã inválido, já utilizado ou personagem com vínculo pendente."; return false; }
                    clan = new ClanRecord { Id = Guid.NewGuid().ToString("N"), Name = name, CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
                    clan.Members[context.EntityId] = new ClanMemberRecord { EntityId = context.EntityId, Name = context.PlayerInfo?.PlayerName ?? context.EntityId, RoleId = 0, JoinedAt = clan.CreatedAt };
                    op.Created = clan; debit = ClanRules.CreationCost; break;
                case "donate":
                    if (clan == null || amount <= 0 || amount > EconomyStore.BalanceLimit || clan.Fund > EconomyStore.BalanceLimit - amount)
                    { error = "Doação inválida ou limite do fundo atingido."; return false; }
                    op.FundDelta = amount; debit = amount; break;
                case "research":
                    var definition = ClanRules.Research(id);
                    if (clan == null || definition == null || research == null || !HasPermission(clan, context.EntityId, Permissions.Research)
                        || clan.Level < ClanRules.ResearchLevel((int)definition["category"]) ||
                        clan.Researches.TryGetValue(id, out var prior) && Math.Max(prior.Until, prior.CooltimeUntil) > Times.UnixTimeNow() ||
                        clan != null && research != null && clan.Researches.Values.Any(r => r.Lab == research.Lab && Math.Max(r.Until, r.CooltimeUntil) > Times.UnixTimeNow()))
                    { error = "Pesquisa indisponível para o nível, cargo ou período atual do clã."; return false; }
                    op.FundDelta = -(long)definition["amount"]; op.Research = research; break;
                case "estate":
                    if (clan == null || amount < 0 || !HasPermission(clan, context.EntityId, Permissions.OccupyWarphole))
                    { error = "Sem permissão para gastar o fundo em território."; return false; }
                    op.FundDelta = -amount; break;
                default: error = "Transação de clã inválida."; return false;
            }
            op.ClanId = clan.Id;
            if (clan.Fund + op.FundDelta < 0) { error = "Saldo insuficiente no fundo do clã."; return false; }
            if (!commit(op, debit)) return false;
            result = Find(clan.Id); return true;
        }
    }
    internal static void ApplyEconomic(ClanEconomyOperation op, long sequence)
    {
        if (op == null) return;
        lock (Gate)
        {
            if (sequence <= _state.EconomySequence) return;
            if (op.Created != null) _state.Clans[op.ClanId] = Json.Read<ClanRecord>(Json.Write(op.Created));
            if (!_state.Clans.TryGetValue(op.ClanId, out var clan)) throw new InvalidOperationException("Clã da transação não encontrado.");
            clan.Fund = checked(clan.Fund + op.FundDelta);
            if (clan.Fund < 0) throw new InvalidOperationException("Transação causaria fundo negativo.");
            if (op.Research != null) clan.Researches[op.Research.Id] = Json.Read<ClanResearchRecord>(Json.Write(op.Research));
            _state.EconomySequence = sequence; SaveLocked();
        }
    }
    internal static bool AddExperience(string member, int xp)
    {
        if (xp <= 0) return false;
        lock (Gate)
        {
            var clan = FindByMemberLocked(member); if (clan == null) return false;
            int before = clan.Level;
            clan.Exp = Math.Min(811176816, clan.Exp + xp); clan.Level = ClanRules.Level(clan.Exp);
            SaveLocked(); return clan.Level != before;
        }
    }
    internal static AllySlot[] AllySlots(string actor)
    {
        lock (Gate)
        {
            var clan = FindByMemberLocked(actor); if (clan == null) return Array.Empty<AllySlot>();
            PruneAllies();
            return clan.Allies.Select(a => new AllySlot
            {
                ClanId = a.Key, IsAlly = a.Value.IsAlly, AllySince = a.Value.IsAlly ? a.Value.Since : null,
                State = a.Value.Locked ? AllySlotState.Locked : a.Value.Proposer == null ? AllySlotState.Solid
                    : a.Value.Proposer == clan.Id ? AllySlotState.Suggested : AllySlotState.BeenSuggested,
                StateExpiresAt = a.Value.Until > 0 ? a.Value.Until : null
            }).ToArray();
        }
    }
    private static void PruneAllies()
    {
        double now = Times.UnixTimeNow(); bool changed = false;
        foreach (var clan in _state.Clans.Values)
            foreach (var entry in clan.Allies.ToArray())
                if (!_state.Clans.ContainsKey(entry.Key) || entry.Value.Until > 0 && entry.Value.Until <= now)
                {
                    if (entry.Value.IsAlly) { entry.Value.Proposer = null; entry.Value.Breaking = false; entry.Value.Until = 0; }
                    else clan.Allies.Remove(entry.Key);
                    changed = true;
                }
        if (changed) SaveLocked();
    }
    internal static bool ChangeAlly(string actor, string target, string action, out string error)
    {
        lock (Gate)
        {
            if (!Leader(actor, out var clan, out error)) return false;
            PruneAllies();
            if (!_state.Clans.TryGetValue(target ?? "", out var other) || other.Id == clan.Id)
            { error = "Clã de destino inválido."; return false; }
            clan.Allies.TryGetValue(other.Id, out var relation);
            double now = Times.UnixTimeNow();
            if (action == "suggest")
            {
                if (relation != null || clan.Allies.Count >= ClanRules.AllyCapacity(clan.Level) || other.Allies.Count >= ClanRules.AllyCapacity(other.Level))
                { error = "Relação já existente ou slots de aliança esgotados."; return false; }
                relation = new ClanAllyRecord { Proposer = clan.Id, Until = now + 86400 };
            }
            else
            {
                if (relation == null || relation.Locked) { error = "Aliança ou proposta não encontrada."; return false; }
                switch (action)
                {
                    case "suggestBreak":
                        if (!relation.IsAlly || relation.Proposer != null) { error = "Não existe aliança disponível para negociar o encerramento."; return false; }
                        relation.Proposer = clan.Id; relation.Breaking = true; relation.Until = now + 86400; break;
                    case "accept":
                        if (relation.Proposer == null || relation.Proposer == clan.Id) { error = "Somente o clã destinatário pode aceitar uma proposta."; return false; }
                        if (relation.Breaking) { clan.Allies.Remove(other.Id); other.Allies.Remove(clan.Id); SaveLocked(); return true; }
                        relation.IsAlly = true; relation.Since = now; relation.Proposer = null; relation.Until = 0; break;
                    case "refuse":
                        if (relation.Proposer == null) { error = "Proposta não encontrada."; return false; }
                        if (!relation.IsAlly) { clan.Allies.Remove(other.Id); other.Allies.Remove(clan.Id); SaveLocked(); return true; }
                        relation.Proposer = null; relation.Breaking = false; relation.Until = 0; break;
                    case "break":
                        if (!relation.IsAlly) { error = "Os clãs não são aliados."; return false; }
                        relation.IsAlly = false; relation.Locked = true; relation.Proposer = null; relation.Breaking = false; relation.Until = now + 86400; break;
                    default: error = "Comando de aliança inválido."; return false;
                }
            }
            clan.Allies[other.Id] = relation;
            other.Allies[clan.Id] = Json.Read<ClanAllyRecord>(Json.Write(relation));
            SaveLocked(); return true;
        }
    }
}

public sealed partial class EconomyStore
{
    internal bool ClanTransaction(PlayerContext context, string action, string id, long amount, ClanResearchRecord research, out ClanRecord clan, out string error)
    {
        lock (_sync)
        {
            string transactionError = null;
            bool ok = ClanStore.TryEconomic(context, action, id, amount, research, (op, debit) =>
            {
                long balance = Balance(context, Shared.Economy.Currency.TStone);
                if (debit > balance) { transactionError = "Saldo insuficiente em T-Stones."; return false; }
                return Commit(Next(), context, new EconomyEffect {
                    ClanOperation = op, Balances = debit > 0 ? new() { [Shared.Economy.Currency.TStone] = balance - debit } : null
                }, out transactionError);
            }, out clan, out error);
            error ??= transactionError; return ok;
        }
    }
}
