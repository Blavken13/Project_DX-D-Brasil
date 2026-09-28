#!/usr/bin/env python3
"""
Facildigital+ patch: bancadas/craft + Radiotower/chat + busca/status de jogadores.

Base investigada:
  repo: Blavken13/Project_DX-D-Brasil
  main: 561c57a77436013ac14e3a7e6ca7bf5bc51b84e5

Uso:
  python facildigital_patch_craft_chat_social.py
  python facildigital_patch_craft_chat_social.py --dry-run
  python facildigital_patch_craft_chat_social.py --build
  python facildigital_patch_craft_chat_social.py --root /caminho/do/repo --build
"""
from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Callable

EXPECTED_BASE_COMMIT = "561c57a77436013ac14e3a7e6ca7bf5bc51b84e5"
SERVER_DIR = Path("Durango-CustomServer/server")


class PatchError(RuntimeError):
    pass


def run(cmd: list[str], cwd: Path) -> subprocess.CompletedProcess[str]:
    return subprocess.run(cmd, cwd=str(cwd), text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)


def find_repo_root(explicit: str | None) -> Path:
    if explicit:
        root = Path(explicit).expanduser().resolve()
        if not (root / SERVER_DIR).is_dir():
            raise PatchError(f"Não encontrei {SERVER_DIR} em {root}")
        return root
    start = Path.cwd().resolve()
    for p in (start, *start.parents):
        if (p / SERVER_DIR).is_dir():
            return p
    raise PatchError("Execute dentro do repositório ou informe --root /caminho/do/repo")


def replace_once(text: str, old: str, new: str, label: str) -> str:
    n = text.count(old)
    if n != 1:
        raise PatchError(f"{label}: trecho-base esperado 1 vez, encontrado {n}")
    return text.replace(old, new, 1)


def insert_before_once(text: str, anchor: str, insertion: str, label: str) -> str:
    return replace_once(text, anchor, insertion + anchor, label)


def insert_after_once(text: str, anchor: str, insertion: str, label: str) -> str:
    return replace_once(text, anchor, anchor + insertion, label)


def patch_workbench_tags(text: str) -> str:
    marker = "FACILDIGITAL+: merge tags derivadas de bancada"
    if marker in text:
        return text
    old = '''    /// <summary>
    /// เติมแท็กลงใน AppearArtifact ถ้ายังไม่มี — คืน true เมื่อมีการเปลี่ยนแปลง
    ///
    /// เขียนทับเฉพาะตอนที่ยังว่าง เผื่อวันหนึ่งมีที่อื่นตั้งแท็กเองแล้วจะได้ไม่ถูกลบ
    /// </summary>
    public static bool Apply(ref AppearArtifact artifact)
    {
        if (artifact.Tags._Tags is { Length: > 0 }) return false;
        Tag[] tags = Of(artifact.EntityType);
        if (tags == null || tags.Length == 0) return false;
        artifact.Tags = new Tags { EntityId = artifact.EntityId, _Tags = tags };
        return true;
    }
'''
    new = '''    /// <summary>
    /// เติม/ผสานแท็กโต๊ะคราฟต์ลงใน AppearArtifact — คืน true เมื่อมีการเปลี่ยนแปลง
    ///
    /// FACILDIGITAL+: merge tags derivadas de bancada.
    /// ของเดิมหยุดทันทีเมื่อ artifact มีแท็กอะไรก็ได้อยู่แล้ว ทำให้โต๊ะบางหลังขาด cook/workbench/kitchen.
    /// ตอนนี้เก็บแท็กเดิมไว้ และเติมเฉพาะแท็กที่ขาดหรือมีระดับต่ำกว่าค่า derived.
    /// </summary>
    public static bool Apply(ref AppearArtifact artifact)
    {
        Tag[] derived = Of(artifact.EntityType);
        if (derived == null || derived.Length == 0) return false;

        var merged = new Dictionary<string, Tag>(StringComparer.Ordinal);
        if (artifact.Tags._Tags != null)
        {
            foreach (Tag tag in artifact.Tags._Tags)
            {
                if (string.IsNullOrEmpty(tag.Id)) continue;
                if (!merged.TryGetValue(tag.Id, out Tag previous) || tag.Level > previous.Level)
                    merged[tag.Id] = tag;
            }
        }

        bool changed = false;
        foreach (Tag tag in derived)
        {
            if (string.IsNullOrEmpty(tag.Id)) continue;
            if (!merged.TryGetValue(tag.Id, out Tag current) || current.Level < tag.Level)
            {
                merged[tag.Id] = tag;
                changed = true;
            }
        }

        if (!string.Equals(artifact.Tags.EntityId, artifact.EntityId, StringComparison.Ordinal))
            changed = true;
        if (!changed) return false;

        artifact.Tags = new Tags
        {
            EntityId = artifact.EntityId,
            _Tags = new List<Tag>(merged.Values).ToArray()
        };
        return true;
    }
'''
    return replace_once(text, old, new, "WorkbenchTags.Apply")


