using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using Messages;
using Durango.Utils;
using Newtonsoft.Json.Linq;

namespace Durango.Online;

public partial class Gateway
{
    private void RegisterSeasonPassRoutes()
    {
        SeasonPassCatalog.Load(Path.Combine(AssetsDir, "season", "lost_horizon_pass.json"));
        _webServer.PostRoute["/season/pass"] = (request, _) =>
        {
            PlayerContext context = ResolveBySession(request);
            if (context == null) return SeasonReply(new JObject { ["ok"] = false, ["reason"] = "unauthorized" }, HttpStatusCode.Unauthorized);
            long now = SeasonPassCatalog.Now();
            if (SeasonPassCatalog.Active(now))
            {
                RecordSeasonLogin(context, now);
            }
            return SeasonReply(SeasonStatus(context, now));
        };
        _webServer.PostRoute["/season/claim"] = (request, postData) =>
        {
            PlayerContext context = ResolveBySession(request);
            if (context == null) return SeasonReply(new JObject { ["ok"] = false, ["reason"] = "unauthorized" }, HttpStatusCode.Unauthorized);
            long now = SeasonPassCatalog.Now();
            if (!SeasonPassCatalog.Active(now)) return SeasonReply(new JObject { ["ok"] = false, ["reason"] = "noseason", ["status"] = SeasonStatus(context, now) });
            if (string.IsNullOrEmpty(context.Path))
                return SeasonReply(new JObject { ["ok"] = false, ["reason"] = "unavailable", ["status"] = SeasonStatus(context, now) });
            if (!int.TryParse(postData.GetValueOrDefault("tier"), out int requested) || requested < 0 || requested > SeasonPassCatalog.MaxTier)
                return SeasonReply(new JObject { ["ok"] = false, ["reason"] = "invalid", ["status"] = SeasonStatus(context, now) }, HttpStatusCode.BadRequest);
            context.SeasonPass ??= new SeasonPassProgress();
            if (SeasonPassCatalog.Normalize(context.SeasonPass, now)) context.Save();
            SeasonPassProgress progress = context.SeasonPass;
            int reached = Math.Min(SeasonPassCatalog.MaxTier, (int)(progress.Points / SeasonPassCatalog.PointsPerTier));
            if (requested > reached)
                return SeasonReply(new JObject { ["ok"] = false, ["reason"] = "notreached", ["status"] = SeasonStatus(context, now) });
            if (requested != 0 && progress.Claimed.Contains(requested))
                return SeasonReply(new JObject { ["ok"] = false, ["reason"] = "taken", ["status"] = SeasonStatus(context, now) });

            var selected = new List<int>();
            for (int number = 1; number <= reached; number++)
                if (!progress.Claimed.Contains(number) && (requested == 0 || requested == number)) selected.Add(number);
            if (selected.Count == 0)
                return SeasonReply(new JObject { ["ok"] = false, ["reason"] = "none", ["status"] = SeasonStatus(context, now) });

            var granted = new List<Item>();
            long tstone = 0, gems = 0;
            foreach (int number in selected)
            {
                JToken row = SeasonPassCatalog.Current["tiers"][number - 1];
                foreach (string track in new[] { "box", "special" })
                {
                    JToken reward = row[track];
                    tstone += (long?)reward["tstone"] ?? 0;
                    gems += (long?)reward["gems"] ?? 0;
                    foreach (JToken spec in (JArray)reward["items"])
                    for (int n = 0; n < (int)spec["count"]; n++)
                    {
                        // Reuse the standard item composer so capsule rewards receive
                        // their ArtifactCapsule extension and remain placeable from inventory.
                        granted.Add(ShopCatalog.MakeItem(new JObject
                        {
                            ["prototype_id"] = (string)spec["id"],
                            ["level"] = (int?)spec["level"] ?? 60
                        }));
                    }
                }
            }

            if (!SafeSave.FlushPending(10000))
                return SeasonReply(new JObject { ["ok"] = false, ["reason"] = "unavailable", ["status"] = SeasonStatus(context, now) });
            int inventoryBefore = context.InventoryItems.Count;
            long tstoneBefore = context.TStone, gemsBefore = context.WarpGem;
            context.InventoryItems.AddRange(granted);
            context.TStone = Math.Min(99_999_999, context.TStone + tstone);
            context.WarpGem = Math.Min(99_999_999, context.WarpGem + gems);
            foreach (int number in selected) progress.Claimed.Add(number);
            if (!SafeSave.WriteAtomic(context.Path, Json.WriteToBytes(context, indented: false), "season-pass"))
            {
                context.InventoryItems.RemoveRange(inventoryBefore, granted.Count);
                context.TStone = tstoneBefore;
                context.WarpGem = gemsBefore;
                foreach (int number in selected) progress.Claimed.Remove(number);
                return SeasonReply(new JObject { ["ok"] = false, ["reason"] = "unavailable", ["status"] = SeasonStatus(context, now) });
            }
            Player online = _host.FindOnlinePlayer(context.EntityId);
            if (online != null && granted.Count > 0)
                online.Send(new InventoryUpdated { EntityId = context.EntityId, Items = granted.ToArray() });
            if (tstone > 0 || gems > 0) _host.PushWalletTo(context.EntityId);
            return SeasonReply(new JObject {
                ["ok"] = true, ["reason"] = "", ["tiers"] = new JArray(selected),
                ["status"] = SeasonStatus(context, now)
            });
        };
    }

