#!/usr/bin/env python3
'''
Facildigital+ — Etapas 3, 4 e 5

Aplica:
- Etapa 3: GET /players?name=&freq= com semântica correta do cliente;
- Etapa 4: GET /online_statuses robusto;
- Etapa 5: FriendStore persistente + handlers + notificações online.

Arquivos alterados:
- Durango-CustomServer/server/Core/Gateway.cs
- Durango-CustomServer/server/Core/Host.cs
- Durango-CustomServer/server/Core/Player.Social.cs
- Durango-CustomServer/server/Core/Player.Friend.cs
- Durango-CustomServer/server/Support/FriendStore.cs (novo)

Uso:
    py -3 facildigital_patch_social_stage3_5.py --dry-run
    py -3 facildigital_patch_social_stage3_5.py --backup --build
'''

from __future__ import annotations

import argparse
import difflib
import shutil
import subprocess
import sys
from pathlib import Path

SERVER = Path("Durango-CustomServer/server")
GATEWAY = SERVER / "Core/Gateway.cs"
HOST = SERVER / "Core/Host.cs"
SOCIAL = SERVER / "Core/Player.Social.cs"
FRIEND = SERVER / "Core/Player.Friend.cs"
FRIEND_STORE = SERVER / "Support/FriendStore.cs"

ANALYZED_HEAD = "31c1f8a33b75dd16866d2d64f1d6ed0c358d066b"


class PatchError(RuntimeError):
    pass


def run(cmd: list[str], cwd: Path) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        cmd,
        cwd=str(cwd),
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
    )


def root_of(explicit: str | None) -> Path:
    if explicit:
        root = Path(explicit).expanduser().resolve()
        if not (root / SERVER).is_dir():
            raise PatchError(f"Não encontrei {SERVER} em {root}")
        return root

    start = Path.cwd().resolve()
    for candidate in (start, *start.parents):
        if (candidate / SERVER).is_dir():
            return candidate

    raise PatchError(
        "Execute o script na raiz de Project_DX-D-Brasil "
        "ou informe --root <caminho>."
    )


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise PatchError(
            f"{label}: âncora esperada exatamente 1 vez; encontrada {count}."
        )
    return text.replace(old, new, 1)


def diff(rel: Path, before: str, after: str) -> str:
    return "".join(
        difflib.unified_diff(
            before.splitlines(keepends=True),
            after.splitlines(keepends=True),
            fromfile=str(rel),
            tofile=str(rel),
        )
    )


