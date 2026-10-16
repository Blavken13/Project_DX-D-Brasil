# Login Android e encerramento após atualização do HyperOS

Análise local em 10/10/2026. A conta é aceita e o Unity começa a executar; o
diagnóstico fornecido confirma SIGSEGV durante a inicialização do jogo, mas não
identifica a biblioteca ou função que falhou. Não há evidência suficiente para
atribuir esse encerramento a login Google ou a bloqueio de aplicativos não registrados.

## Material conferido

- ZIP `diagnostico-2b813f0d-068b-44db-a03a-a6dded1f544d.zip`, encontrado na raiz.
  SHA-256: `317327a45605474f2c6db30634a955700deddcaeb474dbc700e6de116cc64aa4`.
  CRC, tamanhos e hashes declarados no manifesto conferidos.
- Celular: `Xiaomi 2311DRK48G`, Android API 36, build `BP2A.250605.031.A3`, ARM64,
  páginas reais de 4096 bytes. O ZIP não informa a versão completa do HyperOS,
  a data do patch de segurança nem uma captura anterior à atualização.
- APK instalado: 50224 / `5.2.1-losthorizon-conta1`.
  SHA-256: `e616a8655041267944eb75d581c169855d2286a00f60da4be52e70380523a96a`.
- APK mais recente local: 50225.
  SHA-256: `c3efe2a01b36bbda1cff269d77d13f245f81f9641a4c9822e8833307eb42238c`.
  Comparação direta dos ZIPs confirma DEXs, `libnd.so`, `libbr.so`,
  `liblhaccount.so` e `libunity.so` iguais à 50224. A mudança de gameplay na
  50225 não constitui uma correção de login.
- Foram conferidos fontes, builders, o DEX decompilado da 50224 e os ELF reais.
  `OriginalAuthActivity.java` sozinho representa o fluxo anterior: a integração
  de pré-carregamento atual é aplicada no DEX por `build_account_prefetch_apk.py`.

## Fluxo efetivo das builds 50224/50225

1. `OriginalAuthActivity` mostra uma página local em WebView. JavaScript chama
   `PrimalAndroid`; autenticação HTTP ocorre no executor Java.
2. Login novo envia usuário/senha a `/auth/login`; cadastro passa por
   `/auth/register` e depois login. O gateway retorna `auth_token`, identidade
   e validade. Continuar recupera uma sessão salva; na 50224, `AccountPreloader.restore`
   decodifica essa sessão sem a consulta duplicada do fluxo anterior.
3. `AccountPreloader.start` consulta `/accounts` com o token, baixa os resumos
   dos personagens e valida conta, slots, identificadores, níveis e datas.
   Erros tratados deixam o usuário na tela de login.
4. Os bytes validados ficam em memória num servidor HTTP limitado a
   `127.0.0.1`, com caminho aleatório, uso único e prazo. Não são código executável.
5. `accountReady` continua o lançamento: pausa e remove a fonte do vídeo,
   destrói o WebView e abre `CompatGameActivity` por `startActivity(Intent)`.
   O token segue no extra `newdawn_ticket`; as opções seguem em extras booleanos.
   Isso ocorre dentro do mesmo aplicativo e processo, sem comando shell.
6. `NativeRuntime.initialize` inicializa a classe UnityPlayer, carrega
   `libnd.so` e `libbr.so`, e chama a preparação JNI. `libil2cpp.so` é carregada
   e os hooks essenciais de HTTP, tutorial e mídia são preparados.
7. `AccountPreloader.install` carrega `liblhaccount.so`, que encadeia o hook
   HTTP existente e redireciona a primeira consulta elegível `/accounts`
   para a resposta local. Depois ocorre `new UnityPlayer(this)`.
8. O motor segue seu carregamento normal e consulta a conta. `/sessions`
   permanece no gateway real para gerar a sessão de jogo; pré-carregar a conta
   não substitui o carregamento do mundo nem a autenticação do personagem.

O login brasileiro não chama Google Sign-In/OAuth. O APK mantém componentes
Google/Firebase legados no manifesto original; sua presença não demonstra
que tenham causado este crash. `BrazilUnityBridge.java` pertence à experiência
Unity 6 e não representa a inicialização dessas builds com Unity 2017.

## O ZIP registra a passagem pelo login

Tempos dos eventos ordenados pelo relógio monotônico; a saída do sistema só
fornece horário UTC. Valores abaixo são arredondados em relação a UNITY_STARTING.

