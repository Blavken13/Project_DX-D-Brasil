# Moto g84 5G — encerramento no tutorial

Dispositivo informado: Android 15/API 35, Adreno 619, página de 4096 bytes e 7.492 MiB de RAM total. Vídeo da seleção desativado nas duas sessões.

## Evidências e hipótese

- Build 50212: razão 2 (`REASON_SIGNALED`), status 9 (`SIGKILL`), RSS amostrado 788.224 KiB (769,75 MiB). O Android documenta que pressão de memória pode aparecer nessa combinação em aparelhos sem suporte ao motivo específico de baixa memória. Também há outras causas de SIGKILL; este registro não confirma falta de memória. Houve retorno à tela de título e novas tentativas de autenticação antes do fechamento.
- Build 50214: razão 2, status 11 (`SIGSEGV`), RSS amostrado 974.704 KiB (951,86 MiB). Indica acesso inválido nativo. Não é o mesmo sinal da primeira sessão e não prova falta de memória.
- A sessão 50214 durou aproximadamente 144 segundos. O último callback registrado foi cerca de 129 segundos antes do encerramento. `SESSION_CALLBACK_RETURN` não identifica a função que falhou na jangada; os relatórios anteriores não acompanhavam a jogabilidade.
- Sem pilha nativa, endereço da falha ou logcat, não é possível determinar a função responsável. A hipótese de trabalho é instabilidade do cliente nativo durante os efeitos/atualizações/transição do tutorial, potencialmente agravada pelo consumo de memória. Não há evidência suficiente para atribuir o erro à autenticação, ao servidor, ao vídeo ou especificamente ao driver Adreno. A amostra de RSS não informa o pico ou a memória disponível do sistema.

Referência primária: [Android ApplicationExitInfo](https://developer.android.com/reference/android/app/ApplicationExitInfo).

## Build 50216 — mitigação e diagnóstico

Arquivo: `dist/LostHorizon-alfa-tutorial-stability4-50216.apk`.
Versão: `5.2.1-losthorizon-alfa-tutorial-stability4`, código `50216`.

- No modo de compatibilidade, a maior dimensão passa de 1280 para 960 pixels, mantendo a proporção e 30 FPS. Reduz a área dos buffers de renderização; não garante resolver pressão de memória de assets ou uma falha do motor.
- A restauração de K deixa de fazer buscas globais e modificações de prefabs após o objetivo da conversa. A lógica original de materiais, partida e progressão permanece.
- Os cinco objetos de tipo gerenciado mantidos pela ponte nativa recebem handles de GC, garantindo sua retenção entre coletas durante mudanças de cena.
- Eventos do guia entram nos breadcrumbs como `GUIDE_*`, apenas quando mudam. Marcadores antes/depois dos envios da jangada e da partida distinguem a etapa real de falha. Nomes são limitados a identificadores, sem mensagens de jogadores ou credenciais.
- Avisos Android `RUNNING_LOW`, `RUNNING_CRITICAL` e pressão moderada ou maior são encaminhados ao `UnityPlayer.lowMemory`, com breadcrumbs. Não há descarregamento forçado de assets em uso.
- O coletor tenta obter tombstone também para sinais SIGSEGV/SIGBUS, quando disponível. O Android pode não fornecer uma pilha nesse caso; não há garantia de `native_frames` preenchido.

Os hooks de diagnóstico foram conferidos contra os endereços, assinaturas de entrada e métodos do APK original: `TutorialIslandSystem.SendPutTutorialBoatMaterials` (`0x169e338`) e `SendDepartTutorial` (`0x169e5d4`). Instalados antes da criação do UnityPlayer, chamam o código original com a mesma ABI.

## Validação e limite

Compilação e assinatura passaram. Cinco suítes passaram: `verify_mobile_stability`, `verify_runtime_compat`, `verify_presentation`, `verify_raft_k` e `verify_tutorial_bundles`. Foram verificados os métodos/prologues originais, os marcadores no APK, os recursos/dependências do tutorial, o alinhamento ELF de 16 KiB das pontes e a preservação do motor, mídia e DEX originais.

SHA-256: `e44f824036d0a22e3e26017590573461c5f828f456881bbf89776af5f0ba5d07`.

Não havia dispositivo conectado por ADB. Esta build é uma mitigação com diagnóstico adicional; a causa e a resolução do fechamento exigem teste no moto g84. Instalar como atualização, manter o modo de compatibilidade e repetir o preenchimento completo da jangada e a partida. Se fechar novamente, reabrir para coletar o relatório com a versão 50216 e as etapas do tutorial. Não é necessário apagar os dados do jogador.
