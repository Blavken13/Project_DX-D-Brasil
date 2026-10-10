using Durango.Utils;
using Messages;

namespace Durango.Online;

public partial class Player
{
    // Usa a mesma fila de ações canceláveis da coleta, processada na thread do jogador.
    private void ScheduleInterruptibleAction(float duration, uint seq, System.Action complete = null,
        System.Action cancel = null)
    {
        Send(default(ReplySequenceMark), seq);
        Send(new Messages.Timer { Duration = duration }, seq);
        void Cancel()
        {
            cancel?.Invoke();
            Send(new Abort { Text = "Ação interrompida pelo movimento." }, seq);
            Send(default(ReplySequenceMark), seq);
        }
        _pendingCollects.Add(new PendingCollect(Gauge.CurrentTime + duration, () =>
        {
            if (!_context.AppearPlayer.IsAlive) { Cancel(); return; }
            try { complete?.Invoke(); }
            catch (System.Exception ex)
            {
                System.Console.WriteLine($"[ação] Falha ao concluir ação de {EntityId}: {ex.Message}");
                Send(new Abort { Text = "Não foi possível concluir a ação." }, seq);
            }
            finally { Send(default(ReplySequenceMark), seq); }
        }, Cancel));
    }
}
