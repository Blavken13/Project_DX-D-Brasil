using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Durango.Utils;
using Messages;
using Newtonsoft.Json.Linq;
using Shared.Mailing;

namespace Durango.Online;

// This store is the durable authority. Player saves only checkpoint upfront credits.
public sealed class PremiumStore
{
    public const double XpMultiplier = 1.5;
    public const float GatherChance = 0.25f;
    private readonly string _path;
    private readonly Func<double> _clock;
    private State _state;
    public IReadOnlyDictionary<string, Package> Packages { get; }
    public event Action<string> Updated;
    public double Now => _clock();

    public sealed class Package
    {
        public string Id, Name, EffectId;
        public int Days, InventoryBonus, DailyGems, ImmediateGems;
        public ItemSpec[] DailyItems = Array.Empty<ItemSpec>();
    }
    public sealed class ItemSpec { public string PrototypeId, Name; public int Count, Level; }
    public sealed record Subscription
    {
        public string EntityId, PackageId;
        public double Since, Until;
        public long Epoch;
    }
    public sealed class Operation
    {
        public string RequestId, Fingerprint, EntityId, PackageId, Action;
        public int Days, Gems;
        public long Sequence;
        public double At, Before, Until;
    }
    public sealed class State
    {
        public int Version = 1;
        public long Sequence;
        public List<Subscription> Subscriptions = new();
        public List<Operation> Operations = new();
    }
    public PremiumStore(string path, Func<double> clock = null)
    {
        _path = path;
        _clock = clock ?? Times.UnixTimeNow;
        Packages = LoadPackages();
        if (!File.Exists(path))
        {
            if (File.Exists(path + ".bak")) throw new InvalidDataException("Premium ausente, mas existe backup.");
            _state = new State();
        }
        else
        {
            _state = Json.Read<State>(File.ReadAllBytes(path));
            if (_state?.Version != 1 || _state.Sequence < 0 || _state.Subscriptions == null || _state.Operations == null
                || _state.Subscriptions.Any(s => s == null || !Packages.ContainsKey(s.PackageId ?? "")
                    || string.IsNullOrEmpty(s.EntityId) || !double.IsFinite(s.Until) || s.Epoch <= 0)
                || _state.Subscriptions.GroupBy(s => (s.EntityId, s.PackageId)).Any(g => g.Count() != 1)
                || _state.Operations.Any(o => o == null || o.Sequence <= 0 || o.Sequence > _state.Sequence)
                || _state.Operations.Select(o => o.RequestId).Distinct().Count() != _state.Operations.Count)
                throw new InvalidDataException("Armazenamento premium inválido.");
        }
    }
    private static IReadOnlyDictionary<string, Package> LoadPackages()
    {
        var rows = Json.ReadFromFile<JObject>("purchaser/commodities")?["posted_commodities"] as JObject;
        var effects = Json.ReadFromFile<JObject>("survival/status_effects");
        var result = new Dictionary<string, Package>(StringComparer.Ordinal);
        foreach (var entry in new[] { ("day_package_1", "Premium 7 dias"), ("day_package_2", "Premium 15 dias"), ("monthly_package_1", "Premium 30 dias") })
        {
            var row = rows?[entry.Item1] as JObject ?? throw new InvalidDataException("Pacote original ausente: " + entry.Item1);
            var effect = row["contents"]?["status_effects"]?[0];
            string effectId = (string)effect?["status_effects_id"];
            var capacity = effects?[effectId]?[0]?["effects"]?.FirstOrDefault(e => (string)e["key"] == "carry_capacity");
            int GemAmount(JToken contents) => contents?["money"]?.Where(m => (int?)m["currency"] == 1).Sum(m => (int)m["amount"]) ?? 0;
            var package = new Package { Id = entry.Item1, Name = entry.Item2, EffectId = effectId,
                Days = (int)row["period_days"], InventoryBonus = (int)decimal.Parse((string)capacity?["value"] ?? "0", System.Globalization.CultureInfo.InvariantCulture),
                DailyGems = GemAmount(row["daily_contents"]), ImmediateGems = GemAmount(row["contents"]),
                DailyItems = (row["daily_contents"]?["items"] as JArray ?? new JArray()).Select(i => new ItemSpec {
                    PrototypeId = (string)i["prototype_id"], Count = (int)i["count"], Level = (int?)i["level"] ?? 1 }).ToArray() };
            if (package.InventoryBonus <= 0 || package.DailyGems < 0 || package.ImmediateGems < 0
                || package.DailyItems.Any(i => i.Count <= 0 || !Cheats.MakeItem(i.PrototypeId, i.Level).HasValue))
                throw new InvalidDataException("Benefícios premium inválidos: " + package.Id);
            foreach (var spec in package.DailyItems) spec.Name = Cheats.MakeItem(spec.PrototypeId, spec.Level).Value.Name;
            result.Add(package.Id, package);
        }
        return result;
    }
    public Subscription[] Active(string entityId) => _state.Subscriptions.Where(s => s.EntityId == entityId && s.Until > Now).ToArray();
    public bool IsActive(string entityId) => _state.Subscriptions.Any(s => s.EntityId == entityId && s.Until > Now);
    public int InventoryBonus(string entityId) => Active(entityId).Sum(s => Packages[s.PackageId].InventoryBonus);
    public object Describe(string entityId)
    {
        double now = Now;
        var subscriptions = _state.Subscriptions.Where(s => s.EntityId == entityId).ToArray();
        var active = subscriptions.Where(s => s.Until > now).ToArray();
        double until = subscriptions.Select(s => s.Until).DefaultIfEmpty(0).Max();
        return new { active = active.Length > 0, expires_at = until, remaining_seconds = Math.Max(0, until - now),
            inventory_bonus = active.Sum(s => Packages[s.PackageId].InventoryBonus),
            daily_gems = active.Sum(s => Packages[s.PackageId].DailyGems), xp_multiplier = active.Length > 0 ? XpMultiplier : 1,
            gather_extra_chance = active.Length > 0 ? GatherChance : 0, zero_fatigue = active.Length > 0,
            packages = subscriptions.Select(s => new { package_id = s.PackageId, name = Packages[s.PackageId].Name,
                active = s.Until > now, expires_at = s.Until, remaining_seconds = Math.Max(0, s.Until - now) }).ToArray() };
    }
    public object History(string entityId) => _state.Operations.Where(o => entityId == null || o.EntityId == entityId).Reverse().Take(100).ToArray();

