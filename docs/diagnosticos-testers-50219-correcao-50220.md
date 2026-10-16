# Diagnósticos 50219 e correção do APK 50220

Avaliação realizada em 7 de outubro de 2026. Os quatro ZIPs de `docs/Diagnosticos` tiveram seus arquivos conferidos pelos tamanhos e SHA-256 declarados nos manifests. Todos se referem ao APK 50219 fornecido na raiz, SHA-256 `0ea3ff42413a5d3fc4b1bd955ad730b239d4b01e074ca7b6b944cf6ca3cb82ad`.

## Resultado por aparelho

| Modelo informado pelo Android | Android / API | Evidência de encerramento | Avaliação |
| --- | --- | --- | --- |
| Xiaomi 25053PC47G | Android 16 / 36 | Sinal 11 confirmado; logs Unity com tipos inválidos, próximos do encerramento; sem informação de GPU no ZIP | Crash nativo confirmado. Os hooks de logs têm um defeito verificado e são uma hipótese forte para este ponto; não há tombstone para confirmar a pilha da falha. |
| Xiaomi 23100RN82L | Android 15 / 35 | Motivo 13, descrição `SwipeUpClean`; recurso iniciou carregamento; um tipo de log inválido | O registro de saída corresponde à limpeza de recentes indicada pelo fabricante. Não confirma crash espontâneo. Pode ter havido travamento antes da remoção; o ZIP não decide isso. |
| Xiaomi 2602BPC18G | Android 16 / 36 | Motivo 13, `SwipeUpClean`; Mali-G925-Immortalis MC11; carregamentos HTTP e TCP ocorreram; dois tipos de log inválidos | Não há sinal fatal confirmado. Os registros de GPU não mostram um erro específico de shader ou de API gráfica. A sessão estava com `compatibility=false`. |
| Xiaomi 23021RAAEG | Android 15 / 35 | Sinal 11 confirmado; Adreno 610; último evento nativo `TCP_PACKET_ENCODE_BEGIN` | O crash ocorre perto da escrita do cabeçalho TCP. A corrupção do buffer por esse hook foi reproduzida com o código ARM64 real do APK. |

Os quatro aparelhos usam páginas de memória de 4 KiB e suportam ARM64. Não há evidência de problema de páginas de 16 KiB nesses ZIPs. As amostras registradas indicam `low_memory=false`, e os pedidos de login do launcher terminaram com HTTP 200. Isso não garante ausência de outros problemas durante uma sessão longa.

