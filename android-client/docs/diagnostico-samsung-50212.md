# Samsung SM-A256U1 — reports3 / 50212

Relatório recebido em 04/10/2026: Android 16/API 36, Mali-G68, ARM64,
5388 MiB RAM, páginas de 4096 bytes. Crash Java (`java.lang.Error`) com
`bitter.jnibridge.JNIBridge.invoke`, proxy `onServiceConnected` e despacho Android
na thread principal. Último histórico anterior ao crash: `ACCOUNT_REQUEST`.

A versão 50212 é anterior à política `LegacyServicePolicy` das builds de estabilidade.
A política impede a conexão ao serviço opcional legado de identificação publicitária
Google, retornando indisponibilidade antes de o Android executar seu proxy JNI.
Essa é uma hipótese compatível com a pilha, mas o relatório não inclui o nome do
serviço ou a mensagem da exceção; não há confirmação da causa específica neste aparelho.

Build de teste: `LostHorizon-alfa-stability3-50215.apk` /
`5.2.1-losthorizon-alfa-stability3`. Preserva as correções e marcadores anteriores,
adicionando logcat `LHService` para identificar a conexão de serviço e etapas
`SERVICE_BIND_BEGIN`, `SERVICE_BIND_ACCEPTED`, `SERVICE_BIND_UNAVAILABLE`.
Não registra extras, tokens, senhas ou URLs.

Validação no aparelho: coletar crash buffer da instalação 50212 antes de atualizá-la,
confirmar modelo/pacote/build, capturar abertura até a falha e então comparar a 50215,
sem limpar dados da conta. Pendência: endereço ZeroTier e porta ADB do tester;
depuração USB ativada, sozinha, não fornece uma conexão de rede.

Referências: [ADB](https://developer.android.com/tools/adb),
[serviços vinculados](https://developer.android.com/develop/background-work/services/bound-services).
