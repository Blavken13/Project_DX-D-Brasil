using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Durango.Utils;
using Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared.Mailing;
using Yaml;
using Yaml.Util;

namespace Durango.Online;

internal sealed class AdminMailComposer
{
    public string Subject, Text, RecipientId, Fingerprint;
    public bool All;
    public MailType Type;
    public Item[] Items;
    public VoucherInfo[] Vouchers;
    public JArray ResolvedItems, ResolvedVouchers;

    public static JArray ItemCatalog()
    {
        var shop = ShopPrototypeCategories();
        return new JArray(SingletonDict<string, List<Prototype>>.Instance.Keys.OrderBy(id => id).Select(id =>
        {
            var prototype = PrototypeYaml.GetItemPrototype(id);
            shop.TryGetValue(id, out var categories);
            bool skin = categories != null && (string.Equals(prototype.Category, "clothing", StringComparison.OrdinalIgnoreCase)
                || string.Equals(prototype.Category, "accessory", StringComparison.OrdinalIgnoreCase)
                || categories.Any(category => category.Contains("fashion", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(category, "avatar", StringComparison.OrdinalIgnoreCase)));
            return new JObject
            {
                ["prototype_id"] = id,
                ["name"] = (string)prototype.Name,
                ["category"] = prototype.Category,
                ["shop"] = categories != null,
                ["skin"] = skin,
                ["shop_categories"] = new JArray(categories == null ? Array.Empty<string>() : categories.OrderBy(category => category).ToArray())
            };
        }));
    }

    private static Dictionary<string, HashSet<string>> ShopPrototypeCategories()
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var rows = Json.ReadFromFile<JObject>("purchaser/commodities")?["posted_commodities"] as JObject;
        if (rows == null) return result;
        foreach (var row in rows.Properties())
        {
            if (row.Value is not JObject data) continue;
            var contents = data["contents"] as JObject;
            if ((int?)data["type"] == 6)
            {
                string sourceId = (string)data["source_commodity_id"];
                if (!string.IsNullOrWhiteSpace(sourceId)) contents = rows[sourceId]?["contents"] as JObject;
            }
            if (contents == null) continue;
            string category = (string)data["ui_category"];
            foreach (string kind in new[] { "items", "weighted_items", "capsulated_modulars" })
                foreach (var token in contents[kind] as JArray ?? new JArray())
                {
                    string id = (string)token["prototype_id"];
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    if (!result.TryGetValue(id, out var categories))
                        result[id] = categories = new HashSet<string>(StringComparer.Ordinal);
                    if (!string.IsNullOrWhiteSpace(category)) categories.Add(category);
                }
        }
        return result;
    }

