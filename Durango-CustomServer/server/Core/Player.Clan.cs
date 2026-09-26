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

// ═══════════════════════════════════════════════════════════════════════════════════
//  แคลน (Clan / "부족" = เผ่า) — คำสั่งจัดการเผ่าทั้งชุดที่ฝั่งเกมยิงเข้ามา
//
//  ═══ ทำไมเซิร์ฟนี้ยังมี "เผ่าจริง" ไม่ได้ ═══
//  ข้อมูลเผ่าของ NEXON **ไม่ได้เดินทางมาทางสาย TCP ของเกมเลย** ตัวเผ่าถูกดึงผ่าน HTTP gateway:
//    client/ClanSystem.cs:116-140  RequestClanInfo → GET {GatewayUrl}/clans/{id}?detail
//    client/ClanSystem.cs:155-185  ค้นหาเผ่า        → GET {GatewayUrl}/clans?keyword=...
//    client/ClanSystem.cs:217-239  RequestPlayerClan → ถาม /clans ด้วย ClanId ของตัวละคร
//  สาย TCP มีแต่ "คำสั่ง" (สร้าง/เข้า/ออก/เตะ) แล้วฝั่งเกมจะไปโหลดผลลัพธ์จาก gateway ต่อเสมอ
//  ⇒ server/Core/Gateway.cs ของเราไม่มีเส้น /clans และ PlayerContext ไม่มีที่เก็บเผ่า
//    ⇒ ต่อให้ตอบ OK ให้ MakeClan/JoinClan ฝั่งเกมก็จะได้เผ่ากลับมาเป็น null อยู่ดี
//      (client/ClanSystem.cs:263-293 UpdateLocalplayerMember: หาตัวเองในเผ่าไม่เจอ = ล้าง Clan ทิ้ง)
//    ⇒ ตอบ OK = โกหกผู้เล่น (ขึ้นป้าย "สร้างเผ่าสำเร็จ" ที่ ClanSystem.cs:543 แล้วไม่มีเผ่าจริง)
//  ⇒ ทุกคำสั่งที่เป็น "การกระทำ" ในไฟล์นี้จึงตอบ Abort พร้อมข้อความ ไม่ใช่เงียบและไม่ใช่ OK
//
//  ═══ ฝั่งเกมแปลผลลัพธ์ยังไง (ทั้งสองทางมอง Abort = ล้มเหลว ตรงกัน) ═══
//    - client/Durango.Network/Packet.cs:90-101  IsSuccess → 1022(Error)/1024(Abort)/3650(TimedOut) = ล้มเหลว
//    - client/ClanSystem.cs:479-482             HandleResult → สำเร็จ = TypeCode 1231 (OK) เท่านั้น
//  ⚠️ Abort ต้องมี Text เสมอ — client/GameManager.cs:309-312 DefaultAbortHandler เรียก
//     LimitText(msg.Text) ทันที ถ้า Text เป็น null ฝั่งเกมแครช (nil → UnpackGettext คืน null)
//     ตรวจแล้วว่าส่งเป็นสตริงเปล่า ๆ ได้ ไม่ต้องเป็น msgid: client/LocalizeSystem.cs:583-586
//     รับ UnderlyingType == string ตรง ๆ
//
//  ═══ 3 ตัวท้าย (แจ้งเตือน/สมัครช่องแชทเผ่า) มาทางสายอื่น ═══
//  ToggleClanNotification · GetClanNotificationEnabled · ResubscribeClanChannel ฝั่งเกมยิงผ่าน
//  **Connections.Radiotower** ไม่ใช่ Frontend (client/SocialSystem.cs:395, 1228, 1341)
//  และ gateway ของเรา (server/Core/Gateway.cs:261-265) แจกแต่ "frontend_addresses"
//  ไม่มี "radiotower_addresses" ที่ client/Durango.UI/TitleMenuGroup.cs:918-922 ใช้ตั้ง endpoint
//  ⇒ สาย radiotower ไม่เคยต่อติด ตอนนี้ยังมาไม่ถึง handler พวกนี้
//    แต่ลงทะเบียนไว้ให้ถูกชนิดเผื่อวันหลังชี้ radiotower มาที่พอร์ตเกมเดียวกัน (เรามี Connection
//    เดียวต่อผู้เล่น) — ไม่มีผลเสียถ้ายังไม่ต่อ และกัน log "ไม่มี handler" ล่วงหน้า
//
//  ℹ️ ข้อมูลจริงที่มี: server/data/assets/clan.json มีแค่ level_thresholds + level_rewards
//     (ของฝั่ง client ใช้คำนวณแถบ exp เผ่า — client/ClanSystem.cs:363-383) **ไม่มี** ค่าสร้างเผ่า
//     ไม่มีคลังเงินเผ่า ไม่มีรายชื่อสมาชิก ⇒ ไม่มีอะไรให้เอามาตอบเป็นข้อมูลจริงได้เลย
//  ℹ️ ต้นฉบับเซิร์ฟจำลองของ NEXON (nexonSRC/Durango.Offline/Player.cs) ไม่มีคำว่า Clan สักตัว
//     — ระบบเผ่าเป็นของโหมด Online ล้วน ๆ จึงไม่มีสไตล์ต้นฉบับให้ลอก
// ═══════════════════════════════════════════════════════════════════════════════════

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
}