def patch_artifact_manager(text: str) -> str:
    marker = "FACILDIGITAL+: normaliza durability de bancadas antigas"
    if marker in text:
        return text
    anchor = '''                bool changed = WorkbenchTags.Apply(ref artifact);
                changed |= CageTypes.Apply(ref artifact);
'''
    insertion = '''
                // FACILDIGITAL+: normaliza durability de bancadas antigas.
                // Recipe.IsValidWorkbench no client rejeita a bancada quando Durability.Get() <= Min().
                if (WorkbenchTags.Of(artifact.EntityType) is { Length: > 0 })
                {
                    Gauge durability = artifact.States.Durability;
                    bool invalidDurability = durability == null ||
                                             durability.Determination == null ||
                                             durability.Determination.Length == 0;
                    if (!invalidDurability)
                    {
                        try { invalidDurability = durability.Get() <= durability.Min(); }
                        catch (Exception) { invalidDurability = true; }
                    }
                    if (invalidDurability)
                    {
                        artifact.States.Durability = new Gauge(1f, 0f, new[]
                        {
                            new GaugeNode { Time = 0.0, Value = 1f }
                        });
                        changed = true;
                        Console.WriteLine($"[โต๊ะคราฟต์] ซ่อม durability ของโต๊ะเก่า {artifact.EntityId}");
                    }
                }
'''
    return insert_after_once(text, anchor, insertion, "ArtifactManager durability")


