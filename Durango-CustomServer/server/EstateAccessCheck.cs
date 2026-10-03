using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Durango.Online;
using Durango.Utils;
using Messages;
using Newtonsoft.Json;
using Shared.Estate;
using Yaml.Util;
using Rights = Shared.Estate.AccessRights;
using FriendKind = Shared.Player.FriendType;

namespace DurangoServerNx;

internal static class EstateAccessCheck
{
    private static object Call(Player player, string method, params object[] args) => typeof(Player)
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player, args);

    private static PlayerContext Context(string root, string id, Point2 tile)
    {
        var context = new PlayerContext
        {
            RegionId = WorldRegistry.DefaultSharedTamedRegionId,
            PlayerInfo = new Durango.Logic.Clusters.PlayerInfo
                { PlayerEntityId = id, PlayerName = id, PlayerLevel = 60 }
        };
        context.Initialize(Path.Combine(root, id + ".player"));
        context.AppearPlayer.Level = 60;
        context.AppearPlayer.IsAlive = true;
        context.AppearPlayer.Move.Movements = new[] { new Movement
        {
            MotionName = "Stand", PlaybackRate = 1,
            Path = new[] { new Location { Position = new WorldPosition(tile.x * 200, tile.y * 200), Time = Gauge.CurrentTime } }
        } };
        return context;
    }

    public static void Run(string root, Action<bool, string> check)
    {
        var wc = new WorldContext { TerrainId = "pe10gr_1" };
        wc.Initialize(Path.Combine(root, "estate-access.world"));
        var world = new World(wc);
        var registry = new WorldRegistry("estate-access-check", world, WorldRegistry.DefaultSharedTamedRegionId);
        check(registry.EnsureDefaultSharedTamedRegion(), "permissoes: instancia publica da Ilha Domada registrada");
        var tile = world.EntryPoint;
        var owner = Context(root, "estate-owner", tile);
        var friend = Context(root, "estate-friend", tile);
        FriendStore.Load(Path.Combine(root, "estate-friends.json"), new[] { owner.EntityId, friend.EntityId });
        check(FriendStore.RequestFriend(owner.EntityId, friend.EntityId, out _, out _) &&
            FriendStore.AcceptFriendRequest(friend.EntityId, owner.EntityId, out _) &&
            FriendStore.SetFriendType(owner.EntityId, friend.EntityId, FriendKind.BestFriend, out _),
            "permissoes: amizade e classificacao do proprietario preparadas");
        var license = world.DeclareEstate(owner.EntityId, OwnerType.Player, World.CellFromTile(tile), owner.RegionId).Value;
        using var ownerLink = new EconomyProtocolCheck.Link(owner, world, null, true);
        using var friendLink = new EconomyProtocolCheck.Link(friend, world, null, true);
        var loaded = ownerLink.Request<GetEstateLicenses, EstateLicenses>(default).UrbanEstate.Value;
        var rights = loaded.AccessRights.Value;
        check(rights.ForFriends != null && rights.ForFriends.Count == 2 && rights.ForClanMembers != null,
            "permissoes: dominio novo envia mapas completos para a janela fechar sem excecao");
        rights.ForFriends[FriendKind.BestFriend] = Rights.Enter | Rights.UseFacility | Rights.Give;
        rights.ForFriends[FriendKind.JustFriend] = Rights.None;
        rights.ForOthers = Rights.Enter;
        rights.ForClanMembers[2] = Rights.UseFacility | Rights.Take;
        check(world.GetEstate(license.EstateId).AccessForFriends == null,
            "permissoes: editar o mapa recebido nao altera o dominio antes de confirmar");
        ownerLink.Request<SetEstateLicense, OK>(new SetEstateLicense { EstateId = license.EstateId, AccessRights = rights });
        var updated = ownerLink.Request<GetEstateLicenseById, EstateLicense>(new GetEstateLicenseById { EstateId = license.EstateId });
        check(updated.AccessRights.Value.ForFriends[FriendKind.BestFriend] == rights.ForFriends[FriendKind.BestFriend] &&
            updated.AccessRights.Value.ForFriends[FriendKind.JustFriend] == Rights.None &&
            updated.AccessRights.Value.ForOthers == Rights.Enter && updated.AccessRights.Value.ForClanMembers[2] == rights.ForClanMembers[2],
            "permissoes: salvar responde OK e reabrir retorna visitantes amigos e cargos pelo TCP");
        friendLink.Request<SetEstateLicense, Abort>(new SetEstateLicense { EstateId = license.EstateId, AccessRights = default });
        check(world.GetEstate(license.EstateId).AccessForOthers == (int)Rights.Enter,
            "permissoes: amigo nao pode administrar ou sobrescrever o dominio");

        var artifact = Cheats.MakeAppearArtifact(new[] { "prop", "7000", $"position:{tile.x},{tile.y}", "size:1,1" }, out _).Value;
        artifact.EntityId = "estate-access-container";
        artifact.Display.EntityId = artifact.States.EntityId = artifact.EntityId;
        artifact.States.BuildingState = Shared.Building.BuildingState.Completed;
        world.ConstructArtifact(artifact, null, owner.EntityId);
        bool May(Rights required) => (bool)Call(friendLink.Player, "MayTouchArtifact", artifact.EntityId, "verificar permissao", required);
        check(May(Rights.Enter) && May(Rights.UseFacility) && May(Rights.Give) && !May(Rights.Take) &&
            !May(Rights.Occupy) && !May(Rights.Destruct) && !May(Rights.None),
            "permissoes: amigo recebe somente as acoes concedidas sem autoridade administrativa");
        check(!(bool)Call(friendLink.Player, "CanBuildInCurrentSettlement", tile, new Point2(1, 1), null),
            "permissoes: usar instalacoes nao permite construir no dominio");
        var menu = friendLink.Request<Touch, Touched>(new Touch
            { EntityId = artifact.EntityId, EntityType = artifact.EntityType, Tile = artifact.Tile });
        check(!menu.Interactions.Contains((int)Shared.System.Interaction.DestructArtifact) &&
            !menu.Interactions.Contains((int)Shared.System.Interaction.Capsulate),
            "permissoes: menu nao oferece destruir ou recolher com acesso apenas de uso");
        var warehouse = friendLink.Request<GetWarehouse, Warehouse>(new GetWarehouse { EntityId = artifact.EntityId });
        check(warehouse.SectionInfos.Length > 0, "permissoes: amigo autorizado abre deposito pelo TCP");
        string section = warehouse.SectionInfos[0].SectionName;
        var item = Cheats.MakeItem("wood_bough", 5).Value;
        friend.InventoryItems.Add(item);
        friendLink.Send(new AddItemsToWarehouse { EntityId = artifact.EntityId, SectionName = section, ItemIds = new[] { item.Id } });
        friendLink.PumpUntil(() => friend.InventoryItems.All(i => i.Id != item.Id));
        friendLink.Send(new PopItemsFromWarehouse { EntityId = artifact.EntityId, SectionName = section, ItemIds = new[] { item.Id } });
        int aborts = friendLink.Messages.OfType<Abort>().Count();
        friendLink.PumpUntil(() => friendLink.Messages.OfType<Abort>().Count() > aborts);
        check(Player.WarehouseStore.Items(artifact.EntityId, section, false).Any(i => i.Id == item.Id),
            "permissoes: dar itens nao concede retirada do deposito");
        rights.ForFriends[FriendKind.BestFriend] |= Rights.Take | Rights.Occupy | Rights.Destruct;
        ownerLink.Request<SetEstateLicense, OK>(new SetEstateLicense { EstateId = license.EstateId, AccessRights = rights });
        friendLink.Send(new PopItemsFromWarehouse { EntityId = artifact.EntityId, SectionName = section, ItemIds = new[] { item.Id } });
        friendLink.PumpUntil(() => friend.InventoryItems.Any(i => i.Id == item.Id));
        check(May(Rights.Take) && May(Rights.Occupy) && May(Rights.Destruct), "permissoes: novas concessoes valem imediatamente");
        check((bool)Call(friendLink.Player, "CanBuildInCurrentSettlement", tile, new Point2(1, 1), null) &&
            !(bool)Call(friendLink.Player, "CanBuildInCurrentSettlement", tile, new Point2(20, 20), null),
            "permissoes: construir autorizado fica restrito a area do dominio");
        menu = friendLink.Request<Touch, Touched>(new Touch
            { EntityId = artifact.EntityId, EntityType = artifact.EntityType, Tile = artifact.Tile });
        check(menu.Interactions.Contains((int)Shared.System.Interaction.DestructArtifact),
            "permissoes: menu permite destruir somente depois da concessao correspondente");
        friend.AppearPlayer.Move.Movements[0].Path[0].Position = new WorldPosition((tile.x + 30) * 200, tile.y * 200);
        check(!May(Rights.UseFacility), "permissoes: convite nao permite usar estruturas a distancia");
        friend.AppearPlayer.Move.Movements[0].Path[0].Position = new WorldPosition(tile.x * 200, tile.y * 200);

        SafeSave.FlushPending();
        var restored = JsonConvert.DeserializeObject<WorldContext>(File.ReadAllText(wc.Path));
        restored.Initialize(wc.Path);
        var restoredRights = new World(restored).ToLicense(license.EstateId, restored.Estates[license.EstateId]).AccessRights.Value;
        check(restoredRights.ForFriends[FriendKind.BestFriend] == rights.ForFriends[FriendKind.BestFriend] &&
            restoredRights.ForClanMembers[2] == rights.ForClanMembers[2], "permissoes: amigos e cargos sobrevivem ao reinicio");
        check(FriendStore.SetFriendType(owner.EntityId, friend.EntityId, FriendKind.JustFriend, out _) && !May(Rights.UseFacility),
            "permissoes: mudar a classificacao do amigo aplica as permissoes da nova categoria");
        friendLink.Request<GetWarehouse, Abort>(new GetWarehouse { EntityId = artifact.EntityId });
        ownerLink.Request<SetEstateLicense, OK>(new SetEstateLicense { EstateId = license.EstateId, AccessRights = default });
        var cleared = ownerLink.Request<GetEstateLicenseById, EstateLicense>(new GetEstateLicenseById { EstateId = license.EstateId });
        check(cleared.AccessRights.Value.ForFriends.Count == 2 && cleared.AccessRights.Value.ForFriends.Values.All(r => r == Rights.None) &&
            cleared.AccessRights.Value.ForClanMembers.Count == 0 && cleared.AccessRights.Value.ForOthers == Rights.None,
            "permissoes: limpar configuracao revoga direitos e continua enviando mapas validos");
        var legacy = JsonConvert.DeserializeObject<EstateRecord>("{\"access_for_others\":1}").ToAccessRights();
        check(legacy.ForOthers == Rights.Enter && legacy.ForFriends.Count == 2 && legacy.ForClanMembers.Count == 0,
            "permissoes: saves antigos sem mapas abrem a janela sem perder direitos de visitantes");
    }
}
