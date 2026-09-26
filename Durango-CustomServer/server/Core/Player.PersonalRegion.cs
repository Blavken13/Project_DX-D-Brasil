using System;
using Durango.Utils;
using System.Collections.Generic;
using System.Linq;
using Messages;
using Shared.Estate;
using Shared.Region;
using Shared.Teleport;
using Yaml;
using Yaml.Util;

namespace Durango.Online;

// Assentamentos especiais do jogador: Ilha Domada pública, Ilha Particular e Ilha de Clã.
public partial class Player
{
    /// <summary>Monta PersonalRegionInfo da Ilha Particular persistida no save do jogador.</summary>
    private PersonalRegionInfo BuildPersonalRegionInfo()
    {
        if (string.IsNullOrEmpty(_context.PersonalRegionId))
        {
            WorldRegistry registry = _world.Registry;
            if (registry?.EnsureDefaultSharedTamedRegion() != true ||
                !registry.TryGetSettlementRegion(
                    WorldRegistry.DefaultSharedTamedRegionId,
                    out SettlementRegionInstance sharedTamed))
            {
                return default;
            }

            var sharedRegion = new Region
            {
                Id = sharedTamed.RegionId,
                TerrainId = sharedTamed.TemplateId,
                TemplateId = sharedTamed.TemplateId,
                Role = Role.Personal,
                Name = "Ilha Domada",
                CreatedAt = 0
            };

            return new PersonalRegionInfo
            {
                PersonalRegion = new Messages.PersonalRegion
                {
                    Region = sharedRegion,
                    OwnerId = string.Empty,
                    PioneerExp = 0,
                    AdmissionCategories = Array.Empty<LicenseCategory>()
                },
                PersonalEstate = FindOwnedEstateLicense(OwnerType.Player)
            };
        }

        EnsurePersonalWorldRegistered();

        LicenseCategory[] admission = Array.Empty<LicenseCategory>();
        if (_context.PersonalRegionAdmission is { Count: > 0 })
        {
            admission = _context.PersonalRegionAdmission
                .Select(v => (LicenseCategory)Math.Clamp(v, 0, 3))
                .ToArray();
        }

        string templateId = _context.PersonalRegionTemplateId ?? _context.PersonalRegionId;
        var region = new Region
        {
            Id = _context.PersonalRegionId,
            TerrainId = templateId,
            TemplateId = templateId,
            Role = Role.Personal,
            Name = "Ilha Particular",
            CreatedAt = 0
        };

        return new PersonalRegionInfo
        {
            PersonalRegion = new Messages.PersonalRegion
            {
                Region = region,
                OwnerId = EntityId,
                PioneerExp = 0,
                AdmissionCategories = admission
            },
            PersonalEstate = FindOwnedEstateLicense(OwnerType.PersonalPlayer)
        };
    }

    private Messages.PersonalRegion BuildPersonalRegionMessage()
    {
        PersonalRegionInfo info = BuildPersonalRegionInfo();
        return info.PersonalRegion ?? default;
    }

    private void EnsurePersonalWorldRegistered()
    {
        if (string.IsNullOrEmpty(_context.PersonalRegionId) ||
            string.IsNullOrEmpty(_context.PersonalRegionTemplateId))
        {
            return;
        }

        WorldRegistry registry = _world.Registry;
        if (registry == null)
        {
            return;
        }
        registry.RegisterPersonalRegion(
            _context.PersonalRegionId,
            _context.PersonalRegionTemplateId,
            EntityId);
        registry.GetOrCreate(_context.PersonalRegionId);
    }

    private string CurrentClanId()
    {
        string clanId = _context.AppearPlayer.Member.ClanId;
        return string.IsNullOrWhiteSpace(clanId) ? null : clanId;
    }

    private string EnsureClanWorldRegistered()
    {
        string clanId = CurrentClanId();
        return string.IsNullOrEmpty(clanId) ? null : _world.Registry?.RegisterClanRegion(clanId);
    }

    private static bool IsAllowedPersonalTemplate(string templateId)
    {
        List<string> ids = Singleton<Constants>.Instance?.PersonalRegion?.RegionTemplateIds;
        if (ids == null || ids.Count == 0)
        {
            return !string.IsNullOrEmpty(templateId);
        }
        return ids.Contains(templateId);
    }

