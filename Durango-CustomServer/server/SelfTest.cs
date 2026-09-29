using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Durango.Logic.Clusters;
using Durango.Network;
using Durango.Online;
using Durango.Utils;
using Messages;
using Newtonsoft.Json.Linq;

namespace DurangoServerNx;

// FACILDIGITAL_ETAPA01_ACCOUNT_CROSS_DEVICE
// Self-test da autenticação + conta + TCP.
// Verifica dois logins independentes da MESMA conta antes do handshake do jogo.
// Assim o teste detecta regressões em que personagens passam a depender do dispositivo/sessão.
internal static class SelfTest
{
    private const string SelfTestUsername = "facildigital_selftest";
    private const string SelfTestPassword = "Durango-SelfTest-2026";

    public static int Run(int gatewayPort, int gamePort)
    {
        try
        {
            EnsureTestAccount(gatewayPort);

            LoginInfo loginA = LoginTestAccount(gatewayPort);
            Account accountA = FetchAccount(gatewayPort, loginA.AuthToken);

            // Segundo login = simulação HTTP de outro dispositivo com as mesmas credenciais.
            LoginInfo loginB = LoginTestAccount(gatewayPort);
            Account accountB = FetchAccount(gatewayPort, loginB.AuthToken);

            if (!string.Equals(loginA.AccountId, loginB.AccountId, StringComparison.Ordinal))
                throw new InvalidOperationException("dois logins da mesma conta retornaram account_id diferentes");
            if (!string.Equals(accountA.AccountId, loginA.AccountId, StringComparison.Ordinal) ||
                !string.Equals(accountB.AccountId, loginA.AccountId, StringComparison.Ordinal))
                throw new InvalidOperationException("/accounts não retornou o account_id autenticado");

            string[] playersA = PlayerIds(accountA);
            string[] playersB = PlayerIds(accountB);
            if (!playersA.SequenceEqual(playersB, StringComparer.Ordinal))
                throw new InvalidOperationException("a lista de personagens mudou entre dois logins da mesma conta");

            Console.WriteLine($"[selftest] conta cross-device OK — id={AccountKeys.ForLog(loginA.AccountId)} · personagens={playersA.Length}");

            // Usa o token do segundo login para seguir o fluxo real do cliente.
            string sessionBody = HttpPost(
                $"http://127.0.0.1:{gatewayPort}/sessions",
                "platform=Android&token=" + Uri.EscapeDataString(loginB.AuthToken));
            JObject sessionJson = JObject.Parse(sessionBody);
            string entityId = (string)sessionJson["user_id"];
            string sessionToken = (string)sessionJson["session_token"];
            if (string.IsNullOrEmpty(entityId) || string.IsNullOrEmpty(sessionToken))
                throw new InvalidOperationException("/sessions não retornou user_id/session_token");

            Console.WriteLine($"[selftest] /sessions OK — user={entityId}");
            return RunTcp(gamePort, entityId, sessionToken);
        }
        catch (Exception e)
        {
            Console.WriteLine("[selftest] ❌ falhou: " + e.Message);
            return 1;
        }
    }

    /// <summary>Usado também pelo --probe para obter um auth_token válido.</summary>
    internal static string EnsureTestAuthToken(int gatewayPort)
    {
        EnsureTestAccount(gatewayPort);
        return LoginTestAccount(gatewayPort).AuthToken;
    }

    private static int RunTcp(int gamePort, string entityId, string sessionToken)
    {
        Console.WriteLine($"[selftest] conectando TCP 127.0.0.1:{gamePort} entity={entityId}");
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Connect("127.0.0.1", gamePort);
        var connection = new Connection(socket);

        Welcome welcome = default;
        bool gotWelcome = false, gotOk = false, gotClock = false;
        double serverTime = 0;

        connection.Recv(delegate(Clock msg, PacketHeader header) { serverTime = msg.ServerTime; gotClock = true; });
        connection.Recv(delegate(Welcome msg, PacketHeader header) { welcome = msg; gotWelcome = true; });
        connection.Recv(delegate(OK msg, PacketHeader header) { gotOk = true; });
        connection.StartReceive();

        connection.Send(new GetClock { Time = Times.UnixTimeNow() });
        connection.Send(new Auth
        {
            EntityId = entityId,
            SessionToken = sessionToken,
            ClientVersion = "5.2.1",
            DeviceModel = "SelfTest"
        });

        if (!WaitFor(connection, () => gotClock && gotWelcome, 5000))
        {
            connection.Close();
            Console.WriteLine("[selftest] ❌ não recebeu Clock/Welcome");
            return 1;
        }
        Console.WriteLine($"[selftest] Clock OK (serverTime={serverTime:F0})");
        Console.WriteLine($"[selftest] Welcome OK — user={welcome.UserId} name='{welcome.Name}' region={welcome.Region.Id}");

        connection.Send(default(Ready));
        if (!WaitFor(connection, () => gotOk, 5000))
        {
            connection.Close();
            Console.WriteLine("[selftest] ❌ não recebeu OK após Ready");
            return 1;
        }
        Console.WriteLine("[selftest] Ready → OK");

        int chunks = 0;
        connection.Recv(delegate(Chunk msg, PacketHeader header) { chunks++; });
        connection.Send(new SetChunk { Chunk = new Point2(8, 8) });
        WaitFor(connection, () => chunks > 0, 3000);
        Console.WriteLine(chunks > 0 ? $"[selftest] SetChunk → Chunk OK ({chunks})" : "[selftest] ⚠️ não recebeu Chunk");

        connection.Close();
        Console.WriteLine("[selftest] ✅ autenticação, conta cross-device e handshake passaram");
        return 0;
    }

