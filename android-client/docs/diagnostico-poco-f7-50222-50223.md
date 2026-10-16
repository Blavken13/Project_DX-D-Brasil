# POCO F7 — crash da 50222 e teste controlado 50223

Análise de 08/10/2026 do arquivo fornecido pelo usuário:
`diagnostico-f32c3c52-ddcd-4058-9868-a343cf6ed5e5.zip`.

SHA-256 do ZIP:
`5478fb5a2a6c1457445bd13ff400665b5142fbda13d0f1411d382bd35d468d2b`.
CRC, tamanhos e SHA-256 de todos os arquivos declarados no manifesto passaram.

## O que o diagnóstico comprova

- Modelo Android: Xiaomi `25053PC47G`, identificado pelo usuário como POCO F7.
- Android API 36, build `BP2A.250605.031.A3`, somente `arm64-v8a`.
- Página real do kernel: **4096 bytes**. O problema desta tentativa não é
  incompatibilidade com um kernel de páginas de 16 KB.
- APK instalado: `50222`, SHA-256
  `e919d34151281771b50b2617ad81075d5e15bcef3f276101aeadc7c69e2fdf3b`.
- Compatibilidade e desativação do vídeo da seleção estavam ligadas.
- Login do launcher: `POST /accounts`, HTTP 200.
- Encerramento confirmado pelo Android: motivo 2, status 11, sessão
  `f32c3c52-ddcd-4058-9868-a343cf6ed5e5`, PID 9600.
- `trace_status=unavailable`: não há tombstone, biblioteca culpada, endereço
  de falha ou pilha nativa neste ZIP. Nenhuma GPU foi registrada.
- As amostras anteriores ao fechamento mostram `low_memory=false` e
  aproximadamente 4,6 GB de memória disponível.
- Os tipos dos dois logs de erro Unity são válidos; não reapareceu a
  corrupção dos níveis de log detectada na antiga 50219.