    private string MakePersonalRegionId()
    {
        // ID estável por personagem: restart continua apontando para a mesma Ilha Particular.
        string shortId = EntityId.Length <= 8 ? EntityId : EntityId[..8];
        return "personal_" + shortId;
    }

    private void HandleRecommendPersonalRegion(RecommendPersonalRegion msg, uint seq)
    {
        string templateId = msg.TemplateId;
        if (string.IsNullOrEmpty(templateId))
        {
            Send(new Abort { Text = "ต้องเลือกภูมิประเทศของเกาะส่วนตัว" }, seq);
            return;
        }
        if (!IsAllowedPersonalTemplate(templateId))
        {
            Send(new Abort { Text = "ภูมิประเทศนี้ใช้สร้างเกาะส่วนตัวไม่ได้" }, seq);
            return;
        }

        if (!string.IsNullOrEmpty(_context.PersonalRegionId))
        {
            _context.PrivateRegionEntitled = true;
            if (string.IsNullOrEmpty(_context.PersonalRegionTemplateId))
            {
                _context.PersonalRegionTemplateId = templateId;
            }
            EnsurePersonalWorldRegistered();
            Send(BuildPersonalRegionMessage(), seq);
            Console.WriteLine($"[เกาะส่วนตัว] {Short(EntityId)} มีเกาะแล้ว {_context.PersonalRegionId}");
            return;
        }

        if (!_context.PrivateRegionEntitled)
        {
            Send(new Abort { Text = "A Ilha Particular precisa ser adquirida antes da criação." }, seq);
            return;
        }

        _context.PersonalRegionId = MakePersonalRegionId();
        _context.PersonalRegionTemplateId = templateId;
        _context.PersonalRegionAdmission ??= new List<int>();
        EnsurePersonalWorldRegistered();
        if (!string.IsNullOrEmpty(_context.Path))
        {
            _context.Save();
        }
        OnContextChanged();
        Send(BuildPersonalRegionMessage(), seq);
        Console.WriteLine($"[เกาะส่วนตัว] {Short(EntityId)} สร้าง {_context.PersonalRegionId} จาก {templateId}");
    }

    private void HandleReturnToEstate(ReturnToEstate msg, uint seq)
    {
        string dest = null;

        switch (msg.OwnerType)
        {
            case OwnerType.PersonalPlayer:
                if (!string.IsNullOrEmpty(_context.PersonalRegionId))
                {
                    EnsurePersonalWorldRegistered();
                    dest = _context.PersonalRegionId;
                }
                else if (_world.Registry?.EnsureDefaultSharedTamedRegion() == true)
                {
                    // Sem Ilha Particular adquirida, o atalho de Ilha Domada leva ao mundo público.
                    dest = WorldRegistry.DefaultSharedTamedRegionId;
                }
                break;

            case OwnerType.Player:
                if (_world.Registry?.EnsureDefaultSharedTamedRegion() == true)
                {
                    dest = WorldRegistry.DefaultSharedTamedRegionId;
                }
                break;

            case OwnerType.ClanEstate:
            case OwnerType.ClanWarphole:
                dest = EnsureClanWorldRegistered();
                break;
        }

        if (string.IsNullOrEmpty(dest))
        {
            Send(new Abort { Text = "Não há um assentamento desse tipo disponível para retorno." }, seq);
            return;
        }

        const float duration = 1f; // Valor temporário do Alpha/Beta.
        Send(new Messages.Timer { Duration = duration }, seq);
        Console.WriteLine($"[assentamento] {Short(EntityId)} retornando para {dest}");
        System.Threading.Timer timer = null;
        timer = new System.Threading.Timer(_ =>
        {
            try
            {
                _context.RegionId = dest;
                _context.AppearPlayer.Move.Movements = null;
                if (!string.IsNullOrEmpty(_context.Path))
                {
                    _context.Save();
                }
                Send(new Emigrated { Type = TeleportType.Unknown });
            }
            catch (Exception e)
            {
                Console.WriteLine($"[assentamento] falha ao retornar para {dest}: {e.Message}");
            }
            finally
            {
                timer?.Dispose();
            }
        }, null, (int)(duration * 1000f), System.Threading.Timeout.Infinite);
    }

