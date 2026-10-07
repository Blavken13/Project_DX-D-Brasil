# Relatório de Diagnóstico — Encerramentos do Lost Horizon no Android

**Projeto:** Lost Horizon  
**Plataforma:** Android  
**Versões observadas:** `5.2.1-losthorizon-alfa-reports3` e `5.2.1-losthorizon-alfa-stability2`  
**Base:** diagnósticos enviados pelos testers e análise técnica consolidada nesta conversa  
**Data de consolidação:** 2026-10-06

---

## 1. Objetivo

Este relatório consolida os diagnósticos de encerramento do jogo no Android reportados por testers, relacionando o modelo de aparelho, a versão do Android, o ponto visual em que o tester percebe o fechamento, os breadcrumbs internos do cliente, o tipo de encerramento registrado e a interpretação técnica provável.

As etapas de fechamento usadas pelos testers foram:

1. Ao clicar para logar, o jogo fecha.
2. Aparece a logo da Vision Force e o jogo fecha.
3. Aparece a tela de selecionar o personagem e o jogo fecha.
4. Após selecionar o personagem e tocar na tela, o jogo fecha antes de entrar no prólogo do trem.

---

## 2. Resumo executivo

Os novos diagnósticos dos aparelhos Xiaomi mostram uma família de falha diferente das observadas anteriormente em outros aparelhos.

Nos Xiaomi com Android 16, o processo termina por:

```text
event_type = signal
exit_reason = 2
exit_status = 11
```

O `status = 11` corresponde a um **SIGSEGV**, isto é, um crash nativo por acesso inválido à memória.

Isso é diferente de:

- `java_crash`, observado anteriormente em Samsung/Motorola modernos;
- `ANR`, observado anteriormente em um Samsung Android 12.

Os novos eventos Xiaomi mostram que o cliente pode cair em momentos diferentes do fluxo:

```text
AUTH_READY
```

ou:

```text
SESSION_CALLBACK_RETURN
```

ou ainda:

```text
CALLBACK_ROUTE_RETURN
```

O padrão comum é que o encerramento é nativo e ocorre durante ou logo após callbacks da camada de autenticação/sessão.

A evidência atual sugere que o problema pode envolver código nativo do Unity legado, callback assíncrono, referência inválida, race condition, use-after-free ou processamento de dados após resposta de conta/sessão. Essas hipóteses ainda não são conclusivas porque os diagnósticos possuem:

```text
native_frames = []
```

Sem backtrace nativo, ainda não é possível apontar a biblioteca ou a função exata responsável pelo SIGSEGV.

---

## 3. Tester 1 — Xiaomi 2602BPC18G

### Ambiente

```text
APK: 5.2.1-losthorizon-alfa-reports3
Modelo: Xiaomi 2602BPC18G
Android: 16
API: 36
Build Android: BP2A.250605.031.A3
ABI: arm64-v8a
Page size: 4096 bytes
RAM total: 11268 MiB
compatibility: false
title_video_disabled: false
Unity: 2017.4.34f1
```

### Encerramento

```text
Última etapa: AUTH_READY
Motivo: Processo recebeu sinal
exit_reason: 2
status/sinal: 11
```

Breadcrumbs:

```text
NATIVE_LOADING
NATIVE_PREPARED
AUTH_READY
```

### Relação com a etapa percebida

Esse comportamento corresponde à **Etapa 2 — aparece a logo da Vision Force e o jogo fecha**.

### Análise

Esse aparelho não chega a registrar `ACCOUNT_REQUEST`. O processo cai muito cedo, logo depois de preparar a camada nativa e marcar a autenticação como pronta.

O sinal `11` indica `SIGSEGV`. Esse tipo de falha é nativo e não corresponde a uma exceção Java comum.

Não há evidência de falta de RAM:

```text
RAM total = 11268 MiB
```

Também não há evidência de incompatibilidade de page size 16 KB:

```text
page_size = 4096
```

A flag `compatibility=false` é relevante, mas não explica sozinha o crash, pois outro Xiaomi com `compatibility=true` também sofre SIGSEGV.

---

