# Comparação da inicialização: 50226 e Durango Brasil v2.2 Lite

Análise local em 11/10/2026. A diferença principal é a geração do motor: nossa
50226 usa **Unity 2017.4.34f1**; o APK fornecido usa **Unity 6000.6.3f1**.
O outro cliente tem carregador Java, IL2CPP, plugins e dados iniciais diferentes.
Portanto, seu funcionamento no aparelho é uma pista forte para investigar o
conjunto nativo legado, mas não isola uma biblioteca culpada.

O diagnóstico mais recente fornecido registra **50224**, não 50226. Ele confirma
SIGSEGV durante a execução inicial do jogo, sem pilha nativa. A causa específica
do fechamento da 50226 permanece sem confirmação no material disponível.

## Arquivos e verificações

| Artefato | SHA-256 |
| --- | --- |
| `android-client/dist/LostHorizon-login-no-jogo-50226.apk` | `6a1c420dcbfb67fc7a961ded81a0c33b862bf33ae9f0fbbdad850d6a19ab1679` |
| `Durango-Brasil-v2.2-Lite.zip` | `080b8cac71e807987398056303eaca91a119304d8dcfcee8466c84b1a92306e1` |
| `diagnostico-3e6a3e1a-c364-46c7-8f0a-7007c6a384af.zip` | `01deb492235de60168f9c3b9846700a4e85ab7ff9cb27a6e6958971df494feff` |

O arquivo Lite é o próprio APK com extensão ZIP; contém manifesto, DEX, recursos,
assets e bibliotecas na raiz. CRC dos dois APKs conferido; hashes e tamanhos do
manifesto do diagnóstico conferidos. Inspeção dos DEXs com Apktool e dos ELF com
parser local e Capstone, sem instalar ou executar os aplicativos.

As versões do motor foram confirmadas independentemente em `libunity.so`,
`globalgamemanagers` e `level0`. No carregador do Lite também aparece
`6000.6.3f1_45d8eee7de74`.

## Diferenças comprovadas

| Item | Nossa 50226 | Lite da comunidade |
| --- | --- | --- |
| Motor | Unity 2017.4.34f1 | Unity 6000.6.3f1 |
| Entrada | `CompatGameActivity`, herda `Activity` | `DurangoActivity`, herda `UnityPlayerActivity` |
| Player Java | `UnityPlayer` legado | `UnityPlayerForActivityOrService` |
| Bibliotecas ARM | Apenas ARM64 | ARM64 e ARMv7; a ponte `libdbr` existe apenas em ARM64 |
| minSdk / targetSdk | 21 / 28 | 26 / 28 |
| ELF do motor/carregador ARM64 | `libunity` e `libmain`: 4 KB | `libunity` e `libmain`: 16 KB |
| Empacotamento nativo | Sem compressão; todas as bibliotecas com posição ZIP alinhada em 16 KB | Comprimidas; `extractNativeLibs=true` |
| Plugins antes do Player | Pontes `nd`, `br` e `enginelogin` | Pré-carrega `AkSoundEngine`, `snappy` e `BlueDoveMediaRender` |
| Ponte própria | `libnd`, `libbr`, `libenginelogin`; `liblhaccount` também empacotada | `libdbr`, dependência ELF de `libmain` ARM64 |
| Componentes adicionais | Application de diagnóstico, providers Firebase/Facebook e componentes legados | Manifesto sem esses providers e serviços |
| Assets do motor | Dados Unity 2017 | Dados Unity 6 recompilados |

Todos os seis nomes de bibliotecas nativas compartilhados em ARM64 têm conteúdo
diferente, incluindo áudio, vídeo e compressão. `libil2cpp` tem 88.503.232 bytes
na 50226 e 84.753.568 bytes no Lite. Metadata, cenas iniciais e recursos do motor
também diferem. A interseção dos pacotes tem 12 arquivos iguais e 25 diferentes;
a contagem inclui entradas de assinatura.

Ambos declaram `targetSdk=28`: aumentar o target não é a explicação para o outro
abrir. Ambos também usam `hardwareAccelerated=false` na Activity do jogo.

