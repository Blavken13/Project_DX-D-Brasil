using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Durango.Utils;
using Messages;
using Shared.Mailing;
using Shared.Economy;

namespace Durango.Online;

// The mailbox and claim journal commit before inventory changes. MailSequence is
// saved with the inventory so recovery cannot award a consumed attachment again.
public sealed class MailStore
{
    private readonly object _sync = new();
    private readonly string _path;
    private State _state;
    private readonly Func<double> _clock;
    public event Action<string> Updated;

    public object Describe()
    {
        lock (_sync) return new { enabled = true, messages = _state.Messages.Count(m => !m.Deleted),
            pending = _state.Messages.Count(m => !m.Deleted && !m.Accepted),
            accepted = _state.Messages.Count(m => m.Accepted), dispatches = _state.Dispatches.Count };
    }

    public MailStore(string path, Func<double> clock = null)
    {
        _path = path; _clock = clock ?? Times.UnixTimeNow;
        if (!File.Exists(path))
        {
            if (File.Exists(path + ".bak")) throw new InvalidDataException("Correio ausente, mas existe backup.");
            _state = new State();
        }
        else
        {
            _state = Json.Read<State>(File.ReadAllBytes(path));
            if (_state == null || _state.Version != 1 || _state.Sequence < 0 || _state.Messages == null
                || _state.Claims == null || _state.Dispatches == null)
                throw new InvalidDataException("Armazenamento do correio inválido.");
            foreach (var mail in _state.Messages) { mail.Items = CopyItems(mail.Items); mail.Vouchers = CopyVouchers(mail.Vouchers); }
            foreach (var claim in _state.Claims) { claim.Items = CopyItems(claim.Items); claim.Vouchers = CopyVouchers(claim.Vouchers); }
        }
    }

    private State Next() => new() { Sequence = _state.Sequence,
        Messages = new(_state.Messages), Claims = new(_state.Claims), Dispatches = new(_state.Dispatches) };
    private bool Commit(State state, out string error)
    {
        error = null;
        if (!SafeSave.WriteAtomic(_path, Json.WriteToBytes(state), "correio"))
        { error = "Não foi possível salvar o correio. Nenhum anexo foi entregue."; return false; }
        _state = state; return true;
    }

    public bool Dispatch(string requestId, string fingerprint, string[] recipients, string subject, string text,
        MailType type, Item[] items, out DispatchResult result, out string error)
        => Dispatch(requestId, fingerprint, recipients, subject, text, type, items, Array.Empty<VoucherInfo>(), out result, out error);

    public bool Dispatch(string requestId, string fingerprint, string[] recipients, string subject, string text,
        MailType type, Item[] items, VoucherInfo[] vouchers, out DispatchResult result, out string error)
        => Dispatch(requestId, fingerprint, recipients, subject, text, type, items, vouchers, null, out result, out error);

    public bool HasDispatch(string requestId)
    {
        lock (_sync) return _state.Dispatches.Any(d => d.RequestId == requestId);
    }

    public bool Dispatch(string requestId, string fingerprint, string[] recipients, string subject, string text,
        MailType type, Item[] items, VoucherInfo[] vouchers, Dictionary<Currency, int> money, out DispatchResult result, out string error)
    {
        lock (_sync)
        {
            result = null; error = null;
            if (money?.Any(m => m.Key != Currency.Gem || m.Value < 1 || m.Value > EconomyStore.BalanceLimit) == true)
            { error = "Moeda de correio inválida."; return false; }
            var existing = _state.Dispatches.FirstOrDefault(d => d.RequestId == requestId);
            if (existing != null)
            {
                if (existing.Fingerprint != fingerprint) { error = "Este identificador já foi usado com outro conteúdo."; return false; }
                result = new DispatchResult { RequestId = requestId, Recipients = existing.Recipients.Length, Duplicate = true };
                return true;
            }
            if (recipients == null || recipients.Length == 0 || recipients.Any(string.IsNullOrWhiteSpace))
            { error = "Nenhum personagem elegível encontrado."; return false; }
            var ids = recipients.Distinct(StringComparer.Ordinal).ToArray();
            var next = Next();
            foreach (string recipient in ids)
            {
                var attachments = CopyItems(items);
                for (int i = 0; i < attachments.Length; i++)
                {
                    attachments[i].Id = Guid.NewGuid().ToString();
                    if (attachments[i].Ext is ArtifactCapsule capsule)
                    {
                        capsule.EntityId = Guid.NewGuid().ToString();
                        var display = capsule.Display; display.EntityId = capsule.EntityId; capsule.Display = display;
                        attachments[i].Ext = capsule;
                    }
                }
                next.Messages.Add(new Entry { Id = Guid.NewGuid().ToString(), EntityId = recipient,
                    SentAt = _clock(), Subject = subject, Text = text, Type = type, Items = attachments, Vouchers = CopyVouchers(vouchers), Money = CopyMoney(money) });
            }
            next.Dispatches.Add(new DispatchRecord { RequestId = requestId, Fingerprint = fingerprint, Recipients = ids });
            if (!Commit(next, out error)) return false;
            result = new DispatchResult { RequestId = requestId, Recipients = ids.Length };
            foreach (string id in ids) Updated?.Invoke(id);
            Console.WriteLine($"[correio] envio {requestId}: {ids.Length} destinatários, {items.Length} itens e {CopyVouchers(vouchers).Sum(v => v.Count)} vouchers por personagem");
            return true;
        }
    }

