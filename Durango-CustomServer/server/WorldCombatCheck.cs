using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Durango.Online;
using Durango.Utils;
using Messages;
using Newtonsoft.Json.Linq;
using Shared.Ability;
using Shared.Animal;
using Shared.Battle;
using Shared.Pet;
using Shared.Region;
using Yaml;
using Yaml.Util;
using PetFactory = Durango.Online.Player.PetFactory;
using PetStore = Durango.Online.Player.PetStore;

namespace DurangoServerNx;

internal static class WorldCombatCheck
{
    private static int _passed;
    private static void Check(bool value, string text)
    { if (!value) throw new InvalidOperationException(text); _passed++; Console.WriteLine("[world-combat] OK " + text); }
    private static object Call(object instance, string method, params object[] args) => instance.GetType()
        .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(instance, args);
    private static T Field<T>(object instance, string name) => (T)instance.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).GetValue(instance);
    private static void Position(PlayerContext context, WorldPosition position, double at) =>
        context.AppearPlayer.Move.Movements = new[] { new Movement { PlaybackRate = 1,
            Path = new[] { new Location { Position = position, Time = at } } } };
    private static PlayerContext Context(string root, string id, World world)
    {
        var context = new PlayerContext { PlayerInfo = new Durango.Logic.Clusters.PlayerInfo {
            PlayerEntityId = id, PlayerName = id, PlayerLevel = 60 } };
        context.Initialize(Path.Combine(root, id + ".player")); context.AppearPlayer.IsAlive = true;
        Position(context, new WorldPosition(world.EntryPoint.x * 200, world.EntryPoint.y * 200), Gauge.CurrentTime);
        return context;
    }
    private static World World(string root, string terrain, string file, out WorldContext context)
    {
        context = new WorldContext { TerrainId = terrain }; context.Initialize(Path.Combine(root, file + ".world"));
        return new World(context);
    }
    private static double[] Due(Player player) => Field<IList>(player, "_pendingBattleHits").Cast<object>()
        .Select(p => (double)p.GetType().GetProperty("DueAt").GetValue(p)).ToArray();

    public static int Run(string dataDir)
    {
        string root = Path.Combine(Path.GetTempPath(), "Durango-world-combat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string prior = AppData.BasePath;
        try
        {
            AppData.BasePath = root;
            Json.DataDir = dataDir; MoCatalog.Load(dataDir); DataStore.Load(dataDir);
            WorkbenchTags.AssetsDir = Path.Combine(dataDir, "assets");
            TerrainLoader.TerrainDir = Path.Combine(dataDir, "terrains"); RegionCatalog.Load(WorkbenchTags.AssetsDir);
            var templates = Json.ReadFromFile<JObject>("region_templates");
            Check(templates.Properties().All(p => RegionCatalog.GetTemplate(p.Name).AllowsPvp ==
                ((int?)p.Value["role"] == (int)Role.Outpost || (int?)p.Value["role"] == (int)Role.Instance &&
                    p.Value["tags"]?.Values<string>().Contains("pvpisland") == true)),
                "todas as regras de ilha correspondem a Outpost ou Instance com tag pvpisland do cliente original");
            Check(RegionCatalog.GetTemplate("testpvpisland190919").AllowsPvp &&
                !new RegionCatalog.TemplateInfo { Role = Role.Risky, Tags = new() { "pvpisland" } }.AllowsPvp,
                "PvP de evento exige papel Instance; tag sozinha nao libera ilhas instaveis");
            var world = World(root, "op60te_alpha", "outpost", out _);
            Check(RegionCatalog.GetTemplate(world.TerrainInfo.region_template).AllowsPvp, "posto avancado disponivel habilita PvP");
            var safe = World(root, "pe10gr_1", "safe", out _);
            var ac = Context(root, "attacker", world); var bc = Context(root, "victim", world);
            var sc = Context(root, "safe-player", safe);
            var sc2 = Context(root, "safe-other", safe);
            using var a = new EconomyProtocolCheck.Link(ac, world, null);
            using var b = new EconomyProtocolCheck.Link(bc, world, null);
            using var s = new EconomyProtocolCheck.Link(sc, safe, null);
            using var s2 = new EconomyProtocolCheck.Link(sc2, safe, null);
            world.AddPlayer(a.Player); world.AddPlayer(b.Player); safe.AddPlayer(s.Player); safe.AddPlayer(s2.Player);
            Check(Call(a.Player, "TryResolveVictim", bc.EntityId) == b.Player &&
                Call(a.Player, "TryResolveVictim", ac.EntityId) == null && Call(a.Player, "TryResolveVictim", sc.EntityId) == null,
                "somente outro jogador vivo da mesma ilha PvP e alvo valido");
            var menu = a.Request<Touch, Touched>(new Touch { EntityId = bc.EntityId, EntityType = 1 });
            Check(menu.Interactions.Contains((int)Shared.System.Interaction.Attack), "TCP oferece interacao de ataque ao jogador hostil");
            var originalTemplate = safe.TerrainInfo.region_template;
            foreach (int role in new[] { 1, 3, 4, 6, 7, 9 })
            {
                string template = templates.Properties().First(p => (int?)p.Value["role"] == role).Name;
                safe.TerrainInfo.region_template = template;
                Check(Call(s.Player, "TryResolveVictim", sc2.EntityId) == null, "papel " + (Role)role + " mantem PvP bloqueado");
            }
            safe.TerrainInfo.region_template = originalTemplate;
            float meleeScale = BattleDataStore.PvpDamageScale(new() { damage_type = DamageType.Melee });
            float rangedScale = BattleDataStore.PvpDamageScale(new() { damage_type = DamageType.Ranged });
            Check(Math.Abs(meleeScale - .12f) < .0001f && Math.Abs(rangedScale - .0525f) < .0001f,
                "multiplicadores PvP sao carregados do constants original para melee e ranged");

            var action = BattleDataStore.Action("onehand_flurry");
            var survival = Field<SurvivalState>(b.Player, "_survival");
            double now = Gauge.CurrentTime;
            float before = survival.ValueAt(SurvivalState.KeyLife, now);
            Call(a.Player, "QueueBattleHits", action, bc.EntityId, now);
            var due = Due(a.Player);
            Check(due.Length == 3 && survival.ValueAt(SurvivalState.KeyLife, now) == before, "combo PvP agenda tres golpes sem dano instantaneo");
            Call(a.Player, "UpdatePendingBattleHits", due[0] - .001);
            Check(survival.ValueAt(SurvivalState.KeyLife, now) == before, "PvP aguarda o primeiro impacto");
            Call(a.Player, "UpdatePendingBattleHits", due[0] + .001);
            b.PumpUntil(() => b.Messages.OfType<Damaged>().Any(m => m.VictimId == bc.EntityId));
            var damage = b.Messages.OfType<Damaged>().Last(m => m.VictimId == bc.EntityId);
            Check(damage.Damage.Value > 0 && Math.Abs(damage.EventAt - due[0]) < .01,
                "TCP publica dano PvP e timestamp do impacto");
            Position(bc, new WorldPosition(90000, 90000), due[1]);
            before = survival.ValueAt(SurvivalState.KeyLife, now);
            Call(a.Player, "UpdatePendingBattleHits", due[1] + .001);
            Check(survival.ValueAt(SurvivalState.KeyLife, now) == before, "segundo golpe PvP erra jogador fora de alcance");
            Position(bc, ac.AppearPlayer.Move.Movements[0].Path[0].Position, due[2]);
            Check(PartyStore.Change(ac.EntityId, "make", null, out _, out _) &&
                PartyStore.Change(ac.EntityId, "invite", bc.EntityId, out _, out _) &&
                PartyStore.Change(bc.EntityId, "join", null, out _, out _), "grupo confirmado preparado durante o combo");
            Call(a.Player, "UpdatePendingBattleHits", due[2] + .001);
            Check(Call(a.Player, "TryResolveVictim", bc.EntityId) == null && survival.ValueAt(SurvivalState.KeyLife, now) == before,
                "confirmar grupo durante combo bloqueia o impacto pendente");
            PartyStore.Change(bc.EntityId, "leave", null, out _, out _);
            PartyStore.Change(ac.EntityId, "leave", null, out _, out _);
            Call(a.Player, "CancelBattleHits");
            string basicId = BattleDataStore.ActionsOfTag("bare_hands").default_actions.First(id => BattleDataStore.Action(id).attack_info?.Length > 0);
            a.Send(new SelectBattleTarget { EntityId = bc.EntityId });
            a.Send(new UseBattleAction { ActionId = basicId, StartAt = double.MaxValue });
            a.Request<GetActions, Actions>(default);
            due = Due(a.Player);
            Check(due.Length == 1 && Field<string>(a.Player, "_petEnemyId") == bc.EntityId,
                "TCP ataque basico usa alvo selecionado e compartilha o inimigo com o pet");
            before = survival.ValueAt(SurvivalState.KeyLife, now);
            Call(a.Player, "UpdatePendingBattleHits", due[0] + .001);
            Check(survival.ValueAt(SurvivalState.KeyLife, now) < before, "TCP ataque basico PvP aplica dano no impacto");
            double defenseAt = now + 50;
            Call(b.Player, "BeginDefenseAction", new BattleActionData { meta = new(),
                defense_info = new() { active_time = 2, dodge_force = 1 } }, defenseAt);
            Call(a.Player, "QueueBattleHits", BattleDataStore.Action(basicId), bc.EntityId, defenseAt);
            due = Due(a.Player); before = survival.ValueAt(SurvivalState.KeyLife, now);
            Call(a.Player, "UpdatePendingBattleHits", due[0] + .001);
            Check(survival.ValueAt(SurvivalState.KeyLife, now) == before, "PvP verifica esquiva na janela do impacto");
            Call(b.Player, "ClearDefenseAction");

            // Exercise restoration of the actual saved landmark and investment protocol.
            var craterWorld = World(root, "grass_company_safehouse_01", "craters", out var wc);
            var crater = craterWorld.ArtifactManager.Enumerable(c => c.EntityType == 7037).First();
            var broken = crater; broken.States.Crack = null; wc.Artifacts[crater.EntityId] = broken;
            var repaired = new World(wc);
            Check(repaired.ArtifactManager.Get(crater.EntityId).Value.States.Crack?.RequiredInvestment > 0,
                "save antigo com cratera sem Crack recupera custo e estado de inducao");
            var cc = Context(root, "crater-owner", repaired); Position(cc, new WorldPosition(crater.Tile.x * 200, crater.Tile.y * 200), now);
            using var c = new EconomyProtocolCheck.Link(cc, repaired, null); repaired.AddPlayer(c.Player);
            Position(cc, new WorldPosition(crater.Tile.x * 200, crater.Tile.y * 200), Gauge.CurrentTime);
            var touch = c.Request<Touch, Touched>(new Touch { EntityId = crater.EntityId, EntityType = 7037, Tile = crater.Tile });
            Check(touch.Interactions.Contains((int)Shared.System.Interaction.Invest), "cratera reparada oferece investimento no TCP");
            c.Player.AddInductionStones(20);
            int cost = repaired.ArtifactManager.Get(crater.EntityId).Value.States.Crack.Value.RequiredInvestment;
            var timer = c.Request<InvestToCrack, Messages.Timer>(new InvestToCrack { EntityId = crater.EntityId, Tile = crater.Tile, Amount = cost });
            Check(c.Player.InductionStones == 20 && timer.Duration == 4, "investimento aguarda quatro segundos sem debitar antecipadamente");
            Call(c.Player, "UpdateCraterInvestment", Gauge.CurrentTime + timer.Duration + .1);
            Check(c.Player.InductionStones == 20 - cost && wc.CraterResources[crater.EntityId].Count > 0, "inducao gera recursos e cobra uma vez");
            var active = wc.Artifacts[crater.EntityId].States.Crack.Value;
            var restart = new World(wc);
            Check(wc.Artifacts[crater.EntityId].States.Crack.Value.ActivatedUntil == active.ActivatedUntil &&
                wc.CraterResources[crater.EntityId].Count > 0, "restauracao preserva cratera ativa e recursos");
            Check(!restart.ArtifactManager.RestoreCrack(crater.EntityId, default), "migracao nao sobrescreve Crack existente");
            var spots = wc.CraterResources[crater.EntityId].ToArray();
            Call(restart, "ProcessCraterStates", active.ActivatedUntil.Value + .01);
            Check(!wc.CraterResources.ContainsKey(crater.EntityId) && spots.All(n => restart.NaturalTypeAt(new Point2(n.X, n.Y)) == 0),
                "expiracao remove somente recursos temporarios da cratera");
            Check(restart.ActivateCrater(crater.EntityId, active.ActivatedUntil.Value + 1), "cratera pode ser reativada");
            var expired = wc.Artifacts[crater.EntityId];
            var expiredCrack = expired.States.Crack.Value;
            expiredCrack.ActivatedSince = Gauge.CurrentTime - 601; expiredCrack.ActivatedUntil = Gauge.CurrentTime - 1;
            expired.States.Crack = expiredCrack; wc.Artifacts[crater.EntityId] = expired;
            _ = new World(wc);
            Check(!wc.CraterResources.ContainsKey(crater.EntityId) && !wc.Artifacts[crater.EntityId].States.Crack.Value.ActivatedUntil.HasValue,
                "carregar save ja expira cratera vencida enquanto servidor estava desligado");

            // Pet damage is attributed to the pet and uses its own derived statistics.
            var petCatalog = Json.ReadFromFile<JObject>("pet/pets_for_client");
            var fightablePets = petCatalog.Properties().Where(p => (bool?)p.Value["is_fightable"] == true).ToArray();
            Check(fightablePets.Length >= 60 && fightablePets.All(p => {
                var model = (ushort)(int)p.Value["vehicle_entity_type"];
                var motions = AnimalMotions.Of(model);
                var built = PetFactory.Build(ushort.Parse(p.Name), PetRank.D, 60, ac.EntityId);
                return AnimalTypes.Get(model) != null && !string.IsNullOrEmpty(motions?.Move) &&
                    motions.Attacks.Length > 0 && built?.Statistics.DerivedAbilities.GetValueOrDefault(Derived.Attack) > 0;
            }), "catalogo dos 64 pets de combate tem ataque calculado, deslocamento e clipes nativos");
            Position(ac, new WorldPosition(world.EntryPoint.x * 200, world.EntryPoint.y * 200), now);
            var origin = ac.AppearPlayer.Move.Movements[0].Path[0].Position;
            var pet = PetFactory.Build(3001, PetRank.D, 60, ac.EntityId).Value;
            var entry = new PetStore.Entry { Pet = pet };
            typeof(Player).GetMethod("RecalcPetStats", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { entry });
            PetStore.Of(ac.EntityId).Add(entry);
            entry.Pet.Stat.Hungry = new Gauge(entry.HungryMax, 0, new[] { new GaugeNode(now, entry.HungryMax) });
            a.Request<SpawnPet, OK>(new SpawnPet { PetId = entry.Pet.EntityId });
            var animal = world.AnimalManager.SpawnAt(2006, 60, world.EntryPoint);
            animal.Life = animal.LifeMax = 10000; animal.Position = origin; animal.Defense = 300;
            Call(a.Player, "SetBattleMode", true, animal.EntityId);
            Call(a.Player, "UpdatePetCombat", now);
            var battle = Field<object>(a.Player, "_petBattle");
            Check(battle != null, "pet convocado e alimentado acompanha dono em combate");
            double impact = Field<double>(battle, "HitAt");
            Check(impact > now && animal.Life == 10000, "pet anima antes de aplicar dano");
            Call(a.Player, "UpdatePetCombat", impact - .001);
            Check(animal.Life == 10000, "pet aguarda seu tempo de impacto");
            Call(a.Player, "UpdatePetCombat", impact + .001);
            a.PumpUntil(() => a.Messages.OfType<Damaged>().Any(d => d.AttackerId == entry.Pet.EntityId));
            var petHit = a.Messages.OfType<Damaged>().Last(d => d.AttackerId == entry.Pet.EntityId);
            Check(animal.Life == 10000 - petHit.Damage.Value && petHit.Damage.Value ==
                CombatDamage.Calculate(entry.Pet.Statistics.DerivedAbilities[Derived.Attack],
                    new() { damage_bonus = 1, atk_ratio = new() { ["impact"] = 1 } }, animal.Defense,
                    AnimalTypes.Get(animal.EntityType).BodyDefenseRatios), "pet usa ataque proprio e publica sua identidade como atacante");
            Check(animal.AggroTargetId == ac.EntityId, "dino atacado pelo pet entra em combate com o dono");
            before = animal.Life; Call(a.Player, "UpdatePetCombat", impact + .01);
            Check(animal.Life == before, "impacto do pet nao se duplica");

            double next = now + 10;
            Call(a.Player, "UpdatePetCombat", next);
            battle = Field<object>(a.Player, "_petBattle"); impact = Field<double>(battle, "HitAt");
            animal.Position = new WorldPosition(origin.x + 1000, origin.y);
            before = animal.Life; Call(a.Player, "UpdatePetCombat", impact + .001);
            Check(animal.Life == before, "pet erra quando o dino sai do alcance durante a animacao");
            var body = Field<AnimalManager.Animal>(battle, "Body");
            Call(a.Player, "UpdatePetCombat", next + 2);
            Check(body.WalkEndAt > next + 2, "pet persegue alvo distante com movimento do servidor");
            animal.Position = new WorldPosition(origin.x + 5000, origin.y);
            Call(a.Player, "UpdatePetCombat", next + 3);
            Check(Field<object>(a.Player, "_petBattle") == null, "pet abandona alvo fora da distancia do dono");

            animal.Position = origin;
            entry.Pet.Stat.Hungry = new Gauge(entry.HungryMax, 0, new[] { new GaugeNode(now, entry.HungryMax * .4f) });
            Call(a.Player, "UpdatePetCombat", next + 4);
            Check(Field<object>(a.Player, "_petBattle") == null, "pet abaixo de 50 por cento de comida nao inicia combate");
            entry.Pet.Stat.Hungry = new Gauge(entry.HungryMax, 0, new[] { new GaugeNode(now, entry.HungryMax) });
            Call(a.Player, "UpdatePetCombat", next + 5);
            entry.Pet.Stat.Hungry = new Gauge(entry.HungryMax, 0, new[] { new GaugeNode(now, 0) });
            Call(a.Player, "UpdatePetCombat", next + 6);
            Check(Field<object>(a.Player, "_petBattle") == null, "pet sem comida abandona combate");
            entry.Pet.Stat.Hungry = new Gauge(entry.HungryMax, 0, new[] { new GaugeNode(now, entry.HungryMax) });
            entry.Pet.IsBoarding = true; Call(a.Player, "UpdatePetCombat", next + 7);
            Check(Field<object>(a.Player, "_petBattle") == null, "pet montado nao ataca de forma independente");
            entry.Pet.IsBoarding = false; Call(a.Player, "UpdatePetCombat", next + 8);
            Call(a.Player, "SetBattleMode", false, null);
            before = animal.Life; Call(a.Player, "UpdatePetCombat", next + 10);
            Check(Field<object>(a.Player, "_petBattle") == null && animal.Life == before, "sair do combate cancela o golpe pendente do pet");
            Call(a.Player, "SetBattleMode", true, animal.EntityId);
            Call(a.Player, "UpdatePetCombat", next + 12);
            a.Request<ReturnPet, OK>(new ReturnPet { PetId = entry.Pet.EntityId });
            Check(Field<object>(a.Player, "_petBattle") == null && !entry.Pet.IsSpawned, "recolher pet encerra combate imediatamente");

            a.Request<SpawnPet, OK>(new SpawnPet { PetId = entry.Pet.EntityId });
            Position(bc, origin, now); bc.AppearPlayer.IsAlive = true;
            survival.Set(SurvivalState.KeyLife, 100);
            Call(a.Player, "SetBattleMode", true, bc.EntityId);
            Call(a.Player, "UpdatePetCombat", next + 20);
            battle = Field<object>(a.Player, "_petBattle"); impact = Field<double>(battle, "HitAt");
            before = survival.ValueAt(SurvivalState.KeyLife, now);
            Call(a.Player, "UpdatePetCombat", impact + .001);
            b.PumpUntil(() => b.Messages.OfType<Damaged>().Any(d => d.AttackerId == entry.Pet.EntityId));
            Check(survival.ValueAt(SurvivalState.KeyLife, now) < before &&
                b.Messages.OfType<Damaged>().Any(d => d.AttackerId == entry.Pet.EntityId && d.VictimId == bc.EntityId),
                "pet auxilia em PvP e identifica o dono para retaliacao");
            bool createdA = ClanStore.TryCreate(ac, "Combat One", out var clanA, out _);
            bool createdB = ClanStore.TryCreate(bc, "Combat Two", out var clanB, out _);
            Check(createdA && createdB, "clas preparados para verificar protecao");
            Check(ClanStore.ChangeAlly(ac.EntityId, clanB.Id, "suggest", out _) &&
                ClanStore.ChangeAlly(bc.EntityId, clanA.Id, "accept", out _), "alianca confirmada pelo fluxo real de clas");
            Call(a.Player, "UpdatePetCombat", next + 30);
            Check(Call(a.Player, "TryResolveVictim", bc.EntityId) == null && Field<object>(a.Player, "_petBattle") == null,
                "alianca bloqueia ataques do dono e encerra combate do pet");
            Check(ClanStore.TryApply(sc, clanA.Id, out _) && ClanStore.TryApprove(ac.EntityId, sc.EntityId, out _) &&
                ClanStore.CombatAllies(ac.EntityId, sc.EntityId), "membros do mesmo cla sao protegidos");
            Call(a.Player, "SetBattleMode", true, animal.EntityId); Call(a.Player, "UpdatePetCombat", next + 35);
            ac.AppearPlayer.IsAlive = false; Call(a.Player, "UpdatePetCombat", next + 36);
            Check(Field<object>(a.Player, "_petBattle") == null, "morte do dono cancela assistencia do pet");
            Check(a.Messages.OfType<BattleBegun>().Any(m => m.EntityId == entry.Pet.EntityId), "cliente recebe BattleBegun do pet");
            Console.WriteLine($"[world-combat] PASS {_passed} verificacoes. Saves temporarios: {root}");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("[world-combat] FAIL " + ex); return 1; }
        finally { SafeSave.FlushPending(); AppData.BasePath = prior; }
    }
}
