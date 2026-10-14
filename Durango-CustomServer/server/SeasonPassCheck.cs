using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Durango.Online;
using Durango.Utils;
using Newtonsoft.Json.Linq;
using Shared.Quest;
using Yaml.Util;

namespace DurangoServerNx;

internal static class SeasonPassCheck
{
    private static int _passed;
    private static void Check(bool condition, string text)
    {
        if (!condition) throw new InvalidOperationException(text);
        _passed++;
        Console.WriteLine("[season-pass-check] OK " + text);
    }

    private static int FreePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint).Port;
    }

    public static int Run(string dataDir)
    {
        string root = Path.Combine(Path.GetTempPath(), "LostHorizon-season-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string previousAppData = AppData.BasePath;
        Host host = null;
        try
        {
            AppData.BasePath = root;
            Json.DataDir = dataDir;
            MoCatalog.Load(dataDir);
            DataStore.Load(dataDir);
            TerrainLoader.TerrainDir = Path.Combine(dataDir, "terrains");
            WorkbenchTags.AssetsDir = Path.Combine(dataDir, "assets");
            RegionCatalog.Load(WorkbenchTags.AssetsDir);
            host = new Host("season-pass-check");
            host.Load();
            PlayerContext Alice = host.CreateTemporaryContext("season-alice", "Alice", 60);
            PlayerContext Bob = host.CreateTemporaryContext("season-bob", "Bob", 60);
            Alice.OwnerKey = "season-account-a";
            Bob.OwnerKey = "season-account-b";
            host.PersistAsSlot(Alice);
            host.PersistAsSlot(Bob);
            int gatewayPort = FreePort(), gamePort = FreePort();
            while (gamePort == gatewayPort || gamePort == gatewayPort + 1) gamePort = FreePort();
            host.Start(gamePort, gatewayPort, "127.0.0.1", null, WorkbenchTags.AssetsDir, dataDir);
            const string aliceToken = "season-check-session-a", bobToken = "season-check-session-b";
            host.GameServer.IssueSession(Alice.EntityId, aliceToken, Alice.OwnerKey);
            host.GameServer.IssueSession(Bob.EntityId, bobToken, Bob.OwnerKey);
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(15) };

            (int Code, JObject Body) Post(string path, string token, string tier = null)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{gatewayPort}" + path);
                var fields = new Dictionary<string, string> { ["lang"] = "pt" };
                if (tier != null) fields["tier"] = tier;
                request.Content = new FormUrlEncodedContent(fields);
                if (token != null) request.Headers.TryAddWithoutValidation("Authorization", token);
                var task = http.SendAsync(request);
                var deadline = DateTime.UtcNow.AddSeconds(15);
                while (!task.IsCompleted && DateTime.UtcNow < deadline)
                {
                    host.Process();
                    Thread.Sleep(1);
                }
                using var response = task.GetAwaiter().GetResult();
                return ((int)response.StatusCode, JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult()));
            }

            var unauthenticated = Post("/season/pass", null);
            Check(unauthenticated.Code == 401 && (string)unauthenticated.Body["reason"] == "unauthorized", "consulta exige sessão autenticada");
            var status = Post("/season/pass", aliceToken).Body;
            Check((bool)status["active"] && (int)status["maxTier"] == 15 && (int)status["pointsPerTier"] == 2000,
                "temporada ativa com 15 etapas de 2.000 pontos");
            Check((long)status["points"] == 100 && (bool)status["daily"]["missions"][0]["done"], "login concede 100 pontos uma vez por dia");
            Check((long)Post("/season/pass", aliceToken).Body["points"] == 100, "reabrir passe não repete pontuação de login");
            Check((string)Post("/season/claim", aliceToken, "1").Body["reason"] == "notreached", "resgate antecipado recusado");
            Check(Post("/season/claim", aliceToken, "-1").Code == 400 && Post("/season/claim", aliceToken, "16").Code == 400,
                "etapas fora do catálogo recusadas");

            using var link = new EconomyProtocolCheck.Link(Alice, host.GameServer.World, host.Economy, simulatePlayer: true, mailStore: host.Mail);
            link.Player.NoteQuestEvent(QuestEventType.Collected, amount: 50);
            link.Player.NoteQuestEvent(QuestEventType.Crafted, amount: 5);
            link.Player.NoteQuestEvent(QuestEventType.Hunted, amount: 10);
            link.Player.NoteQuestEvent(QuestEventType.AnimalTamed, amount: 1);
            typeof(Player).GetMethod("NoteSeasonAction", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(link.Player, new object[] { "harvest", 5 });
            Check(Alice.SeasonPass.Points == 1700, "ações reais completam diárias e doma semanal");
            link.Player.NoteQuestEvent(QuestEventType.Hunted, amount: 90);
            link.Player.NoteQuestEvent(QuestEventType.Collected, amount: 450);
            link.Player.NoteQuestEvent(QuestEventType.Crafted, amount: 35);
            Check(Alice.SeasonPass.Points == 4700, "missões semanais concedem pontos ao atingir a meta");
            link.Player.NoteQuestEvent(QuestEventType.Hunted, amount: 100);
            link.Player.NoteQuestEvent(QuestEventType.Collected, amount: 500);
            link.Player.NoteQuestEvent(QuestEventType.Crafted, amount: 40);
            Check(Alice.SeasonPass.Points == 4700, "missões concluídas não repetem pontos");
            status = Post("/season/pass", aliceToken).Body;
            Check((int)status["tier"] == 2 && (int)status["claimable"] == 2, "duas etapas liberadas após 4.700 pontos");

            int inventoryBefore = Alice.InventoryItems.Count;
            var claim = Post("/season/claim", aliceToken, "1").Body;
            var firstReward = (JObject)SeasonPassCatalog.Current["tiers"][0];
            var firstItems = new[] { "box", "special" }.SelectMany(track => firstReward[track]["items"]);
            Check((bool)claim["ok"] && claim["tiers"].ToObject<int[]>().SequenceEqual(new[] { 1 }) &&
                (int)claim["status"]["tiers"][0]["state"] == 2, "resgate individual marca a etapa como recebida");
            var granted = Alice.InventoryItems.Skip(inventoryBefore).ToArray();
            Check(firstItems.All(spec => granted.Count(item => item.Prototype == (string)spec["id"]) == (int)spec["count"]),
                "itens das duas linhas entregues nas quantidades do catálogo");
            PlayerContext disk = Json.Read<PlayerContext>(File.ReadAllText(Alice.Path));
            Check(disk.SeasonPass.Claimed.Contains(1) && disk.InventoryItems.Count == Alice.InventoryItems.Count,
                "save contém recompensa e marcação antes de confirmar o resgate");
            int afterClaim = Alice.InventoryItems.Count;
            Check((string)Post("/season/claim", aliceToken, "1").Body["reason"] == "taken" && Alice.InventoryItems.Count == afterClaim,
                "resgate repetido não duplica itens");
            var bobStatus = Post("/season/pass", bobToken).Body;
            Check((long)bobStatus["points"] == 100 && (int)bobStatus["claimable"] == 0 &&
                (string)Post("/season/claim", bobToken, "1").Body["reason"] == "notreached", "contas mantêm progresso e resgates separados");
            claim = Post("/season/claim", aliceToken, "0").Body;
            Check((bool)claim["ok"] && claim["tiers"].ToObject<int[]>().SequenceEqual(new[] { 2 }), "resgatar tudo concede apenas etapas pendentes");
            Check((string)Post("/season/claim", aliceToken, "0").Body["reason"] == "none", "resgate vazio não concede prêmios");

            Alice.SeasonPass.Points = 6000;
            string goodPath = Alice.Path;
            string blocked = Path.Combine(root, "blocked-player");
            Directory.CreateDirectory(blocked);
            typeof(PlayerContext).GetProperty(nameof(PlayerContext.Path)).SetValue(Alice, blocked);
            inventoryBefore = Alice.InventoryItems.Count;
            long tstoneBefore = Alice.TStone, gemsBefore = Alice.WarpGem;
            claim = Post("/season/claim", aliceToken, "3").Body;
            Check(!(bool)claim["ok"] && !Alice.SeasonPass.Claimed.Contains(3) && Alice.InventoryItems.Count == inventoryBefore &&
                Alice.TStone == tstoneBefore && Alice.WarpGem == gemsBefore, "falha no save desfaz itens, moedas e marcação da etapa");
            typeof(PlayerContext).GetProperty(nameof(PlayerContext.Path)).SetValue(Alice, goodPath);
            Alice.SeasonPass.Points = SeasonPassCatalog.MaxTier * SeasonPassCatalog.PointsPerTier;
            claim = Post("/season/claim", aliceToken, "0").Body;
            long expectedTstone = ((JArray)SeasonPassCatalog.Current["tiers"]).Sum(row => new[] { "box", "special" }.Sum(track => (long?)row[track]["tstone"] ?? 0));
            long expectedGems = ((JArray)SeasonPassCatalog.Current["tiers"]).Sum(row => new[] { "box", "special" }.Sum(track => (long?)row[track]["gems"] ?? 0));
            Check((bool)claim["ok"] && Alice.SeasonPass.Claimed.Count == 15 && Alice.TStone == expectedTstone && Alice.WarpGem == expectedGems,
                "passe completo concede T-Stone e Warp Gems reais sem repetir etapas anteriores");
            Check(Alice.InventoryItems.Count(item => item.Prototype == "hat_techwear") == 1 &&
                Alice.InventoryItems.Count(item => item.Prototype == "clothes_techwear") == 1 &&
                !Alice.InventoryItems.Any(item => item.Prototype == "capsulated_s02_car"), "chapéu tecnológico na etapa 8 e traje na 15 substituem o carro");
            Check(Alice.InventoryItems.Any(item => item.Prototype == "feed_store_task_attack_amplifier") &&
                Alice.InventoryItems.Any(item => item.Prototype == "feed_store_unlimited_vigor"), "rações da loja entregues como itens utilizáveis");
            disk = Json.Read<PlayerContext>(File.ReadAllText(Alice.Path));
            Check(disk.SeasonPass.Claimed.Count == 15 && disk.TStone == expectedTstone && disk.WarpGem == expectedGems,
                "serialização preserva todos os resgates e saldos ao reiniciar");
            int finalCount = Alice.InventoryItems.Count;
            Alice.SeasonPass = disk.SeasonPass;
            Check((string)Post("/season/claim", aliceToken, "15").Body["reason"] == "taken" && Alice.InventoryItems.Count == finalCount,
                "progresso recarregado continua impedindo resgate duplicado");

            var reset = new SeasonPassProgress();
            long now = SeasonPassCatalog.Since + 86400;
            SeasonPassCatalog.ApplyEvent(reset, "login", 1, now);
            reset.Claimed.Add(1);
            long dailyReset = reset.DailyReset;
            SeasonPassCatalog.ApplyEvent(reset, "login", 1, dailyReset);
            Check(reset.Points == 200 && reset.Claimed.Contains(1) && reset.Daily["d_login"] == 1,
                "virada diária renova missão sem apagar pontos ou resgates");
            SeasonPassCatalog.ApplyEvent(reset, "hunt", 100, dailyReset);
            long weeklyReset = reset.WeeklyReset;
            Check(reset.Weekly["w_hunt"] == 100, "semana mantém meta de caça até o reset");
            SeasonPassCatalog.Normalize(reset, weeklyReset);
            Check(reset.Weekly.Count == 0 && reset.Points > 200 && reset.Claimed.Contains(1), "virada semanal preserva progresso total");
            long points = reset.Points;
            Check(!SeasonPassCatalog.ApplyEvent(reset, "hunt", 100, SeasonPassCatalog.Until) && reset.Points == points,
                "temporada encerrada não concede novos pontos");
            Check(SafeSave.FlushPending(), "snapshots temporários concluídos");
            Console.WriteLine($"[season-pass-check] PASS {_passed} verificações");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            host?.Close();
            SafeSave.FlushPending();
            AppData.BasePath = previousAppData;
            if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                Directory.Delete(root, true);
        }
    }
}