## 4. Tester 2 — Xiaomi 25053PC47G — ocorrência anterior

### Ambiente

```text
version_name: 5.2.1-losthorizon-alfa-stability2
version_code: 50214
manufacturer: Xiaomi
model: 25053PC47G
Android: 16
API: 36
Build Android: BP2A.250605.031.A3
ABI: arm64-v8a
RAM: 11233 MiB
compatibility: true
page_size: 4096
GPU: Adreno (TM) 825
GPU vendor: Qualcomm
OpenGL ES: 3.2
```

### Encerramento

```text
event_type = signal
exit_reason = 2
exit_status = 11
last_stage = SESSION_CALLBACK_RETURN
rss_kib = 337960
```

Novamente, trata-se de `SIGSEGV`.

### Breadcrumbs relevantes

O fluxo de autenticação termina normalmente:

```text
AUTH_SEND_ENTER
AUTH_READY
AUTH_URI_READ_BEGIN
AUTH_URI_READ_RETURN
AUTH_URL_READ_BEGIN
AUTH_URL_READ_RETURN
AUTH_URL_CONVERT_BEGIN
AUTH_URL_CONVERT_RETURN
AUTH_TOKEN_ALLOCATION_BEGIN
AUTH_TOKEN_ALLOCATION_RETURN
AUTH_TOKEN_FIELD_BEGIN
AUTH_TOKEN_FIELD_RETURN
AUTH_TIMEOUTS_BEGIN
AUTH_TIMEOUTS_RETURN
ACCOUNT_REQUEST
ACCOUNT_SEND_RETURN
```

Depois o callback de conta também é concluído:

```text
CALLBACK_ROUTE_BEGIN
CALLBACK_ROUTE_RETURN
ACCOUNT_CALLBACK_ENTER
ACCOUNT_STATE_3
ACCOUNT_RESPONSE_RECEIVED
ACCOUNT_CALLBACK_RETURN
LEGACY_AD_ID_SKIPPED
```

Mais tarde, o fluxo de sessão é executado:

```text
SESSION_REQUEST
SESSION_SEND_RETURN
SESSION_CALLBACK_ENTER
SESSION_STATE_3
SESSION_RESPONSE_RECEIVED
SESSION_CALLBACK_RETURN
```

Pouco depois, o processo termina com sinal 11.

### Relação com a etapa percebida

Esse comportamento é compatível com a **Etapa 3 — aparece a tela de selecionar o personagem e o jogo fecha**.

O tester também informou que essa situação costuma acontecer com mais frequência após desinstalar e instalar novamente o jogo. Essa observação pode indicar diferença entre primeira execução, dados locais limpos, cache, sessão persistida ou estado local de conta, mas ainda não há dados suficientes para afirmar causalidade.

### Análise

Esse log demonstra que `ACCOUNT_RESPONSE_RECEIVED` e `SESSION_RESPONSE_RECEIVED` ocorreram com sucesso. Logo, nesse caso o servidor não estava simplesmente indisponível.

O fluxo foi:

```text
cliente solicita conta
→ recebe conta
→ cliente solicita sessão
→ recebe sessão
→ callback da sessão retorna
→ SIGSEGV
```

O crash ocorre aproximadamente 1 a 2 segundos após a resposta da sessão.

Isso desloca a suspeita para código executado imediatamente depois do callback de sessão, por exemplo:

- montagem de estado da conta;
- criação da lista de personagens;
- transição de UI;
- construção de objetos Unity;
- leitura de estruturas retornadas;
- callbacks nativos;
- liberação prematura de referências;
- corrida entre callbacks assíncronos.

---

## 5. Tester 2 — atualização informada como “build final 517”

### Ambiente reportado pelo próprio diagnóstico

Apesar de o tester ter informado que atualizou para a “build final 517”, a telemetria ainda registrou:

```text
version_name = 5.2.1-losthorizon-alfa-stability2
version_code = 50214
```

Isso é uma inconsistência importante.

Há três possibilidades:

```text
1. O APK instalado não é de fato a build final 517.
2. O versionCode/versionName não foi atualizado na build 517.
3. O sistema de telemetria está lendo metadata incorreta.
```