def patch_gateway(text: str) -> str:
    if "FACILDIGITAL_STAGE3_PLAYER_SEARCH" not in text:
        old = '''        // FACILDIGITAL+: busca pública de jogadores por nome.
        // PlayerInfoManager.SearchPlayerInfos usa GET /players?name=...&freq=...
        _webServer.GetRoute["/players"] = delegate(HttpListenerRequest request, Dictionary<string, string> _)
        {
            string search = (request.QueryString.Get("name") ?? string.Empty).Trim();
            string freqText = (request.QueryString.Get("freq") ?? string.Empty).Trim();
            var players = new JArray();
            if (search.Length == 0)
                return new WebServer.JsonResponse(new JObject { ["players"] = players }.ToString());
            if (search.Length > 64) search = search.Substring(0, 64);

            bool filterFreq = int.TryParse(freqText, out int requestedFreq);
            int emitted = 0;
            foreach (var hostContext in _host.Contexts)
            {
                PlayerContext player = hostContext?.Player;
                if (player == null || string.IsNullOrEmpty(player.EntityId)) continue;
                string name = player.PlayerInfo?.PlayerName ?? player.AppearPlayer.Name ?? string.Empty;
                if (name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                int freq = player.AppearPlayer.Freq;
                if (filterFreq && freq != requestedFreq) continue;

                players.Add(new JObject
                {
                    ["entity_id"] = player.EntityId,
                    ["freq"] = freq,
                    ["name"] = name
                });
                if (++emitted >= 20) break;
            }
            return new WebServer.JsonResponse(new JObject { ["players"] = players }.ToString());
        };
'''
        new = '''        // FACILDIGITAL_STAGE3_PLAYER_SEARCH
        // PlayerInfoManager.SearchPlayerInfos usa nome parcial e frequência textual em 4 dígitos.
        _webServer.GetRoute["/players"] = delegate(HttpListenerRequest request, Dictionary<string, string> _)
        {
            string search = (request.QueryString.Get("name") ?? string.Empty).Trim();
            string freqText = (request.QueryString.Get("freq") ?? string.Empty).Trim();
            var players = new JArray();

            if (search.Length == 0)
                return new WebServer.JsonResponse(new JObject { ["players"] = players }.ToString());

            if (search.Length > 64) search = search.Substring(0, 64);
            if (freqText.Length > 4) freqText = freqText.Substring(0, 4);

            if (freqText.Length > 0 && freqText.Any(c => c < '0' || c > '9'))
                return new WebServer.JsonResponse(new JObject { ["players"] = players }.ToString());

            var matches = new List<(PlayerContext Player, string Name, int Freq, int Rank)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var hostContext in _host.Contexts)
            {
                PlayerContext player = hostContext?.Player;
                if (player == null || string.IsNullOrEmpty(player.EntityId) || !seen.Add(player.EntityId))
                    continue;

                string name =
                    player.PlayerInfo?.PlayerName ??
                    player.AppearPlayer.Name ??
                    string.Empty;

                if (name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                int freq = player.AppearPlayer.Freq;
                if (freqText.Length > 0 &&
                    freq.ToString("D4").IndexOf(freqText, StringComparison.Ordinal) < 0)
                    continue;

                int rank = string.Equals(name, search, StringComparison.OrdinalIgnoreCase)
                    ? 0
                    : name.StartsWith(search, StringComparison.OrdinalIgnoreCase)
                        ? 1
                        : 2;

                matches.Add((player, name, freq, rank));
            }

            foreach (var match in matches
                         .OrderBy(x => x.Rank)
                         .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(x => x.Freq)
                         .Take(50))
            {
                players.Add(new JObject
                {
                    ["entity_id"] = match.Player.EntityId,
                    ["freq"] = match.Freq,
                    ["name"] = match.Name
                });
            }

            Console.WriteLine(
                $"[player-search] name='{search}' freq='{freqText}' results={players.Count}");

            return new WebServer.JsonResponse(
                new JObject { ["players"] = players }.ToString());
        };
'''
        text = replace_once(text, old, new, "Gateway etapa 3")

    if "FACILDIGITAL_STAGE4_ONLINE_STATUSES" not in text:
        old = '''        // FACILDIGITAL+: status online em lote para PlayerInfoManager.RequestPlayersConnected.
        _webServer.GetRoute["/online_statuses"] = delegate(HttpListenerRequest request, Dictionary<string, string> _)
        {
            string[] entityIds = request.QueryString.GetValues("entity_id") ?? Array.Empty<string>();
            var result = new JObject();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string rawId in entityIds)
            {
                string entityId = (rawId ?? string.Empty).Trim();
                if (entityId.Length == 0 || !seen.Add(entityId)) continue;
                PlayerContext player = _host.FindContextByEntityId(entityId) ?? _gameServer.GetPlayerContext(entityId);
                bool online = _gameServer.IsPlayerOnline(entityId);
                double? disconnectedAt = player?.PlayerInfo?.DisconnectedAt;
                var status = new JObject { ["online"] = online };
                status["disconnected_at"] = disconnectedAt.HasValue && disconnectedAt.Value > 0
                    ? new JValue(disconnectedAt.Value)
                    : JValue.CreateNull();
                result[entityId] = status;
            }
            return new WebServer.JsonResponse(result.ToString());
        };
'''
        new = '''        // FACILDIGITAL_STAGE4_ONLINE_STATUSES
        // PlayerInfoManager envia vários entity_id e espera:
        // entity_id -> { online, disconnected_at }.
        _webServer.GetRoute["/online_statuses"] = delegate(HttpListenerRequest request, Dictionary<string, string> _)
        {
            string[] entityIds =
                request.QueryString.GetValues("entity_id") ??
                Array.Empty<string>();

            var result = new JObject();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            int accepted = 0;
            foreach (string rawId in entityIds)
            {
                if (accepted >= 100) break;

                string entityId = (rawId ?? string.Empty).Trim();
                if (entityId.Length == 0 ||
                    entityId.Length > 128 ||
                    !seen.Add(entityId))
                {
                    continue;
                }

                accepted++;

                PlayerContext player =
                    _host.FindContextByEntityId(entityId) ??
                    _gameServer.GetPlayerContext(entityId);

                bool online = _gameServer.IsPlayerOnline(entityId);
                double? disconnectedAt = player?.PlayerInfo?.DisconnectedAt;

                result[entityId] = new JObject
                {
                    ["online"] = online,
                    ["disconnected_at"] =
                        disconnectedAt.HasValue && disconnectedAt.Value > 0
                            ? new JValue(disconnectedAt.Value)
                            : JValue.CreateNull()
                };
            }

            return new WebServer.JsonResponse(result.ToString());
        };
'''
        text = replace_once(text, old, new, "Gateway etapa 4")

    return text


