using System;
using System.Collections.Generic;
using Durango.Network;
using Messages;

namespace Durango.Online;

/// <summary>
/// FACILDIGITAL_STAGE5_FRIEND_HANDLERS
/// Sistema persistente de amizade no Frontend.
/// Radiotower continua responsável pelo chat e pelos recursos sociais próprios dele.
/// </summary>
public partial class Player
{
    private static readonly object FriendOnlineGate = new();

    private static readonly Dictionary<string, Player> FriendOnlinePlayers =
        new(StringComparer.Ordinal);

    private const string FavoriteRegionUnavailableText =
        "A lista de ilhas favoritas ainda não está disponível.";

    private void RegisterFriendHandlers()
    {
        RegisterFriendOnlinePlayer();

        _connection.Recv(delegate(RequestFriend msg, PacketHeader header)
        {
            if (!FriendStore.RequestFriend(
                    EntityId,
                    msg.EntityId,
                    out bool autoAccepted,
                    out string error))
            {
                Send(new Abort { Text = error }, header.Seq);
                return;
            }

            Send(FriendStore.BuildSocial(EntityId), header.Seq);

            if (autoAccepted)
            {
                PushFriendMessage(
                    msg.EntityId,
                    new FriendRequestAccepted { EntityId = EntityId });

                PushFriendSocial(msg.EntityId);
            }
            else
            {
                PushFriendMessage(
                    msg.EntityId,
                    new FriendRequested { EntityId = EntityId });
            }
        });

        _connection.Recv(delegate(AcceptFriendRequest msg, PacketHeader header)
        {
            if (!FriendStore.AcceptFriendRequest(
                    EntityId,
                    msg.EntityId,
                    out string error))
            {
                Send(new Abort { Text = error }, header.Seq);
                return;
            }

            Send(FriendStore.BuildSocial(EntityId), header.Seq);

            PushFriendMessage(
                msg.EntityId,
                new FriendRequestAccepted { EntityId = EntityId });

            PushFriendSocial(msg.EntityId);
        });

        _connection.Recv(delegate(RefuseFriendRequest msg, PacketHeader header)
        {
            if (!FriendStore.RefuseFriendRequest(
                    EntityId,
                    msg.EntityId,
                    out string error))
            {
                Send(new Abort { Text = error }, header.Seq);
                return;
            }

            Send(FriendStore.BuildSocial(EntityId), header.Seq);
            PushFriendSocial(msg.EntityId);
        });

        _connection.Recv(delegate(CancelFriendRequest msg, PacketHeader header)
        {
            if (!FriendStore.CancelFriendRequest(
                    EntityId,
                    msg.EntityId,
                    out string error))
            {
                Send(new Abort { Text = error }, header.Seq);
                return;
            }

            Send(FriendStore.BuildSocial(EntityId), header.Seq);
            PushFriendSocial(msg.EntityId);
        });

        _connection.Recv(delegate(RemoveFriend msg, PacketHeader header)
        {
            if (!FriendStore.RemoveFriend(
                    EntityId,
                    msg.EntityId,
                    out string error))
            {
                Send(new Abort { Text = error }, header.Seq);
                return;
            }

            Send(FriendStore.BuildSocial(EntityId), header.Seq);
            PushFriendSocial(msg.EntityId);
        });

        _connection.Recv(delegate(SetFriendType msg, PacketHeader header)
        {
            if (!FriendStore.SetFriendType(
                    EntityId,
                    msg.EntityId,
                    msg.Type,
                    out string error))
            {
                Send(new Abort { Text = error }, header.Seq);
                return;
            }

            Send(FriendStore.BuildSocial(EntityId), header.Seq);
        });

        _connection.Recv(delegate(GetMyFriendType msg, PacketHeader header)
        {
            Send(new FriendType
            {
                _FriendType =
                    FriendStore.GetFriendType(msg.EntityId, EntityId)
            }, header.Seq);
        });

        _connection.Recv(delegate(AddFavoriteRegionOwners msg, PacketHeader header)
        {
            Send(new Abort
            {
                Text = FavoriteRegionUnavailableText
            }, header.Seq);
        });

        _connection.Recv(delegate(RemoveFavoriteRegionOwners msg, PacketHeader header)
        {
            Send(new Abort
            {
                Text = FavoriteRegionUnavailableText
            }, header.Seq);
        });

        _connection.Recv(delegate(SetSocialOptions msg, PacketHeader header)
        {
        });
    }

    private void RegisterFriendOnlinePlayer()
    {
        lock (FriendOnlineGate)
        {
            FriendOnlinePlayers[EntityId] = this;
        }

        _connection.ConnetionClosed += delegate
        {
            lock (FriendOnlineGate)
            {
                if (FriendOnlinePlayers.TryGetValue(
                        EntityId,
                        out Player current) &&
                    ReferenceEquals(current, this))
                {
                    FriendOnlinePlayers.Remove(EntityId);
                }
            }
        };
    }

    private static Player FindFriendOnlinePlayer(string entityId)
    {
        if (string.IsNullOrEmpty(entityId))
            return null;

        lock (FriendOnlineGate)
        {
            FriendOnlinePlayers.TryGetValue(
                entityId,
                out Player player);

            return player;
        }
    }

    private static void PushFriendSocial(string entityId)
    {
        Player player = FindFriendOnlinePlayer(entityId);
        player?.Send(FriendStore.BuildSocial(entityId));
    }

    private static void PushFriendMessage<T>(string entityId, T msg)
    {
        Player player = FindFriendOnlinePlayer(entityId);
        player?.Send(msg);
    }
}