Enquanto isso não for resolvido, não é seguro comparar esse evento como sendo inequivocamente da “build 517”.

### Encerramento

```text
event_type = signal
exit_reason = 2
exit_status = 11
last_stage = CALLBACK_ROUTE_RETURN
rss_kib = 341368
```

Novamente: `SIGSEGV`.

### Breadcrumbs relevantes

O fluxo de conta ocorre normalmente:

```text
AUTH_SEND_ENTER
AUTH_READY
ACCOUNT_REQUEST
ACCOUNT_SEND_RETURN
CALLBACK_ROUTE_BEGIN
CALLBACK_ROUTE_RETURN
ACCOUNT_CALLBACK_ENTER
ACCOUNT_STATE_3
ACCOUNT_RESPONSE_RECEIVED
ACCOUNT_CALLBACK_RETURN
LEGACY_AD_ID_SKIPPED
```

Em seguida aparecem vários blocos de autenticação e três pares de callbacks:

```text
CALLBACK_ROUTE_BEGIN
CALLBACK_ROUTE_RETURN
CALLBACK_ROUTE_BEGIN
CALLBACK_ROUTE_RETURN
CALLBACK_ROUTE_BEGIN
CALLBACK_ROUTE_RETURN
```

Nesse evento não aparece `SESSION_REQUEST`. O processo cai antes disso.

### Relação com a etapa percebida

Esse comportamento é mais compatível com a **Etapa 2 — aparece a logo e o jogo fecha**. Também pode ser percebido pelo usuário como etapa 1 dependendo do timing visual.

### Análise

Esse evento ocorre em um ponto anterior ao crash descrito na seção anterior.

Isso mostra que o mesmo aparelho pode sofrer SIGSEGV em diferentes momentos:

```text
antes de SESSION_REQUEST
```

ou:

```text
depois de SESSION_CALLBACK_RETURN
```

Esse comportamento variável é compatível com uma falha dependente de timing, concorrência, estado interno, ordem de callbacks, referência nativa inválida, use-after-free ou race condition. Essa é uma hipótese técnica, não uma conclusão definitiva.

---

## 6. Padrão observado nos callbacks

Nos dois relatórios detalhados do `25053PC47G` aparecem vários:

```text
CALLBACK_ROUTE_BEGIN
CALLBACK_ROUTE_RETURN
```

Na primeira ocorrência:

```text
ACCOUNT_CALLBACK_RETURN
...
SESSION_CALLBACK_RETURN
→ SIGSEGV
```

Na segunda:

```text
ACCOUNT_CALLBACK_RETURN
...
CALLBACK_ROUTE_RETURN
CALLBACK_ROUTE_RETURN
CALLBACK_ROUTE_RETURN
→ SIGSEGV
```

Isso sugere que a investigação deve priorizar o caminho executado durante e logo após callbacks assíncronos.

A telemetria atual, porém, não identifica qual callback corresponde a cada `CALLBACK_ROUTE_*`.

Seria melhor registrar:

```text
CALLBACK_ROUTE_BEGIN account
CALLBACK_ROUTE_RETURN account
CALLBACK_ROUTE_BEGIN session
CALLBACK_ROUTE_RETURN session
CALLBACK_ROUTE_BEGIN character_list
CALLBACK_ROUTE_RETURN character_list
```

---

## 7. LEGACY_AD_ID_SKIPPED

Nos relatórios mais novos aparece:

```text
LEGACY_AD_ID_SKIPPED
```

Isso demonstra que a mitigação para pular algum mecanismo antigo de Advertising ID está realmente sendo executada.

Mesmo assim, o SIGSEGV continua acontecendo.

Portanto, o Advertising ID legado **não é a causa única** desses crashes. Ele pode ter sido um problema separado ou um gatilho adicional, mas não explica sozinho os encerramentos atuais.

---

## 8. Relação com as quatro etapas informadas pelos testers

### Etapa 1 — ao clicar para logar o jogo fecha