    public Mail[] Inbox(string entityId)
    {
        lock (_sync) return _state.Messages.Where(m => m.EntityId == entityId && !m.Deleted).Reverse()
            .OrderByDescending(m => m.SentAt).Select(m => new Mail {
                Id = m.Id, SentAt = m.SentAt, SenderId = "Durango Brasil", MailType = m.Type,
                Text = m.Subject + "\n" + m.Text, AttachedItems = CopyItems(m.Items), AttachedVouchers = CopyVouchers(m.Vouchers),
                Money = CopyMoney(m.Money), Read = m.Read, Accepted = m.Accepted, AcceptedEntityId = m.Accepted ? m.EntityId : null
            }).ToArray();
    }

    public void Recover(PlayerContext player)
    {
        lock (_sync)
        {
            if (player.MailSequence > _state.Sequence) throw new InvalidDataException("Personagem possui resgates posteriores ao correio.");
            foreach (var claim in _state.Claims.Where(c => c.EntityId == player.EntityId && c.Sequence > player.MailSequence)) Apply(player, claim);
        }
    }

    public bool Accept(PlayerContext player, string[] mailIds, out Item[] received, out VoucherInfo[] receivedVouchers, out string error)
    {
        lock (_sync)
        {
            received = Array.Empty<Item>(); receivedVouchers = Array.Empty<VoucherInfo>();
            if (!Select(player.EntityId, mailIds, out var selected, out error)) return false;
            var pending = selected.Where(m => !m.Accepted).ToArray();
            if (pending.Length == 0) return true; // A repeated claim is harmless.
            var items = pending.SelectMany(m => m.Items ?? Array.Empty<Item>()).ToArray();
            var vouchers = MergeVouchers(pending.SelectMany(m => m.Vouchers ?? Array.Empty<VoucherInfo>()));
            var money = pending.SelectMany(m => CopyMoney(m.Money)).GroupBy(m => m.Key)
                .ToDictionary(g => g.Key, g => checked(g.Sum(m => (long)m.Value)));
            if (money.Any(m => m.Key != Currency.Gem || m.Value <= 0 || player.WarpGem > EconomyStore.BalanceLimit - m.Value))
            { error = "A carteira não comporta as Warp Gems desta entrega. Use algumas e tente novamente."; return false; }
            if (items.Length > 0 && player.InventoryItems.Sum(i => (long)Math.Max(1, i.Size)) + items.Sum(i => (long)Math.Max(1, i.Size)) > Player.InventoryCapacity(player))
            { error = "Não há espaço na mochila. Libere espaço e resgate novamente."; return false; }
            foreach (var voucher in vouchers)
            {
                if (!string.Equals(voucher.VoucherId, CrackTuning.VoucherId, StringComparison.Ordinal))
                { error = "A mensagem contém um voucher não suportado."; return false; }
                int current = player.Vouchers?.GetValueOrDefault(voucher.VoucherId) ?? 0;
                if (voucher.Count < 1 || current > InductionRewardTuning.Maximum - voucher.Count)
                { error = $"A carteira de Pedras de Portal suporta no máximo {InductionRewardTuning.Maximum}. Use algumas pedras e tente resgatar novamente."; return false; }
            }
            var next = Next();
            next.Messages = next.Messages.Select(m => pending.Contains(m) ? m with { Accepted = true, Read = true } : m).ToList();
            var claim = new Claim { EntityId = player.EntityId, Sequence = ++next.Sequence, Items = CopyItems(items), Vouchers = CopyVouchers(vouchers), Money = money };
            next.Claims.Add(claim);
            if (!Commit(next, out error)) return false;
            Apply(player, claim); received = CopyItems(items); receivedVouchers = CopyVouchers(vouchers); return true;
        }
    }