def patch_host(text: str) -> str:
    if "FACILDIGITAL_STAGE5_FRIEND_STORE" not in text:
        old = '''        // Auth local: contas persistem ao lado dos saves do cluster.
        AccountStore.Load(System.IO.Path.Combine(AppData.CombinePath(basePath), "accounts.json"));
'''
        new = '''        // Auth local: contas persistem ao lado dos saves do cluster.
        AccountStore.Load(System.IO.Path.Combine(AppData.CombinePath(basePath), "accounts.json"));

        // FACILDIGITAL_STAGE5_FRIEND_STORE
        // Relações sociais ficam fora do save individual para permitir mutações
        // atômicas que envolvem dois personagens.
        FriendStore.Load(
            System.IO.Path.Combine(AppData.CombinePath(basePath), "friends.json"),
            _contexts.Select(c => c.EntityId));
'''
        text = replace_once(text, old, new, "Host FriendStore.Load")

    if "FriendStore.RegisterKnownPlayer(context.EntityId)" not in text:
        old = '''        _contexts.Add(new Context(_worldCtx, context));
        Console.WriteLine($"[host] ผู้เล่น '{context.PlayerInfo.PlayerName}' ({context.EntityId}) → สล็อต {slot}");
        return context;
'''
        new = '''        _contexts.Add(new Context(_worldCtx, context));
        FriendStore.RegisterKnownPlayer(context.EntityId);
        Console.WriteLine($"[host] ผู้เล่น '{context.PlayerInfo.PlayerName}' ({context.EntityId}) → สล็อต {slot}");
        return context;
'''
        text = replace_once(text, old, new, "Host register friend player")

    return text


def patch_social(text: str) -> str:
    if "FACILDIGITAL_STAGE5_GET_SOCIAL" in text:
        return text

    old = '''        // GetSocial (2402) — รายชื่อเพื่อน/คำขอเป็นเพื่อน/บล็อก — client รอ .On<Social>
        // (nexonSRC/SocialSystem.cs:908) ⇒ ตอบ Social ว่างทุกชุด (ยังไม่มีระบบเพื่อน)
        _connection.Recv(delegate(GetSocial msg, PacketHeader header)
        {
            Send(new Social
            {
                FollowingEntityIds = Array.Empty<string>(),
                FriendEntities = new(),
                ReceivedFriendRequests = Array.Empty<string>(),
                SentFriendRequests = Array.Empty<string>(),
                BlockedEntityIds = Array.Empty<string>(),
                FavoriteRegionOwners = Array.Empty<string>()
            }, header.Seq);
        });
'''
    new = '''        // FACILDIGITAL_STAGE5_GET_SOCIAL
        // Amigos e solicitações vêm do FriendStore persistente.
        // Follow/block/favoritos continuam com seus backends separados.
        _connection.Recv(delegate(GetSocial msg, PacketHeader header)
        {
            Send(FriendStore.BuildSocial(EntityId), header.Seq);
        });
'''
    return replace_once(text, old, new, "Player.Social GetSocial")


FRIEND_FILE = '''using System;
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
'''