Há evidência parcial. Os logs mostram crashes ocorrendo durante o fluxo de autenticação, mas alguns chegam a completar `ACCOUNT_RESPONSE_RECEIVED`. Portanto nem todos os casos de “fecha ao logar” morrem exatamente na chamada inicial de login.

### Etapa 2 — aparece a logo da Vision Force e o jogo fecha

Há evidência forte.

Exemplo:

```text
Xiaomi 2602BPC18G
NATIVE_LOADING
NATIVE_PREPARED
AUTH_READY
→ SIGSEGV
```

O segundo evento do `25053PC47G` também morre antes de `SESSION_REQUEST`.

### Etapa 3 — aparece a seleção de personagem e o jogo fecha

Há evidência forte.

Exemplo:

```text
SESSION_REQUEST
SESSION_RESPONSE_RECEIVED
SESSION_CALLBACK_RETURN
→ SIGSEGV
```

Esse fluxo é compatível com a transição para a tela de personagens.

### Etapa 4 — seleciona personagem e fecha antes do prólogo

Os logs fornecidos até agora não demonstram essa etapa com precisão suficiente.

Não há breadcrumbs como:

```text
CHARACTER_SELECTED
ENTER_WORLD_REQUEST
ENTER_WORLD_RESPONSE
SCENE_LOAD_BEGIN
TRAIN_PROLOGUE_BEGIN
```

Portanto nenhum dos eventos atuais deve ser classificado com segurança como etapa 4.

---

## 9. O que os novos logs não sustentam como causa geral

Os relatórios Xiaomi não sustentam como causa principal:

- falta de RAM;
- ANR;
- exceção Java;
- `JNIBridge.onServiceConnected`;
- page size de 16 KB;
- falha de conexão simples com o servidor;
- Advertising ID como causa única;
- falha exclusiva de reprodução do vídeo.

Nos casos Xiaomi detalhados, o processo recebe `SIGSEGV`.

---

## 10. Comparação com diagnósticos Android anteriores

Somando os relatórios anteriores enviados durante os testes, há pelo menos três famílias distintas de falha.

| Família | Aparelhos observados | Android | Tipo de falha |
|---|---|---|---|
| A — Java/JNI ServiceConnection | Samsung `SM-S918B`, Samsung `SM-A256U1`, Motorola `motorola signature` | 16 / 17 | `java.lang.Error` em `$Proxy2.onServiceConnected → JNIBridge` |
| B — ANR de sessão | Samsung `SM-N975F` | 12 | `SESSION_REQUEST → ~40 s → ANR` |
| C — SIGSEGV nativo | Xiaomi `2602BPC18G`, Xiaomi `25053PC47G` | 16 | sinal 11 em auth/session/callbacks |

Essas três famílias não devem ser tratadas como um único bug.

---

## 11. Interpretação técnica consolidada dos Xiaomi

O fluxo mais completo observado é:

```text
AUTH_READY
→ ACCOUNT_REQUEST
→ ACCOUNT_RESPONSE_RECEIVED
→ SESSION_REQUEST
→ SESSION_RESPONSE_RECEIVED
→ SESSION_CALLBACK_RETURN
→ SIGSEGV
```

Esse padrão aponta principalmente para código executado no cliente depois que respostas válidas chegam.

Uma sequência possível é:

```text
servidor envia dados
→ callback recebe resposta
→ cliente processa estado retornado
→ cliente cria/atualiza objetos ou UI
→ código nativo acessa memória inválida
→ SIGSEGV
```

Nesse cenário, o servidor pode fornecer o dado que dispara o problema, mas o defeito continua sendo um crash do cliente.

O fato de o mesmo aparelho cair em pontos diferentes reforça a hipótese de comportamento dependente de timing.

---

## 12. Dados que faltam para fechar o diagnóstico

O maior ponto cego é:

```text
native_frames = []
```

Sem backtrace nativo não é possível saber se a falha está em:

```text
libunity.so
libmain.so
libmono.so
plugin nativo
driver
JNI
outro .so
```

O próximo diagnóstico deve tentar registrar:

```text
signal
fault address
pc
lr
backtrace
library
build-id
```

O ideal seria obter algo como:

