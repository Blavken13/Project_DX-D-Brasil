# Xiaomi 2409FPCC4G — relatório 50214

O tester informou fechamento após Iniciar na seleção de personagens e,
em outras tentativas, no splash. O relatório desta tentativa identifica
Android 15/API 35, páginas de 4096 bytes e GPU PowerVR B-Series BXM-8-256,
OpenGL ES 3.2 build 1.15@6133110. Compatibilidade ligada; vídeo da seleção
habilitado. Saída por sinal 11; sem pilha Java ou nativa disponível.

## Evidências

- Unity iniciou e entrou na tela de título; o perfil solicitou 30 FPS e
  redução de resolução.
- A preparação de URI, URL, token e timeouts retornou normalmente.
- O pedido de conta foi enviado, houve resposta e o callback retornou.
- O filtro do serviço publicitário foi executado.
- A última etapa registrada ocorreu aproximadamente nove segundos antes
  da saída do processo. Ela é global ao processo e não identifica a thread
  que falhou.
- As quatro tentativas detalhadas de envio e callback foram registradas.
  Depois desse limite, os marcadores detalhados deixam de ser emitidos;
  os marcadores específicos de conta/sessão continuam habilitados.
- Não há `SESSION_REQUEST` neste relatório. Isso não confirma a causa do
  fechamento nem prova ausência de outros pedidos de rede.
- `rss_kib: 0` não é uma medição utilizável do consumo de memória.

## Próxima verificação

Repetir com apenas “Desativar vídeo da seleção” habilitado, mantendo o modo
de compatibilidade. Comparar splash, seleção e Iniciar. Reabrir o aplicativo
após a falha e compartilhar o relatório dessa tentativa.

Se a falha persistir, obter logcat do buffer de crash ou um bugreport do
aparelho afetado para localizar biblioteca e endereço. Um teste em outra GPU
valida regressões, mas não reproduz necessariamente esta incompatibilidade.
Não atribuir o SIGSEGV ao PowerVR, vídeo ou ponte sem essa evidência.

Referências: [ApplicationExitInfo](https://developer.android.com/reference/android/app/ApplicationExitInfo)
e [diagnóstico nativo Android](https://source.android.com/docs/core/tests/debug/native-crash).
