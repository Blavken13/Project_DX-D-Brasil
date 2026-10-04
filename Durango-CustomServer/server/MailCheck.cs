using System;
using System.IO;
using System.Linq;
using Durango.Logic.Clusters;
using Durango.Online;
using Durango.Utils;
using Messages;
using Newtonsoft.Json.Linq;
using Yaml;
using Yaml.Util;

namespace DurangoServerNx;

internal static class MailCheck
{
    private static int _passed;
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); _passed++; Console.WriteLine("[mail-check] OK " + message); }

    public static int Run(string dataDir)
    {
        string root = Path.Combine(Path.GetTempPath(), "Durango-mail-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Json.DataDir = dataDir; MoCatalog.Load(dataDir); DataStore.Load(dataDir);
            TerrainLoader.TerrainDir = Path.Combine(dataDir, "terrains");
            WorkbenchTags.AssetsDir = Path.Combine(dataDir, "assets");
            var wc = new WorldContext { TerrainId = "pe10gr_1" }; wc.Initialize(Path.Combine(root, "test.world"));
            var world = new World(wc); var shop = new ShopCatalog();
            var economy = new EconomyStore(Path.Combine(root, "economy.json"), shop);
            string path = Path.Combine(root, "mail.json"); var mail = new MailStore(path);
            var alice = Context(root, "alice"); var bob = Context(root, "bob");
            string before = Json.Write(alice);
            var adminCatalog = AdminMailComposer.ItemCatalog().OfType<JObject>().ToArray();
            var shopRows = adminCatalog.Where(row => (bool?)row["shop"] == true).ToArray();
            var skinRows = adminCatalog.Where(row => (bool?)row["skin"] == true).ToArray();
            Check(shopRows.Length == 357, "catálogo administrativo carrega os 357 protótipos referenciados pela loja");
            Check(skinRows.Length == 166, "catálogo administrativo carrega as 166 skins/roupas/acessórios da loja");
            Check(shopRows.Any(row => (string)row["prototype_id"] == "metal_set")
                && shopRows.Any(row => (string)row["prototype_id"] == "rope")
                && !skinRows.Any(row => (string)row["prototype_id"] == "metal_set")
                && !skinRows.Any(row => (string)row["prototype_id"] == "rope"),
                "materiais com subcategoria material_clothes permanecem na loja sem serem classificados como skin");
            foreach (var skin in skinRows)
            {
                string skinId = (string)skin["prototype_id"];
                try
                {
                    var skinMail = AdminMailComposer.Parse(new JObject { ["target"] = "player", ["recipient_id"] = alice.EntityId,
                        ["subject"] = "Skin de teste", ["message"] = "Prévia", ["type"] = "purchase",
                        ["items"] = new JArray(new JObject { ["prototype_id"] = skinId, ["quantity"] = 1, ["level"] = 1 }),
                        ["vouchers"] = new JArray() }.ToString());
                    if (skinMail.Items.Length != 1) throw new InvalidOperationException("quantidade inesperada");
                }
                catch (Exception error)
                {
                    throw new InvalidOperationException("Skin da loja não pode ser anexada: " + skinId, error);
                }
            }
            Check(skinRows.Length > 0, "todas as skins listadas podem ser montadas como anexo");
            string prototype = SingletonDict<string, System.Collections.Generic.List<Prototype>>.Instance.Keys.First(id =>
                ItemTradeRules.PrototypeCanTrade(id) && PrototypeYaml.GetItemPrototype(id).Size <= 2 && !id.StartsWith("capsulated_"));
            string payload = new JObject { ["target"] = "player", ["recipient_id"] = alice.EntityId,
                ["subject"] = "Sua compra chegou", ["message"] = "Resgate os itens na mochila.", ["type"] = "purchase",
                ["items"] = new JArray(new JObject { ["name"] = prototype, ["quantity"] = 2, ["level"] = 10 }) }.ToString();
            var composed = AdminMailComposer.Parse(payload);
            Check(composed.Items.Length == 2 && composed.Items.All(i => i.Prototype == prototype), "JSON resolve nome/ID, quantidade e nível");
            bool invalid = false;
            try { AdminMailComposer.Parse(payload.Replace(prototype, "missing_item_for_test")); } catch (ArgumentException) { invalid = true; }
            Check(invalid, "item desconhecido recusado antes de enviar");
            bool invalidVoucher = false;
            try
            {
                AdminMailComposer.Parse(new JObject { ["target"] = "player", ["recipient_id"] = alice.EntityId,
                    ["subject"] = "Voucher inválido", ["message"] = "Teste", ["type"] = "purchase", ["items"] = new JArray(),
                    ["vouchers"] = new JArray(new JObject { ["voucher_id"] = "voucher_invalid_for_test", ["quantity"] = 1 }) }.ToString());
            }
            catch (ArgumentException) { invalidVoucher = true; }
            Check(invalidVoucher, "voucher administrativo desconhecido recusado antes de enviar");
            using var link = new EconomyProtocolCheck.Link(alice, world, economy, mailStore: mail);
            // Isolate mail attachments from the normal starter gifts granted at login.
            alice.InventoryItems.Clear(); before = Json.Write(alice);
            Check(link.Messages.OfType<Mails>().Any(m => m._Mails.Length == 0), "TCP envia caixa real no login");
            mail.Updated += id => { if (id == alice.EntityId) link.Player.NotifyMailDelivery(); };
            Check(mail.Dispatch("purchase-test", composed.Fingerprint, new[] { alice.EntityId, bob.EntityId }, composed.Subject,
                composed.Text, composed.Type, composed.Items, out var dispatch, out _) && dispatch.Recipients == 2,
                "envio em lote confirmado para jogadores online e offline");
            link.PumpUntil(() => link.Messages.OfType<MailPut>().Any());
            Check(link.Messages.OfType<MailPut>().Last().Mail.Text == "Sua compra chegou\nResgate os itens na mochila.", "TCP recebe título e conteúdo por MailPut");
            var inbox = mail.Inbox(alice.EntityId); string id = inbox.Single().Id;
            Check(inbox.Single().MailType == Shared.Mailing.MailType.InAppPurchased && inbox.Single().AttachedItems.Length == 2,
                "email de compra inclui anexos na categoria Loja");
            Check(!inbox.Single().AttachedItems.Select(i => i.Id).Intersect(mail.Inbox(bob.EntityId).Single().AttachedItems.Select(i => i.Id)).Any(), "cada destinatário tem IDs de anexos próprios");
            Check(mail.Dispatch("purchase-test", composed.Fingerprint, new[] { alice.EntityId, bob.EntityId }, composed.Subject,
                composed.Text, composed.Type, composed.Items, out dispatch, out _) && dispatch.Duplicate && mail.Inbox(alice.EntityId).Length == 1,
                "repetição administrativa não duplica mensagens");
            Check(!mail.Dispatch("purchase-test", "other", new[] { alice.EntityId }, composed.Subject, composed.Text, composed.Type, composed.Items, out _, out _), "request_id conflitante recusado");
            link.Request<AcceptMails, Abort>(new AcceptMails { MailIds = new[] { mail.Inbox(bob.EntityId).Single().Id } });
            Check(alice.InventoryItems.Count == 0, "TCP recusa resgate de mensagem de outro jogador");
            link.Request<DeleteMails, Abort>(new DeleteMails { MailIds = new[] { id } });
            Check(mail.Inbox(alice.EntityId).Length == 1, "anexos pendentes protegidos da exclusão");
            var ballast = composed.Items[0]; ballast.Id = "test-ballast"; ballast.Size = Player.InventoryMaxSize;
            alice.InventoryItems.Add(ballast);
            link.Request<AcceptMails, Abort>(new AcceptMails { MailIds = new[] { id } });
            Check(alice.InventoryItems.Count == 1 && !mail.Inbox(alice.EntityId).Single().Accepted, "mochila cheia preserva anexos sem entrega parcial");
            alice.InventoryItems.Clear();
            link.Send(new MarkMailsAsRead { MailIds = new[] { id } });
            link.PumpUntil(() => mail.Inbox(alice.EntityId).Single().Read);
            Check(new MailStore(path).Inbox(alice.EntityId).Single().Read, "leitura persistida pelo protocolo TCP");
            link.Request<AcceptMails, OK>(new AcceptMails { MailIds = new[] { id } });
            Check(alice.InventoryItems.Count == 2 && mail.Inbox(alice.EntityId).Single().Accepted, "TCP resgata anexos na mochila e marca aceitação");
            Check(link.Messages.OfType<InventoryUpdated>().Any(m => m.Items?.Length == 2), "cliente recebe InventoryUpdated com os anexos");
            link.Request<AcceptMails, OK>(new AcceptMails { MailIds = new[] { id } });
            Check(alice.InventoryItems.Count == 2, "resgate repetido não duplica itens");
            var response = link.Request<GetMails, Mails>(default);
            Check(response._Mails.Single().Accepted && response._Mails.Single().AcceptedEntityId == alice.EntityId, "consulta TCP retorna estado aceito");
            Check(SafeSave.FlushPending(), "save de inventário e checkpoint concluído");
            var restored = PlayerContext.Load(alice.Path); new MailStore(path).Recover(restored);
            Check(restored.InventoryItems.Count == 2, "reinício preserva inventário sem nova concessão");
            var interrupted = Json.Read<PlayerContext>(before); interrupted.Initialize(Path.Combine(root, "interrupted.player"));
            var reloadedMail = new MailStore(path); reloadedMail.Recover(interrupted); reloadedMail.Recover(interrupted);
            Check(interrupted.InventoryItems.Count == 2, "resgate confirmado recupera save antigo uma única vez");
            string itemId = alice.InventoryItems[0].Id;
            Check(economy.Register(alice, "test", new[] { itemId }, 10, 86400, out _, out _), "item resgatado pode ser anunciado no mercado");
            var crashedSeller = Json.Read<PlayerContext>(before); crashedSeller.Initialize(Path.Combine(root, "crashed-seller.player"));
            reloadedMail.Recover(crashedSeller); economy.Recover(crashedSeller);
            Check(crashedSeller.InventoryItems.All(i => i.Id != itemId) && crashedSeller.InventoryItems.Count == 1,
                "recuperação do correio antes da economia preserva retirada posterior do item");
            link.Request<DeleteMails, OK>(new DeleteMails { MailIds = new[] { id } });
            Check(mail.Inbox(alice.EntityId).Length == 0 && new MailStore(path).Inbox(alice.EntityId).Length == 0, "exclusão persistida após o resgate");
            Check(new MailStore(path).Inbox(bob.EntityId).Length == 1, "mensagem offline preservada para o próximo login");

            string voucherPayload = new JObject { ["target"] = "player", ["recipient_id"] = alice.EntityId,
                ["subject"] = "Pedras de Portal", ["message"] = "Presente para teste de crateras.", ["type"] = "purchase",
                ["items"] = new JArray(), ["vouchers"] = new JArray(new JObject {
                    ["voucher_id"] = CrackTuning.VoucherId, ["quantity"] = 2 }) }.ToString();
            var voucherMail = AdminMailComposer.Parse(voucherPayload);
            Check(voucherMail.Items.Length == 0 && voucherMail.Vouchers.Length == 1
                && voucherMail.Vouchers[0].VoucherId == CrackTuning.VoucherId && voucherMail.Vouchers[0].Count == 2,
                "JSON aceita Pedra de Portal como voucher nativo");
            Check(mail.Dispatch("portal-stone-test", voucherMail.Fingerprint, new[] { alice.EntityId }, voucherMail.Subject,
                voucherMail.Text, voucherMail.Type, voucherMail.Items, voucherMail.Vouchers, out _, out _),
                "Pedras de Portal enviadas pelo correio administrativo");
            link.PumpUntil(() => link.Messages.OfType<MailPut>().Any(m =>
                m.Mail.AttachedVouchers?.Any(v => v.VoucherId == CrackTuning.VoucherId && v.Count == 2) == true));
            var voucherInbox = mail.Inbox(alice.EntityId).Single();
            Check(voucherInbox.AttachedItems.Length == 0 && voucherInbox.AttachedVouchers.Single().Count == 2,
                "caixa TCP publica voucher separado dos itens");
            string voucherMailId = voucherInbox.Id;
            alice.Vouchers[CrackTuning.VoucherId] = InductionRewardTuning.Maximum - 1;
            link.Request<AcceptMails, Abort>(new AcceptMails { MailIds = new[] { voucherMailId } });
            Check(!mail.Inbox(alice.EntityId).Single().Accepted && alice.Vouchers[CrackTuning.VoucherId] == InductionRewardTuning.Maximum - 1,
                "carteira de pedras cheia preserva o anexo sem entrega parcial");
            alice.Vouchers[CrackTuning.VoucherId] = 0;
            link.Request<AcceptMails, OK>(new AcceptMails { MailIds = new[] { voucherMailId } });
            Check(alice.Vouchers[CrackTuning.VoucherId] == 2 && mail.Inbox(alice.EntityId).Single().Accepted,
                "resgate do email credita Pedras de Portal na carteira");
            Check(link.Messages.OfType<WalletUpdated>().Any(m =>
                m.Wallet.Vouchers?.Any(v => v.VoucherId == CrackTuning.VoucherId && v.Count == 2) == true),
                "cliente recebe WalletUpdated após resgatar Pedras de Portal");
            link.Request<AcceptMails, OK>(new AcceptMails { MailIds = new[] { voucherMailId } });
            Check(alice.Vouchers[CrackTuning.VoucherId] == 2, "resgate repetido não duplica Pedras de Portal");
            link.Request<DeleteMails, OK>(new DeleteMails { MailIds = new[] { voucherMailId } });
            Check(mail.Inbox(alice.EntityId).Length == 0, "email de Pedras de Portal pode ser excluído após o resgate");

            string capsuleId = SingletonDict<string, System.Collections.Generic.List<Prototype>>.Instance.Keys.First(id =>
                id.StartsWith("capsulated_", StringComparison.Ordinal) && BlueprintStore.GetBlueprint(id["capsulated_".Length..]) != null);
            var capsuleMail = AdminMailComposer.Parse(payload.Replace(prototype, capsuleId));
            Check(capsuleMail.Items.All(i => i.Ext is ArtifactCapsule), "anexos de cápsulas usam o mesmo construtor da loja");
            Check(mail.Dispatch("capsule-test", capsuleMail.Fingerprint, new[] { alice.EntityId, bob.EntityId },
                capsuleMail.Subject, capsuleMail.Text, capsuleMail.Type, capsuleMail.Items, out _, out _), "cápsulas enviadas para os dois personagens");
            var capsuleA = mail.Inbox(alice.EntityId).First().AttachedItems;
            var capsuleB = mail.Inbox(bob.EntityId).First().AttachedItems;
            Check(capsuleA.Concat(capsuleB).Select(i => ((ArtifactCapsule)i.Ext).EntityId).Distinct().Count() == 4,
                "entidades de cápsulas não são compartilhadas entre anexos nem destinatários");
            link.PumpUntil(() => link.Messages.OfType<MailPut>().Any(m => m.Mail.AttachedItems?.Any(i => i.Ext is ArtifactCapsule) == true));
            Check(new MailStore(path).Inbox(alice.EntityId).First().AttachedItems.All(i => i.Ext is ArtifactCapsule),
                "cápsulas atravessam o protocolo e preservam extensão após reinício");
            string blocked = Path.Combine(root, "blocked"); File.WriteAllText(blocked, "test");
            var unavailable = new MailStore(Path.Combine(blocked, "mail.json"));
            Check(!unavailable.Dispatch("disk-failure", composed.Fingerprint, new[] { alice.EntityId }, composed.Subject,
                composed.Text, composed.Type, composed.Items, out _, out _) && unavailable.Inbox(alice.EntityId).Length == 0,
                "falha de gravação não cria uma entrega em memória");
            Console.WriteLine($"[mail-check] PASS {_passed} verificações"); return 0;
        }
        catch (Exception error) { Console.WriteLine("[mail-check] FAIL " + error); return 1; }
        finally
        {
            SafeSave.FlushPending();
            if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.Ordinal)
                || !Path.GetFileName(root).StartsWith("Durango-mail-check-", StringComparison.Ordinal)) throw new InvalidOperationException("Pasta de teste inválida.");
            Directory.Delete(root, recursive: true);
        }
    }

    private static PlayerContext Context(string root, string id)
    {
        var context = new PlayerContext { PlayerInfo = new Durango.Logic.Clusters.PlayerInfo { PlayerEntityId = id, PlayerName = id, PlayerLevel = 10 }, OwnerKey = "test-" + id };
        context.Initialize(Path.Combine(root, id + ".player")); context.TStone = 5000; return context;
    }
}
