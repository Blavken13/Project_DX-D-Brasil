using System;
using System.Linq;
using Durango.Network;
using Messages;

namespace Durango.Online;

public partial class Player
{
    private void RegisterMailHandlers()
    {
        _connection.Recv(delegate(GetMails _, PacketHeader header) { SendMailbox(header.Seq); });
        _connection.Recv(delegate(AcceptMails msg, PacketHeader header)
        {
            if (_mailStore == null) { Send(new Abort { Text = "Correio não configurado." }, header.Seq); return; }
            if (!_mailStore.Accept(_context, msg.MailIds, out var items, out var vouchers, out string error))
            { Send(new Abort { Text = error }, header.Seq); return; }
            if (items.Length > 0) Send(new InventoryUpdated { EntityId = EntityId, Items = items });
            if (vouchers.Length > 0) SendWalletNow(); else OnContextChanged();
            SendMailbox(); Send(default(OK), header.Seq);
        });
        _connection.Recv(delegate(DeleteMails msg, PacketHeader header)
        {
            if (_mailStore == null || !_mailStore.Delete(EntityId, msg.MailIds, out _))
            { Send(new Abort { Text = "Não foi possível excluir. Resgate os anexos antes de excluir a mensagem." }, header.Seq); return; }
            SendMailbox(); Send(default(OK), header.Seq);
        });
        _connection.Recv(delegate(MarkMailsAsRead msg, PacketHeader header) { _mailStore?.MarkRead(EntityId, msg.MailIds, out _); });
        // Player transfers need inventory escrow; administrative delivery never
        // accepts client-supplied attachment IDs or claims a successful transfer.
        _connection.Recv(delegate(SendMail _, PacketHeader header) { Send(new Abort { Text = "O envio entre jogadores não está habilitado. O correio administrativo está disponível." }, header.Seq); });
        _connection.Recv(delegate(AcceptUserMails _, PacketHeader header) { Send(new Abort { Text = "Mensagem de jogador não encontrada." }, header.Seq); });
        _connection.Recv(delegate(DeleteUserMails _, PacketHeader header) { Send(new Abort { Text = "Mensagem de jogador não encontrada." }, header.Seq); });
        _connection.Recv(delegate(MarkUserMailsAsRead _, PacketHeader __) { });
    }

    private void SendMailbox(uint seq = 0)
    {
        if (_mailStore != null) Send(new Mails { _Mails = _mailStore.Inbox(EntityId), UserMails = Array.Empty<Mail>() }, seq);
    }

    public void NotifyMailDelivery()
    {
        var newest = _mailStore?.Inbox(EntityId).FirstOrDefault();
        if (newest.HasValue && !string.IsNullOrEmpty(newest.Value.Id)) Send(new MailPut { Mail = newest.Value });
    }
}
