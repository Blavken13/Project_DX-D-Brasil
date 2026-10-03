using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Durango.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Durango.Online;

public partial class Gateway
{
    private void RegisterClientReportRoutes()
    {
        // Lazy disk access happens on the collector workers, never in Gateway.Process.
        var store = new Lazy<MobileReportStore>(() => new MobileReportStore(AppData.CombinePath("mobile-reports")));
        var ingestion = new SemaphoreSlim(2);
        var administration = new SemaphoreSlim(2);
        _webServer.BackgroundHandler = (context, path) => {
            bool ingest = path == "/client-reports/mobile";
            if (!ingest && path != "/admin/client-reports" && !path.StartsWith("/admin/client-reports/", StringComparison.Ordinal)) return false;
            var slots = ingest ? ingestion : administration;
            if (!slots.Wait(0)) { context.Response.StatusCode = 503; context.Response.Close(); return true; }
            _ = Task.Run(async () => {
                try { await HandleClientReport(context, path, ingest, store); }
                catch (ArgumentException) { await ReportJson(context, 400, new JObject { ["error"] = "Relatório inválido ou excede os limites." }); }
                catch (OverflowException) { await ReportJson(context, 400, new JObject { ["error"] = "Número excede o limite." }); }
                catch (JsonException) { await ReportJson(context, 400, new JObject { ["error"] = "JSON inválido." }); }
                catch (OperationCanceledException) { try { context.Response.Abort(); } catch { } }
                catch (Exception) { await ReportJson(context, 503, new JObject { ["error"] = "Coletor indisponível; tente novamente mais tarde." }); }
                finally { try { context.Response.Close(); } catch { } slots.Release(); }
            });
            return true;
        };
    }

