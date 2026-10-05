using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Durango.Online;
using Durango.Utils;
using Messages;
using Shared.Clan;
using Shared.Economy;
using Shared.Estate;
using Yaml;
using Yaml.Util;

namespace DurangoServerNx;

internal static class SocialCheck
{
    private static int _checks;
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); _checks++; Console.WriteLine("[social-check] OK " + message); }
    private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    private static PlayerContext Context(string root, string id, World world)
    {
        var ctx = new PlayerContext { RegionId = "pe10gr_1", OwnerKey = "social-" + id,
            PlayerInfo = new Durango.Logic.Clusters.PlayerInfo { PlayerEntityId = id, PlayerName = id, PlayerLevel = 10 } };
        ctx.Initialize(Path.Combine(root, id + ".player")); ctx.TStone = 5_000_000;
        ctx.AppearPlayer.Move.Movements = new[] { new Movement { MotionName = "Stand", PlaybackRate = 1,
            Path = new[] { new Location { Position = new WorldPosition(world.EntryPoint.x * 200, world.EntryPoint.y * 200), Time = Gauge.CurrentTime } } } };
        return ctx;
    }
    private sealed class Contract
    {
        public string id { get; set; }
        public int capacity { get; set; }
        public long fund { get; set; }
        public Pair<string, int>[] members { get; set; }
        public string[] appliers { get; set; }
        public Dictionary<int, ClanRoleRecord> role_infos { get; set; }
    }
    public static int Run(string dataDir)
    {
        string root = Path.Combine(Path.GetTempPath(), "Durango-social-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Json.DataDir = dataDir; MoCatalog.Load(dataDir); DataStore.Load(dataDir);
            TerrainLoader.TerrainDir = Path.Combine(dataDir, "terrains"); WorkbenchTags.AssetsDir = Path.Combine(dataDir, "assets");
            RegionCatalog.Load(Path.Combine(dataDir,"assets"));
            var wc = new WorldContext { TerrainId = "pe10gr_1" }; wc.Initialize(Path.Combine(root, "social.world")); var world = new World(wc);
            var otherWc = new WorldContext { TerrainId = "pe10gr_1" }; otherWc.Initialize(Path.Combine(root, "other.world")); var otherWorld = new World(otherWc);
            var alice = Context(root, "alice", world); var bob = Context(root, "bob", otherWorld);
            var carol = Context(root, "carol", world); var dave = Context(root, "dave", world);
            var economy = new EconomyStore(Path.Combine(root, "economy.json"), new ShopCatalog());
            foreach (var ctx in new[] { alice, bob, carol, dave }) economy.Recover(ctx);
            using var a = new EconomyProtocolCheck.Link(alice, world, economy);
            using var b = new EconomyProtocolCheck.Link(bob, otherWorld, economy);
            using var c = new EconomyProtocolCheck.Link(carol, world, economy);
            using var d = new EconomyProtocolCheck.Link(dave, world, economy);
            Check(!a.Request<GetParty, Party>(default).Info.HasValue, "personagem inicia sem equipe");
            a.Send(default(MakeParty)); a.PumpUntil(() => PartyStore.Accepted(alice.EntityId));
            var party = a.Request<GetParty, Party>(default);
            Check(party.Info.Value.LeaderStatus.EntityId == alice.EntityId && party.Info.Value.LeaderRadioId.Name == "alice", "criação envia liderança e nome no protocolo nativo");
            a.Send(new InviteIntoParty { InviteeEntityId = bob.EntityId }); a.PumpUntil(() => PartyStore.Snapshot(bob.EntityId) != null);
            var invited = b.Request<GetParty, Party>(default);
            Check(invited.Info.Value.MemberStatus.Single().Item1.EntityId == bob.EntityId && !invited.Info.Value.MemberStatus.Single().Item2, "convite funciona entre ilhas e permanece pendente");
            Check(!PartyStore.SameTeam(alice.EntityId, bob.EntityId), "convidado não recebe permissões nem chat de membro");
            b.Send(default(JoinIntoParty)); b.PumpUntil(() => PartyStore.Accepted(bob.EntityId));
            Check(PartyStore.SameTeam(alice.EntityId, bob.EntityId), "aceitar convite confirma entrada");
            Check(!PartyStore.Change(bob.EntityId, "invite", carol.EntityId, out _, out _), "membro não pode convidar");
            Check(!PartyStore.Change(bob.EntityId, "kick", alice.EntityId, out _, out _), "membro não pode expulsar líder");
            a.Send(new ElectPartyLeader { MemberEntityId = bob.EntityId }); a.PumpUntil(() => PartyStore.Snapshot(alice.EntityId).Leader == bob.EntityId);
            Check(PartyStore.Snapshot(alice.EntityId).Leader == bob.EntityId, "transferência de liderança");
            b.Send(new InviteIntoParty { InviteeEntityId = carol.EntityId }); b.PumpUntil(() => PartyStore.Snapshot(carol.EntityId) != null);
            c.Send(default(RejectPartyInvitation)); c.PumpUntil(() => PartyStore.Snapshot(carol.EntityId) == null);
            Check(PartyStore.Snapshot(carol.EntityId) == null && PartyStore.Accepted(bob.EntityId), "recusa remove apenas convite");
            Check(PartyStore.Change(bob.EntityId, "invite", carol.EntityId, out _, out _), "novo convite permitido");
            PartyStore.Clock = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 301;
            Check(PartyStore.Snapshot(carol.EntityId) == null, "convite expirado não permite entrada");
            PartyStore.Clock = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Check(PartyStore.Change(bob.EntityId, "leave", null, out _, out _) && PartyStore.Snapshot(alice.EntityId).Leader == alice.EntityId, "saída do líder elege integrante confirmado");
            Check(PartyStore.Change(alice.EntityId, "leave", null, out _, out _) && PartyStore.Snapshot(alice.EntityId) == null, "último integrante dissolve equipe");
            Check(PartyStore.Change(alice.EntityId, "make", null, out _, out _) && PartyStore.Change(alice.EntityId, "invite", bob.EntityId, out _, out _)
                && PartyStore.Change(bob.EntityId, "join", null, out _, out _), "equipe preparada para validar chat");
            var partyText = new SayInExclusiveChannel { ChannelType = Shared.Chat.ChannelType.Party,
                Message = new Message_ { EntityId = "forged", Time = 1, Speaker = new RadioId { Name = "forged" }, Body = new RadioText { Text = "party-private" } } };
            a.Send(partyText); a.Request<GetParty, Party>(default); b.Request<GetParty, Party>(default); c.Request<GetParty, Party>(default);
            var chat = b.Messages.OfType<SayInExclusiveChannel>().Last();
            Check(chat.Message.EntityId == alice.EntityId && chat.Message.Speaker.Value.Name == "alice" && chat.Message.Time > 1, "chat ignora identidade e horário forjados");
            Check(!c.Messages.OfType<SayInExclusiveChannel>().Any(), "chat de equipe não vaza para terceiros");
            Check(d.Request<SayInExclusiveChannel, Abort>(partyText).Text.Length > 0, "terceiro não envia no chat de equipe");
            Check(PartyStore.Change(alice.EntityId,"invite",carol.EntityId,out _,out _) && PartyStore.Change(alice.EntityId,"invite",dave.EntityId,out _,out _)
                && PartyStore.Change(alice.EntityId,"invite","fifth",out _,out _) && !PartyStore.Change(alice.EntityId,"invite","sixth",out _,out _), "limite de integrantes inclui convites pendentes");
            PartyStore.Change(alice.EntityId,"reject",carol.EntityId,out _,out _); PartyStore.Change(alice.EntityId,"reject",dave.EntityId,out _,out _); PartyStore.Change(alice.EntityId,"reject","fifth",out _,out _);
            typeof(PartyStore).GetField("_path", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null,null);
            PartyStore.Load(alice.Path);
            Check(PartyStore.SameTeam(alice.EntityId,bob.EntityId), "equipe confirmada sobrevive à recarga do arquivo");

            Check(a.Request<GetClanCreationCosts, Costs>(default)._Costs[Currency.TStone] == 10000, "custo original de criação informado");
            long balance = alice.TStone;
            a.Request<MakeClan, OK>(new MakeClan { ClanName = "Horizonte" }); string clanId = ClanStore.ClanIdOf(alice.EntityId);
            Check(!string.IsNullOrEmpty(clanId) && alice.TStone == balance - 10000, "criação debita custo e persiste clã");
            var clan = ClanStore.Find(clanId);
            var contract = Json.Read<Contract>(Json.Write(ClanStore.ToGatewayJson(clan, true)), true);
            Check(contract != null && contract.members.Single().Item1 == alice.EntityId && contract.capacity == 10 && contract.role_infos[0].UserType == UserType.Root,
                "JSON compatível com pares, capacidade e role_infos do cliente");
            Check(a.Request<MakeClan, Abort>(new MakeClan { ClanName = "Outro" }).Text.Length > 0 && alice.TStone == balance - 10000, "criação repetida não cobra novamente");
            c.Request<MakeClan, OK>(new MakeClan { ClanName = "Pioneiros" }); string otherClanId = ClanStore.ClanIdOf(carol.EntityId);
            b.Request<JoinClan, OK>(new JoinClan { ClanId = clanId });
            Check(ClanStore.Find(clanId).Appliers.ContainsKey(bob.EntityId) && ClanStore.ClanIdOf(bob.EntityId) == null, "solicitação aguarda aprovação");
            a.Request<ApproveClanApplier, OK>(new ApproveClanApplier { EntityId = bob.EntityId });
            Check(ClanStore.ClanIdOf(bob.EntityId) == clanId && bob.AppearPlayer.Member.ClanId == clanId, "aprovação sincroniza personagem em outra ilha");
            a.Request<InviteToClan, OK>(new InviteToClan { EntityId = dave.EntityId }); d.Request<JoinClan, OK>(new JoinClan { ClanId = clanId });
            Check(ClanStore.ClanIdOf(dave.EntityId) == clanId, "convite pré-aprovado permite entrada");
            var clanText = partyText; clanText.ChannelType = Shared.Chat.ChannelType.Clan;
            a.Send(clanText); a.Request<GetParty, Party>(default); b.Request<GetParty, Party>(default); c.Request<GetParty, Party>(default); d.Request<GetParty, Party>(default);
            Check(b.Messages.OfType<SayInExclusiveChannel>().Any(m => m.ChannelType == Shared.Chat.ChannelType.Clan)
                && !c.Messages.OfType<SayInExclusiveChannel>().Any(m => m.ChannelType == Shared.Chat.ChannelType.Clan), "chat do clã atravessa ilhas e respeita vínculo");
            Check(b.Request<KickClanMember, Abort>(new KickClanMember { EntityId = dave.EntityId }).Text.Length > 0, "membro não pode expulsar outro");
            a.Request<SetMemberRoleInfo, OK>(new SetMemberRoleInfo { RoleId = 3, Info = new MemberRole { Id = 3, Name = "Recrutador", Permissions = Permissions.ApproveMember } });
            a.Request<SetClanMemberRole, OK>(new SetClanMemberRole { TargetId = bob.EntityId, RoleId = 3 });
            Check(ClanStore.HasPermission(ClanStore.Find(clanId), bob.EntityId, Permissions.ApproveMember) && !ClanStore.CanManageEstate(bob.EntityId, clanId), "cargo personalizado aplica apenas permissões concedidas");
            Check(!ClanStore.SetRoleInfo(bob.EntityId, 4, new MemberRole { Name = "Intruso" }, out _), "cargo não pode ampliar suas próprias permissões");
            Check(!ClanStore.SetRoleInfo(alice.EntityId, 0, new MemberRole { Name = "Mudança" }, out _), "cargo raiz protegido");
            Check(ClanStore.SetRoleGrades(alice.EntityId, new[] { new RoleOrder { RoleId=0, Grade=0 }, new RoleOrder { RoleId=1, Grade=1 }, new RoleOrder { RoleId=3, Grade=2 }, new RoleOrder { RoleId=2, Grade=3 } }, out _), "ordenação de cargos completa");
            Check(ClanStore.SetRoleInfo(alice.EntityId,3,new MemberRole { Name="Recrutador", Permissions=Permissions.ApproveMember|Permissions.PromoteMember },out _)
                && ClanStore.TrySetMemberRole(bob.EntityId,dave.EntityId,2,out _,out _), "permissão delegada permite administrar cargos inferiores");
            Check(!ClanStore.TrySetMemberRole(bob.EntityId,dave.EntityId,0,out _,out _) && !ClanStore.TrySetMemberRole(bob.EntityId,alice.EntityId,2,out _,out _), "hierarquia impede tomada de liderança e rebaixamento do líder");
            Check(!ClanStore.SetRoleGrades(alice.EntityId, new[] { new RoleOrder { RoleId=0, Grade=1 } }, out _), "ordenação parcial e líder rebaixado recusados");
            a.Request<RemoveMemberRole, OK>(new RemoveMemberRole { RoleId = 3, MoveToRoleId = 2 });
            Check(ClanStore.Find(clanId).Members[bob.EntityId].RoleId == 2, "exclusão de cargo migra integrantes");
            Check(!ClanStore.TrySetMemberRole(alice.EntityId, bob.EntityId, 999, out _, out _), "cargo desconhecido recusado");
            long bobBalance = bob.TStone;
            var fund = b.Request<DonateToClanFund, Costs>(new DonateToClanFund { Costs = new() { [Currency.TStone] = 300000 } });
            Check(fund._Costs[Currency.TStone] == 300000 && bob.TStone == bobBalance - 300000, "doação debita carteira e credita fundo");
            b.Request<DonateToClanFund, Abort>(new DonateToClanFund { Costs = new() { [Currency.TStone] = -1 } });
            Check(ClanStore.Find(clanId).Fund == 300000 && bob.TStone == bobBalance - 300000, "doação negativa não altera saldos");
            b.Request<DonateToClanFund, Abort>(new DonateToClanFund { Costs = new() { [Currency.Gem] = 5 } });
            Check(ClanStore.Find(clanId).Fund == 300000, "doação em moeda diferente recusada");
            a.Request<SuggestAlly, OK>(new SuggestAlly { ClanId = otherClanId });
            Check(c.Request<GetAllySlots, AllySlots>(default).Slots.Single().State == AllySlotState.BeenSuggested, "aliança registra proposta bilateral");
            c.Request<AcceptSuggestion, OK>(new AcceptSuggestion { ClanId = clanId });
            Check(a.Request<GetAllySlots, AllySlots>(default).Slots.Single().IsAlly, "aceite consolida aliança");
            a.Request<SuggestBreak, OK>(new SuggestBreak { ClanId = otherClanId }); c.Request<RefuseSuggestion, OK>(new RefuseSuggestion { ClanId = clanId });
            Check(ClanStore.AllySlots(alice.EntityId).Single().IsAlly, "recusa de encerramento mantém aliança");
            a.Request<BreakAlly, OK>(new BreakAlly { ClanId = otherClanId });
            Check(ClanStore.AllySlots(carol.EntityId).Single().State == AllySlotState.Locked, "ruptura unilateral aplica bloqueio bilateral");

            string clanRegion = WorldRegistry.ClanRegionIdFor(clanId);
            var registry = new WorldRegistry("social-check", world, clanRegion) { Economy = economy };
            registry.RegisterClanRegion(clanId); alice.RegionId = clanRegion; dave.RegionId = clanRegion;
            Point2 cell = World.CellFromTile(world.EntryPoint);
            a.Request<DeclareEstate, Abort>(new DeclareEstate { OwnerType = OwnerType.ClanEstate, Cell = cell });
            Check(!world.EnumerateEstates().Any(), "enclave exige nível original 5");
            Check(ClanStore.AddExperience(alice.EntityId, 968964), "XP compartilhada desbloqueia nível 5");
            Check(ClanStore.Find(clanId).Level == 5 && ClanRules.Reward(5,"capacity") == 20 && ClanRules.AllyCapacity(5) == 3, "progressão libera membros, alianças e território");
            Check(ClanRules.Reward(5, "max_estate_number") == 12 && ClanRules.Reward(25, "max_estate_number") == 72,
                "enclave preserva limites originais por nivel: 12 no nivel 5 e 72 no nivel 25");
            Check(a.Request<LeaveClan, Abort>(default).Text.Length > 0 && ClanStore.ClanIdOf(alice.EntityId) == clanId, "líder precisa transferir liderança antes de sair");
            var license = a.Request<DeclareEstate, EstateLicense>(new DeclareEstate { OwnerType = OwnerType.ClanEstate, Cell = cell });
            Check(license.OwnerId == clanId && license.Size == 1, "enclave pertence ao clã");
            Check(d.Request<GetEstateLicenses, EstateLicenses>(default).ClanEstate?.EstateId == license.EstateId, "membro recebe licença compartilhada");
            var record = world.GetEstate(license.EstateId);
            Check(record.AllowsClan(dave.EntityId, Shared.Estate.AccessRights.Give | Shared.Estate.AccessRights.Take)
                && !record.AllowsClan(dave.EntityId, Shared.Estate.AccessRights.Destruct), "direitos padrão permitem depósitos e protegem desmontagem");
            var next = new Point2(cell.x+1,cell.y);
            d.Request<ExpandEstate, Abort>(new ExpandEstate { EstateId = license.EstateId, Cell = next });
            Check(world.GetEstate(license.EstateId).Size == 1, "membro comum não expande enclave");
            long beforeFund = ClanStore.Find(clanId).Fund, personalBalance = alice.TStone;
            a.Request<ExpandEstate, EstateLicense>(new ExpandEstate { EstateId = license.EstateId, Cell = next });
            Check(world.GetEstate(license.EstateId).Size == 2 && ClanStore.Find(clanId).Fund == beforeFund && alice.TStone == personalBalance, "expansão gratuita preserva fundo do clã e carteira pessoal");
            var rights = record.ToAccessRights(); rights.ForClanMembers[2] = Shared.Estate.AccessRights.Enter | Shared.Estate.AccessRights.Give;
            a.Request<SetEstateLicense, OK>(new SetEstateLicense { EstateId = license.EstateId, AccessRights = rights });
            Check(!world.GetEstate(license.EstateId).AllowsClan(dave.EntityId, Shared.Estate.AccessRights.Take), "permissões por cargo restringem retirada");
            a.Request<ShrinkEstate, EstateLicense>(new ShrinkEstate { EstateId = license.EstateId, Cell = next });
            long afterShrinkFund = ClanStore.Find(clanId).Fund;
            a.Request<ExpandEstate, EstateLicense>(new ExpandEstate { EstateId = license.EstateId, Cell = next });
            Check(ClanStore.Find(clanId).Fund == afterShrinkFund, "reexpansão até maior área já paga não cobra novamente");
            a.Request<ExpandEstate, Abort>(new ExpandEstate { EstateId = license.EstateId, Cell = new Point2(65535,65535) });
            Check(world.GetEstate(license.EstateId).Size == 2, "expansão fora da ilha recusada");
            Check(ClanStore.AddExperience(alice.EntityId, 100000000), "progressão até laboratórios de coleta e combate");
            var labBlueprint = BlueprintStore.GetBlueprint("clan_collect_lab");
            Check(labBlueprint != null && a.Player.UnlockedBlueprintIds().Contains("clan_collect_lab"), "blueprint do laboratório vem da progressão do clã");
            var lab = new AppearArtifact { EntityId = "clan-lab", EntityType = (ushort)labBlueprint.EntityType, IsAlive = true,
                Tile = world.EntryPoint, Size = new Point2(1,1), States = new ArtifactState { BuildingState = Shared.Building.BuildingState.Completed } };
            world.ArtifactManager.AddArtifact(lab); world.ArtifactManager.SetOwner(lab.EntityId, alice.EntityId);
            var artifactAccess = new ArtifactAccess { Friends = new(), ClanMembers = new() { [0] = true, [1] = true, [2] = false },
                InventoryAccess = new InventoryAccess { Friends = new(), ClanMembers = new() { [0]=-1, [1]=-1, [2]=1 }, TakenCounts = new() } };
            a.Request<SetArtifactAccess, OK>(new SetArtifactAccess { EntityId=lab.EntityId, Tile=lab.Tile, Access=artifactAccess });
            Check(!(bool)Call(d.Player,"CanUseArtifactInCurrentSettlement",world.ArtifactManager.Get(lab.EntityId).Value,alice.EntityId,Shared.Estate.AccessRights.Give), "permissão individual da estrutura restringe membro");
            artifactAccess.ClanMembers[2] = true;
            a.Request<SetArtifactAccess, OK>(new SetArtifactAccess { EntityId=lab.EntityId, Tile=lab.Tile, Access=artifactAccess });
            Check((bool)Call(d.Player,"CanUseArtifactInCurrentSettlement",world.ArtifactManager.Get(lab.EntityId).Value,alice.EntityId,Shared.Estate.AccessRights.Give), "liberação individual restaura uso da estrutura");
            var quotaArgs = new object[] { lab.EntityId, 1, null };
            Check((bool)Call(d.Player,"CanWithdrawArtifact",quotaArgs), "quota diária permite primeira retirada");
            Call(d.Player,"RecordArtifactWithdrawal",lab.EntityId,1);
            Check(!(bool)Call(d.Player,"CanWithdrawArtifact",quotaArgs), "quota diária bloqueia retirada excedente");
            Check(a.Request<GetAvailableClanResearch, AvailableClanResearch>(new GetAvailableClanResearch { EntityId = lab.EntityId, Tile = lab.Tile }).AvailableResearchIds.Contains("plant"), "laboratório oferece pesquisas da categoria correta");
            long researchFund = ClanStore.Find(clanId).Fund;
            var started = a.Request<StartClanResearch, Messages.ClanResearch>(new StartClanResearch { EntityId = lab.EntityId, Tile = lab.Tile, Id = "plant" });
            Check(started.Until > Times.UnixTimeNow() && ClanStore.Find(clanId).Fund == researchFund - 80000, "pesquisa debita custo original e registra prazo");
            a.Request<StartClanResearch, Abort>(new StartClanResearch { EntityId = lab.EntityId, Tile = lab.Tile, Id = "mine" });
            Check(ClanStore.Find(clanId).Fund == researchFund - 80000, "laboratório ocupado não inicia segunda pesquisa");
            Check(a.Request<GetClanResearch, ClanResearchList>(default).ResearchList.Single().ResearchId == "plant", "pesquisa ativa acessível no protocolo");
            Call(d.Player, "SyncClanBenefits", true);
            d.PumpUntil(() => d.Messages.OfType<StatusEffects>().Any(s => s._StatusEffects.Any(e => e.EffectId == "clan_gathering_plant")));
            Check(d.Messages.OfType<StatusEffects>().Last()._StatusEffects.Any(e => e.EffectId == "clan_gathering_plant"), "pesquisa ativa aplica efeito nativo aos membros");
            Check((float)Call(d.Player,"ClanModifier","gathering_plus") == 6f && d.Player.GatherDurationScale() < 1f, "pesquisa melhora coleta também no servidor");
            var battleLab = lab;
            battleLab.EntityId = "battle-lab";
            battleLab.EntityType = (ushort)BlueprintStore.GetBlueprint("clan_battle_lab").EntityType;
            world.ArtifactManager.AddArtifact(battleLab); world.ArtifactManager.SetOwner(battleLab.EntityId, alice.EntityId);
            var daveSurvival = (SurvivalState)typeof(Player).GetField("_survival", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(d.Player);
            float previousHealth = daveSurvival.MaxOf(SurvivalState.KeyHealth,Gauge.CurrentTime);
            a.Request<StartClanResearch, Messages.ClanResearch>(new StartClanResearch { EntityId=battleLab.EntityId,Tile=battleLab.Tile,Id="recovery" });
            Check(daveSurvival.MaxOf(SurvivalState.KeyHealth,Gauge.CurrentTime) == previousHealth + 100
                && daveSurvival.MomentumSources().Contains("clan_recovery"), "pesquisa de recuperação aumenta saúde máxima e regenera vida");
            a.Request<KickClanMember, OK>(new KickClanMember { EntityId = dave.EntityId });
            Check(ClanStore.ClanIdOf(dave.EntityId) == null && !world.GetEstate(license.EstateId).AllowsClan(dave.EntityId, Shared.Estate.AccessRights.Give), "expulsão remove direitos do enclave imediatamente");
            Check(dave.RegionId == WorldRegistry.DefaultSharedTamedRegionId, "expulsão exige retorno à ilha pública");
            Check(daveSurvival.MaxOf(SurvivalState.KeyHealth,Gauge.CurrentTime) == previousHealth
                && !daveSurvival.MomentumSources().Contains("clan_recovery"), "expulsão remove bônus de saúde e recuperação");

            Check(SafeSave.FlushPending(), "snapshots concluídos");
            var oldWorld = new WorldContext { TerrainId = "pe10gr_1" }; oldWorld.Initialize(Path.Combine(root,"stale.world"));
            var restoredWorld = new World(oldWorld); economy.RecoverClanWorld(restoredWorld, clanRegion);
            Check(restoredWorld.GetEstate(license.EstateId).Size == 2 && !restoredWorld.GetEstate(license.EstateId).AllowsClan(bob.EntityId, Shared.Estate.AccessRights.Take), "journal recupera área e permissões com save da ilha antigo");
            economy.RecoverClanWorld(restoredWorld, clanRegion);
            Check(restoredWorld.GetEstate(license.EstateId).Size == 2, "replay da ilha é idempotente");
            var reloadedEconomy = new EconomyStore(Path.Combine(root,"economy.json"), new ShopCatalog());
            var staleBob = Context(root,"bob",otherWorld); reloadedEconomy.Recover(staleBob);
            Check(staleBob.TStone == bobBalance - 300000 && ClanStore.Find(clanId).Fund == researchFund - 160000, "journal econômico recupera débito sem duplicar fundo");
            var diskClan = Json.Read<ClanStoreState>(File.ReadAllText(Path.Combine(root,"clans.json")));
            Check(diskClan.Clans[clanId].Fund == ClanStore.Find(clanId).Fund && diskClan.Clans[clanId].Researches.ContainsKey("plant"), "fundo, pesquisas e alianças persistidos");
            Check(File.Exists(Path.Combine(root,"parties.json")) && reloadedEconomy != null, "equipes e transações persistidas");
            string blocked = Path.Combine(root, "blocked-journal"); Directory.CreateDirectory(blocked);
            var brokenEconomy = new EconomyStore(blocked, new ShopCatalog());
            long intactFund = ClanStore.Find(clanId).Fund, intactWallet = bob.TStone;
            Check(!brokenEconomy.ClanTransaction(bob,"donate",null,100,null,out _,out _)
                && ClanStore.Find(clanId).Fund == intactFund && bob.TStone == intactWallet, "falha de gravação não debita carteira nem credita fundo");
            var partyPath = typeof(PartyStore).GetField("_path", BindingFlags.Static | BindingFlags.NonPublic);
            string originalPartyPath = (string)partyPath.GetValue(null);
            partyPath.SetValue(null,blocked);
            try { Check(!PartyStore.Change("failed-party","make",null,out _,out _) && PartyStore.Snapshot("failed-party") == null, "falha de gravação não cria equipe em memória"); }
            finally { partyPath.SetValue(null,originalPartyPath); }
            Console.WriteLine($"[social-check] PASS {_checks} verificações"); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("[social-check] FAIL " + e); return 1; }
        finally { PartyStore.Clock = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds(); SafeSave.FlushPending(); Directory.Delete(root,true); }
    }
}
