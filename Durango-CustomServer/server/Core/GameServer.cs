    using System;
    using System.Collections.Generic;
    using System.Net.Sockets;
    using Durango.Network;
    using Durango.Utils;
    using Durango.Utils.Extensions;
    using JetBrains.Annotations;
    using Messages;
    using Shared.Region;

    namespace Durango.Online;

    // พอร์ตจาก nexonSRC/Durango.Online/GameServer.cs (TCP 8191, handshake แท้ GetClock→Auth→Ready)
    // ความต่างจากต้นฉบับ (เอกสารใน docs/server/ServerNx.md):
    //  1) เซิร์ฟนี้รับหลายผู้เล่นในโลกเดียว — ต้นฉบับ offline โฮสต์ 1 คน/สล็อต จึงเซฟเฉพาะ _playerCtx
    //     ที่นี่ ContextChanged เซฟ context ของคนนั้นถ้ามี Path (สล็อตจริงบนดิสก์)
    //  2) IssueSession: token→entityId สำหรับ /sessions (ต้นฉบับไม่มี session token จริง)
    public class GameServer
    {
        public const int DefaultPort = 8191;

        private readonly Listener _listener;

        private readonly PlayerContext _playerCtx;

        private readonly Dictionary<string, PlayerContext> _playerContexts = new();

        private readonly List<Connection> _connections = new();

        private readonly Dictionary<Connection, string> _connectionDict = new();

        private readonly Dictionary<string, string> _sessionTokens = new();

        /// <summary>
        /// token → กุญแจบัญชีของผู้ถือ — ตัวที่ทำให้ "ย้าย token ไปชี้ตัวละครไหนก็ได้" หมดไป
        ///
        /// ⚠️ ไม่มีตารางนี้ = <c>BindSessionToEntity</c> เช็คได้แค่ว่า "token นี้เซิร์ฟออกให้จริงไหม"
        /// ไม่ได้เช็คว่าตัวละครปลายทางเป็นของผู้ถือ token หรือเปล่า ⇒ curl 2 บรรทัดยึดตัวละครใครก็ได้:
        /// <c>POST /sessions</c> (ขอ token ฟรี) → <c>GET /entry?entity_id=&lt;ของเหยื่อ&gt;</c> → Auth ผ่านเป็นเหยื่อ
        /// </summary>
        private readonly Dictionary<string, string> _sessionOwners = new();

        private readonly Dictionary<string, double> _sessionLastSeen = new();

        private readonly Dictionary<string, string> _sessionByOwner = new();

        private const double SessionTimeoutSeconds = 60.0 * 60.0;

        private double _nextSessionCleanupAt;

        public World World { get; }

        /// <summary>
        /// [5 ก.ย. 2026] โลกของทุกเกาะ — Host ตั้งให้หลังสร้าง GameServer
        /// ผู้เล่นแต่ละคนเข้าโลกตาม PlayerContext.RegionId ไม่ใช่โลกเดียวร่วมกันแบบต้นฉบับ
        /// </summary>
        public WorldRegistry Worlds { get; set; }
        public EconomyStore Economy { get; set; }
        public MailStore Mail { get; set; }

        /// <summary>โลกที่ผู้เล่นคนนี้อยู่ — ตกไปที่โลกตั้งต้นถ้ายังไม่มีระบบหลายเกาะ</summary>
        public World WorldOf(PlayerContext context)
        {
            if (Worlds == null)
            {
                return World;
            }

            string regionId = context?.RegionId;
            if (string.IsNullOrEmpty(regionId) ||
                RegionCatalog.TryGet(regionId, out _) ||
                Worlds.IsSettlementRegion(regionId))
            {
                return Worlds.GetOrCreate(regionId);
            }

            Console.WriteLine($"[world] invalid saved region '{regionId}' for {context?.EntityId ?? "(unknown)"}; recovering to default region");
            if (context != null)
            {
                context.RegionId = null;
                context.AppearPlayer.Move.Movements = null;
                context.Save();
            }

            return Worlds.GetOrCreate(null);
        }

        public int Port { get; private set; }

        public GameServer(WorldContext worldCtx, PlayerContext playerCtx)
        {
            _listener = new Listener();
            Port = 8191;
            _playerCtx = playerCtx;
            World = new World(worldCtx);
        }

        public void Start(int port)
        {
            Port = port;
            if (!_listener.Start(port))
                throw new InvalidOperationException($"Não foi possível abrir a porta TCP {port} do jogo.");
            _listener.ClientAccepted += Listener_ClientAccepted;
        }

        public void Close()
        {
            try
            {
                _listener.Close();
                for (int num = _connections.Count - 1; num >= 0; num--)
                {
                    _connections[num].Close();
                }
                _connections.Clear();
                if (Worlds != null) Worlds.StopAll(); else World.Stop();
            }
            catch (Exception)
            {
            }
        }

        public void Process()
        {
            _listener.Process();
            for (int num = _connections.Count - 1; num >= 0; num--)
            {
                _connections[num].Process();
            }
            DropStaleUnauthenticated();
            CleanupExpiredSessions();
            if (Worlds != null) Worlds.ProcessAll(); else World.Process();
        }

        /// <summary>
        /// ตัดสายที่ต่อเข้ามาแล้วไม่ยอมผ่าน Auth ภายในเวลาที่กำหนด
        ///
        /// ⚠️ ไม่มีตัวนี้ = เปิด TCP ค้างไว้เฉย ๆ ก็จองบัฟเฟอร์ ~4 MB ต่อเส้นได้ตลอดกาล
        /// โดยไม่ต้องมี token ไม่ต้องมีบัญชี ไม่ต้องทำอะไรเลย
        /// </summary>
        private void DropStaleUnauthenticated()
        {
            if (_pendingAuth.Count == 0) return;
            double now = Gauge.CurrentTime;

            List<Connection> stale = null;
            foreach (KeyValuePair<Connection, double> pair in _pendingAuth)
            {
                if (now - pair.Value < UnauthenticatedTimeoutSeconds) continue;
                (stale ??= new List<Connection>()).Add(pair.Key);
            }
            if (stale == null) return;

            foreach (Connection connection in stale)
            {
                Console.WriteLine("[auth] ตัดสายที่ไม่ผ่าน Auth ภายในเวลาที่กำหนด");
                _pendingAuth.Remove(connection);
                try { connection.Close(); } catch (Exception) { }
                _connections.Remove(connection);
            }
        }

        private void CleanupExpiredSessions()
        {
            double now = Gauge.CurrentTime;
            if (now < _nextSessionCleanupAt) return;
            _nextSessionCleanupAt = now + 60.0;

            List<string> expired = null;
            foreach (KeyValuePair<string, double> pair in _sessionLastSeen)
            {
                if (now - pair.Value <= SessionTimeoutSeconds) continue;
                (expired ??= new List<string>()).Add(pair.Key);
            }
            if (expired == null) return;

            foreach (string token in expired)
            {
                RemoveSession(token);
            }
        }

        private void RemoveSession(string token)
        {
            if (string.IsNullOrEmpty(token)) return;

            _sessionTokens.TryGetValue(token, out string entityId);
            _sessionOwners.TryGetValue(token, out string ownerKey);

            _sessionTokens.Remove(token);
            _sessionOwners.Remove(token);
            _sessionLastSeen.Remove(token);

            if (!string.IsNullOrEmpty(ownerKey) &&
                _sessionByOwner.TryGetValue(ownerKey, out string currentToken) &&
                string.Equals(currentToken, token, StringComparison.Ordinal))
            {
                _sessionByOwner.Remove(ownerKey);
            }

            RemoveTemporaryContextIfUnused(entityId);
        }

        private void RemoveTemporaryContextIfUnused(string entityId)
        {
            if (string.IsNullOrEmpty(entityId)) return;

            foreach (string current in _sessionTokens.Values)
            {
                if (string.Equals(current, entityId, StringComparison.Ordinal)) return;
            }
            foreach (string current in _connectionDict.Values)
            {
                if (string.Equals(current, entityId, StringComparison.Ordinal)) return;
            }

            if (_playerContexts.TryGetValue(entityId, out PlayerContext context) &&
                string.IsNullOrEmpty(context.Path))
            {
                _playerContexts.Remove(entityId);
            }
        }

        private bool TryGetValidSession(string token, out string entityId)
        {
            entityId = null;
            if (string.IsNullOrEmpty(token) || !_sessionTokens.TryGetValue(token, out entityId))
            {
                return false;
            }

            double now = Gauge.CurrentTime;
            if (!_sessionLastSeen.TryGetValue(token, out double lastSeen) ||
                now - lastSeen > SessionTimeoutSeconds)
            {
                RemoveSession(token);
                entityId = null;
                return false;
            }

            _sessionLastSeen[token] = now;
            return true;
        }

        /// <summary>ลงทะเบียน context (สล็อตจริงหรือชั่วคราว) — /sessions เรียก</summary>
        public bool Register(PlayerContext context)
        {
            if (context != null && !string.IsNullOrEmpty(context.EntityId))
            {
                _playerContexts[context.EntityId] = context;
                return true;
            }
            return false;
        }

        /// <summary>ออก session token ให้ผู้ถือกุญแจบัญชี <paramref name="ownerKey"/></summary>
        public void IssueSession(string entityId, string token, string ownerKey)
        {
            if (string.IsNullOrEmpty(entityId) || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(ownerKey))
            {
                return;
            }

            // หนึ่งบัญชีมี session HTTP ที่ยังใช้งานได้เพียง token เดียว
            // token เก่าถูกยกเลิกทันที ลดทั้ง replay และการสะสม token/context ในหน่วยความจำ
            if (_sessionByOwner.TryGetValue(ownerKey, out string oldToken) &&
                !string.Equals(oldToken, token, StringComparison.Ordinal))
            {
                RemoveSession(oldToken);
            }

            _sessionTokens[token] = entityId;
            _sessionOwners[token] = ownerKey;
            _sessionLastSeen[token] = Gauge.CurrentTime;
            _sessionByOwner[ownerKey] = token;
        }

        /// <summary>กุญแจบัญชีของผู้ถือ token นี้ — null ถ้าไม่รู้จัก token</summary>
        public string OwnerOfSession(string token)
        {
            return TryGetValidSession(token, out _) ? _sessionOwners.Get(token) : null;
        }

        public bool TryGetSessionEntityId(string token, out string entityId)
        {
            return TryGetValidSession(token, out entityId);
        }

        /// <summary>
        /// [5 ก.ย. 2026] ย้าย session token ที่ออกไว้แล้ว ให้ชี้ตัวละครที่ผู้เล่นเลือกบนหน้า Title
        ///
        /// ทำไมต้องมี: ในโหมด Online ตัวเกม **ไม่ส่ง** ฟิลด์ "player" มากับ /sessions
        /// (client/Durango.UI/TitleMenuGroup.cs:334-340 ใส่ "player" เฉพาะตอน GameManager.ConnectCluster != null
        /// คือทาง LAN/ConnectTo เท่านั้น) ⇒ ตอนออก token เซิร์ฟยังไม่รู้ว่าจะเล่นตัวไหน
        /// ตัวละครที่เลือกถูกบอกทีหลังที่ /entry?entity_id=… ซึ่งยิงมาแบบ auth:true
        /// (client/Durango.UI/TitleMenuGroup.cs:1046 RquestEntry → Http.cs:36 ใส่ header Authorization)
        /// ⇒ ผูกที่นี่ได้อย่างปลอดภัย เพราะต้องถือ token ที่เซิร์ฟออกให้เท่านั้นถึงจะย้ายได้
        ///
        /// คืน false เมื่อ token ไม่รู้จัก — ผู้เรียกไม่ต้องทำอะไรต่อ (Auth จะปฏิเสธเองอยู่แล้ว)
        /// </summary>
        public bool BindSessionToEntity(string token, string entityId)
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(entityId)
                || !TryGetValidSession(token, out string previousEntityId))
            {
                return false;
            }

            // ⚠️ ด่านที่ขาดไปตั้งแต่ต้น — เดิมเช็คแค่ว่า token นี้เซิร์ฟออกให้จริงไหม แล้วย้ายให้เลย
            // ซึ่งไม่ได้กันอะไรเลย เพราะ token ขอฟรีได้ที่ /sessions โดยไม่ต้องยืนยันตัวตน
            // ⇒ ต้องเช็คว่า "ตัวละครปลายทางเป็นของบัญชีเดียวกับผู้ถือ token" ด้วย
            string owner = _sessionOwners.Get(token);
            PlayerContext target = _playerContexts.Get(entityId);

            // /entry ต้องชี้ไปยัง context ที่เซิร์ฟรู้จักแล้วเท่านั้น
            // ตัวใหม่จะถูกสร้าง/ลงทะเบียนผ่าน /players ก่อนเข้าด่านนี้
            if (target == null)
            {
                Console.WriteLine($"[auth] ปฏิเสธการผูก session: ไม่รู้จักตัวละคร {entityId}");
                return false;
            }

            if (!AccountKeys.Same(owner, target.OwnerKey))
            {
                Console.WriteLine($"[auth] ปฏิเสธการผูก session: บัญชี {AccountKeys.ForLog(owner)} " +
                                $"ไม่ใช่เจ้าของตัวละคร {entityId} (เจ้าของ {AccountKeys.ForLog(target.OwnerKey)})");
                return false;
            }
            _sessionTokens[token] = entityId;
            _sessionLastSeen[token] = Gauge.CurrentTime;
            if (!string.Equals(previousEntityId, entityId, StringComparison.Ordinal))
            {
                RemoveTemporaryContextIfUnused(previousEntityId);
            }
            return true;
        }

        /// <summary>
        /// context ของตัวละครนี้ — <c>null</c> ถ้าเซิร์ฟไม่รู้จัก
        ///
        /// ⚠️ เดิมถอยไปที่ <c>_playerCtx</c> (สล็อตแรกของเซิร์ฟ) เมื่อหาไม่เจอ
        /// ซึ่งเป็นมรดกจากเซิร์ฟ offline ที่มีผู้เล่นคนเดียว แต่ในโหมดหลายคนมันคือช่องโหว่:
        /// ใส่ <c>entity_id</c> มั่ว ๆ ที่ไม่มีจริง = **ได้ตัวละครของคนแรกไปเล่น** โดยไม่ต้องรู้ id ใครเลย
        /// แล้ว autosave เขียนความเสียหายลงไฟล์จริงภายใน 60 วินาที
        /// ⇒ คืน null แล้วให้ผู้เรียกปฏิเสธการเชื่อมต่อ
        /// </summary>
        [CanBeNull]
        public PlayerContext GetPlayerContext(string entityId) => _playerContexts.Get(entityId);

        /// <summary>
        /// เพดานจำนวนสายที่เปิดค้างพร้อมกัน — **ค่าของเรา**
        ///
        /// ⚠️ <c>--max-players</c> กันได้แค่ประตูหน้า (HTTP /entry) แต่ทางเข้าจริงคือ TCP
        /// ซึ่งเดิม**ไม่มีเพดานเลย** ⇒ เปิดสาย TCP ค้างไว้เฉย ๆ โดยไม่ต้อง Auth ก็จองบัฟเฟอร์
        /// ~4 MB ต่อเส้นได้ไม่จำกัด (Connection.cs จอง 7 ก้อน ก้อนละ 512 KB ตั้งแต่ตอน accept)
        /// ⇒ ไม่กี่ร้อยสายก็ทำ RAM หมด
        ///
        /// ตั้งเป็น 3 เท่าของเพดานผู้เล่น เผื่อช่วงที่คนกำลังต่อใหม่ทับกับคนเก่าที่ยังไม่หลุด
        /// </summary>
        public static int MaxPlayersHint { get; set; } = 200;

        private static int MaxConnections =>
            MaxPlayersHint <= 0 ? 1024 : Math.Max(32, MaxPlayersHint * 3);

        /// <summary>
        /// เวลาที่ยอมให้สายหนึ่งค้างอยู่โดยยังไม่ผ่าน Auth (วินาที) — **ค่าของเรา**
        /// ตัวเกมจริงส่ง Auth ทันทีหลังต่อ ⇒ 30 วินาทีเหลือเฟือแม้เน็ตแย่
        /// </summary>
        private const double UnauthenticatedTimeoutSeconds = 30.0;

        /// <summary>สายที่ยังไม่ผ่าน Auth → เวลาที่ต่อเข้ามา (ใช้ตัดสายที่จองบัฟเฟอร์ทิ้งไว้เฉย ๆ)</summary>
        private readonly Dictionary<Connection, double> _pendingAuth = new();

        /// <summary>
        /// FACILDIGITAL+: sockets Radiotower autenticados.
        /// O client abre uma segunda conexão TCP e autentica com Tune, não com Auth/Ready.
        /// </summary>
        private readonly Dictionary<Connection, string> _radiotowerConnections = new();

        private int PlayersOnline()
        {
            if (Worlds == null) return World?.PlayerCount ?? 0;

            int total = 0;
            foreach (KeyValuePair<string, World> pair in Worlds.Loaded)
            {
                total += pair.Value.PlayerCount;
            }
            return total;
        }

        private Player FindOnlinePlayer(string entityId)
        {
            if (string.IsNullOrEmpty(entityId)) return null;

            if (Worlds == null)
            {
                foreach (Player player in World.PlayersSnapshot())
                {
                    if (string.Equals(player.EntityId, entityId, StringComparison.Ordinal)) return player;
                }
                return null;
            }

            foreach (KeyValuePair<string, World> pair in Worlds.Loaded)
            {
                foreach (Player player in pair.Value.PlayersSnapshot())
                {
                    if (string.Equals(player.EntityId, entityId, StringComparison.Ordinal)) return player;
                }
            }
            return null;
        }

        /// <summary>Consulta usada por GET /online_statuses.</summary>
        public bool IsPlayerOnline(string entityId) => FindOnlinePlayer(entityId) != null;
        public void NotifyMarketSale(EconomyStore.MarketListing listing) => FindOnlinePlayer(listing.SellerId)?.NotifyMarketSale(listing);
        public void NotifyMail(string entityId) => FindOnlinePlayer(entityId)?.NotifyMailDelivery();

        // FACILDIGITAL_STAGE2_CHAT_ROUTING
        /// <summary>
        /// Envia chat exclusivamente pelos sockets Radiotower autenticados.
        ///
        /// Region/PersonalRegions: somente jogadores no mesmo World.
        /// Clan/ClanWar: somente jogadores do mesmo clã, mesmo em outra ilha.
        /// Party ainda não possui backend persistente e não é roteado aqui.
        /// </summary>
        private int BroadcastRadiotower(SayInExclusiveChannel msg, PlayerContext senderContext)
        {
            if (senderContext == null) return 0;

            World senderWorld = WorldOf(senderContext);
            string senderClanId = senderContext.AppearPlayer.Member.ClanId ?? string.Empty;

            var recipients = new List<Connection>();
            foreach (KeyValuePair<Connection, string> pair in _radiotowerConnections)
            {
                Connection targetConnection = pair.Key;
                string targetEntityId = pair.Value;
                if (targetConnection == null || string.IsNullOrEmpty(targetEntityId)) continue;

                PlayerContext targetContext = GetPlayerContext(targetEntityId);
                if (targetContext == null) continue;

                bool allowed;
                switch (msg.ChannelType)
                {
                    case Shared.Chat.ChannelType.Region:
                    case Shared.Chat.ChannelType.PersonalRegions:
                        allowed = ReferenceEquals(WorldOf(targetContext), senderWorld);
                        break;

                    case Shared.Chat.ChannelType.Clan:
                    case Shared.Chat.ChannelType.ClanWar:
                        allowed =
                            !string.IsNullOrEmpty(senderClanId) &&
                            string.Equals(
                                targetContext.AppearPlayer.Member.ClanId,
                                senderClanId,
                                StringComparison.Ordinal);
                        break;

                    default:
                        allowed = false;
                        break;
                }

                if (allowed) recipients.Add(targetConnection);
            }

            foreach (Connection target in recipients)
            {
                target.Send(msg);
            }

            return recipients.Count;
        }

        private static string ShortRadiotowerEntityId(string entityId)
        {
            if (string.IsNullOrEmpty(entityId)) return "(vazio)";
            return entityId.Length <= 8 ? entityId : entityId[..8];
        }

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
                // O socket precisa continuar vinculado ao mesmo personagem que fez Tune.
                if (!_radiotowerConnections.TryGetValue(connection, out string boundEntityId) ||
                    !string.Equals(boundEntityId, entityId, StringComparison.Ordinal))
                {
                    Console.WriteLine(
                        $"[radiotower] chat ignorado em socket sem vínculo: {entityId}");
                    return;
                }

                switch (msg.ChannelType)
                {
                    case Shared.Chat.ChannelType.Region:
                    case Shared.Chat.ChannelType.PersonalRegions:
                        break;

                    case Shared.Chat.ChannelType.Clan:
                    case Shared.Chat.ChannelType.ClanWar:
                        if (string.IsNullOrEmpty(context.AppearPlayer.Member.ClanId))
                        {
                            connection.Send(new Abort
                            {
                                Text = "Você não pertence a um clã."
                            }, header.Seq);
                            return;
                        }
                        break;

                    case Shared.Chat.ChannelType.Party:
                        // Player.Party.cs ainda não mantém uma party real.
                        // Nunca transformar chat de party em broadcast de região.
                        connection.Send(new Abort
                        {
                            Text = "O chat de grupo ainda não está disponível."
                        }, header.Seq);
                        return;

                    default:
                        connection.Send(new Abort
                        {
                            Text = "Canal de chat inválido."
                        }, header.Seq);
                        return;
                }

                // Nunca confiar em identidade/hora/speaker enviados pelo client.
                Message_ message = msg.Message;
                message.EntityId = entityId;
                message.Time = Times.UnixTimeNow();
                message.Speaker = new RadioId
                {
                    Name = context.AppearPlayer.Name ??
                           context.PlayerInfo?.PlayerName ??
                           string.Empty,
                    Freq = context.AppearPlayer.Freq
                };
                msg.Message = message;

                int recipients = BroadcastRadiotower(msg, context);
                Console.WriteLine(
                    $"[chat] {ShortRadiotowerEntityId(entityId)} " +
                    $"channel={msg.ChannelType} recipients={recipients}");
            });
        }

        private void Listener_ClientAccepted(Socket socket)
        {
            if (_connections.Count >= MaxConnections)
            {
                Console.WriteLine($"[auth] ปฏิเสธสายใหม่ — เต็มเพดาน ({_connections.Count}/{MaxConnections})");
                try { socket.Close(); } catch (Exception) { }
                return;
            }

            Connection connection = new(socket);
            connection.Recv(delegate(GetClock getClock, PacketHeader header)
            {
                Clock msg = default;
                msg.ClientTime = getClock.Time;
                msg.ServerTime = Times.UnixTimeNow();
                connection.Send(msg, header.Seq);
            });

            // FACILDIGITAL_STAGE2_KEEPALIVE
            // Connections.Radiotower.MaybeSendKeepalive() envia TypeCode 254
            // a cada 30 s. Não existe reply esperado; basta consumir o pacote.
            connection.Recv(delegate(Keepalive msg, PacketHeader header)
            {
            });
            // FACILDIGITAL+: Tune é o handshake do socket Radiotower.
            // O client não envia Auth/Ready nesta segunda conexão; espera Conversations como reply.
            connection.Recv(delegate(Tune tune, PacketHeader header)
            {
                // FACILDIGITAL_STAGE2_TUNE_GUARD
                // O listener é compartilhado, o socket não:
                // um socket que começou como Frontend não pode virar Radiotower.
                if (_connectionDict.ContainsKey(connection))
                {
                    Console.WriteLine(
                        "[radiotower] Tune recusado em socket Frontend já autenticado");
                    connection.Send(new Abort
                    {
                        Text = "Conexão de chat inválida."
                    }, header.Seq);
                    connection.Close();
                    return;
                }

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

            connection.Recv(delegate(Auth auth, PacketHeader header)
            {
                // FACILDIGITAL_STAGE2_AUTH_GUARD
                // Depois de Tune este socket pertence exclusivamente ao Radiotower.
                if (_radiotowerConnections.ContainsKey(connection))
                {
                    Console.WriteLine("[auth] Auth recusado em socket Radiotower");
                    connection.Send(new Abort
                    {
                        Text = "Conexão de jogo inválida."
                    }, header.Seq);
                    connection.Close();
                    return;
                }

                // [4 ก.ย. 2026] ก่อนหน้านี้เชื่อ auth.EntityId ตรง ๆ — ใครก็ยิง Auth อ้างเป็น entity id ใครก็ได้
                // สวมรอยตัวละครคนอื่นได้ทันที ⇒ ต้องผูกกับ token ที่ /sessions ออกให้เท่านั้น (เหมือน server/ หลัก)
                if (!TryGetSessionEntityId(auth.SessionToken, out string sessionEntityId)
                    || !string.Equals(sessionEntityId, auth.EntityId, StringComparison.Ordinal))
                {
                    Console.WriteLine($"[auth] recusado: token não pertence ao personagem informado ({auth.EntityId})");
                    connection.Send(new Abort { Text = "Falha na autenticação da sessão." }, header.Seq);
                    connection.Close();
                    return;
                }
                string entityId = auth.EntityId;

                Player existingPlayer = FindOnlinePlayer(entityId);
                if (existingPlayer == null && MaxPlayersHint > 0 && PlayersOnline() >= MaxPlayersHint)
                {
                    Console.WriteLine($"[auth] recusado: servidor lotado ({PlayersOnline()}/{MaxPlayersHint})");
                    connection.Send(new Abort { Text = "Servidor lotado. Tente novamente em instantes." }, header.Seq);
                    connection.Close();
                    return;
                }
                if (existingPlayer != null)
                {
                    Console.WriteLine($"[auth] {entityId} entrou novamente — encerrando a sessão anterior");
                    existingPlayer.KickWith("Este personagem foi conectado por uma nova sessão.");
                }

                PlayerContext playerContext = GetPlayerContext(entityId);
                if (playerContext == null)
                {
                    // เดิมตรงนี้ถอยไปใช้ตัวละครสล็อตแรกให้เลย (ดู GetPlayerContext) ⇒ ใส่ id มั่วก็เข้าเล่นได้
                    Console.WriteLine($"[auth] recusado: personagem desconhecido {entityId}");
                    connection.Send(new Abort { Text = "Personagem não encontrado." }, header.Seq);
                    connection.Close();
                    return;
                }
                _connectionDict[connection] = entityId;
                _pendingAuth.Remove(connection);      // ผ่านด่านแล้ว ไม่ต้องนับเวลาอีก
                SendWelcome(connection, entityId, playerContext.PlayerInfo.PlayerName, header.Seq);
            });
            connection.Recv(delegate(Ready ready, PacketHeader readyHeader)
            {
                string text = _connectionDict.Get(connection);
                if (string.IsNullOrEmpty(text))
                {
                    connection.Close();
                }
                else
                {
                    PlayerContext playerContext = GetPlayerContext(text);
                    if (playerContext == null)
                    {
                        // ปกติไม่ควรเกิด (Auth กรองไปแล้ว) — กันไว้เพราะเดิมจุดนี้ NullReference ไม่ได้
                        // เพราะมี fallback อยู่ ตอนตัด fallback ออกจึงต้องมีด่านตรงนี้ด้วย
                        Console.WriteLine($"[auth] Ready: personagem desconhecido {text} — desconectando");
                        connection.Close();
                        return;
                    }
                    connection.Send(default(OK), readyHeader.Seq);
                    bool flag = playerContext.EntityId == text;
                    World playerWorld = WorldOf(playerContext);
                    Player player = new(text, connection, playerWorld, playerContext, flag, Economy, Mail);
                    if (flag)
                    {
                        player.ContextChanged += delegate
                        {
                            // ต้นฉบับเซฟเฉพาะ _playerCtx (offline โฮสต์คนเดียว) — ที่นี่เซฟ context ของสล็อตนั้น
                            // ถ้าเป็นสล็อตจริงบนดิสก์ (context ชั่วคราวที่ยังไม่ผ่าน /players ไม่มี Path จึงไม่เซฟ)
                            if (!string.IsNullOrEmpty(playerContext.Path))
                            {
                                playerContext.Save();
                            }
                        };
                    }
                    playerWorld.AddPlayer(player);
                }
                _connections.Remove(connection);
                _connectionDict.Remove(connection);
            });
            connection.ConnetionClosed += delegate
            {
                _connections.Remove(connection);
                _connectionDict.Remove(connection);
                _pendingAuth.Remove(connection);
                _radiotowerConnections.Remove(connection);
            };
            connection.StartReceive();
            _connections.Add(connection);
            _pendingAuth[connection] = Gauge.CurrentTime;
        }

        private void SendWelcome(Connection connection, string entityId, string name, uint seq)
        {
            Welcome msg = new()
            {
                UserId = entityId,
                Name = name
            };
            PlayerContext playerContext = GetPlayerContext(entityId);
            msg.Storage.Data = playerContext.Storage;
            // [5 ก.ย. 2026] บอกเกาะที่ผู้เล่นอยู่จริง — ต้นฉบับ hardcode "1" ได้เพราะมีโลกเดียว
            // Id ใช้ระบุเกาะในระบบล่องเรือ (ตรงกับ RegionCatalog) ส่วน TerrainId ยังเป็น "1" เพราะ
            // ตัวเกมเอาค่านี้ไปประกอบ URL ขอแผนที่ /terrains/<TerrainId>/… ซึ่ง Gateway เสิร์ฟที่เส้น
            // O Gateway continuará servindo o terrain real; RegionId pode ser uma instância lógica.
            Worlds?.EnsureDefaultSharedTamedRegion();

            if (!string.IsNullOrEmpty(playerContext.PersonalRegionId))
            {
                if (!playerContext.PrivateRegionEntitled)
                {
                    playerContext.PrivateRegionEntitled = true;
                    if (!string.IsNullOrEmpty(playerContext.Path))
                    {
                        playerContext.Save();
                    }
                }

                string privateTemplateId =
                    playerContext.PersonalRegionTemplateId ?? RegionCatalog.DefaultSettlementTemplateId;
                Worlds?.RegisterPersonalRegion(
                    playerContext.PersonalRegionId,
                    privateTemplateId,
                    entityId);
            }

            // Reidrata a associação ao clã antes de enviar AppearPlayer ao cliente.
            // Isso também corrige saves antigos ou alterações feitas enquanto o personagem estava offline.
            ClanStore.EnsureLoaded(playerContext.Path);
            ClanStore.SyncContext(playerContext);

            string clanId = playerContext.AppearPlayer.Member.ClanId;
            if (!string.IsNullOrEmpty(clanId))
            {
                Worlds?.RegisterClanRegion(clanId);
            }

            World playerWorld = WorldOf(playerContext);

            // Migração Alpha/Beta: somente contas com direito explícito recebem Ilha Particular.
            // saves criados antes da restauração de Personal Region podem chegar ao pós-tutorial
            // sem PersonalRegionId. O client depende desse id já no Welcome para habilitar a
            // viagem à Tamed Island; sem ele o botão pode simplesmente não gerar viagem alguma.
            RegionCatalog.TemplateInfo currentRegionTemplate =
                RegionCatalog.GetTemplate(playerWorld.TerrainInfo.region_template);
            if (playerContext.PrivateRegionEntitled &&
                string.IsNullOrEmpty(playerContext.PersonalRegionId) &&
                currentRegionTemplate != null &&
                currentRegionTemplate.Role != Role.Tutorial)
            {
                string personalTemplateId = RegionCatalog.DefaultPersonalTemplateId;
                if (!string.IsNullOrEmpty(personalTemplateId))
                {
                    string shortEntityId = entityId.Length <= 8 ? entityId : entityId[..8];
                    playerContext.PersonalRegionId = "personal_" + shortEntityId;
                    playerContext.PersonalRegionTemplateId = personalTemplateId;
                    playerContext.PersonalRegionAdmission ??= new List<int>();

                    Worlds?.RegisterPersonalRegion(
                        playerContext.PersonalRegionId,
                        playerContext.PersonalRegionTemplateId,
                        entityId);

                    if (!string.IsNullOrEmpty(playerContext.Path))
                    {
                        playerContext.Save();
                    }

                    Console.WriteLine(
                        $"[personal] ALPHA provisionou {playerContext.PersonalRegionId} " +
                        $"para {shortEntityId} usando {personalTemplateId}");
                }
                else
                {
                    Console.WriteLine(
                        $"[personal] não foi possível provisionar ilha pessoal para {entityId}: " +
                        "nenhum terrain Personal instalado");
                }
            }

            msg.Region.CreatedAt = Times.UnixTimeNow();
            // Region.Id/Role informa ao client quando estamos em uma instância de assentamento.
            // SharedTamed, PrivatePlayer e Clan reutilizam terrain, mas possuem RegionId e save próprios.
            string regionId = playerContext.RegionId;
            SettlementRegionInstance settlementRegion = null;
            bool onSettlement = !string.IsNullOrEmpty(regionId) &&
                                Worlds?.TryGetSettlementRegion(regionId, out settlementRegion) == true;
            msg.Region.Id = onSettlement
                ? regionId
                : (string.IsNullOrEmpty(regionId) ? (playerWorld.TerrainId ?? "1") : regionId);
            msg.Region.Name = settlementRegion?.Kind switch
            {
                SettlementRegionKind.SharedTamed => "Ilha Domada",
                SettlementRegionKind.PrivatePlayer => "Ilha Particular",
                SettlementRegionKind.Clan => "Ilha de Clã",
                _ => RegionCatalog.DisplayName(RegionCatalog.GetTemplate(playerWorld.TerrainInfo.region_template))
            };
            msg.Region.TemplateId = onSettlement
                ? settlementRegion.TemplateId
                : playerWorld.TerrainInfo.region_template;
            // TerrainId é o arquivo de terrain real usado para carregar mapa/chunks.
            msg.Region.TerrainId = playerWorld.TerrainId ?? "1";
            RegionCatalog.TemplateInfo regionTemplate = RegionCatalog.GetTemplate(msg.Region.TemplateId);
            msg.Region.Role = onSettlement ? Role.Personal : (regionTemplate?.Role ?? Role.Rural);

            // Enquanto o jogador não possuir Ilha Particular, o atalho "Ilha Domada"
            // aponta para a instância pública compartilhada.
            msg.PersonalRegionId = !string.IsNullOrEmpty(playerContext.PersonalRegionId)
                ? playerContext.PersonalRegionId
                : (Worlds?.EnsureDefaultSharedTamedRegion() == true
                    ? WorldRegistry.DefaultSharedTamedRegionId
                    : null);
            Console.WriteLine(
                $"[welcome] {entityId[..Math.Min(8, entityId.Length)]} Region.Id={msg.Region.Id} Role={msg.Region.Role} TerrainId={msg.Region.TerrainId} TemplateId={msg.Region.TemplateId} PersonalRegionId={msg.PersonalRegionId ?? "(ว่าง)"}");
            msg.Options.Bool = new[]
            {
                new BoolOption { Key = "market.ui_enabled", Value = true },
                new BoolOption { Key = "cashshop.ui_enabled", Value = true }
            };
            msg.Options.Int = new[]
            {
                new IntegerOption { Key = "market.search.limit", Value = 20L }
            };
            connection.Send(msg, seq);
        }
    }
