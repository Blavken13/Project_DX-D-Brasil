using System;
using System.Collections.Generic;
using Durango.Network;
using Messages;
using Shared.Faction;

namespace Durango.Online;

// Facções ativadas e missões de caça do tutorial. Sorteios, entregas e compras
// sem implementação continuam respondendo explicitamente, sem consumir itens.

public partial class Player
{
    // ข้อความกลางของทุก Abort ในไฟล์นี้ — ต่อท้ายด้วยรายละเอียดของแต่ละคำสั่ง
    private const string FactionNotAvailableText = "O sistema de grupos e missões de grupo ainda não está disponível neste servidor.";

    private void RegisterFactionHandlers()
    {
        // ── GetFactions (3600) — ขอสถานะกลุ่มทั้งหมดของผู้เล่น ─────────────────────────
        // จุดยิง: client/FactionSystem.cs:150-153 RequestFactions() ถูกเรียกจาก OnReady
        //         (client/FactionSystem.cs:139-148 — ทุกครั้งที่เข้าเกม หลัง ResetFactions())
        // ฝั่งเกมยิงแบบไม่ผูก .On<> แล้วรับคำตอบด้วย global On<Messages.Factions>
        //         (client/FactionSystem.cs:109 → OnFactions ที่บรรทัด 195-244)
        // ⚠️ บรรทัด 238 IsFactionInitialized = true คือ "ทางเดียว" ที่ธงนี้ถูกตั้ง
        //    ⇒ ไม่ตอบ = บทเรียนนำทางค้าง (ดูหัวไฟล์) + UpdateMissionState ไม่เคยทำงาน
        //
        // ตอบ _Factions ว่าง = "ยังไม่มีกลุ่มไหนเปิดใช้งาน" ซึ่งเป็นความจริงของเซิร์ฟนี้
        // และปลอดภัย: OnReady เรียก ResetFactions() ก่อนขอเสมอ (FactionSystem.cs:141)
        // ⇒ ทุกกลุ่มถูกรีเซ็ตเป็น Level 0 / MissionAvailableAt 0 อยู่แล้ว
        //    (client/Durango.Logic.Faction/Faction.cs:92-104 Reset)
        // ลิสต์ว่างจึงแค่ "ไม่แก้ค่าไหนเลย" ไม่ใช่การแต่งกลุ่มปลอมขึ้นมา และ IsFactionEnabled
        // (Faction.cs:200-203 IsAvailable ต้อง Level > 0) จะเป็นเท็จทุกกลุ่มตามจริง
        //
        // DailyMissionAvailableAt = 0.0 — เราไม่มีระบบ "ภารกิจแรกของวัน" ค่านี้ถูกอ่านที่เดียว
        // คือ client/Durango.UI/FactionsMissionWidget.cs:137 → MissionActionBar.cs:147
        // ซึ่งจะโผล่ก็ต่อเมื่อ RecommendMissions สำเร็จเท่านั้น (MissionGroup.cs:103-115)
        // — เราตอบ Abort ให้ RecommendMissions ⇒ วิดเจ็ตนั้นไม่มีวันถูกวาด **การตีความของเรา**
        _connection.Recv(delegate(GetFactions msg, PacketHeader header)
        {
            EnsureLearningGuideFactionUnlocked();
            Send(BuildFactionsMessage(), header.Seq);
        });

        // ── ActivateFaction (3610) — "เปิดใช้งานกลุ่มนี้ให้ผู้เล่น" ────────────────────────
        // จุดยิง: client/PlayGuideSystem.cs:754-761 (static ActivateFaction) ถูกเรียกจาก
        //   · client/PlayGuideSystem.cs:702 SetCurrentEvent() — **ทุกครั้งที่บทเรียนเปลี่ยน event**
        //   · client/PlayGuideSystem.cs:280-293 ตอน FactionsUpdated (TheFirm/Lama/SubStory
        //     และกลุ่มที่ผูกกับ guide event ที่ผ่านแล้ว)
        // ยิงแบบไม่ผูก .On<> — ของจริงเซิร์ฟจะเปิดกลุ่มแล้ว push Factions ชุดใหม่กลับไป
        //
        // [7 ก.ย. 2026] ต้องจำกลุ่มที่เปิดแล้วแล้ว push Factions กลับ
        // ไม่เช่นนั้น LearningGuide (คู่มือเส้นทางอาชีพ) จะไม่มีวันโผล่ในเมนู
        // เพราะฝั่งเกมเช็ค IsFactionEnabled(Lama) == Level > 0
        // ⚠️ ห้ามตอบ Abort — ตัวนี้ถูกยิงอัตโนมัติซ้ำทุกครั้งที่บทเรียนเปลี่ยนฉาก
        _connection.Recv(delegate(ActivateFaction msg, PacketHeader header)
        {
            if (msg.Faction == FactionType.Invalid)
            {
                return;
            }
            ActivateFactionLevel(msg.Faction, minLevel: 1);
            // ReplyOf=0 เพราะฝั่งเกมรับด้วย global On<Factions>
            Send(BuildFactionsMessage());
        });

        // ── ReportFactionProp (3611) — "แจ้งพิกัดสิ่งของให้กลุ่ม" ───────────────────────────
        // จุดยิง: client/Durango.UI/MissionGroup.cs:132-140 — เป็น handler ของเมนูปฏิสัมพันธ์
        //   Interaction.ReportFactionProp = 611 (client/InteractionData/Interaction.cs:264)
        //   ⇒ ผู้เล่น "กดเลือกเอง" ไม่ใช่ยิงอัตโนมัติ
        // ส่ง EntityId + EntityType + Tile ของ prop นั้น แบบไม่ผูก .On<>
        // ของจริงเซิร์ฟจะบันทึกแล้วให้แต้มมิตรภาพ (push Factions/Rewarded กลับ)
        // เราให้แต้มไม่ได้ ⇒ ตอบ Abort เพื่อให้ผู้เล่นที่เพิ่งกดรู้ว่าทำไมไม่มีอะไรเกิดขึ้น
        _connection.Recv(delegate(ReportFactionProp msg, PacketHeader header)
        {
            Send(new Abort { Text = FactionNotAvailableText + " — ainda não é possível informar coordenadas ao grupo" }, header.Seq);
        });

        // ── GetFactionDeliveryCondition (3612) — เงื่อนไขของ "คลังส่งของ" ประจำค่ายกลุ่ม ───
        // จุดยิง: client/FactionSystem.cs:512-526 GetFactionDeliveryConditions() รอ
        //   .On<FactionDeliveryCondition> (3613) แล้วเรียก onResult(msg) **โดยไม่เช็ค null**
        //   ต้นทาง: client/Durango.UI/DeliveryGroup.cs:51 (เปิดหน้า "캠프창고" จากเมนู
        //   Delivery<ชื่อกลุ่ม>) → callback ที่บรรทัด 88-93 ปิดวงโหลดแล้วส่งต่อให้
        //   DeliveryWidget.Set() (client/Durango.UI/DeliveryWidget.cs:98-132)
        // ⚠️ ไม่ตอบ = วงโหลด (LoadingRing) หมุนค้างตลอดไป ปิดหน้าไม่ได้อย่างสวยงาม
        //
        // ตอบเงื่อนไขว่าง = "คลังนี้ยังไม่ต้องการของอะไร" ปลอดภัยแน่นอน:
        //   · ตัวกรองรายการของทุกตัวเช็ค string.IsNullOrEmpty ก่อนใช้ (DeliveryWidget.cs:246-276)
        //     ⇒ TagId/PrototypeId/CollectibleId/GeneratorId = null ไม่ทำให้แครช
        //   · Count = 0 → SelectableCount = 0 (DeliveryWidget.cs:127) ⇒ เลือกของใส่ไม่ได้
        //     และปุ่มยืนยันปิดตาย ⇒ ผู้เล่นไม่ถูกหลอกให้ส่งของทิ้งไปเปล่า ๆ
        // FactionType สะท้อนค่าที่ขอมากลับไปตรง ๆ เพื่อให้ฝั่งเกมจับคู่คำตอบได้ถูกกลุ่ม
        _connection.Recv(delegate(GetFactionDeliveryCondition msg, PacketHeader header)
        {
            Send(new FactionDeliveryCondition
            {
                FactionType = msg.FactionType,
                Condition = default(ItemTodoCondition),
                Count = 0
            }, header.Seq);
        });

        // Oferece a etapa atual de caça na tenda do abrigo.
        _connection.Recv(delegate(RecommendMissions msg, PacketHeader header)
        {
            RecommendSafehouseMission(msg, header.Seq);
        });

        // Aceita somente a missão oferecida ao personagem, na tenda próxima.
        _connection.Recv(delegate(AcceptMission msg, PacketHeader header)
        {
            AcceptSafehouseMission(msg, header.Seq);
        });

        // Cancela a missão atual sem alterar etapas já concluídas.
        _connection.Recv(delegate(CancelMission msg, PacketHeader header)
        {
            CancelSafehouseMission(msg.MissionId);
            Send(default(OK), header.Seq);
        });

        // ── GetRechargeShuffleCost (3625) — ถามราคาเติมจำนวน "สุ่มภารกิจใหม่" ──────────────
        // จุดยิง: client/FactionSystem.cs:430-442 รอ .On<Costs> (4024) → onResult(costs)
        //   ต้นทาง: client/Durango.UI/FactionsMissionWidget.cs:70 (กดปุ่มสุ่มตอนโควตาหมด)
        //   คำตอบถูกเอาไปเปิดกล่องยืนยันจ่ายเงิน ShowPayConfirm(costs._Costs.Get(Gem, 0))
        //
        // ข้อมูลราคาจริง **ไม่มี** ในเกม (costs.json ไม่มีคีย์นี้ · constants.json →
        // faction.mission.shuffle มีแค่ recharge_cooltime/max_count) ⇒ ห้ามเดาตัวเลข
        // ตอบ Costs ว่าง ตามแนว server/Core/Player.Social.cs:52-55 (GetClanCreationCosts)
        // — Costs.Pack เขียน MapHeader(0) เมื่อ _Costs เป็น null ฝั่งเกมจึงได้ dictionary
        // ว่างที่ไม่ใช่ null (Costs.cs:37-53 Unpack) ⇒ .Get(Gem, 0L) ไม่แครช
        // ⚠️ ผลข้างเคียง: กล่องยืนยันจะขึ้นราคา 0 อัญมณี แต่ปลายทาง (3626) ตอบ Abort อยู่ดี
        //    จึงไม่มีการหักเงินจริง และเส้นทางนี้ยังต้องผ่าน RecommendMissions ที่เรา Abort ก่อน
        //    ⇒ ในทางปฏิบัติเปิดไม่ถึงหน้านี้ **การตีความของเรา**
        _connection.Recv(delegate(GetRechargeShuffleCost msg, PacketHeader header)
        {
            Send(new Costs(), header.Seq);
        });

        // ── RechargeMissionShuffleCount (3626) — จ่ายเงินเติมโควตาสุ่มภารกิจ ───────────────
        // จุดยิง: client/FactionSystem.cs:444-455 รอ .On<MissionInfos> แล้วเด้งแจ้งเตือน
        //   "เติมจำนวนรับภารกิจอื่นแล้ว" (บรรทัด 453) — ไม่มี .Rest
        //   ต้นทาง: client/Durango.UI/FactionsMissionWidget.cs:70-82 หลังกดยืนยันจ่ายเงิน
        // เป็น "การกระทำที่มีค่าใช้จ่าย" — ถ้าตอบ MissionInfos ว่าง ฝั่งเกมจะเด้งข้อความว่า
        // เติมสำเร็จทั้งที่ไม่มีอะไรเกิดขึ้น = โกหกผู้เล่นตรง ๆ ⇒ ตอบ Abort เท่านั้น
        _connection.Recv(delegate(RechargeMissionShuffleCount msg, PacketHeader header)
        {
            Send(new Abort { Text = FactionNotAvailableText + " — ainda não é possível repor as tentativas de sorteio de missões" }, header.Seq);
        });

        // ── ShuffleMission (3627) — "ขอภารกิจอื่นแทนอันนี้" (ใช้โควตาสุ่ม) ─────────────────
        // จุดยิง: client/FactionSystem.cs:408-428 รอ .On<MissionInfos> แล้วขึ้นข้อความ
        //   "ได้รับภารกิจอื่นจาก <กลุ่ม> แล้ว" (บรรทัด 424) — ไม่มี .Rest
        //   ต้นทาง: client/Durango.UI/FactionsMissionWidget.cs:60-69 (ปุ่มสุ่ม เมื่อโควตาเหลือ)
        // เหตุผลเดียวกับ 3626: ตอบสำเร็จทั้งที่ไม่ได้สุ่มอะไร = หลอกผู้เล่น ⇒ Abort
        _connection.Recv(delegate(ShuffleMission msg, PacketHeader header)
        {
            Send(new Abort { Text = FactionNotAvailableText + " — ainda não é possível sortear novas missões" }, header.Seq);
        });

        // ── GetRecommendMissionCost (3628) — ถามราคา "รับภารกิจถัดไปทันที" (ข้ามคูลไทม์) ──
        // จุดยิง: client/FactionSystem.cs:373-385 รอ .On<Costs> → onResult(costs)
        //   ต้นทาง: client/Durango.UI/FactionsMissionWidget.cs:85 (ปุ่มรีเซ็ตคูลไทม์ภารกิจ)
        // เหตุผล/ผลข้างเคียงเหมือน 3625 ทุกประการ — ไม่มีราคาจริงในข้อมูลเกม ⇒ Costs ว่าง
        _connection.Recv(delegate(GetRecommendMissionCost msg, PacketHeader header)
        {
            Send(new Costs(), header.Seq);
        });

        // ── RecommendMissionImmediately (3629) — จ่ายเงินข้ามคูลไทม์ รับภารกิจถัดไปเลย ─────
        // จุดยิง: client/FactionSystem.cs:387-395 (static) ยิงแบบไม่ผูก .On<>
        //   ต้นทาง: client/Durango.UI/FactionsMissionWidget.cs:85-95 หลังกดยืนยันจ่ายเงิน
        // การกระทำที่มีค่าใช้จ่ายอีกตัว และเราสุ่มภารกิจไม่ได้ ⇒ Abort (ห้ามเงียบ เพราะ
        // ผู้เล่นเพิ่งกด "จ่าย" ไป ต้องรู้ว่าไม่มีอะไรเกิดขึ้นและไม่ได้ถูกหักอะไร)
        _connection.Recv(delegate(RecommendMissionImmediately msg, PacketHeader header)
        {
            Send(new Abort { Text = FactionNotAvailableText + " — ainda não é possível receber a próxima missão imediatamente" }, header.Seq);
        });

        // Informa a conclusão persistida usando o ID esperado pelo guia.
        _connection.Recv(delegate(CheckSequenceMissionCleared msg, PacketHeader header)
        {
            Send(new SequenceMissionCleared
            {
                MissionId = msg.MissionId,
                Cleared = _context.SafehouseMissions?.Completed?.Contains(msg.MissionId ?? "") == true
            }, header.Seq);
        });

        // Respeita os quatro minutos de espera definidos no tutorial original.
        _connection.Recv(delegate(SkipTutorialMission msg, PacketHeader header)
        {
            SkipSafehouseHuntingMission(msg.MissionId, header.Seq);
        });

        // ── SendFactionSupportRequest (725982) — ส่งของช่วยเหลือตาม "คำขอสนับสนุน" ของกลุ่ม ─
        // จุดยิง: client/FactionSystem.cs:633-646 รอ .On<AcceptedSupportRewards> แล้วเอา
        //   msg.UpdatedInfo ไปอัปเดตจำนวนครั้งคงเหลือ + ยิง event SupportRewardsAccepted
        //   (หน้าจอจะโชว์ "รางวัลที่ได้รับ") — ไม่มี .Rest
        //   ต้นทาง: client/Durango.UI/FactionSupportRequestWidget.cs:161 ปุ่มส่งของช่วยเหลือ
        // คำขอสนับสนุนของเราว่างเปล่าอยู่แล้ว (server/Core/Player.Social.cs:60-63 ตอบ
        // SupportRequests ชุดว่าง) ⇒ ไม่มีคำขอไหนให้ตอบรับจริง
        // และการตอบ AcceptedSupportRewards = ต้องแต่งรางวัลปลอมขึ้นมา ซึ่งห้ามเด็ดขาด ⇒ Abort
        _connection.Recv(delegate(SendFactionSupportRequest msg, PacketHeader header)
        {
            Send(new Abort { Text = FactionNotAvailableText + " — ainda não é possível enviar suprimentos ao grupo" }, header.Seq);
        });
    }