/// <summary>
/// Cargos fixos do Clan MVP.
///
/// Líder:
/// - administração completa;
/// - transfere liderança;
/// - promove/rebaixa oficiais.
///
/// Oficial:
/// - aprova/recusa solicitações;
/// - convida e remove membros comuns;
/// - administra ClanEstate;
/// - altera informações e emblema.
///
/// Membro:
/// - acessa a Ilha de Clã;
/// - constrói e utiliza estruturas dentro do ClanEstate.
/// </summary>
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

/// <summary>
/// Persistência mínima de clãs do Durango Brasil.
///
/// O arquivo clans.json fica ao lado dos saves .player do cluster, portanto cada
/// cluster possui seus próprios clãs. O ClanStore é a fonte autoritativa:
/// AppearPlayer.Member é apenas a projeção necessária para o protocolo do cliente.
/// </summary>
internal static class ClanStore
{
    private static readonly object Gate = new();
    private static ClanStoreState _state = new();
    private static string _path;
    private static bool _loaded;

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
            clan.Notice ??= string.Empty;
            clan.Intro ??= string.Empty;
            clan.Emblem ??= Array.Empty<byte>();
            if (clan.Level <= 0) clan.Level = 1;
        }
    }

    private static void SaveLocked()
    {
        if (string.IsNullOrWhiteSpace(_path)) return;

        byte[] data = Json.WriteToBytes(_state, indented: false);
        if (data != null)
        {
            SafeSave.QueueAtomic(_path, data, "clan-store");
        }
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
            return _state.Clans.TryGetValue(clanId, out ClanRecord clan) ? clan : null;
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

            ClanRecord existingApplication = FindByApplierLocked(context.EntityId);

            bool preApproved =
                _state.Invites.TryGetValue(context.EntityId, out ClanInviteRecord invite) &&
                string.Equals(invite.ClanId, clan.Id, StringComparison.Ordinal);

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
                !ClanRolePolicy.CanManageMembers(actor.RoleId))
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
                !ClanRolePolicy.CanManageMembers(actor.RoleId))
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
                !ClanRolePolicy.CanManageMembers(actor.RoleId))
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

            if (actor.RoleId == ClanRolePolicy.Officer &&
                target.RoleId != ClanRolePolicy.Member)
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

            return ClanRolePolicy.CanManageSettlement(member.RoleId);
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

        if (notice.Length > 512 || intro.Length > 1024)
        {
            error = "O texto informado excede o limite permitido.";
            return false;
        }

        lock (Gate)
        {
            clan = FindByMemberLocked(actorEntityId);
            if (clan == null ||
                !clan.Members.TryGetValue(actorEntityId, out ClanMemberRecord actor) ||
                !ClanRolePolicy.CanEditClanInfo(actor.RoleId))
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
                !ClanRolePolicy.CanEditClanInfo(actor.RoleId))
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

        if (!ClanRolePolicy.IsValid(roleId))
        {
            error = "Cargo de clã inválido.";
            return false;
        }

        lock (Gate)
        {
            clan = FindByMemberLocked(actorEntityId);
            if (clan == null ||
                !clan.Members.TryGetValue(actorEntityId, out ClanMemberRecord actor) ||
                actor.RoleId != ClanRolePolicy.Leader)
            {
                error = "Somente o Líder pode alterar cargos.";
                return false;
            }

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

            if (roleId == ClanRolePolicy.Leader)
            {
                // Transferência atômica: nunca há dois líderes nem um intervalo sem líder.
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
                !ClanRolePolicy.CanManageMembers(actor.RoleId))
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

        var members = new JArray();
        foreach (ClanMemberRecord member in clan.Members.Values.OrderBy(member => member.RoleId))
        {
            members.Add(new JObject
            {
                ["id"] = member.EntityId ?? string.Empty,
                ["entity_id"] = member.EntityId ?? string.Empty,
                ["name"] = member.Name ?? string.Empty,
                ["player_name"] = member.Name ?? string.Empty,
                ["role_id"] = member.RoleId,
                ["joined_at"] = member.JoinedAt,
                ["level"] = 1
            });
        }

        var appliers = new JArray();
        foreach (ClanMemberRecord member in clan.Appliers.Values)
        {
            appliers.Add(new JObject
            {
                ["id"] = member.EntityId ?? string.Empty,
                ["entity_id"] = member.EntityId ?? string.Empty,
                ["name"] = member.Name ?? string.Empty,
                ["player_name"] = member.Name ?? string.Empty,
                ["role_id"] = -1,
                ["level"] = 1
            });
        }

        var roles = new JArray
        {
            new JObject
            {
                ["id"] = ClanRolePolicy.Leader,
                ["name"] = "Líder",
                ["grade"] = 0
            },
            new JObject
            {
                ["id"] = ClanRolePolicy.Officer,
                ["name"] = "Oficial",
                ["grade"] = 1
            },
            new JObject
            {
                ["id"] = ClanRolePolicy.Member,
                ["name"] = "Membro",
                ["grade"] = 2
            }
        };

        var result = new JObject
        {
            ["id"] = clan.Id ?? string.Empty,
            ["clan_id"] = clan.Id ?? string.Empty,
            ["name"] = clan.Name ?? string.Empty,
            ["clan_name"] = clan.Name ?? string.Empty,
            ["level"] = Math.Max(1, clan.Level),
            ["exp"] = clan.Exp,
            ["created_at"] = clan.CreatedAt,
            ["notice"] = clan.Notice ?? string.Empty,
            ["intro"] = clan.Intro ?? string.Empty,
            ["emblem"] = Convert.ToBase64String(clan.Emblem ?? Array.Empty<byte>()),
            ["member_count"] = clan.Members.Count,
            ["max_member_count"] = 60,
            ["members"] = members,
            ["member_roles"] = roles,
            ["roles"] = roles.DeepClone(),
            ["appliers"] = appliers,
            ["applicants"] = appliers.DeepClone(),
            ["allies"] = new JArray()
        };

        return result;
    }
}