    private static void RecordSeasonLogin(PlayerContext context, long now)
    {
        if (context == null || !SeasonPassCatalog.Active(now)) return;
        context.SeasonPass ??= new SeasonPassProgress();
        bool changed = SeasonPassCatalog.Normalize(context.SeasonPass, now);
        changed |= SeasonPassCatalog.ApplyEvent(context.SeasonPass, "login", 1, now);
        if (changed) context.Save();
    }

    private static JObject SeasonStatus(PlayerContext context, long now)
    {
        if (!SeasonPassCatalog.Active(now)) return new JObject { ["ok"] = true, ["active"] = false, ["now"] = now };
        SeasonPassProgress progress = context.SeasonPass ?? new SeasonPassProgress();
        int reached = Math.Min(SeasonPassCatalog.MaxTier, (int)(progress.Points / SeasonPassCatalog.PointsPerTier));
        var tiers = new JArray();
        int claimable = 0;
        foreach (JToken definition in (JArray)SeasonPassCatalog.Current["tiers"])
        {
            int number = (int)definition["tier"];
            int state = progress.Claimed.Contains(number) ? 2 : number <= reached ? 1 : 0;
            if (state == 1) claimable++;
            tiers.Add(new JObject {
                ["tier"] = number, ["state"] = state,
                ["box"] = definition["box"].DeepClone(), ["special"] = definition["special"].DeepClone()
            });
        }
        return new JObject {
            ["ok"] = true, ["active"] = true, ["now"] = now,
            ["season"] = SeasonPassCatalog.Current["season"].DeepClone(),
            ["points"] = progress.Points, ["tier"] = reached,
            ["maxTier"] = SeasonPassCatalog.MaxTier,
            ["pointsPerTier"] = SeasonPassCatalog.PointsPerTier,
            ["tierPoints"] = progress.Points % SeasonPassCatalog.PointsPerTier,
            ["claimable"] = claimable, ["tiers"] = tiers,
            ["daily"] = SeasonPassCatalog.MissionStatus(progress, "daily"),
            ["weekly"] = SeasonPassCatalog.MissionStatus(progress, "weekly")
        };
    }

    private static WebServer.Response SeasonReply(JObject body, HttpStatusCode code = HttpStatusCode.OK) =>
        new WebServer.JsonResponse(body.ToString(Newtonsoft.Json.Formatting.None), code);
}
