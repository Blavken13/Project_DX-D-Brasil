using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Newtonsoft.Json.Linq;
using Durango.Utils;

namespace Durango.Online;

public partial class Gateway
{
    private AdminAuth _adminAuth;

    private static bool SameAdminOrigin(HttpListenerRequest request)
    {
        string origin = request?.Headers?["Origin"];
        return string.IsNullOrEmpty(origin) || string.Equals(origin,
            request.Url.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
    }

    private void RegisterAdminRoutes()
    {
        RegisterAdminMailRoutes();
        RegisterAdminPremiumRoutes();
        try { _adminAuth = new AdminAuth(Path.Combine(DataDir ?? Json.DataDir, "admin-auth.json")); }
        catch (Exception) { Console.WriteLine("[admin] Credenciais ausentes ou inválidas; login bloqueado."); }
        RegisterClientReportRoutes();

        _webServer.PostRoute["/admin/login"] = (request, data) =>
        {
            if (!SameAdminOrigin(request)) return Forbidden();
            if (_adminAuth == null) return new WebServer.JsonResponse(
                "{\"error\":\"Acesso administrativo não configurado.\"}", HttpStatusCode.ServiceUnavailable);
            if (!_adminAuth.AllowAttempt(request.RemoteEndPoint?.Address.ToString() ?? "unknown"))
                return new WebServer.JsonResponse("{\"error\":\"Aguarde um minuto antes de tentar novamente.\"}", (HttpStatusCode)429);
            data.TryGetValue("username", out string username);
            data.TryGetValue("password", out string password);
            string token = _adminAuth.Login(username, password);
            return token == null
                ? new WebServer.JsonResponse("{\"error\":\"Usuário ou senha incorretos.\"}", HttpStatusCode.Unauthorized)
                : new WebServer.JsonResponse(new JObject { ["session"] = token, ["expires_in"] = AdminAuth.SessionHours * 3600 }.ToString());
        };
        _webServer.PostRoute["/admin/logout"] = (request, _) =>
        {
            if (!SameAdminOrigin(request)) return Forbidden();
            _adminAuth?.Logout(request.Headers["X-Admin-Session"]);
            return new WebServer.JsonResponse("{\"logged_out\":true}");
        };
        _webServer.GetRoute["/admin/players"] = (request, _) =>
        {
            if (!IsAdminAllowed(request)) return Forbidden();
            var online = _host.DescribeOnline().ToDictionary(p => (string)p["entity_id"]);
            var players = new JArray();
            foreach (var context in _host.Contexts)
            {
                var player = context?.Player;
                if (string.IsNullOrEmpty(player?.EntityId)) continue;
                bool connected = online.TryGetValue(player.EntityId, out var live);
                players.Add(new JObject {
                    ["entity_id"] = player.EntityId,
                    ["name"] = player.PlayerInfo?.PlayerName ?? player.AppearPlayer.Name ?? "",
                    ["level"] = player.PlayerInfo?.PlayerLevel ?? player.AppearPlayer.Level,
                    ["region"] = connected ? (string)live["region"] : player.PersonalRegionId,
                    ["online"] = connected,
                    ["banned"] = BanList.IsBanned(player.OwnerKey),
                    ["mail_eligible"] = !string.IsNullOrEmpty(player.Path) && !string.IsNullOrEmpty(player.OwnerKey)
                        && player.PlayerInfo?.IsSoftDeleted != true,
                    ["premium"] = _host.Premium == null ? null : JToken.FromObject(_host.Premium.Describe(player.EntityId)),
                    ["inventory_capacity"] = Player.InventoryCapacity(player),
                    ["t_stone"] = player.TStone, ["warp_gem"] = player.WarpGem,
                    ["durango_coin"] = player.DurangoCoin
                });
            }
            return new WebServer.JsonResponse(players.ToString());
        };
        _webServer.GetRoute["/admin/catalog"] = (request, _) =>
        {
            if (!IsAdminAllowed(request)) return Forbidden();
            string root = Path.GetFullPath(DataDir ?? Json.DataDir);
            var files = new JArray(AdminDataFiles(root).Select(path => new JObject {
                ["path"] = Path.GetRelativePath(root, path).Replace('\\', '/'),
                ["bytes"] = new FileInfo(path).Length
            }));
            string terrains = Path.Combine(root, "terrains");
            return new WebServer.JsonResponse(new JObject {
                ["files"] = files, ["maintenance"] = Host.Maintenance,
                ["terrains"] = new JArray(Directory.Exists(terrains)
                    ? Directory.GetFiles(terrains, "*.zip").Select(Path.GetFileName) : Array.Empty<string>()),
                ["android_catalog_available"] = !string.IsNullOrEmpty(AssetBundleAndroidDir)
                    && File.Exists(Path.Combine(AssetBundleAndroidDir, "Info.5.2.1.json"))
            }.ToString());
        };
        _webServer.GetRoute["/admin/data"] = (request, _) =>
        {
            if (!IsAdminAllowed(request)) return Forbidden();
            string root = Path.GetFullPath(DataDir ?? Json.DataDir);
            string name = request.QueryString["path"];
            // Only enumerate known game JSON. Credential files and arbitrary paths are never exposed.
            string path = AdminDataFiles(root).FirstOrDefault(p =>
                string.Equals(Path.GetRelativePath(root, p).Replace('\\', '/'), name, StringComparison.Ordinal));
            return path == null ? new WebServer.JsonResponse("{\"error\":\"Dados não encontrados.\"}", HttpStatusCode.NotFound)
                : new WebServer.JsonResponse(File.ReadAllText(path));
        };
    }

    private static IEnumerable<string> AdminDataFiles(string root) => Directory.Exists(root)
        ? Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Where(p => !string.Equals(Path.GetFileName(p), "admin-auth.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal)
        : Array.Empty<string>();
}
