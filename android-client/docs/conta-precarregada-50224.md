# Build 50224 — conta carregada na tela de login

APK: `android-client/dist/LostHorizon-conta-precarregada-50224.apk`.

SHA-256: `e616a8655041267944eb75d581c169855d2286a00f60da4be52e70380523a96a`.

Foi instalada por atualização no aparelho USB Xiaomi 23021RAAEG (Redmi Note 12, Android API 35, páginas de 4096 bytes), preservando a sessão existente. O fluxo de **Continuar** carregou os três personagens, passou pela tela de seleção e entrou no mundo. A captura do mundo foi revisada em `android-client/work/usb-account-test/current-screen.png`. Não houve encerramento observado nesse percurso. Esse aparelho não é o Poco F7.

## Análise da hipótese

O comportamento relatado pelo tester torna pertinente investigar a autenticação e a consulta da conta. O diagnóstico do Poco da build 50222 confirma SIGSEGV cerca de um segundo após a abertura do Unity. O launcher recebeu HTTP 200, mas os registros nativos terminam depois do primeiro envio HTTP, classificado como outra rota, sem a resposta ou uma pilha nativa. Portanto, o arquivo não comprova que os personagens causam o crash. Também não permite concluir que o defeito está no vídeo, GPU ou tamanho de página.

O launcher anterior já consultava `/accounts` ao restaurar uma sessão e descartava a resposta. O jogo fazia posteriormente a própria consulta. `/accounts` fornece a identidade da conta, os slots e os resumos dos personagens: identificador, nome, nível e datas. Os dados completos do personagem escolhido, inventário, mundo e recursos continuam pertencendo ao carregamento normal do jogo.

A build 50224 permite testar a hipótese com uma mudança concreta: baixa e valida a resposta antes de abrir o Unity e entrega os mesmos bytes ao leitor HTTP e ao desserializador existentes do jogo. Uma conta incompatível pode ser rejeitada no login, sem prosseguir para o Unity. Um defeito nativo independente desses dados ainda pode ocorrer; a confirmação no Poco está pendente.

## Comportamento

Depois de **Entrar**, ou de **Continuar** com uma sessão válida, aparece um painel com uma barra horizontal no rodapé da tela de login. Ele informa a consulta, o download dos personagens, a validação e a preparação do jogo. O progresso usa os bytes recebidos quando o tamanho da resposta é conhecido; durante a espera pela resposta ele é indeterminado. Não há atraso artificial: no USB a consulta e a validação levaram aproximadamente 111 ms, então a barra pode aparecer brevemente em conexões rápidas.

A validação confere a identidade da conta autenticada, a lista de personagens, os slots, identificadores duplicados, níveis e datas. Uma conta sem personagens é válida. Erro de rede ou resposta inválida permanece na tela de login e libera uma nova tentativa; HTTP 401/403 pede um novo login. A sessão salva é decodificada localmente para evitar uma consulta duplicada antes da barra.

A resposta fica somente na memória, em um servidor HTTP restrito a `127.0.0.1`, com endereço aleatório de uso único e validade de até 120 segundos. A ponte `liblhaccount.so` redireciona exclusivamente a primeira consulta `/accounts` do gateway configurado para essa resposta. `/sessions` e as demais rotas continuam com o fluxo original de autenticação. O cache é liberado após o consumo ou o prazo. Senhas e conteúdo dos personagens não são adicionados aos diagnósticos.

## Evidência do teste USB

O relatório [validacao-conta-50224-usb.json](validacao-conta-50224-usb.json) verifica o hash do APK instalado e a ordem das etapas usando os tempos monotônicos da mesma sessão:

| Etapa | Sequência |
| --- | ---: |
| ACCOUNT_PREFETCH_START | 48 |
| ACCOUNT_PREFETCH_HTTP_200 | 49 |
| ACCOUNT_PREFETCH_VALIDATED_3 | 50 |
| LOGIN_VIDEO_RELEASING | 51 |
| GAME_PREPARING | 54 |
| ACCOUNT_CACHE_BRIDGE_READY | 56 |
| UNITY_STARTING | 57 |
| ACCOUNT_PREFETCH_CONSUMED | 63 |

O consumo aconteceu aproximadamente 4,27 segundos após iniciar o Unity. O log `LHAccount` do mesmo PID confirmou `ACCOUNT_PREFETCH_ROUTED`, e o jogo exibiu os personagens antes de entrar no mundo. Os cliques foram realizados no aparelho: a configuração de segurança do Android bloqueou `adb shell input` com `INJECT_EVENTS`.

Também passaram 18 casos de validação na implementação real de `org.json` do Android e 11 casos ARM64 da função nativa real de envio com as APIs gerenciadas substituídas por doubles de teste. Eles cobrem conta vazia, personagens válidos, outra identidade, erro do servidor, campos inválidos, overflow, duplicatas, sessão expirada, uso único, rotas não relacionadas, exceções gerenciadas e expiração do cache. Esses testes não registram contas nem escrevem no servidor.

O APK mantém o certificado de assinatura, o DEX original, Unity, IL2CPP, `libnd.so` e os recursos do jogo. Foram preservados 1770 arquivos e 105 classes não relacionadas do launcher. Mudam somente manifesto, DEX do launcher e identificação da build; acrescenta-se `liblhaccount.so`. Assinaturas e alinhamento de 16 KiB foram verificados. O perfil da 50223 foi mantido, incluindo vídeo de título desativado por padrão e observadores nativos opcionais desativados.

O login novo com senha não foi exercitado separadamente no USB: a integração testada utilizou **Continuar**. Ambos passam pelo mesmo ponto de pré-carregamento no launcher. O Poco estava fora da VPN e não foi acessado.

## Reproduzir

```powershell
& 'C:/Users/Ander/AppData/Local/Programs/Python/Python312/python.exe' android-client/build_account_prefetch_apk.py
& 'C:/Users/Ander/AppData/Local/Programs/Python/Python312/python.exe' android-client/build_account_prefetch_apk.py --verify-only
& 'C:/Users/Ander/AppData/Local/Programs/Python/Python312/python.exe' android-client/tests/run_account_prefetch_usb.py 725b9a01
& 'C:/Users/Ander/AppData/Local/Programs/Python/Python312/python.exe' android-client/tests/verify_account_prefetch_device_trace.py android-client/work/usb-account-test android-client/dist/LostHorizon-conta-precarregada-50224.apk --output android-client/docs/validacao-conta-50224-usb.json
```

A build parte do APK 50223 com checksum fixado. Os novos helpers Java são compilados separadamente; os stubs de compilação não entram no APK. O script adapta o DEX já existente do launcher, preservando o exportador ZIP e os diagnósticos que não estão completos nos fontes antigos do launcher. A integração da 50224 está nesse script; os scripts anteriores continuam produzindo seus próprios fluxos.