    private void HandleVisitEstate(VisitEstate msg, uint seq)
    {
        string dest = msg.RegionId;

        switch (msg.OwnerType)
        {
            case OwnerType.Player:
                if (_world.Registry?.EnsureDefaultSharedTamedRegion() != true)
                {
                    Send(new Abort { Text = "A Ilha Domada pública não está disponível." }, seq);
                    return;
                }
                dest = WorldRegistry.DefaultSharedTamedRegionId;
                break;

            case OwnerType.PersonalPlayer:
                if (string.IsNullOrEmpty(dest) || _world.Registry?.IsPersonalRegion(dest) != true)
                {
                    Send(new Abort { Text = "A Ilha Particular de destino não existe." }, seq);
                    return;
                }
                break;

            case OwnerType.ClanEstate:
            case OwnerType.ClanWarphole:
                string ownClanRegion = EnsureClanWorldRegistered();
                if (string.IsNullOrEmpty(ownClanRegion) ||
                    (!string.IsNullOrEmpty(dest) &&
                     !string.Equals(dest, ownClanRegion, StringComparison.OrdinalIgnoreCase)))
                {
                    Send(new Abort { Text = "Você não pertence ao clã dessa ilha." }, seq);
                    return;
                }
                dest = ownClanRegion;
                break;

            default:
                Send(new Abort { Text = "Esse tipo de assentamento ainda não pode ser visitado." }, seq);
                return;
        }

        const float duration = 1f;
        Send(new Messages.Timer { Duration = duration }, seq);
        Console.WriteLine($"[assentamento] {Short(EntityId)} visitando {dest}");
        System.Threading.Timer timer = null;
        timer = new System.Threading.Timer(_ =>
        {
            try
            {
                _context.RegionId = dest;
                _context.AppearPlayer.Move.Movements = null;
                if (!string.IsNullOrEmpty(_context.Path))
                {
                    _context.Save();
                }
                Send(new Emigrated { Type = TeleportType.Unknown });
            }
            catch (Exception e)
            {
                Console.WriteLine($"[assentamento] falha ao visitar {dest}: {e.Message}");
            }
            finally
            {
                timer?.Dispose();
            }
        }, null, (int)(duration * 1000f), System.Threading.Timeout.Infinite);
    }

    private void HandleSetPersonalRegionAdmission(SetPersonalRegionAdmission msg)
    {
        if (string.IsNullOrEmpty(_context.PersonalRegionId))
        {
            return;
        }
        if (msg.AdmissionCategories == null || msg.AdmissionCategories.Length == 0)
        {
            _context.PersonalRegionAdmission = new List<int>();
        }
        else
        {
            _context.PersonalRegionAdmission = msg.AdmissionCategories.Select(c => (int)c).ToList();
        }
        if (!string.IsNullOrEmpty(_context.Path))
        {
            _context.Save();
        }
        Console.WriteLine($"[เกาะส่วนตัว] {Short(EntityId)} ตั้ง admission = {_context.PersonalRegionAdmission.Count}");
    }

    private const int PersonalEstateMaxSize = 30;

    private EstateLicenses BuildEstateLicenses()
    {
        EstateLicense? personal = null;
        EstateLicense? urban = null;
        int largestPersonal = 0;
        int largestUrban = 0;
        foreach (var kv in _world.EnumerateEstates())
        {
            if (kv.Value.OwnerId != EntityId) continue;
            EstateLicense lic = _world.ToLicense(kv.Key, kv.Value);
            if (kv.Value.Type == (int)OwnerType.PersonalPlayer)
            {
                personal = lic;
                if (kv.Value.Size > largestPersonal) largestPersonal = kv.Value.Size;
            }
            else if (kv.Value.Type == (int)OwnerType.Player)
            {
                urban = lic;
                if (kv.Value.Size > largestUrban) largestUrban = kv.Value.Size;
            }
        }
        return new EstateLicenses
        {
            PersonalEstate = personal,
            UrbanEstate = urban,
            LargestPersonalEstateSize = largestPersonal,
            LargestUrbanEstateSize = largestUrban
        };
    }

    private string CurrentRegionIdForEstate()
    {
        if (!string.IsNullOrEmpty(_context.RegionId)) return _context.RegionId;
        return _world.TerrainId ?? "1";
    }