```text
signal 11 (SIGSEGV)
#00 pc ... libunity.so
#01 pc ... libmono.so
#02 pc ... libmain.so
```

---

## 13. Melhorias recomendadas nos breadcrumbs

### Nomear callbacks

Em vez de:

```text
CALLBACK_ROUTE_BEGIN
CALLBACK_ROUTE_RETURN
```

usar:

```text
CALLBACK_ROUTE_BEGIN account
CALLBACK_ROUTE_RETURN account
CALLBACK_ROUTE_BEGIN session
CALLBACK_ROUTE_RETURN session
CALLBACK_ROUTE_BEGIN character_list
CALLBACK_ROUTE_RETURN character_list
```

### Granularizar criação da tela de personagens

Após `SESSION_CALLBACK_RETURN`:

```text
CHARACTER_DATA_BEGIN
CHARACTER_DATA_RETURN
CHARACTER_COUNT_N
CHARACTER_UI_BEGIN
CHARACTER_UI_RETURN
CHARACTER_SELECT_VISIBLE
```

### Instrumentar a entrada no mundo

Para localizar a etapa 4:

```text
CHARACTER_SELECTED
ENTER_WORLD_REQUEST
ENTER_WORLD_RESPONSE
CHARACTER_CONTEXT_BEGIN
CHARACTER_CONTEXT_RETURN
SCENE_LOAD_BEGIN
TRAIN_PROLOGUE_BEGIN
```

---

## 14. Prioridades de investigação

### Prioridade 1 — confirmar a build realmente instalada

A chamada “build final 517” ainda reporta:

```text
version_code = 50214
version_name = 5.2.1-losthorizon-alfa-stability2
```

Isso precisa ser corrigido antes de comparar builds.

### Prioridade 2 — capturar tombstone/backtrace nativo

Esse é o passo mais importante. Sem o endereço da falha e a biblioteca correspondente, o diagnóstico permanece limitado.

### Prioridade 3 — instrumentar callbacks

É necessário descobrir qual callback está ativo imediatamente antes do SIGSEGV.

### Prioridade 4 — instrumentar seleção de personagem e entrada no mundo

Isso permitirá separar de forma inequívoca as etapas 2, 3 e 4.

---

## 15. Conclusão

Os novos relatórios dos Xiaomi Android 16 mostram uma terceira classe de falha no cliente Android:

```text
SIGSEGV nativo
```

Os aparelhos afetados até agora são:

```text
Xiaomi 2602BPC18G
Xiaomi 25053PC47G
```

No `2602BPC18G`, o processo cai muito cedo:

```text
AUTH_READY
→ SIGSEGV
```

No `25053PC47G`, uma ocorrência chega a:

```text
ACCOUNT_RESPONSE_RECEIVED
SESSION_RESPONSE_RECEIVED
SESSION_CALLBACK_RETURN
→ SIGSEGV
```

e outra cai antes de `SESSION_REQUEST`:

```text
ACCOUNT_CALLBACK_RETURN
...
CALLBACK_ROUTE_RETURN
→ SIGSEGV
```

O padrão variável no mesmo aparelho é compatível com uma falha sensível a timing ou estado, possivelmente envolvendo callbacks assíncronos e memória nativa.

Ainda não é possível apontar uma função exata porque:

```text
native_frames = []
```

Também é necessário resolver a inconsistência de versão da suposta “build 517”, pois o diagnóstico continua reportando:

```text
version_code = 50214
```

A investigação deve prosseguir prioritariamente com:

1. confirmação inequívoca da build instalada;
2. captura de tombstone/backtrace nativo;
3. identificação nominal dos callbacks;
4. instrumentação da seleção de personagem;
5. instrumentação da entrada no mundo/prólogo.

---

## 16. Classificação atual dos defeitos Android

```text
Família A
Java/JNI ServiceConnection
→ Samsung Android 16
→ Motorola Android 17

Família B
ANR após SESSION_REQUEST
→ Samsung Android 12

Família C
SIGSEGV nativo em auth/session/callbacks
→ Xiaomi Android 16
```

A abordagem de correção deve ser separada para cada família.