public partial class Player
{
    // สวิตช์แจ้งเตือนแชทช่องเผ่า (Clan / ClanWar) ที่ผู้เล่นกดเปิด-ปิดเอง
    // เก็บไว้ "ในเซสชันนี้เท่านั้น" — ยังไม่บันทึกลง PlayerContext เพราะงานนี้ห้ามแก้ไฟล์อื่น
    // ⇒ ออกจากเกมแล้วค่าจะกลับเป็นว่าง (= ไม่ได้เปิดช่องไหนเลย) **การตีความของเรา**
    // ค่าเริ่มต้น null แปลว่า "ผู้เล่นยังไม่เคยตั้ง" ตอบกลับเป็นแมปว่างซึ่งฝั่งเกมอ่านว่าปิดหมด
    // (client/SocialSystem.cs:1240-1244 IsClanPushEnabled → dict.Get(key, defaultValue: false))
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
                ClanStore.IsRelatedToClan(player.EntityId, clanId);

            if (!relevant)
            {
                continue;
            }

            if (!ClanStore.SyncContext(player._context))
            {
                continue;
            }

            player.OnContextChanged();

            // Cada jogador pode estar em uma ilha diferente. O AppearPlayer atualizado
            // é distribuído no mundo onde aquele personagem está conectado.
            player._world.BroadCast(player._context.AppearPlayer);
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
        RegisterClanOnlinePresence();