    public bool Change(PlayerContext player, string packageId, int days, string action, string requestId,
        out Operation operation, out bool duplicate, out string error)
    {
        operation = null; duplicate = false; error = null;
        if (player == null || string.IsNullOrEmpty(player.EntityId) || !Packages.TryGetValue(packageId ?? "", out var package))
        { error = "Personagem ou pacote não encontrado."; return false; }
        if (action != "grant" && action != "revoke" || action == "grant" && (days < 1 || days > 3650))
        { error = "Informe uma duração de 1 a 3650 dias."; return false; }
        if (string.IsNullOrEmpty(requestId) || !System.Text.RegularExpressions.Regex.IsMatch(requestId, "^[A-Za-z0-9_-]{1,100}$"))
        { error = "Identificador da operação inválido."; return false; }
        string fingerprint = Json.Write(new { player.EntityId, packageId, days, action });
        var existing = _state.Operations.FirstOrDefault(o => o.RequestId == requestId);
        if (existing != null)
        {
            if (existing.Fingerprint != fingerprint) { error = "Identificador já utilizado com outro conteúdo."; return false; }
            Recover(player); operation = existing; duplicate = true; return true;
        }
        Recover(player);
        int gems = action == "grant" ? package.ImmediateGems : 0;
        if (player.WarpGem > EconomyStore.BalanceLimit - gems)
        { error = "A carteira não comporta as Warp Gems da ativação."; return false; }
        double now = Now;
        var previous = _state.Subscriptions.FirstOrDefault(s => s.EntityId == player.EntityId && s.PackageId == packageId);
        double until = action == "grant" ? Math.Max(now, previous?.Until ?? 0) + days * 86400d : now;
        if (until > now + 3650 * 86400d) { error = "Prazo acumulado máximo: 3650 dias."; return false; }
        var next = new State { Sequence = _state.Sequence + 1, Subscriptions = new(_state.Subscriptions), Operations = new(_state.Operations) };
        operation = new Operation { RequestId = requestId, Fingerprint = fingerprint, EntityId = player.EntityId, PackageId = packageId,
            Action = action, Days = days, Gems = gems, Sequence = next.Sequence, At = now, Before = previous?.Until ?? 0, Until = until };
        next.Subscriptions.RemoveAll(s => s.EntityId == player.EntityId && s.PackageId == packageId);
        next.Subscriptions.Add(new Subscription { EntityId = player.EntityId, PackageId = packageId, Since = previous?.Until > now ? previous.Since : now,
            Until = until, Epoch = previous?.Until > now ? previous.Epoch : next.Sequence });
        next.Operations.Add(operation);
        if (!SafeSave.WriteAtomic(_path, Json.WriteToBytes(next), "premium"))
        { error = "Não foi possível salvar o premium. Nenhuma concessão realizada."; operation = null; return false; }
        _state = next;
        Recover(player);
        Updated?.Invoke(player.EntityId);
        return true;
    }
    public void Recover(PlayerContext player)
    {
        if (player.PremiumSequence > _state.Sequence) throw new InvalidDataException("Personagem possui créditos posteriores ao premium.");
        player.Premium = this;
        var operations = _state.Operations.Where(o => o.EntityId == player.EntityId && o.Sequence > player.PremiumSequence).ToArray();
        if (operations.Length == 0) return;
        player.WarpGem = checked(player.WarpGem + operations.Sum(o => (long)o.Gems));
        if (player.WarpGem > EconomyStore.BalanceLimit) throw new InvalidDataException("Créditos premium excedem a carteira.");
        player.PremiumSequence = operations.Max(o => o.Sequence);
        player.Save();
    }
    // Native packages pay once on each UTC day the character accesses the game.
    // Mail dispatch IDs survive claims, deletions, renewals and server restarts.
    public bool DeliverDaily(PlayerContext player, MailStore mail, out string error)
    {
        error = null;
        long day = (long)Math.Floor(Now / 86400);
        foreach (var subscription in Active(player.EntityId))
        {
            var package = Packages[subscription.PackageId];
            string id = $"premium-daily-{player.EntityId}-{subscription.PackageId}-{day}";
            if (mail.HasDispatch(id)) continue;
            var items = new List<Item>();
            foreach (var spec in package.DailyItems)
                for (int i = 0; i < spec.Count; i++)
                {
                    var item = Cheats.MakeItem(spec.PrototypeId, spec.Level);
                    if (!item.HasValue) { error = "Consumível premium não encontrado: " + spec.PrototypeId; return false; }
                    items.Add(item.Value);
                }
            var money = package.DailyGems > 0 ? new Dictionary<Shared.Economy.Currency, int> { [Shared.Economy.Currency.Gem] = package.DailyGems } : new();
            if (!mail.Dispatch(id, id, new[] { player.EntityId }, package.Name + " · entrega diária",
                "Resgate suas Warp Gems e consumíveis. Entrega diária para o personagem que acessou o jogo (dia UTC).",
                MailType.Periodic, items.ToArray(), Array.Empty<VoucherInfo>(), money, out _, out error)) return false;
        }
        return true;
    }
}