def patch_player_crafting(text: str) -> str:
    marker = "FACILDIGITAL+: valida as mesmas workbench_tags usadas pelo client"
    if marker in text:
        return text
    old = '''    private bool CheckWorkbench(CraftRecipeData recipe, PropKey? workbench, out string error)
    {
        error = null;
        if (recipe.workbench_tags == null || recipe.workbench_tags.Count == 0) return true;
        if (!workbench.HasValue || string.IsNullOrEmpty(workbench.Value.EntityId))
        {
            error = "สูตรนี้ต้องทำที่โต๊ะ";
            return false;
        }
        AppearArtifact? artifact = _world.ArtifactManager.Get(workbench.Value.EntityId);
        if (!artifact.HasValue)
        {
            error = "A bancada informada não existe.";
            return false;
        }

        if (!MayTouchArtifact(workbench.Value.EntityId, "usar bancada"))
        {
            error = "Você não tem permissão para usar esta bancada.";
            return false;
        }

        MergedBlueprint blueprint = BlueprintStore.GetBlueprint(artifact.Value.EntityType);
        if (blueprint?.Components == null || !blueprint.Components.Contains("Workbench"))
        {
            error = "Esta estrutura não é uma bancada de fabricação.";
            return false;
        }
        return true;
    }
'''
    new = '''    private bool CheckWorkbench(CraftRecipeData recipe, PropKey? workbench, out string error)
    {
        // FACILDIGITAL+: valida as mesmas workbench_tags usadas pelo client.
        error = null;
        if (recipe.workbench_tags == null || recipe.workbench_tags.Count == 0) return true;

        if (!workbench.HasValue || string.IsNullOrEmpty(workbench.Value.EntityId))
        {
            error = "Esta receita precisa de uma bancada específica.";
            return false;
        }

        AppearArtifact? artifact = _world.ArtifactManager.Get(workbench.Value.EntityId);
        if (!artifact.HasValue)
        {
            error = "A bancada informada não existe.";
            return false;
        }
        if (!MayTouchArtifact(workbench.Value.EntityId, "usar bancada"))
        {
            error = "Você não tem permissão para usar esta bancada.";
            return false;
        }

        AppearArtifact bench = artifact.Value;
        MergedBlueprint blueprint = BlueprintStore.GetBlueprint(bench.EntityType);
        if (blueprint?.Components == null || !blueprint.Components.Contains("Workbench"))
        {
            error = "Esta estrutura não é uma bancada de fabricação.";
            return false;
        }
        if (bench.States.BuildingState != Shared.Building.BuildingState.Completed)
        {
            error = "A bancada ainda não está concluída.";
            return false;
        }

        Gauge durability = bench.States.Durability;
        if (durability != null)
        {
            bool unusable = durability.Determination == null || durability.Determination.Length == 0;
            if (!unusable)
            {
                try { unusable = durability.Get() <= durability.Min(); }
                catch (Exception) { unusable = true; }
            }
            if (unusable)
            {
                error = "A bancada está sem durabilidade.";
                Console.WriteLine($"[craft] bancada inválida {Short(bench.EntityId)}: durability");
                return false;
            }
        }

        Messages.Tag[] actualTags = bench.Tags._Tags ?? Array.Empty<Messages.Tag>();
        foreach (KeyValuePair<string, int> required in recipe.workbench_tags)
        {
            bool matched = false;
            int actualLevel = 0;
            foreach (Messages.Tag tag in actualTags)
            {
                if (!string.Equals(tag.Id, required.Key, StringComparison.Ordinal)) continue;
                actualLevel = Math.Max(actualLevel, tag.Level);
                if (tag.Level >= required.Value) { matched = true; break; }
            }
            if (!matched)
            {
                error = $"A bancada não possui a capacidade necessária: {required.Key} Nv.{required.Value}.";
                Console.WriteLine($"[craft] bancada {Short(bench.EntityId)} rejeitada: " +
                                  $"{required.Key} precisa={required.Value} atual={actualLevel}");
                return false;
            }
        }
        return true;
    }
'''
    return replace_once(text, old, new, "Player.Crafting.CheckWorkbench")