    private bool TryResolveEstateDeclaration(
        OwnerType requestedType,
        out OwnerType actualType,
        out string ownerId,
        out string error)
    {
        actualType = requestedType;
        ownerId = EntityId;
        error = null;

        SettlementRegionInstance settlement = null;
        if (_world.Registry != null &&
            _world.Registry.TryGetSettlementRegion(_context.RegionId, out settlement))
        {
            switch (settlement.Kind)
            {
                case SettlementRegionKind.SharedTamed:
                    actualType = OwnerType.Player;
                    ownerId = EntityId;
                    return true;

                case SettlementRegionKind.PrivatePlayer:
                    if (!string.Equals(_context.RegionId, _context.PersonalRegionId, StringComparison.OrdinalIgnoreCase))
                    {
                        error = "Só o proprietário pode reivindicar domínio na Ilha Particular.";
                        return false;
                    }
                    actualType = OwnerType.PersonalPlayer;
                    ownerId = EntityId;
                    return true;

                case SettlementRegionKind.Clan:
                    string clanId = CurrentClanId();
                    if (string.IsNullOrEmpty(clanId) ||
                        !string.Equals(clanId, settlement.OwnerId, StringComparison.Ordinal))
                    {
                        error = "Você não pertence ao clã proprietário desta ilha.";
                        return false;
                    }

                    if (!ClanStore.CanManageEstate(EntityId, clanId))
                    {
                        error = "Somente Líder ou Oficial pode declarar o domínio do clã.";
                        return false;
                    }

                    actualType = OwnerType.ClanEstate;
                    ownerId = clanId;
                    return true;
            }
        }

        if (requestedType != OwnerType.Player)
        {
            error = "Esse tipo de domínio só pode ser declarado em uma ilha de assentamento compatível.";
            return false;
        }

        return true;
    }

    private string EstateAuthorityOwner(EstateRecord estate)
    {
        if (estate == null) return null;

        if (estate.Type == (int)OwnerType.ClanEstate ||
            estate.Type == (int)OwnerType.ClanWarphole)
        {
            string clanId = CurrentClanId();

            return !string.IsNullOrEmpty(clanId) &&
                   string.Equals(estate.OwnerId, clanId, StringComparison.Ordinal) &&
                   ClanStore.CanManageEstate(EntityId, clanId)
                ? clanId
                : null;
        }

        return string.Equals(estate.OwnerId, EntityId, StringComparison.Ordinal)
            ? EntityId
            : null;
    }

    private bool CanBuildInCurrentSettlement(Point2 tile, Point2 size, out string error)
    {
        error = null;
        SettlementRegionInstance settlement = null;
        bool isSettlement = _world.Registry?.TryGetSettlementRegion(
            _context.RegionId,
            out settlement) == true;

        string requiredOwner = EntityId;
        OwnerType? requiredType = null;
        bool requireEstate = false;

        if (isSettlement)
        {
            switch (settlement.Kind)
            {
                case SettlementRegionKind.PrivatePlayer:
                    if (!string.Equals(_context.RegionId, _context.PersonalRegionId, StringComparison.OrdinalIgnoreCase))
                    {
                        error = "Não é permitido construir na Ilha Particular de outro jogador.";
                        return false;
                    }
                    requiredType = OwnerType.PersonalPlayer;
                    break;

                case SettlementRegionKind.SharedTamed:
                    requiredType = OwnerType.Player;
                    requireEstate = true;
                    break;

                case SettlementRegionKind.Clan:
                    string clanId = CurrentClanId();
                    if (string.IsNullOrEmpty(clanId) ||
                        !string.Equals(clanId, settlement.OwnerId, StringComparison.Ordinal))
                    {
                        error = "Você não pertence ao clã proprietário desta ilha.";
                        return false;
                    }
                    requiredOwner = clanId;
                    requiredType = OwnerType.ClanEstate;
                    requireEstate = true;
                    break;
            }
        }

        int width = Math.Max(1, size.x);
        int height = Math.Max(1, size.y);
        for (int dx = 0; dx < width; dx++)
        {
            for (int dy = 0; dy < height; dy++)
            {
                Point2 cell = World.CellFromTile(new Point2(tile.x + dx, tile.y + dy));
                if (!_world.TryGetEstateIdAtCell(cell, out string estateId))
                {
                    if (requireEstate)
                    {
                        error = settlement.Kind == SettlementRegionKind.Clan
                            ? "Construa dentro do domínio do clã."
                            : "Reivindique um domínio antes de construir nesta Ilha Domada.";
                        return false;
                    }
                    continue;
                }

                EstateRecord estate = _world.GetEstate(estateId);
                if (estate == null ||
                    !string.Equals(estate.OwnerId, requiredOwner, StringComparison.Ordinal) ||
                    (requiredType.HasValue && estate.Type != (int)requiredType.Value))
                {
                    error = "A área de construção invade um domínio sem permissão.";
                    return false;
                }
            }
        }

        return true;
    }

