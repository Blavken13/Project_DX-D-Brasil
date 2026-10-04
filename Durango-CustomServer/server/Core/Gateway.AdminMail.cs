using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Durango.Utils;
using Newtonsoft.Json.Linq;

namespace Durango.Online;

public partial class Gateway
{
    private void RegisterAdminMailRoutes()
    {
        _webServer.GetRoute["/admin/mail/status"] = (request, _) =>
            !IsAdminAllowed(request) ? Forbidden() : new WebServer.JsonResponse(Json.Write(_host.Mail.Describe()));
        _webServer.GetRoute["/admin/mail/items"] = (request, _) =>
            !IsAdminAllowed(request) ? Forbidden() : new WebServer.JsonResponse(AdminMailComposer.ItemCatalog().ToString());
        _webServer.GetRoute["/admin/mail/inbox"] = (request, _) =>
        {
            if (!IsAdminAllowed(request)) return Forbidden();
            string id = request.QueryString["entity_id"];
            if (string.IsNullOrWhiteSpace(id) || _host.FindContextByEntityId(id) == null)
                return new WebServer.JsonResponse("{\"error\":\"Personagem não encontrado.\"}", HttpStatusCode.NotFound);
            return new WebServer.JsonResponse(Json.Write(_host.Mail.Inbox(id)));
        };
        _webServer.PostRoute["/admin/mail/preview"] = (request, body) => AdminMail(request, body, false);
        _webServer.PostRoute["/admin/mail/send"] = (request, body) => AdminMail(request, body, true);
    }

    private WebServer.Response AdminMail(HttpListenerRequest request, Dictionary<string, string> body, bool send)
    {
        if (!IsAdminAllowed(request)) return Forbidden();
        try
        {
            body.TryGetValue("json", out string json);
            var mail = AdminMailComposer.Parse(json);
            var eligible = _host.Contexts.Select(c => c.Player).Where(p => p != null
                && !string.IsNullOrEmpty(p.Path) && !string.IsNullOrEmpty(p.OwnerKey) && !string.IsNullOrEmpty(p.EntityId)
                && p.PlayerInfo?.IsSoftDeleted != true);
            string[] recipients = eligible.Where(p => mail.All || p.EntityId == mail.RecipientId).Select(p => p.EntityId).Distinct().ToArray();
            if (recipients.Length == 0 && !send) throw new ArgumentException("Nenhum personagem salvo e vinculado a uma conta corresponde ao destinatário.");
            if (!send) return new WebServer.JsonResponse(new JObject {
                ["subject"] = mail.Subject, ["message"] = mail.Text, ["recipients"] = recipients.Length,
                ["target"] = mail.All ? "all" : "player", ["items"] = mail.ResolvedItems, ["vouchers"] = mail.ResolvedVouchers,
                ["attachments_per_player"] = mail.Items.Length,
                ["portal_stones_per_player"] = mail.Vouchers.Where(v => v.VoucherId == CrackTuning.VoucherId).Sum(v => v.Count)
            }.ToString());
            body.TryGetValue("request_id", out string requestId);
            if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 100
                || !System.Text.RegularExpressions.Regex.IsMatch(requestId, "^[A-Za-z0-9_-]+$"))
                throw new ArgumentException("Informe request_id com até 100 letras, números, hífens ou sublinhados.");
            if (!_host.Mail.Dispatch(requestId, mail.Fingerprint, recipients, mail.Subject, mail.Text, mail.Type, mail.Items, mail.Vouchers, out var result, out string error))
                return new WebServer.JsonResponse(new JObject { ["error"] = error }.ToString(), HttpStatusCode.Conflict);
            return new WebServer.JsonResponse(new JObject {
                ["sent"] = result.Recipients, ["request_id"] = result.RequestId, ["duplicate"] = result.Duplicate
            }.ToString());
        }
        catch (Exception error) when (error is ArgumentException || error is Newtonsoft.Json.JsonException)
        {
            return new WebServer.JsonResponse(new JObject { ["error"] = error.Message }.ToString(), HttpStatusCode.BadRequest);
        }
    }
}
