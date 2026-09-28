using System;
using System.Collections.Generic;
using System.Linq;
using Durango.Utils;
using Messages;
using Newtonsoft.Json;
using PlayerFriendType = Shared.Player.FriendType;

namespace Durango.Online;

/// <summary>
/// FACILDIGITAL_STAGE5_FRIEND_STORE
/// Persistência atômica das relações de amizade.
/// </summary>
public static class FriendStore
{
    private sealed class EntryDto
    {
        [JsonProperty("entity_id")]
        public string EntityId;

        [JsonProperty("friends")]
        public Dictionary<string, int> Friends;

        [JsonProperty("received_requests")]
        public string[] ReceivedRequests;

        [JsonProperty("sent_requests")]
        public string[] SentRequests;
    }

    private sealed class State
    {
        public readonly Dictionary<string, PlayerFriendType> Friends =
            new(StringComparer.Ordinal);

        public readonly HashSet<string> Received =
            new(StringComparer.Ordinal);

        public readonly HashSet<string> Sent =
            new(StringComparer.Ordinal);

        public State Clone()
        {
            var copy = new State();

            foreach (KeyValuePair<string, PlayerFriendType> pair in Friends)
                copy.Friends[pair.Key] = pair.Value;

            copy.Received.UnionWith(Received);
            copy.Sent.UnionWith(Sent);

            return copy;
        }
    }

    private static readonly object Sync = new();

    private static readonly Dictionary<string, State> States =
        new(StringComparer.Ordinal);

    private static readonly HashSet<string> KnownPlayers =
        new(StringComparer.Ordinal);

    private static string _path;

    public static void Load(
        string path,
        IEnumerable<string> knownEntityIds)
    {
        lock (Sync)
        {
            _path = path;

            States.Clear();
            KnownPlayers.Clear();

            if (knownEntityIds != null)
            {
                foreach (string entityId in knownEntityIds)
                {
                    if (!string.IsNullOrWhiteSpace(entityId))
                        KnownPlayers.Add(entityId.Trim());
                }
            }

            EntryDto[] loaded =
                SafeSave.ReadWithBackup<EntryDto[]>(
                    path,
                    "friends",
                    bytes => Json.Read<EntryDto[]>(bytes));

            if (loaded != null)
            {
                foreach (EntryDto dto in loaded)
                {
                    if (dto == null ||
                        string.IsNullOrWhiteSpace(dto.EntityId))
                    {
                        continue;
                    }

                    string entityId = dto.EntityId.Trim();

                    if (!KnownPlayers.Contains(entityId))
                        continue;

                    State state =
                        GetOrCreateLocked(entityId);

                    if (dto.Friends != null)
                    {
                        foreach (
                            KeyValuePair<string, int> pair
                            in dto.Friends)
                        {
                            if (!IsValidTargetLocked(
                                    entityId,
                                    pair.Key))
                            {
                                continue;
                            }

                            PlayerFriendType type =
                                pair.Value ==
                                (int)PlayerFriendType.BestFriend
                                    ? PlayerFriendType.BestFriend
                                    : PlayerFriendType.JustFriend;

                            state.Friends[pair.Key] = type;
                        }
                    }

                    if (dto.ReceivedRequests != null)
                    {
                        foreach (string other
                                 in dto.ReceivedRequests)
                        {
                            if (IsValidTargetLocked(
                                    entityId,
                                    other))
                            {
                                state.Received.Add(other);
                            }
                        }
                    }

                    if (dto.SentRequests != null)
                    {
                        foreach (string other
                                 in dto.SentRequests)
                        {
                            if (IsValidTargetLocked(
                                    entityId,
                                    other))
                            {
                                state.Sent.Add(other);
                            }
                        }
                    }
                }
            }

            NormalizeLocked();

            Console.WriteLine(
                $"[friends] carregado: {States.Count} " +
                $"personagens em {path}");
        }
    }