    private bool CanUseArtifactInCurrentSettlement(AppearArtifact artifact, string artifactOwner)
    {
        if (string.Equals(artifactOwner, EntityId, StringComparison.Ordinal))
        {
            return true;
        }

        SettlementRegionInstance settlement = null;
        if (_world.Registry == null ||
            !_world.Registry.TryGetSettlementRegion(_context.RegionId, out settlement))
        {
            return false;
        }

        Point2 cell = World.CellFromTile(artifact.Tile);
        if (!_world.TryGetEstateIdAtCell(cell, out string estateId))
        {
            return false;
        }

        EstateRecord estate = _world.GetEstate(estateId);
        if (estate == null) return false;

        if (settlement.Kind == SettlementRegionKind.SharedTamed)
        {
            return estate.Type == (int)OwnerType.Player &&
                   string.Equals(estate.OwnerId, EntityId, StringComparison.Ordinal);
        }

        if (settlement.Kind == SettlementRegionKind.Clan)
        {
            string clanId = CurrentClanId();
            return !string.IsNullOrEmpty(clanId) &&
                   string.Equals(clanId, settlement.OwnerId, StringComparison.Ordinal) &&
                   estate.Type == (int)OwnerType.ClanEstate &&
                   string.Equals(estate.OwnerId, clanId, StringComparison.Ordinal);
        }

        return false;
    }

    private void BroadcastEstateGridsAround(Point2 cell)
    {
        int tileX = cell.x * World.EstateGridSize;
        int tileY = cell.y * World.EstateGridSize;
        int cx = tileX / 16;
        int cy = tileY / 16;
        var chunks = new List<Point2>();
        for (int x = cx - 1; x <= cx + 1; x++)
        for (int y = cy - 1; y <= cy + 1; y++)
        {
            if (x >= 0 && y >= 0) chunks.Add(new Point2(x, y));
        }
        _world.BroadCast(_world.BuildEstateGridsForChunks(chunks));
    }

    private void HandleDeclareEstate(DeclareEstate msg, uint seq)
    {
        if (!TryResolveEstateDeclaration(
                msg.OwnerType,
                out OwnerType actualType,
                out string ownerId,
                out string declarationError))
        {
            Send(new Abort { Text = declarationError }, seq);
            return;
        }

        Point2 estateTile = World.TileFromCell(msg.Cell);
        int maxTileX = _world.NumChunksX * 16;
        int maxTileY = _world.NumChunksY * 16;
        if (estateTile.x < 0 ||
            estateTile.y < 0 ||
            estateTile.x + World.EstateGridSize > maxTileX ||
            estateTile.y + World.EstateGridSize > maxTileY)
        {
            Send(new Abort { Text = "พื้นที่นี้อยู่นอกขอบเขตเกาะ" }, seq);
            return;
        }
        if (!IsWithinTiles(estateTile, ArtifactReachTiles + World.EstateGridSize))
        {
            Send(new Abort { Text = "ต้องอยู่ใกล้พื้นที่ก่อนประกาศที่ดิน" }, seq);
            return;
        }

        EstateLicense? license = _world.DeclareEstate(
            ownerId,
            actualType,
            msg.Cell,
            CurrentRegionIdForEstate());
        if (!license.HasValue)
        {
            Send(new Abort { Text = "ประกาศที่ดินไม่ได้ — ช่องถูกจองแล้วหรือมีที่ดินชนิดนี้อยู่แล้ว" }, seq);
            return;
        }
        Send(license.Value, seq);
        BroadcastEstateGridsAround(msg.Cell);
        Console.WriteLine($"[domínio] {Short(EntityId)} declarou {actualType} em [{msg.Cell.x},{msg.Cell.y}] → {license.Value.EstateId}");
        OnContextChanged();
    }

