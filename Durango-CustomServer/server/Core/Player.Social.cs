using System;
using Durango.Network;
using Messages;

namespace Durango.Online;

// Handlers sociais auxiliares. Equipes e clãs possuem registros próprios.
public partial class Player
{
    private void RegisterSocialHandlers()
    {
        // FACILDIGITAL_STAGE5_GET_SOCIAL
        // Amigos e solicitações vêm do FriendStore persistente.
        // Follow/block/favoritos continuam com seus backends separados.
        _connection.Recv(delegate(GetSocial msg, PacketHeader header)
        {
            Send(FriendStore.BuildSocial(EntityId), header.Seq);
        });

        // GetMemos (2439) — บันทึก (สัตว์/พืช/แร่ ที่เคยเจอ) — client รอ .On<Memos>
        // (nexonSRC/MemoSystem.cs:46) ⇒ ตอบชุดว่าง (MemoStorage ยังไม่ผูก — เก็บทีหลังถ้าต้องการ)
        _connection.Recv(delegate(GetMemos msg, PacketHeader header)
        {
            Send(new Memos(), header.Seq);
        });

        // Custo original de criação em constants.json.
        _connection.Recv(delegate(GetClanCreationCosts msg, PacketHeader header)
        {
            Send(new Costs { _Costs = new() { [Shared.Economy.Currency.TStone] = ClanRules.CreationCost } }, header.Seq);
        });

        // GetSupportRequests (2347809) — คำขอสนับสนุนกลุ่ม (faction) — ฝั่งเกมรับด้วย
        // **global On<SupportRequests>** (nexonSRC/FactionSystem.cs:110) ไม่ได้ผูก .On กับคำขอ
        // ⇒ ตอบแบบ ReplyOf=0 ชุดว่าง (EndAt=0 = ไม่มีรอบขอสนับสนุนอยู่)
        _connection.Recv(delegate(GetSupportRequests msg, PacketHeader header)
        {
            Send(new SupportRequests());
        });

        // GetNomadInfo (100000) — สถานะ "โนมัด" (เก็บของข้ามเกาะชั่วคราว) — ฝั่งเกมรับ global
        // On(NomadInfo) (nexonSRC/PlayGuideSystem.cs:241-248: IsNomad/NomadCount) ⇒ ตอบ
        // ไม่ใช่โนมัด (ยังไม่มีระบบนี้)
        _connection.Recv(delegate(GetNomadInfo msg, PacketHeader header)
        {
            Send(new NomadInfo { IsNomad = false, NomadCount = 0 });
        });

        // GetReturnerInfo (3450983) — สถานะ "ผู้กลับ" (ผู้เล่นที่ห่างหายไปแล้วกลับมา — ได้โบนัส)
        // ฝั่งเกมรับ global On(ReturnerInfo) (nexonSRC/PlayGuideSystem.cs:206-220: IsReturner/Until
        // แล้วตั้งตัวจับเวลาขอซ้ำเมื่อ Until หมด) ⇒ ตอบไม่ใช่ผู้กลับ · Until=0 ไม่กระตุ้นให้ขอซ้ำ
        _connection.Recv(delegate(GetReturnerInfo msg, PacketHeader header)
        {
            Send(new ReturnerInfo { IsReturner = false, Since = 0.0, Until = 0.0, ReturnerCount = 0 });
        });

        // GetExpiredProducts é registrado junto aos outros fluxos em Player.Market.cs.

        // EngagementAgreementChanged (1444250) — client ส่งสถานะยินยอม (สัญญา/ข้อตกลง) แบบ
        // **ไม่ผูกรอ** (nexonSRC/EngagementSystem.cs:55 UpdateEngagement) ⇒ รับเงียบ ๆ กัน log
        _connection.Recv(delegate(EngagementAgreementChanged msg, PacketHeader header)
        {
        });
    }
}
