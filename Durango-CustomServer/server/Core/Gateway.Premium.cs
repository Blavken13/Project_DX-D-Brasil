using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Newtonsoft.Json.Linq;

namespace Durango.Online;

public partial class Gateway
{
    private void RegisterAdminPremiumRoutes()
    {
        _webServer.GetRoute["/admin/premium"] = (request, _) =>
        {
            if (!IsAdminAllowed(request)) return Forbidden();
            if (_host.Premium == null) return PremiumUnavailable();
            return new WebServer.JsonResponse(new JObject {
                ["packages"] = JToken.FromObject(_host.Premium.Packages.Values),
                ["xp_multiplier"] = PremiumStore.XpMultiplier, ["gather_extra_chance"] = PremiumStore.GatherChance,
                ["zero_fatigue"] = true, ["history"] = JToken.FromObject(_host.Premium.History(request.QueryString["entity_id"]))
            }.ToString());
        };
        _webServer.PostRoute["/admin/premium/grant"] = (request, body) => ChangePremium(request, body, "grant");
        _webServer.PostRoute["/admin/premium/revoke"] = (request, body) => ChangePremium(request, body, "revoke");
    }
    private static WebServer.Response PremiumUnavailable() => new WebServer.JsonResponse(
        "{\"error\":\"Premium não configurado.\"}", HttpStatusCode.ServiceUnavailable);

    private WebServer.Response ChangePremium(HttpListenerRequest request, Dictionary<string, string> body, string action)
    {
        if (!IsAdminAllowed(request) || !SameAdminOrigin(request)) return Forbidden();
        if (_host.Premium == null) return PremiumUnavailable();
        body.TryGetValue("entity_id", out string entityId);
        body.TryGetValue("package_id", out string packageId);
        body.TryGetValue("request_id", out string requestId);
        body.TryGetValue("days", out string duration);
        int days = 0;
        if (action == "grant" && !int.TryParse(duration, out days))
            return new WebServer.JsonResponse("{\"error\":\"Informe a duração em dias inteiros.\"}", HttpStatusCode.BadRequest);
        var player = _host.FindContextByEntityId(entityId);
        if (player == null || string.IsNullOrEmpty(player.Path) || string.IsNullOrEmpty(player.OwnerKey) || player.PlayerInfo?.IsSoftDeleted == true)
            return new WebServer.JsonResponse("{\"error\":\"Personagem salvo e vinculado a uma conta não encontrado.\"}", HttpStatusCode.NotFound);
        if (!_host.Premium.Change(player, packageId, days, action, requestId, out var operation, out bool duplicate, out string error))
            return new WebServer.JsonResponse(new JObject { ["error"] = error }.ToString(), HttpStatusCode.BadRequest);
        return new WebServer.JsonResponse(new JObject {
            ["duplicate"] = duplicate, ["operation"] = JToken.FromObject(operation),
            ["premium"] = JToken.FromObject(_host.Premium.Describe(entityId))
        }.ToString());
    }
}
