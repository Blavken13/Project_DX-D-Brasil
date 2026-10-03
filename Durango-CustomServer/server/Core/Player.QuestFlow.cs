using System;
using Durango.Network;
using Messages;

namespace Durango.Online;

// Missoes/presenca restauradas; sistemas sem dados continuam respondendo com estados vazios ou Abort.

public partial class Player
{
    private void RegisterQuestFlowHandlers()
    {
        // A barra original mostra pontos e bonus reais, inclusive apos reconexao.
        _connection.Recv(delegate(GetQuestScoreInfos msg, PacketHeader header)
        { Send(BuildQuestScoreInfos(msg.Category), header.Seq); });

        _connection.Recv(delegate(RequestQuestReward msg, PacketHeader header)
        {
            if (TryClaimPlayableQuestReward(msg.QuestId, header.Seq)) return;
            Console.WriteLine($"[เควส] {Short(EntityId)} ขอรับรางวัลเควส '{msg.QuestId}' — นอกเส้น Daily/Once เฟส 1");
            Send(new Abort { Text = "O resgate de recompensas de missões ainda não está disponível." }, header.Seq);
        });

        // Rejeicoes tambem respondem QuestScoreInfos para liberar a interface.
        _connection.Recv(delegate(RequestQuestScoreReward msg, PacketHeader header)
        { ClaimQuestScoreStones(msg, header.Seq); });

        // ── CustomQuestEvent (312798) — สคริปต์ไกด์แจ้ง "เกิดเหตุการณ์คำสำคัญนี้แล้ว" ──
        // ยิงจากคำสั่งในสคริปต์ PlayGuide ชื่อ "CustomQuestEvent"
        // (client/Durango.Logic.PlayGuide/CustomCommand.cs:73 ลงทะเบียน → :617-627 ส่ง)
        // **ไม่ผูกรออะไรกลับ** และไม่ใช่การกระทำที่ผู้เล่นกดเอง (สคริปต์ยิงให้อัตโนมัติ)
        // ⇒ ห้ามตอบ Abort เพราะจะเด้ง toast ขึ้นกลางฉากไกด์ทั้งที่ผู้เล่นไม่ได้ทำอะไรผิด
        // ของจริงเซิร์ฟจะเอา Keyword ไปเดินความคืบหน้าเควสที่รอคำสำคัญนี้ — เรายังไม่มีเอนจิน
        // ⇒ รับไว้เงียบ ๆ + log ไว้ให้ตามรอยได้ว่าสคริปต์ยิงคำไหนมาบ้าง
        _connection.Recv(delegate(CustomQuestEvent msg, PacketHeader header)
        {
            Console.WriteLine($"[เควส] {Short(EntityId)} แจ้งเหตุการณ์เควส '{msg.Keyword}' — ยังไม่มีเอนจินเควสรับไปเดินต่อ");
        });

        // ── InteractWithEpicNPC (3141593) — คุยกับ NPC เนื้อเรื่อง (K / T) ──────────
        // ยิงจาก ClientInteractionQuest.MenuClicked (client/ClientInteractionQuest.cs:77-95)
        // มีสองทาง: ส่งของให้ NPC (ItemIds = ของที่ผู้เล่นเลือก) หรือคุยเฉย ๆ (ItemIds = null)
        // **ไม่ผูกรออะไรกลับ** — ของจริงเซิร์ฟจะกินของแล้วเดินเควสให้ (Shared.Quest/EpicNPCType.cs
        // K=0 / T=1)
        //
        // ⚠️ **ห้ามกินของทิ้ง** — เซิร์ฟไม่มีเควสให้เครดิตกลับ กินไปคือของหายฟรี
        // ⇒ ไม่แตะกระเป๋าเลย แล้วตอบ Abort ให้ผู้เล่นรู้ว่าไม่ได้ส่งของสำเร็จ
        // (ในทางปฏิบัติเส้นนี้ยังไปไม่ถึง เพราะเซิร์ฟไม่เคยส่ง AppearEpicNPC(3141592) ออกไป
        //  ⇒ NPC เนื้อเรื่องไม่เคยโผล่ในเกม — handler นี้จึงเป็นตัวกันเหนียว)
        _connection.Recv(delegate(InteractWithEpicNPC msg, PacketHeader header)
        {
            int itemCount = msg.ItemIds?.Length ?? 0;
            Console.WriteLine($"[เควส] {Short(EntityId)} คุยกับ NPC เนื้อเรื่อง {msg.Npc} (ยื่นของ {itemCount} ชิ้น) — ยังไม่มีเอนจินเควส · ไม่กินของ");
            Send(new Abort { Text = "A história deste NPC ainda não está disponível." }, header.Seq);
        });

        // ── RequestEpicWarp (77777) — วาร์ปตามเนื้อเรื่องหลังจบหนังบท ──────────────
        // ยิงจาก QuestSystem.OnQuestRewardResults หลังเล่นหนังจบ
        // (client/Durango.Logic/QuestSystem.cs:155-166 chapter.PlayMovie(...) → Send)
        // **ไม่ผูกรออะไรกลับ** — ของจริงเซิร์ฟย้ายผู้เล่นไปเกาะบทถัดไปแล้วส่ง Emigrated
        //
        // เซิร์ฟยังไม่มี "เกาะของบทเนื้อเรื่อง" ให้ย้ายไป (ระบบเดินทางที่มีคือท่าเรือ/กลับบ้าน
        // ที่ Core/Player.Warp.cs) ⇒ ตอบ Abort · ห้ามย้ายมั่วไปเกาะอื่นเพราะผู้เล่นจะหลุด
        // ออกจากเกาะของตัวเองโดยไม่ได้ตั้งใจ
        // (เส้นนี้ยังไปไม่ถึงเช่นกัน เพราะเซิร์ฟไม่เคยส่ง QuestRewardResults ตามที่อธิบายข้างบน)
        _connection.Recv(delegate(RequestEpicWarp msg, PacketHeader header)
        {
            Console.WriteLine($"[เควส] {Short(EntityId)} ขอวาร์ปตามเนื้อเรื่อง — เซิร์ฟยังไม่มีเกาะของบทเนื้อเรื่อง");
            Send(new Abort { Text = "As viagens da história ainda não estão disponíveis." }, header.Seq);
        });

        // ── RequestReturnerGuideAction (3450984) — ปุ่มพิเศษของไกด์ "ผู้กลับ" ───────
        // ยิงจาก PlayGuideSystem.NotifyQuizAnswered (client/PlayGuideSystem.cs:965-996)
        // ตามคำตอบในบทสนทนาไกด์ · **ไม่ผูกรออะไรกลับ**
        // ค่าที่เป็นไปได้ (GameCode/Shared.Guide/ReturnerGuideAction.cs):
        //   AdvisorReset=0 (คืนแต้มที่ปรึกษา) · SkillReset=1 (คืนแต้มสกิล) · ReliefGoodsReceive=2 (รับของช่วยเหลือ)
        // ทั้งสามอย่างคือ "แจกของ/คืนแต้มจริง" ที่เซิร์ฟทำไม่ได้ ⇒ ตอบ Abort ไม่แจกลม ๆ
        //
        // ในทางปฏิบัติไม่ถูกเรียก เพราะ Core/Player.Social.cs:78 ตอบ GetReturnerInfo ว่า
        // IsReturner=false ⇒ ไกด์ผู้กลับไม่เริ่มเลย — handler นี้เป็นตัวกันเหนียว
        _connection.Recv(delegate(RequestReturnerGuideAction msg, PacketHeader header)
        {
            Console.WriteLine($"[ผู้กลับ] {Short(EntityId)} ขอทำ {msg.Action} — เซิร์ฟยังไม่มีระบบผู้กลับ");
            Send(new Abort { Text = "Os benefícios de retorno ainda não estão disponíveis." }, header.Seq);
        });

        // ── RequestArchipelagoRegionClear (240002) — กด "รายงานภารกิจบุกเบิก" ───────
        // ยิงจาก ArchipelagoToDoCollection.ReportArchipelagoMission
        // (client/Durango.Logic/ArchipelagoToDoCollection.cs:154-157) →
        // ArchipelagoMissionSystem.RequestRegionClear (client/Durango.Logic/ArchipelagoMissionSystem.cs:125-128)
        // **ไม่ผูกรออะไรกลับ** — ของจริงเซิร์ฟจ่ายรางวัลแล้ว push CurrentArchipelagoTodos(240001)
        // ที่ฝั่งเกมรับด้วย global handler (client/Durango.Logic/ArchipelagoMissionSystem.cs:27)
        //
        // เซิร์ฟไม่มีระบบภารกิจหมู่เกาะ (ไม่เคยส่ง CurrentArchipelagoTodos เลย ⇒ ToDo ไม่เคยขึ้น
        // และปุ่มนี้ไม่เคยโผล่) ⇒ ตอบ Abort ไม่จ่ายรางวัลลม ๆ
        _connection.Recv(delegate(RequestArchipelagoRegionClear msg, PacketHeader header)
        {
            Console.WriteLine($"[หมู่เกาะ] {Short(EntityId)} รายงานภารกิจบุกเบิกจบ — เซิร์ฟยังไม่มีระบบภารกิจหมู่เกาะ");
            Send(new Abort { Text = "As missões de pioneirismo dos arquipélagos ainda não estão disponíveis." }, header.Seq);
        });

        // ── ReissueArchipelagoTodos (240005) — กด "รับภารกิจบุกเบิกใหม่" ────────────
        // ยิงจาก ArchipelagoToDoCollection.RequestNewArchipelagoMission หลังผู้เล่นกดยืนยัน
        // ในกล่องข้อความ (client/Durango.Logic/ArchipelagoToDoCollection.cs:183-192) →
        // ArchipelagoMissionSystem.RequestReissueArchipelagoTodos (ArchipelagoMissionSystem.cs:130-133)
        // **ไม่ผูกรออะไรกลับ** — ของจริงเซิร์ฟสุ่ม todo ชุดใหม่แล้ว push CurrentArchipelagoTodos
        // เหตุผลเดียวกับตัวบน ⇒ ตอบ Abort
        _connection.Recv(delegate(ReissueArchipelagoTodos msg, PacketHeader header)
        {
            Console.WriteLine($"[หมู่เกาะ] {Short(EntityId)} ขอภารกิจบุกเบิกชุดใหม่ — เซิร์ฟยังไม่มีระบบภารกิจหมู่เกาะ");
            Send(new Abort { Text = "As missões de pioneirismo dos arquipélagos ainda não estão disponíveis." }, header.Seq);
        });

        // ── RequestFullCountPOIsReward (9031) — รางวัล "สำรวจจุดสำคัญครบทั้งเกาะ" ───
        // ยิงจากปุ่มรางวัลบนแผนที่โลก: ExploreReward.Set(RewardState.Available)
        // (client/Durango.UI/ExploreReward.cs:53-62) → MapSystem.RequestFullCountPOIsReward
        // (client/MapSystem.cs:416-422 ใส่ RegionId ของเกาะที่ยืนอยู่)
        // **ไม่ผูกรออะไรกลับ** — ของจริงเซิร์ฟจ่ายเงินแล้ว push ExploredPOIs(903) ใหม่ที่มี
        // FullCountRewarded=true (client/MapSystem.cs:166 global On<ExploredPOIs>)
        //
        // ปุ่มนี้จะโผล่ก็ต่อเมื่อ ExploredPOIs.RewardCost != null (client/Durango.UI/WorldMapGroup.cs:717-724)
        // แต่ Core/Player.Map.cs:143-148 ส่ง RewardCost=null (ไม่มีตารางรางวัลจริง) ⇒ กดไม่ได้อยู่แล้ว
        // ⇒ handler นี้เป็นตัวกันเหนียว · ตอบ Abort ไม่จ่ายเงินลม ๆ
        _connection.Recv(delegate(RequestFullCountPOIsReward msg, PacketHeader header)
        {
            Console.WriteLine($"[แผนที่] {Short(EntityId)} ขอรางวัลสำรวจครบของเกาะ '{msg.RegionId}' — เซิร์ฟยังไม่มีตารางรางวัลสำรวจ");
            Send(new Abort { Text = "As recompensas por explorar todos os pontos de interesse ainda não estão disponíveis." }, header.Seq);
        });

        // ── RequestDumpedPersonalIsland (381922) — ดัมป์เกาะส่วนตัวออกมาเป็นไฟล์ ────
        // เป็นเครื่องมือ debug ล้วน ๆ สองจุด:
        //   · Commands.ClientCheatDownloadPersonalIsland (client/Durango.Development/Commands.cs:569-581)
        //   · เมนู debug "dump_personal_island" (client/Durango.System.Config/ConfigInstance.cs:542-548)
        // ทั้งคู่ผูก **.On<DumpedPersonalIsland>(381923) กับ seq**
        //
        // ทำไมไม่ตอบ DumpedPersonalIsland: ก้อนนั้นต้องมี TerrainId + AppearPlayer + Artifacts
        // + InventoryItems + Garden (ไบต์ของแปลงปลูกทั้งเกาะ) ครบชุด ฝั่งเกมเอาไปเขียนเป็น
        // WorldContext/PlayerContext ของโหมดออฟไลน์ทันที (ConfigInstance.cs:551-570)
        // ⇒ ตอบไม่ครบ = สร้างไฟล์เซฟออฟไลน์ที่พังไว้ในเครื่องผู้เล่น อันตรายกว่าไม่ตอบ
        // ⇒ ตอบ Abort · **ผลข้างเคียง**: ป๊อปอัพ DumpPersonalIslandPopup จะค้างเปิดอยู่
        //   (มันปิดตัวเองในคอลแบ็กเท่านั้น) — ยอมรับได้เพราะเข้าถึงได้จากเมนู debug เท่านั้น
        _connection.Recv(delegate(RequestDumpedPersonalIsland msg, PacketHeader header)
        {
            Console.WriteLine($"[debug] {Short(EntityId)} ขอดัมป์เกาะส่วนตัวของ {Short(msg.PlayerEntityId)} — ยังไม่รองรับ");
            Send(new Abort { Text = "A exportação de ilhas particulares ainda não está disponível." }, header.Seq);
        });
    }

}