    private void HandleExpandEstate(ExpandEstate msg, uint seq)
    {
        EstateRecord estate = _world.GetEstate(msg.EstateId);
        string authorityOwner = EstateAuthorityOwner(estate);
        EstateLicense? license = string.IsNullOrEmpty(authorityOwner)
            ? null
            : _world.ExpandEstate(msg.EstateId, authorityOwner, msg.Cell, PersonalEstateMaxSize);
        if (!license.HasValue)
        {
            Send(new Abort { Text = "ขยายที่ดินไม่ได้" }, seq);
            return;
        }
        Send(license.Value, seq);
        BroadcastEstateGridsAround(msg.Cell);
        OnContextChanged();
    }

    private void HandleShrinkEstate(ShrinkEstate msg, uint seq)
    {
        EstateRecord estate = _world.GetEstate(msg.EstateId);
        string authorityOwner = EstateAuthorityOwner(estate);
        EstateLicense? license = string.IsNullOrEmpty(authorityOwner)
            ? null
            : _world.ShrinkEstate(msg.EstateId, authorityOwner, msg.Cell);
        if (!license.HasValue)
        {
            Send(new Abort { Text = "ลดขนาดที่ดินไม่ได้" }, seq);
            return;
        }
        Send(license.Value, seq);
        BroadcastEstateGridsAround(msg.Cell);
        OnContextChanged();
    }

    private void HandleRemoveEstate(RemoveEstate msg)
    {
        EstateRecord rec = _world.GetEstate(msg.EstateId);
        Point2 cell = default;
        if (rec != null && rec.Cells.Count > 0)
        {
            string[] parts = rec.Cells[0].Split(',');
            cell = new Point2(int.Parse(parts[0]), int.Parse(parts[1]));
        }
        string authorityOwner = EstateAuthorityOwner(rec);
        if (!string.IsNullOrEmpty(authorityOwner) &&
            _world.RemoveEstate(msg.EstateId, authorityOwner))
        {
            if (rec != null) BroadcastEstateGridsAround(cell);
            Console.WriteLine($"[ที่ดิน] {Short(EntityId)} รื้อ {msg.EstateId}");
            OnContextChanged();
        }
    }

    private void HandleSetEstateLicense(SetEstateLicense msg, uint seq)
    {
        EstateRecord rec = _world.GetEstate(msg.EstateId);
        if (string.IsNullOrEmpty(EstateAuthorityOwner(rec)))
        {
            Send(new Abort { Text = "Domínio não encontrado ou sem permissão administrativa." }, seq);
            return;
        }
        rec.AccessForOthers = (int)msg.AccessRights.ForOthers;
        _world.Save();
        Send(default(OK), seq);
        if (rec.Cells.Count > 0)
        {
            string[] parts = rec.Cells[0].Split(',');
            BroadcastEstateGridsAround(new Point2(int.Parse(parts[0]), int.Parse(parts[1])));
        }
        OnContextChanged();
    }

    private void HandleExtendEstate(ExtendEstateActivation msg, uint seq)
    {
        EstateRecord rec = _world.GetEstate(msg.EstateId);
        if (string.IsNullOrEmpty(EstateAuthorityOwner(rec)))
        {
            Send(new Abort { Text = "Domínio não encontrado ou sem permissão administrativa." }, seq);
            return;
        }
        // Regra temporária do Alpha/Beta: renovação gratuita por 7 dias.
        double now = Times.UnixTimeNow();
        double baseTime = rec.ExpiresAt.HasValue && rec.ExpiresAt.Value > now ? rec.ExpiresAt.Value : now;
        rec.ExpiresAt = baseTime + 7 * 24 * 3600;
        _world.Save();
        Send(_world.ToLicense(msg.EstateId, rec), seq);
        if (rec.Cells.Count > 0)
        {
            string[] parts = rec.Cells[0].Split(',');
            BroadcastEstateGridsAround(new Point2(int.Parse(parts[0]), int.Parse(parts[1])));
        }
        OnContextChanged();
    }

    private EstateLicense? FindOwnedEstateLicense(OwnerType type)
    {
        foreach (var kv in _world.EnumerateEstates())
        {
            if (kv.Value.Type != (int)type) continue;
            if (!string.IsNullOrEmpty(EstateAuthorityOwner(kv.Value)))
            {
                return _world.ToLicense(kv.Key, kv.Value);
            }
        }
        return null;
    }
}