    public static void RegisterKnownPlayer(string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId))
            return;

        lock (Sync)
        {
            string id = entityId.Trim();

            KnownPlayers.Add(id);
            GetOrCreateLocked(id);
        }
    }

    public static Social BuildSocial(string entityId)
    {
        lock (Sync)
        {
            State state =
                GetOrCreateLocked(entityId);

            return new Social
            {
                FollowingEntityIds =
                    Array.Empty<string>(),

                FriendEntities =
                    new Dictionary<string, PlayerFriendType>(
                        state.Friends,
                        StringComparer.Ordinal),

                ReceivedFriendRequests =
                    state.Received
                        .OrderBy(
                            x => x,
                            StringComparer.Ordinal)
                        .ToArray(),

                SentFriendRequests =
                    state.Sent
                        .OrderBy(
                            x => x,
                            StringComparer.Ordinal)
                        .ToArray(),

                BlockedEntityIds =
                    Array.Empty<string>(),

                FavoriteRegionOwners =
                    Array.Empty<string>()
            };
        }
    }

    public static PlayerFriendType GetFriendType(
        string ownerEntityId,
        string otherEntityId)
    {
        lock (Sync)
        {
            if (string.IsNullOrEmpty(ownerEntityId) ||
                string.IsNullOrEmpty(otherEntityId))
            {
                return PlayerFriendType.Invalid;
            }

            if (!States.TryGetValue(
                    ownerEntityId,
                    out State state))
            {
                return PlayerFriendType.Invalid;
            }

            return state.Friends.TryGetValue(
                       otherEntityId,
                       out PlayerFriendType type)
                ? type
                : PlayerFriendType.Invalid;
        }
    }

    public static bool RequestFriend(
        string senderEntityId,
        string targetEntityId,
        out bool autoAccepted,
        out string error)
    {
        autoAccepted = false;
        error = null;

        lock (Sync)
        {
            if (!ValidatePairLocked(
                    senderEntityId,
                    targetEntityId,
                    out error))
            {
                return false;
            }

            State sender =
                GetOrCreateLocked(senderEntityId);

            State target =
                GetOrCreateLocked(targetEntityId);

            if (sender.Friends.ContainsKey(targetEntityId) ||
                sender.Sent.Contains(targetEntityId))
            {
                return true;
            }

            Dictionary<string, State> backup =
                CloneStatesLocked();

            if (sender.Received.Contains(targetEntityId) &&
                target.Sent.Contains(senderEntityId))
            {
                RemovePendingLocked(
                    targetEntityId,
                    senderEntityId);

                AddFriendshipLocked(
                    senderEntityId,
                    targetEntityId);

                autoAccepted = true;
            }
            else
            {
                sender.Sent.Add(targetEntityId);
                target.Received.Add(senderEntityId);
            }

            return SaveOrRollbackLocked(
                backup,
                out error);
        }
    }

    public static bool AcceptFriendRequest(
        string receiverEntityId,
        string senderEntityId,
        out string error)
    {
        error = null;

        lock (Sync)
        {
            if (!ValidatePairLocked(
                    receiverEntityId,
                    senderEntityId,
                    out error))
            {
                return false;
            }

            State receiver =
                GetOrCreateLocked(receiverEntityId);

            State sender =
                GetOrCreateLocked(senderEntityId);

            if (receiver.Friends.ContainsKey(senderEntityId))
                return true;

            if (!receiver.Received.Contains(senderEntityId) ||
                !sender.Sent.Contains(receiverEntityId))
            {
                error =
                    "Solicitação de amizade não encontrada.";

                return false;
            }

            Dictionary<string, State> backup =
                CloneStatesLocked();

            RemovePendingLocked(
                senderEntityId,
                receiverEntityId);

            AddFriendshipLocked(
                receiverEntityId,
                senderEntityId);

            return SaveOrRollbackLocked(
                backup,
                out error);
        }
    }

    public static bool RefuseFriendRequest(
        string receiverEntityId,
        string senderEntityId,
        out string error)
    {
        error = null;

        lock (Sync)
        {
            if (!ValidatePairLocked(
                    receiverEntityId,
                    senderEntityId,
                    out error))
            {
                return false;
            }

            Dictionary<string, State> backup =
                CloneStatesLocked();

            bool changed =
                RemovePendingLocked(
                    senderEntityId,
                    receiverEntityId);

            if (!changed)
                return true;

            return SaveOrRollbackLocked(
                backup,
                out error);
        }
    }

    public static bool CancelFriendRequest(
        string senderEntityId,
        string targetEntityId,
        out string error)
    {
        error = null;

        lock (Sync)
        {
            if (!ValidatePairLocked(
                    senderEntityId,
                    targetEntityId,
                    out error))
            {
                return false;
            }

            Dictionary<string, State> backup =
                CloneStatesLocked();

            bool changed =
                RemovePendingLocked(
                    senderEntityId,
                    targetEntityId);

            if (!changed)
                return true;

            return SaveOrRollbackLocked(
                backup,
                out error);
        }
    }

    public static bool RemoveFriend(
        string entityId,
        string friendEntityId,
        out string error)
    {
        error = null;

        lock (Sync)
        {
            if (!ValidatePairLocked(
                    entityId,
                    friendEntityId,
                    out error))
            {
                return false;
            }

            Dictionary<string, State> backup =
                CloneStatesLocked();

            State a =
                GetOrCreateLocked(entityId);

            State b =
                GetOrCreateLocked(friendEntityId);

            bool changed =
                a.Friends.Remove(friendEntityId);

            changed |=
                b.Friends.Remove(entityId);

            changed |=
                RemovePendingLocked(
                    entityId,
                    friendEntityId);

            changed |=
                RemovePendingLocked(
                    friendEntityId,
                    entityId);

            if (!changed)
                return true;

            return SaveOrRollbackLocked(
                backup,
                out error);
        }
    }

    public static bool SetFriendType(
        string ownerEntityId,
        string friendEntityId,
        PlayerFriendType type,
        out string error)
    {
        error = null;

        lock (Sync)
        {
            if (!ValidatePairLocked(
                    ownerEntityId,
                    friendEntityId,
                    out error))
            {
                return false;
            }

            if (type != PlayerFriendType.JustFriend &&
                type != PlayerFriendType.BestFriend)
            {
                error =
                    "Tipo de amizade inválido.";

                return false;
            }

            State owner =
                GetOrCreateLocked(ownerEntityId);

            if (!owner.Friends.ContainsKey(friendEntityId))
            {
                error =
                    "O jogador não está na sua lista de amigos.";

                return false;
            }

            if (owner.Friends[friendEntityId] == type)
                return true;

            Dictionary<string, State> backup =
                CloneStatesLocked();

            owner.Friends[friendEntityId] = type;

            return SaveOrRollbackLocked(
                backup,
                out error);
        }
    }

    private static void NormalizeLocked()
    {
        foreach (string entityId in KnownPlayers)
            GetOrCreateLocked(entityId);

        foreach (
            KeyValuePair<string, State> pair
            in States.ToArray())
        {
            string entityId = pair.Key;
            State state = pair.Value;

            foreach (
                string other
                in state.Friends.Keys.ToArray())
            {
                if (!IsValidTargetLocked(
                        entityId,
                        other))
                {
                    state.Friends.Remove(other);
                }
            }

            state.Received.RemoveWhere(
                other =>
                    !IsValidTargetLocked(
                        entityId,
                        other));

            state.Sent.RemoveWhere(
                other =>
                    !IsValidTargetLocked(
                        entityId,
                        other));
        }

        foreach (
            KeyValuePair<string, State> pair
            in States.ToArray())
        {
            foreach (
                string friendId
                in pair.Value.Friends.Keys.ToArray())
            {
                State friend =
                    GetOrCreateLocked(friendId);

                if (!friend.Friends.ContainsKey(pair.Key))
                {
                    friend.Friends[pair.Key] =
                        PlayerFriendType.JustFriend;
                }

                pair.Value.Received.Remove(friendId);
                pair.Value.Sent.Remove(friendId);
                friend.Received.Remove(pair.Key);
                friend.Sent.Remove(pair.Key);
            }
        }

        foreach (
            KeyValuePair<string, State> pair
            in States.ToArray())
        {
            foreach (
                string targetId
                in pair.Value.Sent.ToArray())
            {
                GetOrCreateLocked(targetId)
                    .Received
                    .Add(pair.Key);
            }

            foreach (
                string senderId
                in pair.Value.Received.ToArray())
            {
                GetOrCreateLocked(senderId)
                    .Sent
                    .Add(pair.Key);
            }
        }
    }

    private static bool ValidatePairLocked(
        string a,
        string b,
        out string error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(a) ||
            string.IsNullOrWhiteSpace(b))
        {
            error = "Jogador inválido.";
            return false;
        }

        if (string.Equals(
                a,
                b,
                StringComparison.Ordinal))
        {
            error =
                "Você não pode adicionar a si mesmo.";

            return false;
        }

        if (!KnownPlayers.Contains(a) ||
            !KnownPlayers.Contains(b))
        {
            error =
                "Jogador não encontrado.";

            return false;
        }

        return true;
    }

    private static bool IsValidTargetLocked(
        string owner,
        string target)
    {
        return
            !string.IsNullOrWhiteSpace(target) &&
            !string.Equals(
                owner,
                target,
                StringComparison.Ordinal) &&
            KnownPlayers.Contains(target);
    }

    private static State GetOrCreateLocked(
        string entityId)
    {
        if (string.IsNullOrEmpty(entityId))
            return new State();

        if (!States.TryGetValue(
                entityId,
                out State state))
        {
            state = new State();
            States[entityId] = state;
        }

        return state;
    }

    private static void AddFriendshipLocked(
        string aId,
        string bId)
    {
        State a =
            GetOrCreateLocked(aId);

        State b =
            GetOrCreateLocked(bId);

        if (!a.Friends.ContainsKey(bId))
            a.Friends[bId] =
                PlayerFriendType.JustFriend;

        if (!b.Friends.ContainsKey(aId))
            b.Friends[aId] =
                PlayerFriendType.JustFriend;

        RemovePendingLocked(aId, bId);
        RemovePendingLocked(bId, aId);
    }

    private static bool RemovePendingLocked(
        string senderId,
        string receiverId)
    {
        State sender =
            GetOrCreateLocked(senderId);

        State receiver =
            GetOrCreateLocked(receiverId);

        bool changed =
            sender.Sent.Remove(receiverId);

        changed |=
            receiver.Received.Remove(senderId);

        return changed;
    }

    private static Dictionary<string, State>
        CloneStatesLocked()
    {
        var copy =
            new Dictionary<string, State>(
                StringComparer.Ordinal);

        foreach (
            KeyValuePair<string, State> pair
            in States)
        {
            copy[pair.Key] =
                pair.Value.Clone();
        }

        return copy;
    }

    private static bool SaveOrRollbackLocked(
        Dictionary<string, State> backup,
        out string error)
    {
        if (SaveLocked())
        {
            error = null;
            return true;
        }

        States.Clear();

        foreach (
            KeyValuePair<string, State> pair
            in backup)
        {
            States[pair.Key] = pair.Value;
        }

        error =
            "Falha ao salvar os dados de amizade.";

        return false;
    }

    private static bool SaveLocked()
    {
        if (string.IsNullOrEmpty(_path))
            return false;

        EntryDto[] snapshot =
            States
                .Where(
                    pair =>
                        KnownPlayers.Contains(pair.Key))
                .OrderBy(
                    pair => pair.Key,
                    StringComparer.Ordinal)
                .Select(
                    pair =>
                        new EntryDto
                        {
                            EntityId = pair.Key,

                            Friends =
                                pair.Value.Friends
                                    .OrderBy(
                                        x => x.Key,
                                        StringComparer.Ordinal)
                                    .ToDictionary(
                                        x => x.Key,
                                        x => (int)x.Value,
                                        StringComparer.Ordinal),

                            ReceivedRequests =
                                pair.Value.Received
                                    .OrderBy(
                                        x => x,
                                        StringComparer.Ordinal)
                                    .ToArray(),

                            SentRequests =
                                pair.Value.Sent
                                    .OrderBy(
                                        x => x,
                                        StringComparer.Ordinal)
                                    .ToArray()
                        })
                .ToArray();

        return SafeSave.WriteAtomic(
            _path,
            Json.WriteToBytes(
                snapshot,
                indented: true),
            "friends");
    }
}