    /// <summary>
    /// [7 ก.ย. 2026] ปลด Lama อย่างน้อยเลเวล 1 — จำเป็นสำหรับเมนู LearningGuide
    /// บนเกาะส่วนตัวเกมไม่ยิง ActivateFaction(Lama) เอง (IsAfterRural ตัด Personal)
    /// จึงต้องเปิดฝั่งเซิร์ฟตอนถูกถาม GetFactions
    /// </summary>
    private void EnsureLearningGuideFactionUnlocked()
    {
        ActivateFactionLevel(FactionType.Lama, minLevel: 1);
    }

    private void ActivateFactionLevel(FactionType type, int minLevel)
    {
        if (type == FactionType.Invalid || minLevel <= 0)
        {
            return;
        }
        _context.ActivatedFactions ??= new Dictionary<int, int>();
        int key = (int)type;
        if (_context.ActivatedFactions.TryGetValue(key, out int current) && current >= minLevel)
        {
            return;
        }
        _context.ActivatedFactions[key] = minLevel;
        if (!string.IsNullOrEmpty(_context.Path))
        {
            _context.Save();
        }
        OnContextChanged();
        Console.WriteLine($"[กลุ่ม] {Short(EntityId)} เปิด {type} เลเวล {minLevel}");
    }

    private Factions BuildFactionsMessage()
    {
        EnsureLearningGuideFactionUnlocked();
        var list = new List<Messages.Faction>();
        if (_context.ActivatedFactions != null)
        {
            foreach (KeyValuePair<int, int> kv in _context.ActivatedFactions)
            {
                if (kv.Key < 0 || kv.Value <= 0)
                {
                    continue;
                }
                list.Add(new Messages.Faction
                {
                    Type = (FactionType)kv.Key,
                    Point = 0,
                    Level = kv.Value,
                    AvailableAt = 0.0,
                    PointBefore = null,
                    StartsAt = 0.0,
                    EndsAt = 0.0
                });
            }
        }
        return new Factions
        {
            _Factions = list.ToArray(),
            DailyMissionAvailableAt = 0.0
        };
    }
}