Quando o motivo é sinal, `ApplicationExitInfo.getStatus()` registra o número
do sinal. O status 11 corresponde a SIGSEGV. Isso identifica a categoria
do encerramento, sem localizar a função que falhou.
Referências: [ApplicationExitInfo](https://developer.android.com/reference/android/app/ApplicationExitInfo),
[diagnóstico nativo Android](https://source.android.com/docs/core/tests/debug/native-crash).

## Linha do tempo da tentativa

| Evento | UTC do relatório (ms) |
| --- | ---: |
| Login do launcher retorna HTTP 200 | 1791505724897 |
| Preparação do jogo | 1791505724953 |
| Início do Unity | 1791505724985 |
| Primeiro envio HTTP do motor retorna | 1791505725889 |
| Android registra saída por sinal 11 | 1791505726015 |

A saída foi registrada 1.030 ms depois de `UNITY_STARTING`, e 126 ms depois
do último evento nativo. O horário de saída do Android não é um endereço
ou uma pilha da instrução que falhou.

O motor carregou configurações e executou 62 pares de eventos de
desserialização. Antes do fechamento ocorreram dois logs de erro Unity;
o conteúdo foi omitido pelo diagnóstico por privacidade. `ui_prefab_map`
tem BEGIN sem END e `client_version` retornou nulo, mas o fluxo continuou
até o envio HTTP. Esses eventos não comprovam qual deles causou o crash.
As tentativas de tradução gettext ausentes foram seguidas pelo carregamento
de `offline/i18n/pt_BR`; não atribuir o crash a esses fallbacks.

URI e URL da primeira requisição foram lidas/convertidas e o envio retornou.
A rota está redigida como `/[other-route]`. Não houve registro de resposta
HTTP, tela de título/seleção, início de mídia ou configuração de GPU.
Não há base para afirmar que foi um callback HTTP, serviço Google, driver,
áudio ou vídeo. Desativar o vídeo da seleção não resolveu esta tentativa.

O usuário informou que o aparelho deixou a VPN. A análise e a montagem
seguintes ocorreram localmente, sem novas tentativas de ADB após essa informação.

## Variante 50223 para isolar a instrumentação

APK: `android-client/dist/LostHorizon-poco-f7-teste-sem-observadores-50223.apk`.

SHA-256:
`7b68daf23fc4e4277dc0166b4208015273711a45c8eec21934bfa67ded4cec5b`.

A 50222 instalou os 28 observadores opcionais antes de iniciar Unity.
A 50223 desativa apenas as chamadas que os instalam, dentro de
`lh_trace_install`. Isso é um **teste A/B**, não uma correção comprovada
da causa do SIGSEGV.

Não se substituiu o instalador inteiro: as três resoluções de APIs feitas
por ele também são usadas pelos eventos HTTP diretos da ponte obrigatória.
Elas continuam funcionando. Os hooks essenciais de autenticação, tutorial
e controle de mídia ficam intactos.

A alteração nativa ocupa 28 instruções, 112 bytes de regiões de código,
substituindo as chamadas de instalação por retorno de observador indisponível.
O ELF, os demais endereços, os reparos de ABI, a Unity, IL2CPP, os recursos
e ambos os arquivos DEX permanecem preservados. Os controles do perfil e
a exportação dos ZIPs continuam disponíveis.

O novo ZIP terá menos eventos nativos. É esperado que o instalador registre
os observadores como indisponíveis/prologue mismatch: nessa variante isso
representa a desativação intencional, não outro defeito encontrado no aparelho.

## Validação concluída

- Instalador real executado em emulação ARM64 em quatro situações: captura
  ligada/desligada, com/sem arquivo de cobertura disponível.
- Na base com captura ativa, o emulador observou as 28 chamadas de instalação;
  na variante, observou zero. Resolução das APIs, eventos de fallback,
  ponteiros de diagnóstico, pilha e registradores preservados.
- APIs externas de arquivo/log e instalação são simuladas nesse teste;
  ele não reproduz o crash físico nem valida drivers Android.
- Diferenças nativas restritas às 28 instruções previstas, sem alterar
  tamanho ou layout ELF.
- APK assinado, esquemas v1/v2/v3 e alinhamento ZIP de 16 KB verificados.
- Certificado comparado com a 50222: igual, permitindo atualização direta.
- Versão codificada 50223 / `5.2.1-losthorizon-poco-f7-teste2` conferida.
- 1.770 arquivos do APK 50222 idênticos. Só manifesto, `libnd.so` e
  carimbo de diagnóstico foram alterados.

Relatório de montagem:
`dist/LostHorizon-poco-f7-teste-sem-observadores-50223.apk.build.json`.

## Próximo teste no celular

Instalar a 50223 como atualização sobre a 50222. Manter as opções atuais,
repetir login e observar se passa do splash até a seleção. Não limpar dados
nem desinstalar para esse teste.

Se fechar, reabrir e exportar o diagnóstico dessa nova sessão, verificando
`version_code=50223`. Se abrir, testar a entrada no personagem e exportar
também o resultado disponível para comparar.

Se a falha desaparecer, isso sustenta investigar a instrumentação opcional;
não identifica sozinho qual observador estava envolvido. Se persistir,
ela continua sem esses 28 observadores e será necessário obter a pilha
nativa por ADB/bugreport ou outro mecanismo validado.

Scripts reproduzíveis, executados da raiz:

```powershell
python android-client/tests/analyze_device_diagnostic.py diagnostico-f32c3c52-ddcd-4058-9868-a343cf6ed5e5.zip --output android-client/work/poco-f7-50222-diagnostic.json
python android-client/tests/verify_diagnostic_observers.py
python android-client/build_poco_f7_no_observers_apk.py
```

**A causa específica da falha continua pendente. A 50223 ainda não foi
executada no POCO F7.**
