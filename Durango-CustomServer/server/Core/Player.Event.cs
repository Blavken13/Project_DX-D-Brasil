using System;
using Durango.Network;
using Messages;

namespace Durango.Online;

// Missoes/presenca restauradas; sistemas sem dados continuam respondendo com estados vazios ou Abort.

public partial class Player
{
    // ตัวเลือกแจ้งเตือนของกระดานเวลา (ปุ่มกระดิ่งในหน้า Timeline)
    // **ค่าของเรา**: เก็บในหน่วยความจำต่อ session เท่านั้น — PlayerContext ยังไม่มีช่องเก็บ
    // และเซิร์ฟยังไม่มีระบบ push จริง ⇒ ค่าเริ่มต้น false (อนุรักษ์นิยม: ไม่เปิดแจ้งเตือน)
    private bool _timelineEstateNotification;

    private void RegisterEventHandlers()
    {
        // Calendario de pedras: dados, resgate diario e bonus de ciclo.
        _connection.Recv(delegate(GetAttendanceRewards msg, PacketHeader header)
        { SendInductionAttendanceRewards(msg.Category, header.Seq); });
        _connection.Recv(delegate(GiveAttendanceReward msg, PacketHeader header)
        { ClaimInductionAttendance(msg.Category, msg.RewardNumber, msg.IsRestore, false, header.Seq); });
        _connection.Recv(delegate(GiveAttendanceAppendix msg, PacketHeader header)
        { ClaimInductionAttendance(msg.Category, msg.SelectedReward, false, true, header.Seq); });

        // ── ฤดูกาล (Season) ─────────────────────────────────────────────────────────
        //
        // GetSeasons (871245) — ขอรายชื่อฤดูกาลใหม่ ยิงจาก
        // client/Durango.Logic/SeasonSystem.cs:41 `.On<Seasons>(OnSeasons)` ⇒ ตอบ Seasons (871246)
        // ⚠️ SeasonSystem.OnSeasons — ถ้า `msg._Seasons == null` มัน **return ทันทีที่ :48-51
        //    ก่อนถึง Initialized = true ที่ :66** ⇒ ต้องส่งอาร์เรย์ว่างที่ไม่ใช่ null เสมอ
        // ว่าง = "ตอนนี้ไม่มีฤดูกาลที่เปิดอยู่" — server/data/assets/season/ มีแต่ตารางรางวัล
        // season pass (season2_rewards_client.json) ไม่มีนิยามช่วงเวลา/แบนเนอร์ของฤดูกาลจริง
        // ⇒ ไม่เดาแต่งขึ้นเอง (ผลพลอยได้: ไม่มี Until ⇒ ฝั่งเกมไม่ตั้งเวลาขอซ้ำ = ไม่วนถาม)
        _connection.Recv(delegate(GetSeasons msg, PacketHeader header)
        {
            Send(new Seasons { _Seasons = Array.Empty<Season>() }, header.Seq);
        });

        // ── บัญชี / ข้อมูลส่วนบุคคล ──────────────────────────────────────────────────
        //
        // DeregisterUser (1999) — **ลบบัญชีถาวร** (เมนูตั้งค่า → "ถอนสมาชิก")
        // ยิงจาก client/Durango.System.Config/ConfigInstance.cs:855
        //   `.On<OK>(...)` → เรียก Platform.Leave() แล้วเด้งกลับหน้าไตเติล (ลบจริง)
        //   `.Rest(...)`   → ขึ้นกล่องข้อความ "요청을 처리하지 못했습니다" (ทำรายการไม่สำเร็จ)
        // ⇒ **ห้ามตอบ OK เด็ดขาด** — เราไม่มีระบบลบบัญชีและจะไม่ลบข้อมูลผู้เล่นใด ๆ ทั้งสิ้น
        //   ตอบ Abort (1024) ให้ตกเข้า .Rest = ปฏิเสธอย่างชัดเจน ไม่มีอะไรถูกลบ
        _connection.Recv(delegate(DeregisterUser msg, PacketHeader header)
        {
            Send(new Abort { Text = "Este servidor não permite excluir contas. Seus dados foram preservados." }, header.Seq);
        });

        // DeleteEngagementData (1444251) — ผู้เล่นปิดสวิตช์ยินยอมในป็อปอัปข้อตกลง
        // ยิงจาก client/Durango.UI.Popup/EngagementConfigPopup.cs:37
        //   `Connections.Frontend.Send(default(DeleteEngagementData));` — **ไม่ผูกรอคำตอบ**
        // (คู่กับ EngagementAgreementChanged 1444250 ที่รับไว้แล้วใน Player.Social.cs:91)
        // ⇒ รับเงียบ ๆ กัน log "ไม่มี handler" — และ **ไม่ลบข้อมูลจริงใด ๆ** เช่นกัน
        //   (เซิร์ฟไม่ได้เก็บข้อมูล engagement ไว้ตั้งแต่แรก จึงไม่มีอะไรให้ลบ)
        _connection.Recv(delegate(DeleteEngagementData msg, PacketHeader header)
        {
        });

        // ── มินิเกมเต้น ──────────────────────────────────────────────────────────────
        //
        // MiniGameDanceStarted (4625401) — แจ้งว่าเริ่มเล่นแล้ว
        // ยิงจาก client/Durango.UI/MiniGameDanceGroup.cs:333 (ใน StartGame) แบบ **ไม่ผูกรอ**
        // ⇒ รับเงียบ ๆ (มินิเกมคำนวณ/แสดงผลจบในตัวเกมเองทั้งหมด)
        _connection.Recv(delegate(MiniGameDanceStarted msg, PacketHeader header)
        {
        });

        // MiniGameDanceScore (4625400) — ส่งคะแนนรวมตอนจบเพลง
        // ยิงจาก client/Durango.UI/MiniGameDanceGroup.cs:479 แบบ **ไม่ผูกรอ**
        // แล้วเปิดหน้าสรุปผลเองทันที (KillGame + OpenWindow(Mode.End)) ไม่รอเซิร์ฟตอบ
        // ⇒ รับเงียบ ๆ (เรายังไม่มีที่เก็บสถิติ/รางวัลของมินิเกม จึงไม่แต่งผลตอบกลับ)
        _connection.Recv(delegate(MiniGameDanceScore msg, PacketHeader header)
        {
        });

        // ── กระดานคะแนนเครื่องต่อย (Punch Machine) ──────────────────────────────────
        //
        // GetPunchMachineLeaderboard (785103) — เปิดหน้ากระดานคะแนนของเครื่องต่อยเครื่องนั้น
        // ยิงจาก client/PunchingLeaderboardSystem.cs:72 แบบ **ไม่ผูก .On** — ฝั่งเกมรับคำตอบ
        // ด้วย **global On<PunchMachineLeaderboards>** (PunchingLeaderboardSystem.cs:60)
        // ⇒ ต้องส่งแบบ ReplyOf=0 (ไม่ผูก seq) ไม่งั้นไปไม่ถึงตัวรับ
        // ⚠️ OnPunchMachineLeaderboards:79-88 ไล่ `leaderboard.Contents[j].UserId` ทันที
        //    ⇒ ทั้ง 3 กระดานต้องมี Contents ที่ไม่ใช่ null (ว่างได้)
        // MyScore = null ⇒ ฟิลด์ถูก pack เป็น nil ตรงตาม LeaderboardContent? (ยังไม่เคยมีสถิติ)
        // ไม่แต่งอันดับปลอม — เซิร์ฟยังไม่เก็บคะแนนเครื่องต่อย
        _connection.Recv(delegate(GetPunchMachineLeaderboard msg, PacketHeader header)
        {
            Send(new PunchMachineLeaderboards
            {
                RegionRecentLeaderboard = new Leaderboard { Contents = Array.Empty<LeaderboardContent>() },
                RegionTotalLeaderboard = new Leaderboard { Contents = Array.Empty<LeaderboardContent>() },
                GlobalLeaderboard = new Leaderboard { Contents = Array.Empty<LeaderboardContent>() },
                MyScore = null
            });
        });

        // ── กระดานเวลา (Timeline) — ตัวเลือกแจ้งเตือน ───────────────────────────────
        //
        // GetTimelineOption (81234526) — ขอสถานะปุ่มกระดิ่งในหน้ากระดานเวลา
        // ยิงจาก client/Durango.Logic.Timeline/TimelineLogList.cs:161
        //   `.On(delegate(TimelineOption msg, ...))` ⇒ ต้องตอบชนิด TimelineOption (81234527)
        // ฝั่งเกมแคชค่าไว้ใน _option แล้วเอาไปตั้งไฟปุ่ม
        // (client/Durango.UI/TimelineLogGroup.cs:217-222 `_timelinePushButton.Selected`)
        // ⇒ ไม่ตอบ = ปุ่มค้างโปร่งใส (alpha 0) ตลอดกาล
        _connection.Recv(delegate(GetTimelineOption msg, PacketHeader header)
        {
            Send(new TimelineOption { EstateNotification = _timelineEstateNotification }, header.Seq);
        });

        // SetTimelineOption (81234528) — กดปุ่มกระดิ่งเปิด/ปิดแจ้งเตือนที่ดิน
        // ยิงจาก client/Durango.Logic.Timeline/TimelineLogList.cs:177 ด้วย `.All(...)` แล้วเช็ค
        // `Packet.IsSuccess(packet)` — ล้มเหลวเฉพาะ TypeCode 1022(Error)/1024(Abort)/3650(TimedOut)
        // (server/GameCode/Durango.Network/Packet.cs:116-127) ⇒ ตอบ OK (1231) = สำเร็จ
        // ถ้าล้มเหลวฝั่งเกมจะย้อนค่ากลับ (_option = prev) แล้วเรียก onResult(false)
        // **การตีความของเรา**: นี่คือ "ค่าตั้งค่าส่วนตัว" ไม่ใช่รางวัล ⇒ เก็บค่าไว้จริงต่อ session
        // แล้วตอบ OK ได้อย่างซื่อสัตย์ (GetTimelineOption จะอ่านค่าเดียวกันนี้กลับไป)
        // ข้อจำกัดที่รู้ตัว: ยังไม่ persist ลง PlayerContext และเซิร์ฟยังไม่มีระบบ push จริง
        _connection.Recv(delegate(SetTimelineOption msg, PacketHeader header)
        {
            _timelineEstateNotification = msg.EstateNotification;
            Send(default(OK), header.Seq);
        });
    }
}