O Android informa o sinal de término em `ApplicationExitInfo.getStatus()` quando o motivo é um sinal. O motivo genérico 13 e a descrição de fabricante precisam ser interpretados juntos; não se deve converter automaticamente `SwipeUpClean` em crash. Referência: [ApplicationExitInfo](https://developer.android.com/reference/android/app/ApplicationExitInfo).

## Defeito comprovado na instrumentação

O binário Unity 2017.4 deste cliente recebe um argumento inicial não utilizado em métodos estáticos. A instrumentação do 50219 declarou onze desses métodos sem esse argumento. Isso deslocava a interpretação dos parâmetros e, em alguns casos, reduzia um ponteiro de 64 bits a um inteiro de 32 bits antes de chamar a função original.

O caso TCP é reproduzível: o buffer `0x300002000` chegava à função original como `0x2000`. O diagnóstico também interpretava o pedido `GetClock`, tipo 4000, como `type=0 name=unknown seq=0 reply=1`, o mesmo padrão do último evento do Xiaomi 23021RAAEG. Na versão corrigida, o buffer e os demais argumentos chegam intactos, e o evento identifica `type=4000 name=GetClock seq=1 reply=0`.

Os tipos inválidos de log nos quatro ZIPs também são evidência da interpretação deslocada: valores como `613784816` não são níveis válidos de log Unity. Uma mensagem de Wwise próxima da falha não prova que a biblioteca de áudio foi sua causa.

A convenção foi conferida diretamente nas instruções deste APK. A documentação histórica da Unity também descreve o argumento inicial de métodos estáticos e o `MethodInfo` adicional: [IL2CPP: chamadas de métodos](https://unity.com/blog/engine-platform/il2cpp-internals-method-calls).

## Correção aplicada

O 50220 adiciona adaptadores ARM64 antes e depois dos observadores afetados: resultado HTTP, exceção, callback de log Unity, cabeçalho TCP, `Resources.Load` e `LoadAll`, carregamento e conclusão de cena, aviso de pouca memória e estado/erro do loader.

Os adaptadores preservam os ponteiros, o `MethodInfo`, o tempo TCP em `d0`, o valor de retorno e a pilha. Os corpos dos observadores e suas informações originais de unwind permanecem nos mesmos endereços. O ELF conserva as permissões dos segmentos e o alinhamento de 16 KiB. Os hooks de instância continuam usando a convenção anterior.

O APK mantém o launcher de diagnóstico completo do 50219, sua exportação de ZIPs, a Unity, o código nativo do jogo, as texturas, os shaders, o áudio, o vídeo e os recursos. A comparação de conteúdo confirmou apenas três alterações: manifesto/versionamento, `libnd.so` e o carimbo técnico `diagnostics/build.json`; 1.770 arquivos do APK base permaneceram idênticos.

Não há evidência suficiente para indicar outra mudança específica de GPU ou áudio. Primeiro é necessário repetir os testes com a instrumentação corrigida, porque os hooks anteriores podiam alterar a própria execução observada. Isso corrige um defeito concreto de compatibilidade do APK de diagnóstico; não comprova que todas as falhas dos quatro aparelhos tenham sido eliminadas.

## Validação

- Os manifests dos quatro ZIPs foram validados integralmente.
- 88 verificações de entrada/saída dos onze adaptadores foram aprovadas em emulação ARM64.
- O observador TCP real reproduziu o buffer corrompido do 50219 e preservou todos os argumentos, o tempo, o retorno e a pilha no 50220.
- Os bytes mapeados da biblioteca foram comparados; as únicas alterações nos endereços existentes são os headers ELF necessários e 22 instruções de desvio.
- APK assinado e verificado nos esquemas v1, v2 e v3; alinhamento ZIP de 16 KiB verificado.
- Pacote `com.nexon.durango.global`, versão `50220`, nome `5.2.1-losthorizon-alfa-compat-abi1`, arquitetura `arm64-v8a` e atividade inicial conferidos por `aapt`.

Nenhum aparelho estava conectado ao ADB. A emulação não valida os drivers, a reprodução de mídia, a GPU nem o desempenho durante gameplay.

Scripts reproduzíveis:

```powershell
python android-client/tests/analyze_tester_diagnostics.py
python android-client/tests/verify_diagnostic_abi.py
python android-client/build_diagnostic_compat_apk.py
```

O builder exige o APK 50219 fornecido na raiz e confere seu hash antes de aplicar a correção. Para os testes ARM64, os módulos `unicorn==2.1.4` e `capstone==5.0.9` estão em `android-client/work/python-deps`. O Apktool 2.12.1 e as ferramentas de assinatura do SDK estão em `android-client/work`.

## Restauração e entrega

A pasta `android-client` foi excluída no commit `6dd89a4`. Seus 174 arquivos versionados foram restaurados da revisão anterior `0f28eee`. A pasta `android-client/dist` foi recriada com o APK 50219 fornecido e o novo APK 50220. APKs históricos gerados, caches e chaves de assinatura eram ignorados pelo Git; suas versões antigas não puderam ser recuperadas do histórico.

Foi criada uma nova chave, conforme solicitado, em `android-client/durango-br-test.jks`, com senha em `android-client/durango-br-test.pass`. Os dois arquivos permanecem ignorados pelo Git. Guarde uma cópia segura de ambos para permitir futuras atualizações assinadas com esta chave. A senha não foi incluída nos relatórios.

APK: `android-client/dist/LostHorizon-alfa-compat-diagnostico-50220.apk`.

SHA-256: `6497a96b0e68965829e85a31a8cac96873fc69a28db71ad7167a11e7166995f3`.

A assinatura é diferente da instalação 50219. Para instalar a nova versão, exporte os diagnósticos necessários e reinstale o aplicativo; uma atualização direta com outra chave será rejeitada pelo Android. Referência: [assinatura e atualizações de aplicativos Android](https://developer.android.com/studio/publish/app-signing).

Peça aos quatro testers que tentem login, seleção/criação do personagem, prólogo e entrada na ilha, mantendo inicialmente as mesmas opções de compatibilidade usadas no teste anterior. Se houver fechamento, reabra e exporte o novo ZIP, conferindo `version_code=50220`. Registre se houve crash sozinho, tela congelada ou remoção manual dos recentes. Essa distinção permitirá separar incompatibilidades remanescentes de fechamentos sem sinal fatal.
