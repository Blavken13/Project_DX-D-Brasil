using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Durango.Network;
using Durango.Utils;
using Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared.Chat;
namespace Durango.Online;
internal sealed class ClanMemberRecord
{
    [JsonProperty("entity_id")]
    public string EntityId;
    [JsonProperty("name")]
    public string Name;
    [JsonProperty("role_id")]
    public int RoleId;
    [JsonProperty("joined_at")]
    public long JoinedAt;
}
internal sealed class ClanRecord
{
    [JsonProperty("id")]
    public string Id;
    [JsonProperty("name")]
    public string Name;
    [JsonProperty("level")]
    public int Level = 1;
    [JsonProperty("exp")]
    public long Exp;
    public long Fund;
    public Dictionary<int, ClanRoleRecord> Roles = ClanRules.DefaultRoles();
    public Dictionary<string, ClanAllyRecord> Allies = new();
    public Dictionary<string, ClanResearchRecord> Researches = new();
    [JsonProperty("created_at")]
    public long CreatedAt;
    [JsonProperty("notice")]
    public string Notice = string.Empty;
    [JsonProperty("intro")]
    public string Intro = string.Empty;
    [JsonProperty("emblem")]
    public byte[] Emblem = Array.Empty<byte>();
    [JsonProperty("members")]
    public Dictionary<string, ClanMemberRecord> Members = new();
    [JsonProperty("appliers")]
    public Dictionary<string, ClanMemberRecord> Appliers = new();
}
internal sealed class ClanInviteRecord
{
    [JsonProperty("target_entity_id")]
    public string TargetEntityId;
    [JsonProperty("clan_id")]
    public string ClanId;
    [JsonProperty("invited_by")]
    public string InvitedBy;
    [JsonProperty("created_at")]
    public long CreatedAt;
}
internal sealed class ClanStoreState
{
    [JsonProperty("clans")]
    public Dictionary<string, ClanRecord> Clans = new();
    [JsonProperty("invites")]
    public Dictionary<string, ClanInviteRecord> Invites = new();
    public long EconomySequence;
}
internal static class ClanRolePolicy
{
    public const int Leader = 0;
    public const int Officer = 1;
    public const int Member = 2;
    public static bool IsValid(int roleId) =>
        roleId is Leader or Officer or Member;
    public static bool CanManageMembers(int roleId) =>
        roleId is Leader or Officer;
    public static bool CanManageSettlement(int roleId) =>
        roleId is Leader or Officer;
    public static bool CanEditClanInfo(int roleId) =>
        roleId is Leader or Officer;
}
internal static partial class ClanStore
{
    private static readonly object Gate = new();
    private static ClanStoreState _state = new();
    private static string _path;
    private static bool _loaded;
    private static string _committed;
    public static void EnsureLoaded(string playerPath)
    {
        if (_loaded) return;
        if (string.IsNullOrWhiteSpace(playerPath)) return;
        lock (Gate)
        {
            if (_loaded) return;
            string directory = Path.GetDirectoryName(playerPath);
            if (string.IsNullOrWhiteSpace(directory)) return;
            _path = Path.Combine(directory, "clans.json");
            if (File.Exists(_path) || File.Exists(_path + ".bak"))
            {
                ClanStoreState loaded = SafeSave.ReadWithBackup(
                    _path,
                    "clan-store",
                    data => Json.Read<ClanStoreState>(data));
                if (loaded != null)
                {
                    _state = loaded;
                }
            }
            NormalizeLocked();
            _committed = Json.Write(_state);
            _loaded = true;
            Console.WriteLine(
                $"[clã] carregados {_state.Clans.Count} clãs de {_path}");
        }
    }
    private static void NormalizeLocked()
    {
        _state ??= new ClanStoreState();
        _state.Clans ??= new Dictionary<string, ClanRecord>();
        _state.Invites ??= new Dictionary<string, ClanInviteRecord>();
        foreach (ClanRecord clan in _state.Clans.Values)
        {
            if (clan == null) continue;
            clan.Members ??= new Dictionary<string, ClanMemberRecord>();
            clan.Appliers ??= new Dictionary<string, ClanMemberRecord>();
            clan.Roles ??= ClanRules.DefaultRoles();
            clan.Allies ??= new();
            clan.Researches ??= new();
            clan.Notice ??= string.Empty;
            clan.Intro ??= string.Empty;
            clan.Emblem ??= Array.Empty<byte>();
            if (clan.Level <= 0) clan.Level = 1;
        }
    }
    private static void SaveLocked()
    {
        if (string.IsNullOrWhiteSpace(_path) || !SafeSave.WriteAtomic(_path, Json.WriteToBytes(_state, false), "clan-store"))
        {
            _state = Json.Read<ClanStoreState>(_committed) ?? new();
            throw new IOException("Não foi possível salvar a alteração do clã.");
        }
        _committed = Json.Write(_state);
    }
    private static ClanRecord FindByMemberLocked(string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId)) return null;
        foreach (ClanRecord clan in _state.Clans.Values)
        {
            if (clan?.Members != null && clan.Members.ContainsKey(entityId))
            {
                return clan;
            }
        }
        return null;
    }
    private static ClanRecord FindByApplierLocked(string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId)) return null;
        foreach (ClanRecord clan in _state.Clans.Values)
        {
            if (clan?.Appliers != null && clan.Appliers.ContainsKey(entityId))
            {
                return clan;
            }
        }
        return null;
    }
    public static ClanRecord Find(string clanId)
    {
        lock (Gate)
        {
            if (string.IsNullOrWhiteSpace(clanId)) return null;
            return _state.Clans.TryGetValue(clanId, out ClanRecord clan) ? Json.Read<ClanRecord>(Json.Write(clan)) : null;
        }
    }
    public static List<ClanRecord> Search(string keyword)
    {
        lock (Gate)
        {
            IEnumerable<ClanRecord> query = _state.Clans.Values.Where(clan => clan != null);
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.Where(clan =>
                    clan.Name?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            return query
                .OrderBy(clan => clan.Name, StringComparer.OrdinalIgnoreCase)
                .Take(30)
                .Select(c => Json.Read<ClanRecord>(Json.Write(c)))
                .ToList();
        }
    }
    public static bool TryCreate(
        PlayerContext context,
        string requestedName,
        out ClanRecord created,
        out string error)
    {
        created = null;
        error = null;
        string name = requestedName?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length < 2 || name.Length > 24)
        {
            error = "O nome do clã deve possuir entre 2 e 24 caracteres.";
            return false;
        }
        lock (Gate)
        {
            if (FindByMemberLocked(context.EntityId) != null)
            {
                error = "Você já pertence a um clã.";
                return false;
            }
            if (FindByApplierLocked(context.EntityId) != null)
            {
                error = "Cancele sua solicitação pendente antes de criar um clã.";
                return false;
            }
            if (_state.Clans.Values.Any(clan =>
                    string.Equals(clan?.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                error = "Já existe um clã com esse nome.";
                return false;
            }
            string clanId = Guid.NewGuid().ToString("N");
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            created = new ClanRecord
            {
                Id = clanId,
                Name = name,
                Level = 1,
                Exp = 0,
                CreatedAt = now
            };
            created.Members[context.EntityId] = new ClanMemberRecord
            {
                EntityId = context.EntityId,
                Name = context.PlayerInfo?.PlayerName ?? context.EntityId,
                RoleId = 0,
                JoinedAt = now
            };
            _state.Clans[clanId] = created;
            SaveLocked();
            return true;
        }
    }
    public static bool TryApply(
        PlayerContext context,
        string clanId,
        out string error)
    {
        error = null;
        lock (Gate)
        {
            if (FindByMemberLocked(context.EntityId) != null)
            {
                error = "Você já pertence a um clã.";
                return false;
            }
            if (!_state.Clans.TryGetValue(clanId ?? string.Empty, out ClanRecord clan))
            {
                error = "Clã não encontrado.";
                return false;
            }
            if (clan.Members.Count >= ClanRules.Reward(clan.Level, "capacity")) { error = "O clã atingiu seu limite de integrantes."; return false; }
            ClanRecord existingApplication = FindByApplierLocked(context.EntityId);
            bool preApproved =
                _state.Invites.TryGetValue(context.EntityId, out ClanInviteRecord invite) &&
                string.Equals(invite.ClanId, clan.Id, StringComparison.Ordinal) &&
                DateTimeOffset.UtcNow.ToUnixTimeSeconds() - invite.CreatedAt < 86400;
            if (preApproved)
            {
                if (existingApplication != null &&
                    !string.Equals(existingApplication.Id, clan.Id, StringComparison.Ordinal))
                {
                    error = "Cancele sua solicitação pendente antes de aceitar este convite.";
                    return false;
                }
                existingApplication?.Appliers.Remove(context.EntityId);
                clan.Appliers.Remove(context.EntityId);
                _state.Invites.Remove(context.EntityId);
                clan.Members[context.EntityId] = new ClanMemberRecord
                {
                    EntityId = context.EntityId,
                    Name = context.PlayerInfo?.PlayerName ?? context.EntityId,
                    RoleId = ClanRolePolicy.Member,
                    JoinedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };
                SaveLocked();
                return true;
            }
            if (existingApplication != null)
            {
                if (string.Equals(existingApplication.Id, clanId, StringComparison.Ordinal))
                {
                    return true;
                }
                error = "Você já possui uma solicitação de entrada pendente.";
                return false;
            }
            clan.Appliers[context.EntityId] = new ClanMemberRecord
            {
                EntityId = context.EntityId,
                Name = context.PlayerInfo?.PlayerName ?? context.EntityId,
                RoleId = -1,
                JoinedAt = 0
            };
            SaveLocked();
            return true;
        }
    }
    public static bool TryApprove(
        string actorEntityId,
        string applicantEntityId,
        out string error)
    {
        error = null;
        lock (Gate)
        {
            ClanRecord clan = FindByMemberLocked(actorEntityId);
            if (clan == null ||
                !clan.Members.TryGetValue(actorEntityId, out ClanMemberRecord actor) ||
                !HasPermission(clan, actorEntityId, Shared.Clan.Permissions.ApproveMember))
            {
                error = "Somente Líder ou Oficial pode aprovar novos membros.";
                return false;
            }
            if (!clan.Appliers.TryGetValue(applicantEntityId ?? string.Empty, out ClanMemberRecord applicant))
            {
                error = "Solicitação de entrada não encontrada.";
                return false;
            }
            if (FindByMemberLocked(applicantEntityId) != null)
            {
                clan.Appliers.Remove(applicantEntityId);
                SaveLocked();
                error = "Esse jogador já pertence a outro clã.";
                return false;
            }
            if (clan.Members.Count >= ClanRules.Reward(clan.Level, "capacity")) { error = "O clã atingiu seu limite de integrantes."; return false; }
            clan.Appliers.Remove(applicantEntityId);
            applicant.RoleId = ClanRolePolicy.Member;
            applicant.JoinedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            clan.Members[applicantEntityId] = applicant;
            SaveLocked();
            return true;
        }
    }
    public static bool TryDropApplication(
        string actorEntityId,
        string applicantEntityId,
        out string error)
    {
        error = null;
        lock (Gate)
        {
            ClanRecord clan = FindByMemberLocked(actorEntityId);
            if (clan == null ||
                !clan.Members.TryGetValue(actorEntityId, out ClanMemberRecord actor) ||
                !HasPermission(clan, actorEntityId, Shared.Clan.Permissions.ApproveMember))
            {
                error = "Somente Líder ou Oficial pode recusar solicitações.";
                return false;
            }
            if (!clan.Appliers.Remove(applicantEntityId ?? string.Empty))
            {
                error = "Solicitação de entrada não encontrada.";
                return false;
            }
            SaveLocked();
            return true;
        }
    }
    public static bool TryCancelApplication(
        string entityId,
        string clanId,
        out string error)
    {
        error = null;
        lock (Gate)
        {
            ClanRecord clan = FindByApplierLocked(entityId);
            if (clan == null ||
                (!string.IsNullOrEmpty(clanId) &&
                 !string.Equals(clan.Id, clanId, StringComparison.Ordinal)))
            {
                error = "Você não possui solicitação pendente para esse clã.";
                return false;
            }
            clan.Appliers.Remove(entityId);
            SaveLocked();
            return true;
        }
    }
    public static bool TryLeave(string entityId, out string error)
    {
        error = null;
        lock (Gate)
        {
            ClanRecord clan = FindByMemberLocked(entityId);
            if (clan == null ||
                !clan.Members.TryGetValue(entityId, out ClanMemberRecord member))
            {
                error = "Você não pertence a um clã.";
                return false;
            }
            if (member.RoleId == 0)
            {
                if (clan.Members.Count > 1)
                {
                    error = "O líder precisa transferir a liderança antes de sair do clã.";
                    return false;
                }
                _state.Clans.Remove(clan.Id);
                foreach (var other in _state.Clans.Values) other.Allies.Remove(clan.Id);
                foreach (string invitedEntityId in _state.Invites
                             .Where(pair => string.Equals(
                                 pair.Value?.ClanId,
                                 clan.Id,
                                 StringComparison.Ordinal))
                             .Select(pair => pair.Key)
                             .ToArray())
                {
                    _state.Invites.Remove(invitedEntityId);
                }
                SaveLocked();
                return true;
            }
            clan.Members.Remove(entityId);
            SaveLocked();
            return true;
        }
    }
    public static bool TryKick(
        string actorEntityId,
        string targetEntityId,
        out string error)
    {
        error = null;
        lock (Gate)
        {
            ClanRecord clan = FindByMemberLocked(actorEntityId);
            if (clan == null ||
                !clan.Members.TryGetValue(actorEntityId, out ClanMemberRecord actor) ||
                !HasPermission(clan, actorEntityId, Shared.Clan.Permissions.ApproveMember))
            {
                error = "Você não possui permissão para remover membros.";
                return false;
            }
            if (!clan.Members.TryGetValue(targetEntityId ?? string.Empty, out ClanMemberRecord target))
            {
                error = "Membro não encontrado.";
                return false;
            }
            if (target.RoleId == ClanRolePolicy.Leader)
            {
                error = "O Líder do clã não pode ser removido.";
                return false;
            }
            if (clan.Roles[actor.RoleId].Grade >= clan.Roles[target.RoleId].Grade)
            {
                error = "Oficiais só podem remover membros comuns.";
                return false;
            }
            clan.Members.Remove(targetEntityId);
            _state.Invites.Remove(targetEntityId);
            SaveLocked();
            return true;
        }
    }
    public static string ClanIdOf(string entityId)
    {
        lock (Gate)
        {
            return FindByMemberLocked(entityId)?.Id;
        }
    }
    public static bool IsRelatedToClan(string entityId, string clanId)
    {
        if (string.IsNullOrEmpty(entityId) || string.IsNullOrEmpty(clanId))
        {
            return false;
        }
        lock (Gate)
        {
            ClanRecord memberClan = FindByMemberLocked(entityId);
            if (string.Equals(memberClan?.Id, clanId, StringComparison.Ordinal))
            {
                return true;
            }
            ClanRecord applicationClan = FindByApplierLocked(entityId);
            if (string.Equals(applicationClan?.Id, clanId, StringComparison.Ordinal))
            {
                return true;
            }
            return _state.Invites.TryGetValue(entityId, out ClanInviteRecord invite) &&
                   string.Equals(invite.ClanId, clanId, StringComparison.Ordinal);
        }
    }
    public static bool CanManageEstate(string entityId, string clanId)
    {
        lock (Gate)
        {
            ClanRecord clan = FindByMemberLocked(entityId);
            if (clan == null ||
                !string.Equals(clan.Id, clanId, StringComparison.Ordinal) ||
                !clan.Members.TryGetValue(entityId, out ClanMemberRecord member))
            {
                return false;
            }
            return HasPermission(clan, entityId, Shared.Clan.Permissions.OccupyWarphole);
        }
    }
    public static bool TryRename(
        string actorEntityId,
        string requestedName,
        out ClanRecord clan,
        out string error)
    {
        clan = null;
        error = null;
        string name = requestedName?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length < 2 || name.Length > 24)
        {
            error = "O nome do clã deve possuir entre 2 e 24 caracteres.";
            return false;
        }
        lock (Gate)
        {
            clan = FindByMemberLocked(actorEntityId);
            if (clan == null ||
                !clan.Members.TryGetValue(actorEntityId, out ClanMemberRecord actor) ||
                actor.RoleId != ClanRolePolicy.Leader)
            {
                error = "Somente o Líder pode alterar o nome do clã.";
                return false;
            }
            string currentClanId = clan.Id;
            if (_state.Clans.Values.Any(other =>
                    other != null &&
                    !string.Equals(other.Id, currentClanId, StringComparison.Ordinal) &&
                    string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                error = "Já existe um clã com esse nome.";
                return false;
            }
            clan.Name = name;
            SaveLocked();
            return true;
        }
    }
    public static bool TrySetInfo(
        string actorEntityId,
        string notice,
        string intro,
        out ClanRecord clan,
        out string error)
    {
        clan = null;
        error = null;
        notice ??= string.Empty;
        intro ??= string.Empty;
        if (notice.Length > 250 || intro.Length > 55)
        {
            error = "O texto informado excede o limite permitido.";
            return false;
        }
        lock (Gate)
        {
            clan = FindByMemberLocked(actorEntityId);
            if (clan == null ||
                !clan.Members.TryGetValue(actorEntityId, out ClanMemberRecord actor) ||
                !HasPermission(clan, actorEntityId, Shared.Clan.Permissions.EditClanInfo))
            {
                error = "Somente Líder ou Oficial pode editar as informações do clã.";
                return false;
            }
            clan.Notice = notice;
            clan.Intro = intro;
            SaveLocked();
            return true;
        }
    }
    public static bool TrySetEmblem(
        string actorEntityId,
        byte[] emblem,
        out ClanRecord clan,
        out string error)
    {
        clan = null;
        error = null;
        if (emblem == null || emblem.Length == 0)
        {
            error = "O emblema recebido está vazio.";
            return false;
        }
        if (emblem.Length > 64 * 1024)
        {
            error = "O emblema excede o limite de 64 KiB.";
            return false;
        }
        lock (Gate)
        {
            clan = FindByMemberLocked(actorEntityId);
            if (clan == null ||
                !clan.Members.TryGetValue(actorEntityId, out ClanMemberRecord actor) ||
                !HasPermission(clan, actorEntityId, Shared.Clan.Permissions.EditClanInfo))
            {
                error = "Somente Líder ou Oficial pode alterar o emblema do clã.";
                return false;
            }
            clan.Emblem = emblem.ToArray();
            SaveLocked();
            return true;
        }
    }
    public static bool TrySetMemberRole(
        string actorEntityId,
        string targetEntityId,
        int roleId,
        out ClanRecord clan,
        out string error)
    {
        clan = null;
        error = null;
        if (roleId < 0)
        {
            error = "Cargo de clã inválido.";
            return false;
        }
        lock (Gate)
        {
            clan = FindByMemberLocked(actorEntityId);
            if (clan == null ||
                !clan.Members.TryGetValue(actorEntityId, out ClanMemberRecord actor) ||
                !HasPermission(clan, actorEntityId, Shared.Clan.Permissions.PromoteMember))
            {
                error = "Seu cargo não permite alterar cargos de outros integrantes.";
                return false;
            }
            if (!clan.Roles.ContainsKey(roleId)) { error = "Cargo não encontrado."; return false; }
            if (!clan.Members.TryGetValue(
                    targetEntityId ?? string.Empty,
                    out ClanMemberRecord target))
            {
                error = "Membro não encontrado.";
                return false;
            }
            if (string.Equals(actorEntityId, targetEntityId, StringComparison.Ordinal))
            {
                if (roleId == ClanRolePolicy.Leader)
                {
                    return true;
                }
                error = "Transfira a liderança para outro membro antes de alterar seu próprio cargo.";
                return false;
            }
            if (actor.RoleId != ClanRolePolicy.Leader && (roleId == ClanRolePolicy.Leader ||
                clan.Roles[actor.RoleId].Grade >= clan.Roles[target.RoleId].Grade ||
                clan.Roles[actor.RoleId].Grade >= clan.Roles[roleId].Grade))
            { error = "Você só pode alterar cargos abaixo da sua posição na hierarquia."; return false; }
            if (roleId == ClanRolePolicy.Leader)
            {
                actor.RoleId = ClanRolePolicy.Officer;
                target.RoleId = ClanRolePolicy.Leader;
            }
            else
            {
                target.RoleId = roleId;
            }
            SaveLocked();
            return true;
        }
    }
    public static bool TryInvite(
        string actorEntityId,
        string targetEntityId,
        out ClanRecord clan,
        out string error)
    {
        clan = null;
        error = null;
        if (string.IsNullOrWhiteSpace(targetEntityId) ||
            string.Equals(actorEntityId, targetEntityId, StringComparison.Ordinal))
        {
            error = "Jogador de destino inválido.";
            return false;
        }
        lock (Gate)
        {
            clan = FindByMemberLocked(actorEntityId);
            if (clan == null ||
                !clan.Members.TryGetValue(actorEntityId, out ClanMemberRecord actor) ||
                !HasPermission(clan, actorEntityId, Shared.Clan.Permissions.ApproveMember))
            {
                error = "Somente Líder ou Oficial pode convidar jogadores.";
                return false;
            }
            if (FindByMemberLocked(targetEntityId) != null)
            {
                error = "Esse jogador já pertence a um clã.";
                return false;
            }
            ClanRecord application = FindByApplierLocked(targetEntityId);
            if (application != null)
            {
                error = string.Equals(application.Id, clan.Id, StringComparison.Ordinal)
                    ? "Esse jogador já solicitou entrada no clã."
                    : "Esse jogador possui uma solicitação pendente em outro clã.";
                return false;
            }
            if (_state.Invites.TryGetValue(targetEntityId, out ClanInviteRecord existing) &&
                string.Equals(existing.ClanId, clan.Id, StringComparison.Ordinal))
            {
                return true;
            }
            _state.Invites[targetEntityId] = new ClanInviteRecord
            {
                TargetEntityId = targetEntityId,
                ClanId = clan.Id,
                InvitedBy = actorEntityId,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            SaveLocked();
            return true;
        }
    }
    public static bool SyncContext(PlayerContext context)
    {
        if (context == null) return false;
        lock (Gate)
        {
            ClanRecord clan = FindByMemberLocked(context.EntityId);
            ClanRecord applying = clan == null ? FindByApplierLocked(context.EntityId) : null;
            Messages.Member member = context.AppearPlayer.Member;
            member.EntityId = context.EntityId;
            if (clan != null && clan.Members.TryGetValue(context.EntityId, out ClanMemberRecord record))
            {
                member.ClanId = clan.Id;
                member.ClanName = clan.Name ?? string.Empty;
                member.RoleId = record.RoleId;
                member.ApplyingClanId = string.Empty;
            }
            else
            {
                member.ClanId = string.Empty;
                member.ClanName = string.Empty;
                member.RoleId = -1;
                member.ApplyingClanId = applying?.Id ?? string.Empty;
            }
            bool changed =
                !string.Equals(context.AppearPlayer.Member.ClanId, member.ClanId, StringComparison.Ordinal) ||
                !string.Equals(context.AppearPlayer.Member.ClanName, member.ClanName, StringComparison.Ordinal) ||
                context.AppearPlayer.Member.RoleId != member.RoleId ||
                !string.Equals(
                    context.AppearPlayer.Member.ApplyingClanId,
                    member.ApplyingClanId,
                    StringComparison.Ordinal);
            context.AppearPlayer.Member = member;
            if (changed && !string.IsNullOrEmpty(context.Path))
            {
                context.Save();
            }
            return changed;
        }
    }
    public static JObject ToGatewayJson(ClanRecord clan, bool detail)
    {
        if (clan == null) return new JObject();
        var roles = new JObject();
        foreach (var role in clan.Roles.Values)
            roles[role.Id.ToString()] = new JObject {
                ["id"] = role.Id, ["name"] = role.Name, ["grade"] = role.Grade,
                ["permissions"] = (int)role.Permissions, ["user_type"] = (int)role.UserType
            };
        return new JObject {
            ["id"] = clan.Id, ["name"] = clan.Name, ["fund"] = clan.Fund,
            ["level"] = clan.Level, ["exp"] = clan.Exp, ["notice"] = clan.Notice, ["intro"] = clan.Intro,
            ["capacity"] = ClanRules.Reward(clan.Level, "capacity"), ["member_count"] = clan.Members.Count,
            ["mainland"] = "Ilha de Clã", ["renameable_until"] = 0, ["emblem_changeable_until"] = 0,
            ["members"] = detail ? new JArray(clan.Members.Values.Select(m => new JArray(m.EntityId, m.RoleId))) : null,
            ["appliers"] = detail ? new JArray(clan.Appliers.Keys) : null,
            ["role_infos"] = roles
        };
    }

}
public partial class Player
{
    internal static event Action<string> ClanChanged;
    private Dictionary<ChannelType, bool> _clanChannelNotifications;
    private static readonly object ClanOnlineGate = new();
    private static readonly Dictionary<string, Player> ClanOnlinePlayers =
        new(StringComparer.Ordinal);
    private void RegisterClanOnlinePresence()
    {
        lock (ClanOnlineGate)
        {
            ClanOnlinePlayers[EntityId] = this;
        }
        _connection.ConnetionClosed += delegate
        {
            lock (ClanOnlineGate)
            {
                if (ClanOnlinePlayers.TryGetValue(EntityId, out Player current) &&
                    ReferenceEquals(current, this))
                {
                    ClanOnlinePlayers.Remove(EntityId);
                }
            }
        };
    }
    private static Player FindClanOnlinePlayer(string entityId)
    {
        if (string.IsNullOrEmpty(entityId)) return null;
        lock (ClanOnlineGate)
        {
            return ClanOnlinePlayers.TryGetValue(entityId, out Player player)
                ? player
                : null;
        }
    }
    private static void PushClanStateToOnline(string clanId)
    {
        Player[] snapshot;
        lock (ClanOnlineGate)
        {
            snapshot = ClanOnlinePlayers.Values.ToArray();
        }
        foreach (Player player in snapshot)
        {
            string contextClanId = player._context.AppearPlayer.Member.ClanId;
            bool relevant =
                string.Equals(contextClanId, clanId, StringComparison.Ordinal) ||
                string.Equals(player._context.AppearPlayer.Member.ApplyingClanId, clanId, StringComparison.Ordinal) ||
                ClanStore.IsRelatedToClan(player.EntityId, clanId);
            if (!relevant)
            {
                continue;
            }
            if (ClanStore.SyncContext(player._context))
            {
                player.OnContextChanged();
                player._world.BroadCast(player._context.AppearPlayer);
            }
            player.Send(default(ClanInfoUpdated));
            player.SyncClanBenefits(true);
            if (player._world.Registry?.TryGetSettlementRegion(player.LogicalRegionId(), out var island) == true
                && island.Kind == SettlementRegionKind.Clan && ClanStore.ClanIdOf(player.EntityId) != island.OwnerId)
            {
                player._context.RegionId = WorldRegistry.DefaultSharedTamedRegionId;
                player._context.AppearPlayer.Move.Movements = null;
                player.OnContextChanged(); player.Send(new Emigrated { Type = Shared.Teleport.TeleportType.Unknown });
            }
            ClanChanged?.Invoke(player.EntityId);
        }
    }
    private static void NotifyClanInviteTarget(Player target, ClanRecord clan)
    {
        if (target == null || clan == null) return;
        target.Send(new Info
        {
            Text = $"Você recebeu um convite para o clã '{clan.Name}'. " +
                   "Abra a lista de clãs e solicite a entrada para aceitar."
        });
    }
    private void RefreshOwnClanState()
    {
        if (!ClanStore.SyncContext(_context))
        {
            return;
        }
        OnContextChanged();
        _world.BroadCast(_context.AppearPlayer);
    }
    private void RegisterClanHandlers()
    {
        ClanStore.EnsureLoaded(_context.Path);
        ClanStore.SyncContext(_context);
        RegisterClanOnlinePresence();
        _economy?.RecoverClanWorld(_world, LogicalRegionId());
        InitializeClanArtifactAccess();
        _connection.Recv(delegate(GetClanEstateLicense msg, PacketHeader header)
        {
            var license = BuildEstateLicenses().ClanEstate;
            if (license.HasValue) Send(license.Value, header.Seq);
            else Send(new Abort { Text = "O clã ainda não possui um enclave." }, header.Seq);
        });
        if (!_connection.HasHandler(GetClanCreationCosts.TypeCode))
        {
            _connection.Recv(delegate(GetClanCreationCosts msg, PacketHeader header)
            {
                Send(new Costs { _Costs = new() { [Shared.Economy.Currency.TStone] = ClanRules.CreationCost } }, header.Seq);
            });
        }
        _connection.Recv(delegate(MakeClan msg, PacketHeader header)
        {
            ClanStore.EnsureLoaded(_context.Path);
            if (!EconomyAvailable(header.Seq)) return;
            if (!_economy.ClanTransaction(_context, "create", msg.ClanName, 0, null, out ClanRecord clan, out string error))
            {
                Send(new Abort { Text = error }, header.Seq);
                return;
            }
            PushClanStateToOnline(clan.Id);
            SendWalletNow();
            _world.Registry?.RegisterClanRegion(clan.Id);
            Console.WriteLine(
                $"[clã] {Short(EntityId)} criou '{clan.Name}' ({clan.Id})");
            Send(default(OK), header.Seq);
            Send(default(ClanInfoUpdated));
        });
        _connection.Recv(delegate(JoinClan msg, PacketHeader header)
        {
            ClanStore.EnsureLoaded(_context.Path);
            if (!ClanStore.TryApply(_context, msg.ClanId, out string error))
            {
                Send(new Abort { Text = error }, header.Seq);
                return;
            }
            PushClanStateToOnline(msg.ClanId);
            Console.WriteLine(
                $"[clã] {Short(EntityId)} solicitou/aceitou entrada em {msg.ClanId}");
            Send(default(OK), header.Seq);
            Send(default(ClanInfoUpdated));
        });
        _connection.Recv(delegate(LeaveClan msg, PacketHeader header)
        {
            ClanStore.EnsureLoaded(_context.Path);
            string previousClanId = CurrentClanId();
            if (!ClanStore.TryLeave(EntityId, out string error))
            {
                Send(new Abort { Text = error }, header.Seq);
                return;
            }
            PushClanStateToOnline(previousClanId);
            RefreshOwnClanState();
            Console.WriteLine($"[clã] {Short(EntityId)} saiu do clã");
            Send(default(OK), header.Seq);
        });
        _connection.Recv(delegate(RenameClan msg, PacketHeader header)
        {
            ClanStore.EnsureLoaded(_context.Path);
            if (!ClanStore.TryRename(
                    EntityId,
                    msg.ClanName,
                    out ClanRecord clan,
                    out string error))
            {
                Send(new Abort { Text = error }, header.Seq);
                return;
            }
            PushClanStateToOnline(clan.Id);
            Console.WriteLine(
                $"[clã] {Short(EntityId)} renomeou o clã para '{clan.Name}'");
            Send(default(OK), header.Seq);
        });
        _connection.Recv(delegate(SetClanInfo msg, PacketHeader header)
        {
            ClanStore.EnsureLoaded(_context.Path);
            if (!ClanStore.TrySetInfo(
                    EntityId,
                    msg.Notice,
                    msg.Intro,
                    out ClanRecord clan,
                    out string error))
            {
                Send(new Abort { Text = error }, header.Seq);
                return;
            }
            PushClanStateToOnline(clan.Id);
            Console.WriteLine(
                $"[clã] {Short(EntityId)} atualizou notice/intro de {clan.Id}");
            Send(default(OK), header.Seq);
        });
        _connection.Recv(delegate(SetClanEmblem msg, PacketHeader header)
        {
            ClanStore.EnsureLoaded(_context.Path);
            if (!ClanStore.TrySetEmblem(
                    EntityId,
                    msg.Emblem,
                    out ClanRecord clan,
                    out string error))
            {
                Send(new Abort { Text = error }, header.Seq);
                return;
            }
            PushClanStateToOnline(clan.Id);
            Console.WriteLine(
                $"[clã] {Short(EntityId)} atualizou o emblema de {clan.Id} " +
                $"({msg.Emblem.Length} bytes)");
            Send(default(OK), header.Seq);
        });
        _connection.Recv(delegate(KickClanMember msg, PacketHeader header)
        {
            ClanStore.EnsureLoaded(_context.Path);
            string clanId = CurrentClanId();
            if (!ClanStore.TryKick(EntityId, msg.EntityId, out string error))
            {
                Send(new Abort { Text = error }, header.Seq);
                return;
            }
            PushClanStateToOnline(clanId);
            Console.WriteLine(
                $"[clã] {Short(EntityId)} removeu {Short(msg.EntityId)} do clã");
            Send(default(OK), header.Seq);
        });
        _connection.Recv(delegate(SetClanMemberRole msg, PacketHeader header)
        {
            ClanStore.EnsureLoaded(_context.Path);
            if (!ClanStore.TrySetMemberRole(
                    EntityId,
                    msg.TargetId,
                    msg.RoleId,
                    out ClanRecord clan,
                    out string error))
            {
                Send(new Abort { Text = error }, header.Seq);
                return;
            }
            PushClanStateToOnline(clan.Id);
            Console.WriteLine(
                $"[clã] {Short(EntityId)} definiu cargo {msg.RoleId} " +
                $"para {Short(msg.TargetId)}");
            Send(default(OK), header.Seq);
        });
        _connection.Recv(delegate(SetMemberRoleGrades msg, PacketHeader header)
        {
            if (!ClanStore.SetRoleGrades(EntityId, msg.RoleOrders, out string error)) { Send(new Abort { Text = error }, header.Seq); return; }
            PushClanStateToOnline(CurrentClanId()); Send(default(OK), header.Seq);
        });
        _connection.Recv(delegate(SetMemberRoleInfo msg, PacketHeader header)
        {
            if (!ClanStore.SetRoleInfo(EntityId, msg.RoleId, msg.Info, out string error)) { Send(new Abort { Text = error }, header.Seq); return; }
            PushClanStateToOnline(CurrentClanId()); Send(default(OK), header.Seq);
        });
        _connection.Recv(delegate(RemoveMemberRole msg, PacketHeader header)
        {
            if (!ClanStore.RemoveRole(EntityId, msg.RoleId, msg.MoveToRoleId, out string error)) { Send(new Abort { Text = error }, header.Seq); return; }
            PushClanStateToOnline(CurrentClanId()); Send(default(OK), header.Seq);
        });
        _connection.Recv(delegate(InviteToClan msg, PacketHeader header)
        {
            ClanStore.EnsureLoaded(_context.Path);
            Player target = FindClanOnlinePlayer(msg.EntityId);
            if (target == null)
            {
                Send(new Abort
                {
                    Text = "O jogador precisa estar online para receber o convite."
                }, header.Seq);
                return;
            }
            if (!ClanStore.TryInvite(
                    EntityId,
                    msg.EntityId,
                    out ClanRecord clan,
                    out string error))
            {
                Send(new Abort { Text = error }, header.Seq);
                return;
            }
            NotifyClanInviteTarget(target, clan);
            Console.WriteLine(
                $"[clã] {Short(EntityId)} convidou {Short(msg.EntityId)} para {clan.Id}");
            Send(default(OK), header.Seq);
        });
        _connection.Recv(delegate(ApproveClanApplier msg, PacketHeader header)
        {
            ClanStore.EnsureLoaded(_context.Path);
            string clanId = CurrentClanId();
            if (!ClanStore.TryApprove(EntityId, msg.EntityId, out string error))
            {
                Send(new Abort { Text = error }, header.Seq);
                return;
            }
            PushClanStateToOnline(clanId);
            Console.WriteLine(
                $"[clã] {Short(EntityId)} aprovou {Short(msg.EntityId)}");
            Send(default(OK), header.Seq);
        });
        _connection.Recv(delegate(DropClanApplier msg, PacketHeader header)
        {
            ClanStore.EnsureLoaded(_context.Path);
            string clanId = CurrentClanId();
            if (!ClanStore.TryDropApplication(EntityId, msg.EntityId, out string error))
            {
                Send(new Abort { Text = error }, header.Seq);
                return;
            }
            PushClanStateToOnline(clanId);
            Send(default(OK), header.Seq);
        });
        _connection.Recv(delegate(CancelClanJoinRequest msg, PacketHeader header)
        {
            ClanStore.EnsureLoaded(_context.Path);
            if (!ClanStore.TryCancelApplication(EntityId, msg.ClanId, out string error))
            {
                Send(new Abort { Text = error }, header.Seq);
                return;
            }
            PushClanStateToOnline(msg.ClanId);
            RefreshOwnClanState();
            Send(default(OK), header.Seq);
        });
        _connection.Recv(delegate(GetClanFund msg, PacketHeader header)
        {
            SendClanFund(header.Seq);
        });
        _connection.Recv(delegate(DonateToClanFund msg, PacketHeader header)
        {
            if (!EconomyAvailable(header.Seq)) return;
            if (msg.Costs == null || msg.Costs.Count != 1 || !msg.Costs.TryGetValue(Shared.Economy.Currency.TStone, out long amount) || amount <= 0)
            { Send(new Abort { Text = "Informe uma doação positiva em T-Stones." }, header.Seq); return; }
            if (!_economy.ClanTransaction(_context, "donate", null, amount, null, out var clan, out string error))
            { Send(new Abort { Text = error }, header.Seq); return; }
            SendWalletNow(); SendClanFund(header.Seq); PushClanStateToOnline(clan.Id);
        });
        _connection.Recv(delegate(RequestClanRewards msg, PacketHeader header)
        {
            SendRecipes(0); SyncClanBenefits();
        });
        _connection.Recv(delegate(RequestClanStatusEffects msg, PacketHeader header)
        {
            SyncClanBenefits(); SendStatusEffects();
        });
        _connection.Recv(delegate(ToggleClanNotification msg, PacketHeader header)
        {
            _clanChannelNotifications = msg.ChannelNotificationsEnabled;
            var saved = new Dictionary<int, bool>();
            if (msg.ChannelNotificationsEnabled != null)
            {
                foreach (var pair in msg.ChannelNotificationsEnabled)
                {
                    saved[(int)pair.Key] = pair.Value;
                }
            }
            _context.ClanChannelNotifications = saved;
            OnContextChanged();
        });
        _connection.Recv(delegate(GetClanNotificationEnabled msg, PacketHeader header)
        {
            if (_clanChannelNotifications == null && _context.ClanChannelNotifications is { Count: > 0 })
            {
                var restored = new Dictionary<ChannelType, bool>();
                foreach (var pair in _context.ClanChannelNotifications)
                {
                    restored[(ChannelType)pair.Key] = pair.Value;
                }
                _clanChannelNotifications = restored;
            }
            Send(new ToggleClanNotification
            {
                ChannelNotificationsEnabled = _clanChannelNotifications ?? new Dictionary<ChannelType, bool>()
            }, header.Seq);
        });
        _connection.Recv(delegate(ResubscribeClanChannel msg, PacketHeader header)
        {
            RefreshOwnClanState(); Send(default(ClanInfoUpdated));
        });
    }
}