def patch_game_server(text: str) -> str:
    if "FACILDIGITAL+: sockets Radiotower autenticados" not in text:
        anchor = '''        private readonly Dictionary<Connection, double> _pendingAuth = new();
'''
        insertion = '''
        /// <summary>
        /// FACILDIGITAL+: sockets Radiotower autenticados.
        /// O client abre uma segunda conexão TCP e autentica com Tune, não com Auth/Ready.
        /// </summary>
        private readonly Dictionary<Connection, string> _radiotowerConnections = new();
'''
        text = insert_after_once(text, anchor, insertion, "GameServer campo Radiotower")

    if "FACILDIGITAL+: sessão Radiotower leve" not in text:
        anchor = '''        private void Listener_ClientAccepted(Socket socket)
'''
        insertion = '''        /// <summary>Consulta usada por GET /online_statuses.</summary>
        public bool IsPlayerOnline(string entityId) => FindOnlinePlayer(entityId) != null;

        /// <summary>
        /// FACILDIGITAL+: sessão Radiotower leve.
        /// Não cria um segundo Player; o socket social continua em GameServer._connections.
        /// </summary>
        private void RegisterRadiotowerHandlers(Connection connection, string entityId, PlayerContext context)
        {
            connection.Recv(delegate(GetLatestChatLog msg, PacketHeader header)
            {
                connection.Send(new ChatLogs { Logs = Array.Empty<Message_>() }, header.Seq);
            });

            connection.Recv(delegate(GetClanNotificationEnabled msg, PacketHeader header)
            {
                var enabled = new Dictionary<Shared.Chat.ChannelType, bool>();
                if (context.ClanChannelNotifications != null)
                {
                    foreach (KeyValuePair<int, bool> pair in context.ClanChannelNotifications)
                        enabled[(Shared.Chat.ChannelType)pair.Key] = pair.Value;
                }
                connection.Send(new ToggleClanNotification
                {
                    ChannelNotificationsEnabled = enabled
                }, header.Seq);
            });

            connection.Recv(delegate(ToggleClanNotification msg, PacketHeader header)
            {
                var saved = new Dictionary<int, bool>();
                if (msg.ChannelNotificationsEnabled != null)
                {
                    foreach (KeyValuePair<Shared.Chat.ChannelType, bool> pair in msg.ChannelNotificationsEnabled)
                        saved[(int)pair.Key] = pair.Value;
                }
                context.ClanChannelNotifications = saved;
                if (!string.IsNullOrEmpty(context.Path)) context.Save();
            });

            connection.Recv(delegate(ResubscribeClanChannel msg, PacketHeader header) { });

            connection.Recv(delegate(SayInExclusiveChannel msg, PacketHeader header)
            {
                Message_ message = msg.Message;
                message.EntityId = entityId;
                message.Time = Times.UnixTimeNow();
                message.Speaker = new RadioId
                {
                    Name = context.AppearPlayer.Name ?? context.PlayerInfo?.PlayerName ?? string.Empty,
                    Freq = context.AppearPlayer.Freq
                };
                msg.Message = message;
                World world = WorldOf(context);
                world?.BroadCast(msg);
            });
        }

'''
        text = insert_before_once(text, anchor, insertion, "GameServer handlers Radiotower")

    if "FACILDIGITAL+: Tune é o handshake do socket Radiotower" not in text:
        anchor = '''            connection.Recv(delegate(Auth auth, PacketHeader header)
'''
        insertion = '''            // FACILDIGITAL+: Tune é o handshake do socket Radiotower.
            // O client não envia Auth/Ready nesta segunda conexão; espera Conversations como reply.
            connection.Recv(delegate(Tune tune, PacketHeader header)
            {
                if (!TryGetSessionEntityId(tune.SessionToken, out string sessionEntityId)
                    || !string.Equals(sessionEntityId, tune.EntityId, StringComparison.Ordinal))
                {
                    Console.WriteLine($"[radiotower] Tune recusado: token/entity inválido ({tune.EntityId})");
                    connection.Send(new Abort { Text = "Falha ao autenticar o servidor de chat." }, header.Seq);
                    connection.Close();
                    return;
                }

                PlayerContext context = GetPlayerContext(tune.EntityId);
                if (context == null)
                {
                    connection.Send(new Abort { Text = "Personagem não encontrado no servidor de chat." }, header.Seq);
                    connection.Close();
                    return;
                }

                _pendingAuth.Remove(connection);
                _radiotowerConnections[connection] = tune.EntityId;
                RegisterRadiotowerHandlers(connection, tune.EntityId, context);
                connection.Send(new Conversations { _Conversations = Array.Empty<Conversation>() }, header.Seq);
                Console.WriteLine($"[radiotower] conectado {tune.EntityId}");
            });

'''
        text = insert_before_once(text, anchor, insertion, "GameServer Tune")

    if "_radiotowerConnections.Remove(connection);" not in text:
        old = '''                _pendingAuth.Remove(connection);
            };
'''
        new = '''                _pendingAuth.Remove(connection);
                _radiotowerConnections.Remove(connection);
            };
'''
        text = replace_once(text, old, new, "GameServer cleanup Radiotower")
    return text