FRIEND_STORE_FILE = '''using System;
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
'''


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--root")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--backup", action="store_true")
    ap.add_argument("--build", action="store_true")
    args = ap.parse_args()

    try:
        root = root_of(args.root)
        print(f"[social] repo: {root}")

        git = shutil.which("git")
        if git:
            p = run([git, "rev-parse", "HEAD"], root)
            if p.returncode == 0:
                head = p.stdout.strip()
                print(f"[social] HEAD: {head}")
                if head != ANALYZED_HEAD:
                    print(
                        "[social] AVISO: HEAD difere da base analisada. "
                        "As âncoras validarão compatibilidade."
                    )

            p = run([git, "status", "--short"], root)
            if p.returncode == 0 and p.stdout.strip():
                print("[social] AVISO: existem alterações locais:")
                print(p.stdout.rstrip())

        changes: list[tuple[Path, str, str]] = []

        for rel, patcher in (
            (GATEWAY, patch_gateway),
            (HOST, patch_host),
            (SOCIAL, patch_social),
        ):
            path = root / rel
            if not path.is_file():
                raise PatchError(f"Arquivo não encontrado: {rel}")

            before = path.read_text(encoding="utf-8")
            after = patcher(before)

            if before != after:
                changes.append((rel, before, after))
                print(f"[change] {rel}")
            else:
                print(f"[skip] {rel}")

        friend_path = root / FRIEND
        if not friend_path.is_file():
            raise PatchError(f"Arquivo não encontrado: {FRIEND}")

        before_friend = friend_path.read_text(encoding="utf-8")
        if "FACILDIGITAL_STAGE5_FRIEND_HANDLERS" not in before_friend:
            changes.append(
                (FRIEND, before_friend, FRIEND_FILE)
            )
            print(f"[change] {FRIEND}")
        else:
            print(f"[skip] {FRIEND}")

        store_path = root / FRIEND_STORE
        if store_path.exists():
            before_store = store_path.read_text(
                encoding="utf-8"
            )
            if "FACILDIGITAL_STAGE5_FRIEND_STORE" not in before_store:
                raise PatchError(
                    f"{FRIEND_STORE} já existe mas não é "
                    "o FriendStore desta etapa."
                )
            print(f"[skip] {FRIEND_STORE}")
        else:
            changes.append(
                (FRIEND_STORE, "", FRIEND_STORE_FILE)
            )
            print(f"[create] {FRIEND_STORE}")

        if args.dry_run:
            for rel, before, after in changes:
                print()
                print(diff(rel, before, after))

            print(
                f"[dry-run] {len(changes)} "
                "arquivo(s) seriam alterados."
            )
            return 0

        for rel, before, after in changes:
            path = root / rel

            if args.backup and path.exists():
                backup = path.with_suffix(
                    path.suffix + ".bak.social"
                )
                shutil.copy2(path, backup)
                print(
                    f"[backup] "
                    f"{backup.relative_to(root)}"
                )

            path.parent.mkdir(
                parents=True,
                exist_ok=True
            )

            path.write_text(
                after,
                encoding="utf-8"
            )

        if git:
            p = run(
                [
                    git,
                    "diff",
                    "--check",
                    "--",
                    str(GATEWAY),
                    str(HOST),
                    str(SOCIAL),
                    str(FRIEND),
                    str(FRIEND_STORE),
                ],
                root,
            )

            if p.returncode != 0:
                print(p.stdout)
                raise PatchError(
                    "git diff --check encontrou erros."
                )

            print("[ok] git diff --check")

        if args.build:
            dotnet = shutil.which("dotnet")
            if not dotnet:
                raise PatchError(
                    "dotnet não encontrado no PATH."
                )

            project = SERVER / "DurangoServer.csproj"

            p = run(
                [
                    dotnet,
                    "build",
                    str(project),
                    "-c",
                    "Debug",
                ],
                root,
            )

            print(p.stdout)

            if p.returncode != 0:
                raise PatchError(
                    "dotnet build falhou."
                )

            print("[ok] build Debug concluído")

        print()
        print("[ok] Etapas 3, 4 e 5 aplicadas.")
        return 0

    except PatchError as exc:
        print(f"[ERRO] {exc}", file=sys.stderr)
        return 2

    except Exception as exc:
        print(
            f"[ERRO inesperado] "
            f"{type(exc).__name__}: {exc}",
            file=sys.stderr,
        )
        return 3


if __name__ == "__main__":
    raise SystemExit(main())