    private sealed class LoginInfo
    {
        public string AccountId;
        public string AuthToken;
    }

    private static void EnsureTestAccount(int gatewayPort)
    {
        string form = "username=" + Uri.EscapeDataString(SelfTestUsername) +
                      "&password=" + Uri.EscapeDataString(SelfTestPassword) +
                      "&password_confirm=" + Uri.EscapeDataString(SelfTestPassword);
        HttpResult result = HttpPostAllowError($"http://127.0.0.1:{gatewayPort}/auth/register", form);
        if (result.StatusCode != HttpStatusCode.Created && result.StatusCode != HttpStatusCode.Conflict)
            throw new InvalidOperationException($"não foi possível preparar conta de self-test: HTTP {(int)result.StatusCode} {result.Body}");
    }

    private static LoginInfo LoginTestAccount(int gatewayPort)
    {
        string body = HttpPost(
            $"http://127.0.0.1:{gatewayPort}/auth/login",
            "username=" + Uri.EscapeDataString(SelfTestUsername) +
            "&password=" + Uri.EscapeDataString(SelfTestPassword));
        JObject json = JObject.Parse(body);
        string accountId = (string)json["account_id"];
        string authToken = (string)json["auth_token"];
        if (string.IsNullOrEmpty(accountId) || string.IsNullOrEmpty(authToken))
            throw new InvalidOperationException("/auth/login não retornou account_id/auth_token");
        return new LoginInfo { AccountId = accountId, AuthToken = authToken };
    }

    private static Account FetchAccount(int gatewayPort, string authToken)
    {
        string body = HttpPost(
            $"http://127.0.0.1:{gatewayPort}/accounts",
            "platform=Android&token=" + Uri.EscapeDataString(authToken));
        Account account = Newtonsoft.Json.JsonConvert.DeserializeObject<Account>(body);
        if (account == null) throw new InvalidOperationException("/accounts retornou JSON inválido");
        account.Players ??= new List<Durango.Logic.Clusters.PlayerInfo>();
        return account;
    }

    private static string[] PlayerIds(Account account) =>
        (account?.Players ?? new List<Durango.Logic.Clusters.PlayerInfo>())
            .Where(p => p != null && !string.IsNullOrEmpty(p.PlayerEntityId))
            .Select(p => p.PlayerEntityId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

    private static bool WaitFor(Connection connection, Func<bool> done, int timeoutMs)
    {
        for (int i = 0; i < timeoutMs / 10; i++)
        {
            connection.Process();
            if (done()) return true;
            Thread.Sleep(10);
        }
        connection.Process();
        return done();
    }

    private sealed class HttpResult
    {
        public HttpStatusCode StatusCode;
        public string Body;
    }

    private static string HttpPost(string url, string form)
    {
        HttpResult result = HttpPostAllowError(url, form);
        if ((int)result.StatusCode < 200 || (int)result.StatusCode >= 300)
            throw new InvalidOperationException($"HTTP {(int)result.StatusCode} em {url}: {result.Body}");
        return result.Body;
    }

    private static HttpResult HttpPostAllowError(string url, string form)
    {
#pragma warning disable SYSLIB0014
        var req = (HttpWebRequest)WebRequest.Create(url);
#pragma warning restore SYSLIB0014
        req.Method = "POST";
        req.ContentType = "application/x-www-form-urlencoded";
        byte[] bytes = Encoding.UTF8.GetBytes(form);
        req.ContentLength = bytes.Length;
        using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);

        try
        {
            using var resp = (HttpWebResponse)req.GetResponse();
            using var reader = new StreamReader(resp.GetResponseStream());
            return new HttpResult { StatusCode = resp.StatusCode, Body = reader.ReadToEnd() };
        }
        catch (WebException e) when (e.Response is HttpWebResponse resp)
        {
            using (resp)
            using (var reader = new StreamReader(resp.GetResponseStream()))
                return new HttpResult { StatusCode = resp.StatusCode, Body = reader.ReadToEnd() };
        }
    }
}