    public static AdminMailComposer Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 40000) throw new ArgumentException("Informe um JSON de até 40 mil caracteres.");
        var data = JObject.Parse(json);
        var allowed = new HashSet<string> { "target", "recipient_id", "subject", "message", "type", "items", "vouchers" };
        if (data.Properties().Any(p => !allowed.Contains(p.Name))) throw new ArgumentException("Campo desconhecido no JSON do email.");
        string target = String(data, "target"), subject = String(data, "subject"), message = String(data, "message");
        if (target != "player" && target != "all") throw new ArgumentException("target deve ser player ou all.");
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 120 || subject.Contains('\n') || subject.Contains('\r')) throw new ArgumentException("subject deve ter de 1 a 120 caracteres, em uma linha.");
        if (string.IsNullOrWhiteSpace(message) || message.Length > 5000) throw new ArgumentException("message deve ter de 1 a 5000 caracteres.");
        string recipient = String(data, "recipient_id", optional: true);
        if (target == "player" && string.IsNullOrWhiteSpace(recipient)) throw new ArgumentException("recipient_id é obrigatório para um jogador.");
        if (target == "all" && !string.IsNullOrWhiteSpace(recipient)) throw new ArgumentException("Remova recipient_id para enviar a todos.");
        string type = String(data, "type", optional: true) ?? "system";
        if (type != "system" && type != "purchase") throw new ArgumentException("type deve ser system ou purchase.");
        var items = new List<Item>(); var resolved = new JArray();
        if (data["items"] != null && data["items"] is not JArray) throw new ArgumentException("items deve ser uma lista.");
        var lines = (JArray)data["items"] ?? new JArray();
        if (lines.Count > 20) throw new ArgumentException("Use no máximo 20 linhas de anexos.");
        foreach (var token in lines)
        {
            if (token is not JObject line || line.Properties().Any(p => p.Name != "prototype_id" && p.Name != "name" && p.Name != "quantity" && p.Name != "level")) throw new ArgumentException("Anexo inválido.");
            string id = String(line, "prototype_id", true), name = String(line, "name", true);
            if (string.IsNullOrWhiteSpace(id))
            {
                if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Informe name ou prototype_id no anexo.");
                var matches = SingletonDict<string, List<Prototype>>.Instance.Keys.Where(key =>
                    string.Equals((string)PrototypeYaml.GetItemPrototype(key).Name, name.Trim(), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(key, name.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length != 1) throw new ArgumentException(matches.Length == 0 ? $"Item não encontrado: {name}" : $"Nome ambíguo: {name}. Use prototype_id: {string.Join(", ", matches.Take(8))}");
                id = matches[0];
            }
            var prototype = PrototypeYaml.GetItemPrototype(id);
            if (prototype == null) throw new ArgumentException($"Item não encontrado: {id}");
            if (name != null && !string.Equals(name, (string)prototype.Name, StringComparison.OrdinalIgnoreCase) && !string.Equals(name, id, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("name não corresponde ao prototype_id informado.");
            int quantity = Integer(line, "quantity", 1), level = Integer(line, "level", 1);
            if (quantity < 1 || quantity > 100 || items.Count + quantity > 200 || level < 1 || level > 60) throw new ArgumentException("Quantidade: 1 a 100 por linha, até 200 itens. Nível: 1 a 60.");
            try
            {
                for (int i = 0; i < quantity; i++) items.Add(ShopCatalog.MakeItem(new JObject { ["prototype_id"] = id, ["level"] = level }));
            }
            catch (InvalidOperationException) { throw new ArgumentException("Não foi possível montar o anexo: " + id + ". Verifique se o protótipo e o blueprint estão disponíveis."); }
            resolved.Add(new JObject { ["prototype_id"] = id, ["name"] = (string)prototype.Name, ["quantity"] = quantity, ["level"] = level });
        }
        var vouchers = new List<VoucherInfo>(); var resolvedVouchers = new JArray();
        if (data["vouchers"] != null && data["vouchers"] is not JArray) throw new ArgumentException("vouchers deve ser uma lista.");
        var voucherLines = (JArray)data["vouchers"] ?? new JArray();
        if (voucherLines.Count > 1) throw new ArgumentException("Use no máximo uma linha de voucher por email.");
        foreach (var token in voucherLines)
        {
            if (token is not JObject line || line.Properties().Any(p => p.Name != "voucher_id" && p.Name != "quantity"))
                throw new ArgumentException("Voucher inválido.");
            string voucherId = String(line, "voucher_id");
            if (!string.Equals(voucherId, CrackTuning.VoucherId, StringComparison.Ordinal))
                throw new ArgumentException("Este painel permite como voucher apenas a Pedra de Portal.");
            int quantity = Integer(line, "quantity", 1);
            if (quantity < 1 || quantity > InductionRewardTuning.Maximum)
                throw new ArgumentException($"Pedras de Portal: quantidade deve ficar entre 1 e {InductionRewardTuning.Maximum}.");
            vouchers.Add(new VoucherInfo { VoucherId = voucherId, Count = quantity });
            resolvedVouchers.Add(new JObject { ["voucher_id"] = voucherId, ["name"] = "Pedra de Portal", ["quantity"] = quantity });
        }
        return new AdminMailComposer { Subject = subject.Trim(), Text = message, RecipientId = recipient, All = target == "all",
            Type = type == "purchase" ? MailType.InAppPurchased : MailType.Event, Items = items.ToArray(), Vouchers = vouchers.ToArray(),
            ResolvedItems = resolved, ResolvedVouchers = resolvedVouchers,
            Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data.ToString(Formatting.None)))) };
    }
    private static string String(JObject data, string key, bool optional = false)
    {
        var token = data[key];
        if (token == null && optional) return null;
        if (token?.Type != JTokenType.String) throw new ArgumentException(key + " deve ser texto.");
        return (string)token;
    }
    private static int Integer(JObject data, string key, int fallback)
    {
        var token = data[key]; if (token == null) return fallback;
        if (token.Type != JTokenType.Integer || !int.TryParse(token.ToString(), out int value)) throw new ArgumentException(key + " deve ser inteiro.");
        return value;
    }
}
