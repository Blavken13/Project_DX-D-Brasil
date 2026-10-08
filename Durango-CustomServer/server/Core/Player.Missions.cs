using Durango.Network;
using Messages;

namespace Durango.Online;

// Estado persistido das missões de caça do tutorial. As demais facções continuam
// sem missões disponíveis; as respostas preservam a estrutura do protocolo.
public partial class Player
{
    private void RegisterMissionHandlers()
    {
        _connection.Recv(delegate(GetMissions msg, PacketHeader header)
        {
            Send(BuildMissionInfosReply(), header.Seq);
            EnsureSafehouseHuntTarget();
        });
    }

    private MissionInfos BuildMissionInfosReply() => BuildSafehouseMissionInfos();
}