def patch_gateway(text: str) -> str:
    marker = "FACILDIGITAL+: busca pública de jogadores por nome"
    if marker in text:
        return text
    anchor = '''        _webServer.PostRoute["/players"] = delegate(HttpListenerRequest request, Dictionary<string, string> postData)
'''
    insertion = '''        // FACILDIGITAL+: busca pública de jogadores por nome.
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

        // FACILDIGITAL+: status online em lote para PlayerInfoManager.RequestPlayersConnected.
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
    return insert_before_once(text, anchor, insertion, "Gateway social endpoints")


def patch_player_disconnect(text: str) -> str:
    marker = "FACILDIGITAL+: grava o instante real da desconexão"
    if marker in text:
        return text
    old = '''        _connection.ConnetionClosed += delegate
        {
            // แช่หลอดไว้ที่ค่าปัจจุบันก่อนปล่อย context — ไม่งั้นเส้นแนวโน้มที่ส่งไปแล้วจะเดินต่อ
'''
    new = '''        _connection.ConnetionClosed += delegate
        {
            // FACILDIGITAL+: grava o instante real da desconexão para /online_statuses.
            _context.PlayerInfo.DisconnectedAt = Times.UnixTimeNow();
            OnContextChanged();

            // แช่หลอดไว้ที่ค่าปัจจุบันก่อนปล่อย context — ไม่งั้นเส้นแนวโน้มที่ส่งไปแล้วจะเดินต่อ
'''
    return replace_once(text, old, new, "Player DisconnectedAt")


PATCHES: list[tuple[Path, Callable[[str], str]]] = [
    (SERVER_DIR / "Support/WorkbenchTags.cs", patch_workbench_tags),
    (SERVER_DIR / "Core/ArtifactManager.cs", patch_artifact_manager),
    (SERVER_DIR / "Core/Player.Crafting.cs", patch_player_crafting),
    (SERVER_DIR / "Core/GameServer.cs", patch_game_server),
    (SERVER_DIR / "Core/Gateway.cs", patch_gateway),
    (SERVER_DIR / "Core/Player.cs", patch_player_disconnect),
]


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--root")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--backup", action="store_true")
    ap.add_argument("--build", action="store_true")
    args = ap.parse_args()

    try:
        root = find_repo_root(args.root)
        print(f"[patch] repo: {root}")
        git = shutil.which("git")
        if git:
            p = run([git, "rev-parse", "HEAD"], root)
            head = p.stdout.strip() if p.returncode == 0 else ""
            if head:
                print(f"[patch] HEAD: {head}")
                if head != EXPECTED_BASE_COMMIT:
                    print("[patch] AVISO: HEAD difere da base investigada; as âncoras validarão compatibilidade.")

        changed: list[Path] = []
        pending: list[tuple[Path, str]] = []
        for rel, patcher in PATCHES:
            path = root / rel
            if not path.is_file():
                raise PatchError(f"Arquivo não encontrado: {rel}")
            before = path.read_text(encoding="utf-8")
            after = patcher(before)
            if after == before:
                print(f"[skip] {rel}")
                continue
            changed.append(rel)
            pending.append((path, after))
            print(f"[change] {rel}")

        if args.dry_run:
            print(f"[dry-run] {len(changed)} arquivo(s) seriam alterados.")
            return 0

        for path, after in pending:
            if args.backup:
                shutil.copy2(path, path.with_suffix(path.suffix + ".bak.facildigital"))
            path.write_text(after, encoding="utf-8")

        if git:
            p = run([git, "diff", "--check", "--", str(SERVER_DIR)], root)
            if p.returncode != 0:
                print(p.stdout)
                raise PatchError("git diff --check encontrou problemas")
            print("[ok] git diff --check")

        if args.build:
            dotnet = shutil.which("dotnet")
            if not dotnet:
                raise PatchError("dotnet não encontrado no PATH")
            project = SERVER_DIR / "DurangoServer.csproj"
            p = run([dotnet, "build", str(project), "-c", "Release"], root)
            print(p.stdout)
            if p.returncode != 0:
                raise PatchError("dotnet build falhou")
            print("[ok] build concluído")

        print("[ok] patch aplicado")
        print("Teste: reinicie o servidor, abra sua fogueira, envie chat de Região e busque outro jogador pelo nome.")
        return 0
    except PatchError as e:
        print(f"[ERRO] {e}", file=sys.stderr)
        return 2
    except Exception as e:
        print(f"[ERRO inesperado] {type(e).__name__}: {e}", file=sys.stderr)
        return 3


if __name__ == "__main__":
    raise SystemExit(main())