| Evento | Tempo relativo |
| --- | ---: |
| ACCOUNT_PREFETCH_START | -209 ms |
| ACCOUNT_PREFETCH_HTTP_200 | -105 ms |
| ACCOUNT_PREFETCH_VALIDATED_1 | -101 ms |
| LOGIN_VIDEO_RELEASING | -87 ms |
| NATIVE_PREPARED | -11 ms |
| ACCOUNT_CACHE_BRIDGE_READY | -1 ms |
| UNITY_STARTING | 0 ms |
| AUTH_READY e leitura/conversão da URI retornam | +1616 a +1617 ms |
| HTTP_BEGIN, rota `/[other-route]` | +1617 ms |
| HTTP_SEND_RETURN | +1620 ms |
| Saída Android: reason=2, status=11 | +1743 ms (UTC) |

`reason=2` é saída por sinal, e status 11 corresponde a SIGSEGV. O ZIP informa
`trace_status=unavailable`: não contém tombstone, backtrace ou endereço da falha.
O último envio retornou aproximadamente 123 ms antes da saída. Essa proximidade
não comprova que HTTP ou seu callback causaram o crash; outra thread pode ter falhado.
HTTP_SEND_RETURN informa retorno do envio assíncrono, não recebimento da resposta.

Não há registro de consumo da conta pelo Unity, resposta HTTP do motor, pedido
de `/sessions`, tela de título ou identificação da GPU. O download dos resumos
antes do Unity funcionou; isso não comprova que seu leitor no jogo chegou a rodar.

Há oito amostras de memória com `low_memory=false`, aproximadamente 2913–2969 MiB
livres. A última tem 2922 MiB. O motivo de saída não foi falta de memória.
Isso reduz a hipótese de encerramento por pressão global de RAM; não exclui
defeitos de alocação ou acesso inválido em código nativo.

Os 28 `HOOK_REJECTED` e `installed=false` de coverage correspondem aos observadores
opcionais desativados na variante herdada da 50223. Não são evidência de uma
negação do Android. A falha persistiu sem esses observadores, enquanto a ponte
HTTP essencial executou e retornou.

As opções divergem: `session.json` registra compatibilidade/vídeo desativado como
true; GAME_CONFIG registra ambos false. Os getters são consultados em momentos
diferentes. Isso não permite afirmar que as opções permaneceram iguais durante
toda a sessão. Não apareceu evento de título ou mídia que confirme sua execução.

## Relação com segurança e possíveis causas

**Ponto sensível real: alteração de código nativo em memória.**
`runtime_compat.c:75` aloca trampolins com mmap (RW), muda-os para RX e solicita
temporariamente RWX para modificar entradas de `libil2cpp.so`. O trecho de
`account_handoff.c:124` também solicita RWX e restaura RX. Os ELF reais contêm
mprotect; a desassemblagem das funções confirma constantes 3, 5 e 7 para os
argumentos de proteção da ponte principal, e 7/5 na ponte de conta, consistentes
com os fontes. Os hooks usam endereços específicos e atravessam JNI/IL2CPP.

Uma política de memória mais restritiva poderia afetar essa arquitetura.
Entretanto, neste aparelho foram registrados NATIVE_PREPARED e
ACCOUNT_CACHE_BRIDGE_READY, e o primeiro envio passou pelo hook. Uma recusa
simples ao instalar esses hooks deveria retornar falha antes de iniciar Unity.
O ZIP não demonstra tal recusa. A ponte ainda é uma candidata a investigar
por execução, ABI, trampolim ou acesso a objetos gerenciados, junto do motor
Unity 2017 e de seus plugins legados.

A ponte HTTP intercepta todos os envios e lê URI até para rotas que não são de
autenticação; só adiciona o token às rotas de conta/sessão. Portanto, a rota
`/[other-route]` não exclui participação da ponte, mas também não prova que
credenciais, `/accounts` ou personagens causaram a falha.

**Serviço Google legado.** Relatórios antigos da 50212 apresentaram crash Java
em `bitter.jnibridge.JNIBridge.invoke` / `ServiceConnection.onServiceConnected`.
O serviço de ID publicitário é uma hipótese documentada, distinta de login Google.
O APK atual contém `LegacyServicePolicy`, que recusa especificamente essa conexão
opcional. O ZIP novo tem SIGSEGV e não identifica conexão de serviço; não se deve
reutilizar a causa presumida dos casos Java para explicar este caso.

