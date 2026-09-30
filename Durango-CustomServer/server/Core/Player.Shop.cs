using System;
using Durango.Network;
using Messages;

namespace Durango.Online;

public partial class Player
{
    private void RegisterShopHandlers()
    {
        _connection.Recv(delegate(GetCommodities m, PacketHeader h)
        { Send(new Commodities { CommodityInfos = _economy?.Commodities(EntityId) ?? Array.Empty<CommodityInfo>() }, h.Seq); });
        _connection.Recv(delegate(GetPurchases m, PacketHeader h) { SendShopPurchases(); });
        _connection.Recv(delegate(GetSpecialDeals m, PacketHeader h) { Send(new SpecialDeals { Deals = Array.Empty<SpecialDeal>() }, h.Seq); });
        _connection.Recv(delegate(GetAcceptableSubPurchases m, PacketHeader h) { Send(new AcceptableSubPurchases { Ids = Array.Empty<AcceptableSubPurchase>() }); });
        _connection.Recv(delegate(GetUserFirstPurchaseHistory m, PacketHeader h)
        { Send(new UserFirstPurchaseHistory { _UserFirstPurchaseHistory = _economy?.FirstPurchases(EntityId) ?? Array.Empty<UserFirstPurchase>() }, h.Seq); });
        _connection.Recv(delegate(PurchaseCommodity m, PacketHeader h)
        {
            if (!EconomyAvailable(h.Seq)) return;
            if (!_economy.Purchase(_context, m.CommodityId, out var purchases, out var items, out string error)) { Send(new Abort { Text = error }, h.Seq); return; }
            if (items.Length > 0) SendEconomyInventory(items);
            SendWalletNow();
            Send(new Purchased { Purchases = purchases }, h.Seq);
            SendShopPurchases();
        });
        _connection.Recv(delegate(AcceptPurchase m, PacketHeader h)
        {
            if (!string.IsNullOrEmpty(m.SubId)) { Send(new Abort { Text = "Esta compra nao possui uma recompensa parcial disponivel." }, h.Seq); return; }
            AcceptShopPurchase(m.PurchaseId ?? "", h.Seq);
        });
        _connection.Recv(delegate(AcceptAllPurchases m, PacketHeader h) { AcceptShopPurchase(null, h.Seq); });
        _connection.Recv(delegate(PurchaseCommodityWithVoucher m, PacketHeader h)
        { Send(new Abort { Text = "Este servidor usa saldos de moedas do jogo. Vales de compra externos nao estao disponiveis." }, h.Seq); });
        _connection.Recv(delegate(PurchaseCommodityWithSteamDlc m, PacketHeader h)
        { Send(new Abort { Text = "Compras de DLC com pagamento externo nao estao disponiveis." }, h.Seq); });
        _connection.Recv(delegate(AcceptTENCoupon m, PacketHeader h)
        { Send(new Abort { Text = "Cupons da Nexon nao sao validos neste servidor." }, h.Seq); });
        _connection.Recv(delegate(TransferDurangoCoin m, PacketHeader h)
        { Send(new Abort { Text = "A transferencia de Durango Coins entre jogadores ainda esta desativada no Alpha." }, h.Seq); });
    }

    private void SendShopPurchases() => Send(new Purchases { _Purchases = _economy?.Purchases(EntityId) ?? Array.Empty<Purchase>() });

    private void AcceptShopPurchase(string id, uint seq)
    {
        if (!EconomyAvailable(seq)) return;
        if (!_economy.Accept(_context, id, out var items, out string error))
        {
            if (id == null && _economy.Purchases(EntityId).Length == 0) Send(default(OK), seq);
            else Send(new Abort { Text = error }, seq);
            return;
        }
        SendEconomyInventory(items);
        SendWalletNow();
        Send(default(OK), seq);
        SendShopPurchases();
    }
}