        // Alpha/Beta: criação de clã sem custo até a economia de clã ser balanceada.
        // Se outro sistema já registrou esse handler, não o substituímos.
        if (!_connection.HasHandler(GetClanCreationCosts.TypeCode))
        {
            _connection.Recv(delegate(GetClanCreationCosts msg, PacketHeader header)
            {
                Send(new Costs(), header.Seq);
            });
        }
        // ── กลุ่มที่ 1: คำสั่งจัดการเผ่า (ทำจริงไม่ได้ → Abort พร้อมข้อความ) ────────────

        // MakeClan (3651) — ปุ่ม "สร้างเผ่า" · client/ClanSystem.cs:535-548
        // ยิงพร้อม ClanName + Currency แล้ว .On<OK> จะเด้งป้าย "สร้างเผ่า <ชื่อ> แล้ว"
        // ต่อด้วย .All → HandleResult(ClanSystem.cs:479-482) ปิดหน้าต่างถ้าสำเร็จ
        // ⇒ ต้องไม่ตอบ OK เด็ดขาด ไม่งั้นป้ายขึ้นแต่เผ่าไม่มีอยู่จริง
        _connection.Recv(delegate(MakeClan msg, PacketHeader header)
        {
            ClanStore.EnsureLoaded(_context.Path);

            if (!ClanStore.TryCreate(_context, msg.ClanName, out ClanRecord clan, out string error))
            {
                Send(new Abort { Text = error }, header.Seq);
                return;
            }

            PushClanStateToOnline(clan.Id);
            _world.Registry?.RegisterClanRegion(clan.Id);

            Console.WriteLine(
                $"[clã] {Short(EntityId)} criou '{clan.Name}' ({clan.Id})");

            Send(default(OK), header.Seq);
            Send(default(ClanInfoUpdated));
        });

        // JoinClan (3655) — ขอเข้าเผ่า (จากหน้าค้นหาเผ่า) · client/ClanSystem.cs:484-496
        // .On<OK> เด้งป้าย "ยื่นใบสมัครแล้ว" · .All → HandleResult
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

        // LeaveClan (3652) — ออกจากเผ่า · client/ClanSystem.cs:558-576
        // ฝั่งเกมกันไว้ชั้นหนึ่งแล้ว (PlayerClan == null → ไม่ยิง) ⇒ ที่มาถึงตรงนี้แปลว่า
        // ฝั่งเกมคิดว่ามีเผ่าอยู่ แต่ฝั่งเราไม่มีที่เก็บ ⇒ บอกตรง ๆ ว่าทำให้ไม่ได้
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

        // RenameClan (36510) — เปลี่ยนชื่อเผ่า · client/ClanSystem.cs:453-465
        // .All → ถ้าสำเร็จเรียก RefreshPlayerClan() ไปโหลดเผ่าใหม่จาก gateway
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

        // SetClanInfo (3699) — ป้ายประกาศ (Notice) + คำแนะนำเผ่า (Intro)
        // client/ClanSystem.cs:433-451 SetClanComment ← เรียกจากปุ่มส่งของกระดานสองอัน
        // (client/Durango.UI/ClanInfoPage.cs:155-168 Intro · :170-181 Notice — ผูกปุ่มไว้ที่ :91,:93)
        // .All → onResult(false) จะคาโหมดแก้ไขไว้ให้ผู้เล่นเห็นว่าไม่ได้บันทึก — ถูกต้องแล้ว
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