    private async Task HandleClientReport(HttpListenerContext context, string path, bool ingest, Lazy<MobileReportStore> store)
    {
        var request = context.Request;
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        if (ingest) {
            if (request.HttpMethod != "POST") { await ReportJson(context, 405, new JObject { ["error"] = "Use POST." }); return; }
            if (!(request.ContentType ?? "").StartsWith("application/json", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(request.Headers["Content-Encoding"])) {
                await ReportJson(context, 415, new JObject { ["error"] = "Use JSON UTF-8 sem compressão." }); return;
            }
            if (request.ContentLength64 > 65536) { await ReportJson(context, 413, new JObject { ["error"] = "Corpo muito grande." }); return; }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var memory = new MemoryStream(); byte[] buffer = new byte[4096]; int length;
            while ((length = await request.InputStream.ReadAsync(buffer.AsMemory(), timeout.Token)) > 0) {
                if (memory.Length + length > 65536) { await ReportJson(context, 413, new JObject { ["error"] = "Corpo muito grande." }); return; }
                memory.Write(buffer, 0, length);
            }
            using var reader = new JsonTextReader(new StringReader(new UTF8Encoding(false, true).GetString(memory.ToArray()))) { MaxDepth = 8, DateParseHandling = DateParseHandling.None };
            var envelope = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if (reader.Read()) throw new ArgumentException();
            if (envelope["schema_version"]?.Type != JTokenType.Integer || (int)envelope["schema_version"] != 1 || envelope["report"] is not JObject input) throw new ArgumentException();
            var item = MobileReportStore.Validate(input);
            int code = store.Value.Accept(item, ReportClientAddress(request));
            if (code == 429) context.Response.Headers["Retry-After"] = "60";
            await ReportJson(context, code, code == 200 ? new JObject { ["accepted"] = new JArray(item["event_id"]) }
                : new JObject { ["error"] = code == 409 ? "ID já usado por outro relatório." : "Limite temporário atingido." });
            return;
        }
        if (!IsAdminAllowed(request)) { await ReportJson(context, 403, new JObject { ["error"] = "Acesso negado." }); return; }
        if (request.HttpMethod != "GET") { await ReportJson(context, 405, new JObject { ["error"] = "Use GET." }); return; }
        var rows = store.Value.Snapshot();
        if (path == "/admin/client-reports/export") {
            string format = request.QueryString["format"] ?? "csv";
            if (format != "csv" && format != "json") throw new ArgumentException();
            context.Response.ContentType = format == "csv" ? "text/csv; charset=utf-8" : "application/json; charset=utf-8";
            context.Response.Headers["Content-Disposition"] = "attachment; filename=mobile-errors-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "." + format;
            using var writer = new StreamWriter(context.Response.OutputStream, new UTF8Encoding(format == "csv"), 8192, true);
            if (format == "csv") {
                await writer.WriteLineAsync(string.Join(";", MobileReportStore.Columns));
                foreach (var row in rows) await writer.WriteLineAsync(string.Join(";", MobileReportStore.Columns.Select(c => MobileReportStore.CsvCell(row[c]))));
            } else {
                await writer.WriteAsync("["); bool first = true;
                foreach (var row in rows) { if (!first) await writer.WriteAsync(","); first = false; await writer.WriteAsync(row.ToString(Formatting.None)); }
                await writer.WriteAsync("]");
            }
            await writer.FlushAsync(); return;
        }
        if (path.StartsWith("/admin/client-reports/", StringComparison.Ordinal)) {
            string id = path["/admin/client-reports/".Length..];
            var item = rows.FirstOrDefault(x => (string)x["event_id"] == id);
            await ReportJson(context, item == null ? 404 : 200, item ?? new JObject { ["error"] = "Relatório não encontrado." }); return;
        }
        var filtered = rows.Where(x => new[] { "event_type", "version_name", "model", "signature" }.All(field => {
            string filter = request.QueryString[field]; return string.IsNullOrEmpty(filter) || ((string)x[field] ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase);
        })).ToArray();
        int offset = int.TryParse(request.QueryString["offset"], out int parsed) ? Math.Clamp(parsed, 0, MobileReportStore.MaxReports) : 0;
        var groups = filtered.GroupBy(x => (string)x["signature"]).OrderByDescending(g => g.Count()).Take(100).Select(g => new JObject {
            ["signature"] = g.Key, ["count"] = g.Count(), ["installations"] = g.Select(x => (string)x["installation_id"]).Distinct().Count(),
            ["event_type"] = g.First()["event_type"], ["models"] = new JArray(g.Select(x => (string)x["model"]).Distinct()),
            ["versions"] = new JArray(g.Select(x => (string)x["version_name"]).Distinct()) });
        await ReportJson(context, 200, new JObject { ["total"] = rows.Length, ["filtered_total"] = filtered.Length,
            ["installations"] = filtered.Select(x => (string)x["installation_id"]).Distinct().Count(), ["offset"] = offset, ["limit"] = 100,
            ["retention_days"] = 30, ["capacity"] = MobileReportStore.MaxReports, ["groups"] = new JArray(groups),
            ["reports"] = new JArray(filtered.Skip(offset).Take(100)) });
    }

    private static string ReportClientAddress(HttpListenerRequest request)
    {
        string peer = request.RemoteEndPoint?.Address.ToString() ?? "unknown";
        string trusted = Environment.GetEnvironmentVariable("DURANGO_REPORTS_TRUSTED_PROXY");
        // Only our explicitly configured proxy can supply an address for rate limits.
        if (!string.IsNullOrEmpty(trusted) && peer == trusted) {
            string forwarded = (request.Headers["X-Forwarded-For"] ?? "").Split(',').Last().Trim();
            if (IPAddress.TryParse(forwarded, out var address)) return address.ToString();
        }
        return peer;
    }

    private static async Task ReportJson(HttpListenerContext context, int status, JObject body)
    {
        try {
            byte[] bytes = Encoding.UTF8.GetBytes(body.ToString(Formatting.None));
            context.Response.StatusCode = status; context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
        } catch (IOException) { } catch (HttpListenerException) { } catch (ObjectDisposedException) { }
    }
}
