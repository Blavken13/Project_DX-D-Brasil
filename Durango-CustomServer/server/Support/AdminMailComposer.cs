using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Durango.Utils;
using Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shared.Economy;
using Shared.Item;
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
    public Dictionary<Currency, int> Money;
    public JArray ResolvedItems, ResolvedVouchers;

    private static readonly string[] WeaponMaterialAttributes = { "hardness_hard", "weight_light", "structure_compact" };
    private static readonly string[] ArmorMaterialAttributes = { "fiber_tough", "weight_light" };

    public static JObject ItemOptions(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || PrototypeYaml.GetItemPrototype(id) == null) throw new ArgumentException("Item não encontrado: " + id);
        Item item;
        try { item = ShopCatalog.MakeItem(new JObject { ["prototype_id"] = id, ["level"] = 60 }); }
        catch (InvalidOperationException) { throw new ArgumentException("Não foi possível montar o item: " + id); }
        var tags = Json.ReadFromFile<JObject>("tags");
        var blocks = (item.Performance ?? Array.Empty<Performance>()).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var extra = item.Tags?.Any(t => t.Id == "weapon") == true ? WeaponMaterialAttributes :
            item.Tags?.Any(t => t.Id == "armor") == true ? ArmorMaterialAttributes : Array.Empty<string>();
        var attributes = new JArray(tags.Properties().Where(p =>
        {
            var definition = p.Value;
            return !p.Name.StartsWith("reform_", StringComparison.Ordinal) &&
                ((bool?)definition["visible"] == true && definition["modifiers"] is JObject modifiers && modifiers.Count > 0 &&
                 blocks.Contains((string)definition["required_performance"] ?? "")) || extra.Contains(p.Name);
        }).OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => new JObject {
            ["id"] = p.Name, ["max_level"] = Math.Clamp((int?)p.Value["max_level"] ?? 60, 1, 60)
        }));
        bool equipment = item.Tags?.Any(t => t.Id == "weapon" || t.Id == "armor") == true;
        var support = Json.ReadFromFile<JObject>("item/tech_support");
        var improvements = new JArray(equipment && item.ReformSlots?.Length > 0
            ? support.Properties().Where(p => p.Value["tags"] is JObject && EligibleReform(item, p.Name)).OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(p => new JObject { ["id"] = p.Name, ["effects"] = new JArray(((JObject)p.Value["tags"]).Properties().Select(t => t.Name)) })
            : Enumerable.Empty<JObject>());
        return new JObject { ["attributes"] = attributes, ["improvements"] = improvements,
            ["max_improvements"] = equipment ? item.ReformSlots?.Length ?? 0 : 0 };
    }

    private static bool EligibleReform(Item item, string id)
    {
        var recipe = CraftRecipeStore.Get(id);
        if (recipe?.type != CraftType.Reform) return false;
        var slot = recipe.slots?.FirstOrDefault(s => s.slot_id == "base");
        if (slot == null) return false;
        bool matches(Dictionary<string, int> required) => required == null || required.Count == 0 ||
            required.Any(r => item.Tags?.Any(t => t.Id == r.Key && t.Level >= r.Value) == true);
        return matches(slot.required_tags) && matches(slot.required_materials);
    }

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
        var allowed = new HashSet<string> { "target", "recipient_id", "subject", "message", "type", "items", "vouchers", "money" };
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
            if (token is not JObject line || line.Properties().Any(p => p.Name != "prototype_id" && p.Name != "name" && p.Name != "quantity" && p.Name != "level" && p.Name != "attributes" && p.Name != "improvements")) throw new ArgumentException("Anexo inválido.");
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
            var options = ItemOptions(id);
            if (line["attributes"] != null && line["attributes"] is not JArray) throw new ArgumentException("attributes deve ser uma lista.");
            if (line["improvements"] != null && line["improvements"] is not JArray) throw new ArgumentException("improvements deve ser uma lista.");
            var requestedAttributes = (JArray)line["attributes"] ?? new JArray();
            var requestedImprovements = (JArray)line["improvements"] ?? new JArray();
            if (requestedAttributes.Count > 8) throw new ArgumentException("Use no máximo oito atributos por anexo.");
            if (requestedImprovements.Count > Math.Min(2, (int)options["max_improvements"]))
                throw new ArgumentException("Este item não comporta essa quantidade de melhorias.");
            var chosenAttributes = new List<Tag>();
            var eligibleAttributes = ((JArray)options["attributes"]).OfType<JObject>().ToDictionary(a => (string)a["id"]);
            foreach (var attr in requestedAttributes)
            {
                if (attr is not JObject value || value.Properties().Any(p => p.Name != "id" && p.Name != "level"))
                    throw new ArgumentException("Atributo inválido.");
                string attrId = String(value, "id");
                if (!eligibleAttributes.TryGetValue(attrId, out var definition) || chosenAttributes.Any(a => a.Id == attrId))
                    throw new ArgumentException("Atributo não disponível para este item: " + attrId);
                int attrLevel = Integer(value, "level", 1);
                if (attrLevel < 1 || attrLevel > (int)definition["max_level"])
                    throw new ArgumentException("Nível inválido para o atributo: " + attrId);
                chosenAttributes.Add(new Tag { Id = attrId, Level = attrLevel });
            }
            var eligibleImprovements = ((JArray)options["improvements"]).OfType<JObject>()
                .Select(v => (string)v["id"]).ToHashSet(StringComparer.Ordinal);
            var improvements = new List<string>();
            foreach (var improvement in requestedImprovements)
            {
                if (improvement.Type != JTokenType.String || !eligibleImprovements.Contains((string)improvement) ||
                    improvements.Contains((string)improvement))
                    throw new ArgumentException("Melhoria não disponível para este item.");
                improvements.Add((string)improvement);
            }
            try
            {
                for (int i = 0; i < quantity; i++)
                {
                    Item item = ShopCatalog.MakeItem(new JObject { ["prototype_id"] = id, ["level"] = level });
                    foreach (var attr in chosenAttributes)
                    {
                        item.Tags = (item.Tags ?? Array.Empty<Tag>()).Where(t => t.Id != attr.Id).Append(attr).ToArray();
                        item.TagModifications = (item.TagModifications ?? Array.Empty<Tag>()).Where(t => t.Id != attr.Id).Append(attr).ToArray();
                    }
                    ItemCraftModifications.Recalculate(ref item);
                    for (int slot = 0; slot < improvements.Count; slot++)
                    {
                        string improvement = improvements[slot];
                        if (!ItemCraftModifications.Apply(ref item, improvement, CraftRecipeStore.Get(improvement),
                            new Dictionary<string, Item[]>(), slot, out string error))
                            throw new ArgumentException(error ?? "Melhoria inválida: " + improvement);
                    }
                    items.Add(item);
                }
            }
            catch (InvalidOperationException) { throw new ArgumentException("Não foi possível montar o anexo: " + id + ". Verifique se o protótipo e o blueprint estão disponíveis."); }
            resolved.Add(new JObject { ["prototype_id"] = id, ["name"] = (string)prototype.Name, ["quantity"] = quantity,
                ["level"] = level, ["attributes"] = JArray.FromObject(chosenAttributes.Select(a => new { id = a.Id, level = a.Level })),
                ["improvements"] = new JArray(improvements) });
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
        var money = new Dictionary<Currency, int>();
        if (data["money"] != null)
        {
            if (data["money"] is not JObject moneyData || moneyData.Properties().Any(p => p.Name != "warp_gem"))
                throw new ArgumentException("money aceita apenas warp_gem.");
            if (moneyData["warp_gem"] != null)
            {
                int gems = Integer(moneyData, "warp_gem", 0);
                if (gems < 1 || gems > EconomyStore.BalanceLimit)
                    throw new ArgumentException("Warp Gems: quantidade deve ficar entre 1 e 99.999.999.");
                money[Currency.Gem] = gems;
            }
        }
        return new AdminMailComposer { Subject = subject.Trim(), Text = message, RecipientId = recipient, All = target == "all",
            Type = type == "purchase" ? MailType.InAppPurchased : MailType.Event, Items = items.ToArray(), Vouchers = vouchers.ToArray(), Money = money,
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