            Console.WriteLine(
                $"[clã] {Short(EntityId)} atualizou notice/intro de {clan.Id}");

            Send(default(OK), header.Seq);
        });

        // SetClanEmblem (3695) — ตราเผ่า (ส่งมาเป็น byte[]) · client/ClanSystem.cs:520-533
        // รอ .On<OK> อย่างเดียว (ไม่มี .All) ⇒ ตอบ Abort = ไม่มีอะไรถูกเรียกต่อ
        // ผู้เล่นเห็นข้อความจาก DefaultAbortHandler แทน
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

            Console.WriteLine(
                $"[clã] {Short(EntityId)} atualizou o emblema de {clan.Id} " +
                $"({msg.Emblem.Length} bytes)");

            Send(default(OK), header.Seq);
        });

        // KickClanMember (3661) — เตะสมาชิกออกจากเผ่า · client/ClanSystem.cs:578-590
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

        // SetClanMemberRole (3662) — ย้ายตำแหน่งสมาชิก · client/ClanSystem.cs:678-692
        // (TargetId + RoleId) รอ .On<OK> แล้วค่อย RefreshPlayerClan
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

        // ── กลุ่มที่ 1ข: จัดการ "ตำแหน่ง" (role) ในเผ่า — หน้าต่างตั้งค่าตำแหน่ง ────────────
        // ทั้งสามตัวนี้ฝั่งเกมยิงจาก client/Durango.UI/ClanRoleManageGroup.cs (ผูก event ที่ :32-34)
        // และ **รอคำตอบทุกตัว** ผ่าน .All(...) ⇒ ไม่ลงทะเบียน = เซิร์ฟเงียบสนิท = callback
        // ไม่ถูกเรียกเลย ซึ่งอันตรายกว่าตอบล้มเหลว (ดูหมายเหตุ SetMemberRoleInfo ข้างล่าง)

        // SetMemberRoleGrades (792252) — ลากสลับลำดับชั้นของตำแหน่ง
        // ฟิลด์จริง (server/GameCode/Messages/SetMemberRoleGrades.cs:9): RoleOrder[] RoleOrders
        //   (RoleOrder = RoleId + Grade — server/GameCode/Messages/RoleOrder.cs:7-9)
        // จุดยิง: client/Durango.UI/ClanRoleManageGroup.cs:87-90 OnUpdateRoleOrder
        //         → client/ClanSystem.cs:603-633 .All → onResult(IsSuccess) + RefreshPlayerClan
        //         (ที่นี่ส่ง onResult = null มา ⇒ ล้มเหลวแล้วเงียบ ไม่มี UI ค้าง)
        _connection.Recv(delegate(SetMemberRoleGrades msg, PacketHeader header)
        {
            Send(new Abort
            {
                Text = "Nesta fase os cargos são fixos: Líder, Oficial e Membro."
            }, header.Seq);
        });

        // SetMemberRoleInfo (3681) — แก้ชื่อ/สิทธิ์ของตำแหน่ง
        // ฟิลด์จริง (server/GameCode/Messages/SetMemberRoleInfo.cs:9-11): int RoleId · MemberRole Info
        // จุดยิง: client/Durango.UI/ClanRoleManageGroup.cs:92-107 OnChangeRole
        //         → client/ClanSystem.cs:636-654 .All → onResult(IsSuccess) + RefreshPlayerClan
        // ⚠️ ตัวนี้ "ห้ามเงียบ" เด็ดขาด: ClanRoleManageGroup.cs:98 ตั้ง _isModifying = true ก่อนยิง
        //    แล้วปลดล็อกกลับเป็น false ได้ที่เดียวคือ :101 ซึ่งอยู่ใน callback ของคำตอบ
        //    ⇒ ไม่ตอบ = _isModifying ค้าง true ตลอดกาล → :94 ดีดออกทุกครั้ง → ผู้เล่นแก้ตำแหน่ง
        //      ไม่ได้อีกเลยจนกว่าจะปิดเกม (ตอบ Abort = IsSuccess false แต่ callback ยังทำงาน ปลดล็อกได้)
        _connection.Recv(delegate(SetMemberRoleInfo msg, PacketHeader header)
        {
            Send(new Abort
            {
                Text = "Nesta fase os cargos são fixos: Líder, Oficial e Membro."
            }, header.Seq);
        });

        // RemoveMemberRole (792253) — ลบตำแหน่งทิ้ง แล้วย้ายคนในตำแหน่งนั้นไปตำแหน่งอื่น
        // ฟิลด์จริง (server/GameCode/Messages/RemoveMemberRole.cs:9-11): int RoleId · int MoveToRoleId
        // จุดยิง: client/Durango.UI/ClanRoleManageGroup.cs:109+ OnRemoveRole (:158 หลังเลือกปลายทาง)
        //         → client/ClanSystem.cs:657-676 .All → onResult(IsSuccess) + RefreshPlayerClan
        _connection.Recv(delegate(RemoveMemberRole msg, PacketHeader header)
        {
            Send(new Abort
            {
                Text = "Os cargos básicos do clã não podem ser removidos."
            }, header.Seq);
        });

        // ℹ️ GetClanEstateLicense (3697) **ไม่ต้องมี handler** — ตรวจทั้ง client/ แล้วไม่มีจุดไหน
        //    ส่งมันออกมาเลย (เจอแต่ตัวนิยาม client/Messages/GetClanEstateLicense.cs) = message ตายแล้ว
        //    ส่วนใบอนุญาตที่ดินของเผ่าเป็นงานของ server/Core/Player.Estate.cs ไม่ใช่ไฟล์นี้

        // InviteToClan (3660) — ชวนคนที่ยืนอยู่ตรงหน้าเข้าเผ่า
        // ยิงจากเมนูปฏิสัมพันธ์ Interaction.InviteToClan (client/ClanSystem.cs:59-66 → :592-601)
        // รอ .On<OK> เพื่อเด้งป้าย "ชวน <ชื่อ> เข้าเผ่าแล้ว"
        _connection.Recv(delegate(InviteToClan msg, PacketHeader header)
        {
            ClanStore.EnsureLoaded(_context.Path);

            Player target = FindClanOnlinePlayer(msg.EntityId);
            if (target == null || !ReferenceEquals(target._world, _world))
            {
                Send(new Abort
                {
                    Text = "O jogador precisa estar online e na mesma ilha para receber o convite."
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

        // ApproveClanApplier (3657) — รับใบสมัครเข้าเผ่า · client/ClanSystem.cs:498-507
        // .All เรียก RefreshPlayerClan() ทุกกรณี (ไม่สนสำเร็จหรือไม่) ⇒ ตอบ Abort ปลอดภัย
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

        // DropClanApplier (3659) — ปฏิเสธใบสมัครเข้าเผ่า · client/ClanSystem.cs:509-518
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

        // CancelClanJoinRequest (1923487521) — ผู้เล่นถอนใบสมัครที่ยื่นค้างไว้เอง
        // client/ClanSystem.cs:734-743 CancelWaitingClan รอ .On<OK> เพื่อเด้งป้ายยืนยัน
        // ⚠️ ยิงได้ก็ต่อเมื่อ WaitingClan ไม่ว่าง ซึ่งของเราไม่มีทางเกิด (ไม่มีเส้น /clans)
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

        // ── กลุ่มที่ 2: คลังเงินเผ่า ──────────────────────────────────────────────────

        // GetClanFund (3678) — ขอยอดคลังเงินเผ่า · client/ClanSystem.cs:395-405
        // รอ .On<Costs> (TypeCode 4024) ⇒ เป็น "คำถาม" ไม่ใช่การกระทำ ⇒ ตอบโครงว่างที่ถูกชนิด
        // จุดเรียก: ClanInfoPage.cs:152 (หน้าเผ่า) · EstateGridGroup.cs:656 (ซื้อที่ดินด้วยเงินเผ่า)
        //           PresetCurrencyWidget.cs:173 (ป้ายยอดเงิน)
        // ทั้งสามจุดเป็น callback ทางเดียว ไม่ตอบ = ป้ายยอดเงินค้างเป็นค่าเดิมตลอด
        // ⚠️ ห้ามแต่งยอดเงิน — Costs ว่าง = คลังเผ่าว่าง (ซึ่งเป็นความจริง เพราะไม่มีคลัง)
        //    ฝั่งเกม Unpack สร้าง Dictionary ว่างให้เองเสมอ (client/Messages/Costs.cs:37-52)
        //    ⇒ ส่ง _Costs = null ได้ ตัว Pack แปลงเป็น map ขนาด 0 ให้ (Messages/Costs.cs:23-27)
        // หมายเหตุ: ClanSystem.GetClanFund ยิงเฉพาะตอน PlayerClan != null ⇒ ในทางปฏิบัติ
        //           ยังมาไม่ถึงจนกว่าจะมีเผ่าจริง แต่ลงทะเบียนไว้ให้ครบชนิด
        _connection.Recv(delegate(GetClanFund msg, PacketHeader header)
        {
            Send(new Costs(), header.Seq);
        });

        // DonateToClanFund (3679) — บริจาคเงิน (TStone) เข้าคลังเผ่า
        // client/Durango.UI/ClanInfoPage.cs:383-401 ตรวจยอดเงินในกระเป๋าเองก่อน แล้วยิงมา
        // พร้อม Costs แล้วรอ .On<Costs> เพื่อเอายอดคลังใหม่ไปแสดง
        // ⇒ เป็น "การกระทำ" ที่ทำจริงไม่ได้ (ไม่มีคลังเผ่า) ⇒ Abort — สำคัญมากที่ต้องไม่ตอบ
        //   Costs กลับไป เพราะจะกลายเป็นว่าผู้เล่นเห็นยอดคลังขยับทั้งที่ไม่มีอะไรเกิดขึ้น
        //   (เงินในกระเป๋าฝั่งเซิร์ฟไม่ถูกหัก เพราะเราไม่ได้แตะ Wallet เลย — ถูกต้องแล้ว)
        _connection.Recv(delegate(DonateToClanFund msg, PacketHeader header)
        {
            Send(new Abort { Text = "เซิร์ฟเวอร์นี้ยังไม่เปิดระบบเผ่า จึงยังบริจาคเข้าคลังเผ่าไม่ได้" }, header.Seq);
        });

        // ── กลุ่มที่ 3: ของรางวัล/บัฟของเผ่า (ยิงมาแบบไม่รอคำตอบ) ─────────────────────

        // RequestClanRewards (3706) — ขอรับของรางวัลระดับเผ่า · client/ClanSystem.cs:295-303
        // ⚠️ ฝั่งเกม `Connections.Frontend.Send(default(RequestClanRewards))` **ไม่มี .On และ
        //    ไม่มี global handler ของผลลัพธ์เลย** (ไล่ทั้ง client แล้วไม่เจอตัวรับ)
        //    มันถูกเรียกจาก push ClanRewardsUpdated(3705) ที่มาทาง Radiotower เท่านั้น
        //    ⇒ ของจริงคือเซิร์ฟหยอดของเข้ากระเป๋า/กล่องจดหมาย ไม่ได้ตอบ message กลับ
        // เราไม่มีเผ่า ⇒ ไม่มีรางวัลให้ ⇒ **ห้ามแจกของมั่ว** รับไว้เฉย ๆ กัน log "ไม่มี handler"
        // (และเราไม่เคยส่ง ClanRewardsUpdated ⇒ ในทางปฏิบัติตัวนี้จะไม่ถูกยิงมาเลย)
        _connection.Recv(delegate(RequestClanRewards msg, PacketHeader header)
        {
        });

        // RequestClanStatusEffects (3704) — ขอบัฟ (status effect) ที่ได้จากเผ่า
        // client/ClanSystem.cs:305-313 ยิงแบบไม่รอคำตอบเช่นกัน ตัวรับจริงคือ global
        // Connections.Frontend.On<Messages.StatusEffects> (client/StatusEffectSystem.cs:29)
        // ⚠️ ห้าม push StatusEffects เปล่ากลับไป — client/Durango.Logic/StatusEffects.cs
        //    แทนที่ list ทั้งก้อนต่อ 1 EntityId (ดูคอมเมนต์ที่ server/Core/Player.cs:310-315)
        //    ⇒ ส่งชุดว่างจะไปล้างบัฟจริงของผู้เล่น (อาหาร/ไฟ/สภาพอากาศ) ทิ้งหมด
        // เราไม่มีบัฟเผ่า ⇒ ไม่ต้องส่งอะไร ชุดบัฟที่ถูกต้องถูกส่งอยู่แล้วโดย SendStatusEffects()
        // (server/Core/Player.cs:681) ⇒ รับไว้เฉย ๆ กัน log
        _connection.Recv(delegate(RequestClanStatusEffects msg, PacketHeader header)
        {
        });

        // ── กลุ่มที่ 4: ช่องแชทเผ่า (มาทางสาย Radiotower — ดูหมายเหตุหัวไฟล์) ──────────

        // ToggleClanNotification (4025) — ผู้เล่นกดเปิด/ปิดแจ้งเตือนแชทช่องเผ่า
        // client/SocialSystem.cs:1224-1232 ToggleClanPush: สลับค่าในเครื่องก่อน แล้วส่งทั้งแมป
        // มาให้เซิร์ฟจำ **ไม่รอคำตอบ** ⇒ ห้ามตอบอะไรกลับ แค่จำไว้
        _connection.Recv(delegate(ToggleClanNotification msg, PacketHeader header)
        {
            _clanChannelNotifications = msg.ChannelNotificationsEnabled;
            // [7 ก.ย. 2026] เซฟลงไฟล์ผู้เล่นด้วย — เดิมอยู่แต่ในหน่วยความจำ ออกเกมแล้วค่าหาย
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

        // GetClanNotificationEnabled (4027) — ถามค่าที่จำไว้ ตอนต่อห้องแชทติดใหม่
        // client/SocialSystem.cs:395-398 (ในสาย ConnectionHelper_Ready) รอ **.On<ToggleClanNotification>**
        // แล้วเอา msg.ChannelNotificationsEnabled ไปวางทับ _clanChannelPushEnabled ตรง ๆ
        // ⇒ ต้องตอบด้วยชนิด ToggleClanNotification (ไม่ใช่ OK) และ **ห้ามส่งแมปเป็น null**
        //    เพราะ SocialSystem.cs:1227 และ :1243 เรียก .Get(key, false) บนตัวนั้นทันที
        //    (ค่าตั้งต้นฝั่งเกมเป็น Dictionary ว่างอยู่แล้ว — SocialSystem.cs:179 — ถ้าเราส่ง null ไปทับจะพัง)
        _connection.Recv(delegate(GetClanNotificationEnabled msg, PacketHeader header)
        {
            // เพิ่งต่อเข้ามา — กู้ค่าจากไฟล์เซฟก่อนตอบ
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

        // ResubscribeClanChannel (24) — สมัครช่องแชทเผ่าใหม่หลังเปลี่ยนเผ่า
        // client/SocialSystem.cs:1334-1343 ClanChanged: หน่วง 10 วินาทีแล้วยิง **ไม่รอคำตอบ**
        // เราไม่มีระบบช่องแชทแยกตามเผ่า (SayInExclusiveChannel ของเรากระจายทั้งโลก —
        // server/Core/Player.cs:438-448) ⇒ รับไว้เฉย ๆ กัน log "ไม่มี handler"
        _connection.Recv(delegate(ResubscribeClanChannel msg, PacketHeader header)
        {
        });
    }
}