O Lite tem `gfx-disable-mt-rendering=1` e `gfx-threading-mode=4` no `boot.config`;
nossa versão só contém `wait-for-native-debugger=0`. Entretanto, os builders
legados do projeto já desativam `m_MTRendering` em PlayerSettings. A diferença de
boot.config não prova que nossa build esteja renderizando com múltiplas threads;
copiar essas chaves entre gerações não constitui uma correção validada.

## Ordem de inicialização

**50226, conferida no DEX efetivamente empacotado:**

1. Providers declarados e `DiagnosticApplication` participam da abertura do
   processo; a Application inicia os coletores Java.
2. `CompatGameActivity.onCreate` chama `EngineLogin.prepareGame`.
3. `NativeRuntime.initialize` inicializa a classe `UnityPlayer`, cujo inicializador
   estático carrega `libmain`; carrega `libnd` e `libbr` e configura diagnóstico.
4. `NativeRuntime.prepare` abre IL2CPP antes de construir o Player e instala
   hooks de HTTP, tutorial e mídia. O instalador usa trampolins e `mprotect`.
5. `EngineLogin` carrega `libenginelogin`, limpa o buffer provisório de token e
   instala seis hooks para o modal, conta e bloqueio da interação de fundo.
6. A Activity registra `UNITY_STARTING`, constrói o Player e mostra sua View.
   O modal de login depende de o motor chegar à tela de seleção.

Isso explica por que mover o login para dentro do motor pode mudar a percepção
do sintoma: uma falha nativa anterior à seleção passa a ocorrer antes de qualquer
formulário. A mudança não demonstra, sozinha, uma regressão nova no Unity.

**Lite, conferido no DEX e nas dependências ELF:**

1. O inicializador estático de `DurangoActivity` tenta pré-carregar áudio,
   compressão e vídeo; trata falhas Java desse pré-carregamento e registra log.
2. `onCreate` delega à Activity do Unity moderno, que cria
   `UnityPlayerForActivityOrService` e usa seu FrameLayout.
3. O carregador moderno tenta o caminho absoluto de `libmain` e tem fallback
   para `System.loadLibrary("main")`; depois chama `NativeLoader.load`.
4. No ARM64, `libmain` tem dependência direta de `libdbr`: a ponte integra o
   carregamento nativo, mesmo sem uma chamada Java explícita para carregar `dbr`.

**O Lite também contém hooks.** A inspeção de `libdbr` encontra `hook_code`,
hooks de HTTP/conta e imports de `mmap`, `mprotect`, `dlopen` e `pthread_create`.
Logo, a comparação não sustenta atribuir o problema a uma proibição genérica
do Android a hooks. As implementações e seus pontos de instalação são diferentes;
uma falha específica da nossa ponte continua possível.

## O que o diagnóstico desse tester demonstra

Modelo registrado: **Xiaomi 25053PC47G**, Android API 36, build
`BP2A.250605.031.A3`, somente `arm64-v8a`, páginas reais de **4096 bytes**.
O sistema completo do HyperOS não aparece no ZIP.

`session.json` registra `version_code=50224` e hash
`e616a8655041267944eb75d581c169855d2286a00f60da4be52e70380523a96a`.
Esse hash foi conferido contra o APK 50224 local. Pode ser uma sessão anterior
preservada na exportação; o ZIP não demonstra uma tentativa na 50226.

| Evento | Tempo em relação a UNITY_STARTING |
| --- | ---: |
| Consulta de conta retorna HTTP 200 | -64,5 ms |
| Conta validada: zero personagens | -63,4 ms |
| NATIVE_PREPARED | -0,9 ms |
| ACCOUNT_CACHE_BRIDGE_READY | -0,4 ms |
| UNITY_STARTING | 0 ms |
| AUTH_READY e primeiro envio | +656 ms |
| Primeiro HTTP_SEND_RETURN | +657,9 ms |
| Segundo HTTP_SEND_RETURN | +659,7 ms |
| Saída Android, reason=2 / status=11 | +719 ms, pelo relógio UTC |