**Páginas de 16 KB.** A inspeção confirma `libunity.so` e `libmain.so` com segmentos
ELF de 4 KB, apesar de as pontes terem 16 KB. Isso continua relevante para outros
aparelhos, mas esta tentativa usa páginas reais de 4 KB e não demonstra esse problema.

**Apps não registrados e comandos.** Não foram encontradas chamadas shell,
ProcessBuilder ou carregamento dinâmico de DEX no caminho de lançamento analisado.
Uma restrição a injetar toques por ADB (`INJECT_EVENTS`), já observada no teste USB
do projeto, controla automação de entrada; o jogo não depende dela para iniciar.
A documentação atual da verificação de desenvolvedores Android descreve controle
de instalação/atualização, inicialmente em lojas participantes no Brasil e outros
paísses. Não documenta uma proibição geral de JNI após login por falta de registro.
É necessário o texto exato da atualização para identificar a política citada.

O relato de outro servidor que deixou de funcionar após adicionar Google login
é uma pista útil, mas o APK desse servidor pode ter mudado SDKs, Activity,
plugins ou lifecycle junto do login. Sem sua implementação/log, não identifica
uma causa comum. Nosso caso comprovado ocorre na inicialização nativa posterior
à aceitação da conta.

## Próxima investigação que pode identificar a função

1. Capturar logcat do processo e buffer de crash durante uma tentativa no aparelho
   afetado, incluindo sinais, biblioteca, PC e backtrace. Quando houver ADB
   autorizado, `adb logcat -b crash -d` é uma leitura inicial útil; capturar em tempo
   real reduz a chance de perder o registro. Se necessário, obter bugreport.
2. Para a hipótese de segurança, procurar mensagem SELinux `avc: denied`, permissão
   execmem/execmod/execute e falha de mmap/mprotect com errno. Adicionar futuramente
   esse errno à instrumentação essencial evita tratar todo insucesso como genérico.
3. Com o endereço da falha, conferir se pertence a Unity, IL2CPP, plugin ou trampolim.
   Depois montar um teste A/B que preserve os mesmos recursos e a mesma conta.
   Se apontar à ponte, considerar redirecionamento preparado no build para reduzir
   a necessidade de alterar páginas executáveis em runtime, mantendo autenticação.
4. Comparar o novo resultado ao POCO F7 da 50222: ambos chegaram ao primeiro envio
   do motor e saíram por SIGSEGV; o F7 ainda tinha observadores, este aparelho não.
   Isso orienta a busca no fluxo compartilhado, sem provar biblioteca comum.

Nenhum APK, servidor ou configuração de segurança do celular foi alterado nesta
análise. A 50225 não foi instalada como parte deste trabalho.

## Artefatos e reprodução

- `docs/diagnostico-login-hyperos-2b813f0d.json`: análise completa do ZIP.
- `docs/auditoria-login-apk-50225-hyperos.json`: comparação dos APKs, ELF e cronologia.
- `tools/inspect-login-apk.py`: inspeção local dos artefatos, sem executar o jogo.

```powershell
python android-client/tests/analyze_device_diagnostic.py diagnostico-2b813f0d-068b-44db-a03a-a6dded1f544d.zip --output docs/diagnostico-login-hyperos-2b813f0d.json
python tools/inspect-login-apk.py --diagnostic diagnostico-2b813f0d-068b-44db-a03a-a6dded1f544d.zip --output docs/auditoria-login-apk-50225-hyperos.json
```

## Referências oficiais conferidas

- [ApplicationExitInfo](https://developer.android.com/reference/android/app/ApplicationExitInfo): interpretação de razão e sinal.
- [Diagnóstico de crashes nativos](https://source.android.com/docs/core/tests/debug/native-crash): tombstone, sinal e backtrace.
- [Validação SELinux](https://source.android.com/docs/security/features/selinux/validate): coleta e interpretação de negações.
- [Páginas de 16 KB](https://developer.android.com/guide/practices/page-sizes): alinhamento ELF e limites da compatibilidade.
- [Android 14: carregamento dinâmico](https://developer.android.com/about/versions/14/behavior-changes-14#safer-dynamic-code-loading): exigência para apps que miram API 34+, não diagnóstico automático para o target 28 deste APK.
- [Verificação de desenvolvedores](https://developer.android.com/developer-verification): escopo de instalação e atualização.
- [Segurança de aplicativos Xiaomi](https://trust.mi.com/docs/miui-security-white-paper-global/4/1): assinatura e proteção de runtime; referência geral MIUI, sem confirmação do patch HyperOS relatado.