    public bool MarkRead(string entityId, string[] ids, out string error) => Change(entityId, ids, false, out error);
    public bool Delete(string entityId, string[] ids, out string error) => Change(entityId, ids, true, out error);
    private bool Change(string entityId, string[] ids, bool delete, out string error)
    {
        lock (_sync)
        {
            if (!Select(entityId, ids, out var selected, out error)) return false;
            if (delete && selected.Any(m => !m.Accepted && ((m.Items?.Length ?? 0) > 0 || (m.Vouchers?.Length ?? 0) > 0 || (m.Money?.Count ?? 0) > 0)))
            { error = "Resgate os anexos antes de excluir a mensagem."; return false; }
            var next = Next();
            next.Messages = next.Messages.Select(m => selected.Contains(m) ? m with { Read = true, Deleted = delete } : m).ToList();
            return Commit(next, out error);
        }
    }

    private bool Select(string entityId, string[] ids, out Entry[] selected, out string error)
    {
        selected = Array.Empty<Entry>(); error = null;
        if (ids == null || ids.Length == 0 || ids.Length > 100 || ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct().Count() != ids.Length)
        { error = "Seleção de mensagens inválida."; return false; }
        selected = _state.Messages.Where(m => m.EntityId == entityId && !m.Deleted && ids.Contains(m.Id)).ToArray();
        if (selected.Length != ids.Length) { error = "Mensagem não encontrada na sua caixa de correio."; return false; }
        return true;
    }

    private static void Apply(PlayerContext player, Claim claim)
    {
        if (player.MailSequence >= claim.Sequence) return;
        foreach (var item in CopyItems(claim.Items))
            if (!player.InventoryItems.Any(i => i.Id == item.Id)) player.InventoryItems.Add(item);
        player.Vouchers ??= new Dictionary<string, int>();
        foreach (var voucher in CopyVouchers(claim.Vouchers))
            player.Vouchers[voucher.VoucherId] = checked(player.Vouchers.GetValueOrDefault(voucher.VoucherId) + voucher.Count);
        if (claim.Money != null)
            foreach (var money in claim.Money)
            {
                if (money.Key != Currency.Gem || money.Value <= 0 || player.WarpGem > EconomyStore.BalanceLimit - money.Value)
                    throw new InvalidDataException("Crédito de correio inválido.");
                player.WarpGem = checked(player.WarpGem + money.Value);
            }
        player.MailSequence = claim.Sequence; player.Save();
    }
    private static Item[] CopyItems(Item[] items)
    {
        var copy = Json.Read<Item[]>(Json.Write(items ?? Array.Empty<Item>()));
        var list = copy.ToList(); ItemExtRepair.Normalize(list, "correio"); return list.ToArray();
    }
    private static VoucherInfo[] CopyVouchers(VoucherInfo[] vouchers) =>
        (vouchers ?? Array.Empty<VoucherInfo>()).Select(v => new VoucherInfo { VoucherId = v.VoucherId, Count = v.Count }).ToArray();
    private static VoucherInfo[] MergeVouchers(IEnumerable<VoucherInfo> vouchers) =>
        vouchers.Where(v => !string.IsNullOrWhiteSpace(v.VoucherId) && v.Count > 0)
            .GroupBy(v => v.VoucherId, StringComparer.Ordinal)
            .Select(g => new VoucherInfo { VoucherId = g.Key, Count = checked(g.Sum(v => v.Count)) }).ToArray();
    private static Dictionary<Currency, int> CopyMoney(Dictionary<Currency, int> money) => money == null ? new() : new(money);
    public sealed class State
    {
        public int Version = 1; public long Sequence;
        public List<Entry> Messages = new(); public List<Claim> Claims = new(); public List<DispatchRecord> Dispatches = new();
    }
    public sealed record Entry
    {
        public string Id, EntityId, Subject, Text; public double SentAt; public MailType Type;
        public Item[] Items = Array.Empty<Item>(); public VoucherInfo[] Vouchers = Array.Empty<VoucherInfo>(); public bool Read, Accepted, Deleted;
        public Dictionary<Currency, int> Money = new();
    }
    public sealed class Claim { public long Sequence; public string EntityId; public Item[] Items; public VoucherInfo[] Vouchers = Array.Empty<VoucherInfo>(); public Dictionary<Currency, long> Money = new(); }
    public sealed class DispatchRecord { public string RequestId, Fingerprint; public string[] Recipients; }
    public sealed class DispatchResult { public string RequestId; public int Recipients; public bool Duplicate; }
}