A saída por sinal 11 corresponde a SIGSEGV. O ZIP tem `trace_status=unavailable`,
sem PC, backtrace, biblioteca culpada ou informação da GPU. A interpretação dos
campos segue a [API ApplicationExitInfo](https://developer.android.com/reference/android/app/ApplicationExitInfo).

O runtime e código gerenciado chegaram a executar; a consulta Java da conta
funcionou. Os dois envios nativos foram registrados como `/[other-route]` e
retornaram. Não existe registro de callback concluído, consumo da conta pelo
motor ou entrada no mundo. O último retorno antecede a saída em aproximadamente
59 ms, mas não identifica a thread que falhou nem prova que HTTP foi a causa.

As amostras registram 4.786–4.858 MiB disponíveis e `low_memory=false`.
Não é um encerramento classificado como falta de memória; ainda pode existir
acesso inválido ou defeito de alocação em código nativo.

Os 28 `HOOK_REJECTED` correspondem aos observadores opcionais desativados na
variante herdada da 50223. A preparação essencial termina e os envios passam
pelo hook; esses eventos não demonstram uma recusa do sistema.

Há divergência de configuração: a sessão registra compatibilidade e vídeo
desativado como true, enquanto `GAME_CONFIG` registra ambos false. Esse registro
acontece antes de `NATIVE_PREPARED` e não prova o estado posterior. Não é possível
considerar esse ZIP um teste conclusivo com o vídeo realmente desativado.

## Causas e próximo teste discriminante

**Área prioritária: inicialização e execução do conjunto nativo legado.** Motor
Unity 2017, IL2CPP, plugins de áudio/vídeo e nossas pontes são candidatos reais.
O outro APK substitui todos esses elementos relevantes; seu funcionamento não
permite escolher apenas um deles como causa.

**16 KB é uma diferença real, mas não explica a tentativa registrada:** esse
tester usa 4 KB. Alinhar a posição dos arquivos no ZIP não altera os segmentos
ELF de `libunity`/`libmain`. O Lite extrai suas bibliotecas comprimidas; não estar
alinhado no ZIP não é o mesmo defeito nesse modelo de empacotamento.
Veja o [guia Android sobre páginas e alinhamento](https://developer.android.com/guide/practices/page-sizes).

**A presença de ARMv7 no Lite também não explica esse aparelho:** o diagnóstico
informa somente suporte a ARM64. **Google/Firebase/Facebook** diferem entre os
clientes, mas este ZIP mostra SIGSEGV depois da preparação nativa, sem evidência
que identifique um desses componentes como causador.

O próximo passo necessário para apontar uma função culpada é capturar uma
tentativa da **50226** nesse aparelho com logcat/crash buffer ou tombstone.
Conferir `version_code`, hash instalado, eventos `ENGINE_LOGIN_*`, PC e biblioteca
da thread que falha. Uma captura iniciada antes da abertura preserva também
eventos anteriores à criação da Activity. A análise da pilha segue o
[guia AOSP de crashes nativos](https://source.android.com/docs/core/tests/debug/native-crash).

Com esse dado, o teste deve variar um fator por vez: ponte se o PC apontar aos
hooks/trampolins, mídia se apontar ao plugin/codec, ou motor/driver se apontar à
Unity/EGL/GLES. Uma migração Unity 6 precisa manter motor, IL2CPP, metadata,
cenas, shaders e plugins compatíveis entre si; os arquivos comparados já mostram
que trocar somente `libunity.so` não reproduz o cliente que funciona.

## Evidências reproduzíveis

- `comparacao-startup-50226-comunidade.json`: inventário, hashes, manifestos,
  segmentos ELF, dependências e diferenças de arquivos.
- `comparacao-startup-50226-bootstrap-nativo.json`: símbolos/desassemblagem dos
  carregadores, ponte Lite, cronologia e comparação com as builds 50224/50225.
- `diagnostico-50226-3e6a3e1a.json`: análise do ZIP fornecido; o nome identifica
  esta investigação, enquanto a sessão interna é explicitamente 50224.

```powershell
python tools/compare-apk-startup.py
python android-client/tests/analyze_device_diagnostic.py diagnostico-3e6a3e1a-c364-46c7-8f0a-7007c6a384af.zip --output docs/diagnostico-50226-3e6a3e1a.json
python tools/inspect-community-bootstrap.py
```

Nenhum APK ou servidor foi alterado. Foram produzidos apenas scripts de inspeção,
arquivos de trabalho para leitura do DEX e relatórios locais. Não houve teste
físico nem captura de uma falha da 50226 nesta análise.
