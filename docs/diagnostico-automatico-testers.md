# Diagnóstico automático dos testers: avaliação e proposta

Data: 03/10/2026. Escopo: avaliação do APK existente, servidor e painel; sem implementar ou publicar alterações.

## Recomendação

Expandir o diagnóstico próprio do launcher com relatórios estruturados de falhas, contexto da sessão anterior e envio assíncrono na próxima abertura. Receber os dados em um coletor isolado do loop do servidor do jogo, na mesma VPS, e apresentar grupos de falhas no painel administrativo.

Começar pelas fontes que já existem: histórico de encerramentos do Android, exceções Java, etapas das nossas pontes, informações gráficas e erros de recursos observados pelo gateway. Não introduzir interceptações de sinais nativos ou leitura contínua de logcat no primeiro release.

O recurso pode ter custo baixo e limitado. O impacto real deve ser medido; não é possível prometer custo computacional literalmente zero. O projeto deve evitar rede, varreduras e escrita frequente durante o gameplay.

## O que o código atual oferece

- `DiagnosticApplication` inicia `CrashDiagnostics` na criação do processo.
- `CrashDiagnostics` recupera o histórico do Android 11/API 30+, incluindo motivo, sinal/status e RSS amostrada.
- No Android 12/API 31+, tenta extrair a pilha nativa de `ApplicationExitInfo`, quando o sistema disponibiliza o tombstone.
- Registra exceções Java com classe e frames, sem mensagens potencialmente sensíveis.
- Preserva etapas da ponte nativa, modelo/Android/ABI/página do kernel e opções de compatibilidade.
- `runtime_compat.c` obtém nome, fornecedor e versão do dispositivo gráfico na entrada do título.
- Guarda até cinco relatórios locais e oferece visualização, compartilhamento e salvamento manual.
- O gateway já escreve ocorrências de HTTP 404 de bundles Android e tabelas nos logs do servidor.
- O painel possui login administrativo e pode receber uma nova aba, sem transportar credenciais administrativas para o APK.

## Melhorias necessárias antes de automatizar o envio

| Achado no código | Consequência | Alteração proposta |
| --- | --- | --- |
| `collect()` cria relatório mesmo sem crash confirmado | Reaberturas normais podem inundar o painel se todo TXT for enviado | Classificar eventos; transmitir falhas e manter encerramentos normais apenas como contadores |
| Cabeçalho é montado com versão e preferências da abertura atual | APK atualizado ou opções alteradas podem ser associados à falha anterior | Capturar versão/configuração por sessão e armazenar com o evento |
| `graphics-info.txt` é consultado depois, sem identidade da sessão | Informação ausente ou antiga pode ser atribuída ao processo errado | Identificar o arquivo pela sessão e marcar dados indisponíveis explicitamente |
| `collect()` e `stage()` compartilham o monitor de `CrashDiagnostics` | Parsing do tombstone em background pode bloquear uma chamada `stage()` na UI | Fazer coleta fora desse lock; seções críticas curtas e nenhuma rede sob lock |
| Cursor `last-exit` é atualizado antes da gravação final do relatório | Falha de disco ou interrupção pode perder o registro | Gravar relatório e fila de forma durável antes de avançar o cursor |
| Histórico filtrado por PID anterior e apenas uma ocorrência processada | Pode perder outros registros relevantes | Examinar um conjunto pequeno por pacote, ordenar/deduplicar e associar por processo, tempo e sessão |
| Handler Java executa diagnóstico antes de chamar o handler anterior | Falha do diagnóstico pode impedir o tratamento original | Registro mínimo e protegido, preservando a chamada anterior em `finally` e limitando recursão |
| `TombstoneSummary.clean()` remove strings alfanuméricas longas genericamente | Um Build ID longo pode perder identidade útil | Validar Build IDs hexadecimais em campo específico; manter a filtragem de campos livres |
| Relatórios são somente TXT | Agregação precisa interpretar texto | JSON versionado como formato principal; gerar TXT legível para exportação |

Incluir também falhas de inicialização recuperadas sem morte do processo, identificadas como `startup_failure`, separadas de crashes confirmados pelo Android. `relatorio-0.txt` e relatórios sem falha não devem entrar como crash.

## Fontes e cobertura

| Fonte | O que ajuda a identificar | Limite |
| --- | --- | --- |
| Histórico do Android | Crash Java/nativo, ANR, falta de memória, sinal e encerramento pelo usuário | Android 11+; histórico circular e diferenças de suporte do sistema |
| Tombstone filtrado | Biblioteca, Build ID, sinal e frames com offsets relativos | Android 12+; pode não existir; símbolos privados do jogo podem não estar disponíveis |
| Exceções Java do launcher | Classe, cadeia de causas e frames | Não captura automaticamente todas as exceções gerenciadas C# do Unity |
| Etapas da sessão | Falha ao preparar motor, autenticação, título/vídeo e tutorial onde já há instrumentação | Etapa indica proximidade temporal, não causa comprovada |
| Metadados gráficos | GPU, driver/API observada e resolução/perfil | Somente depois da inicialização gráfica; não criar contexto EGL apenas para obter esses dados |
| Gateway: 404/5xx de conteúdo | Bundles/tabelas ausentes ou falhas de entrega | Conteúdo carregado do cache e problemas locais de GPU não passam pelo gateway |
| Relato manual estruturado | Personagem preto, textura faltante ou outro defeito sem crash | Exige indicação do tester; não há detector confiável de imagem preta implementado |

Um personagem preto não necessariamente produz exceção. A primeira versão deve oferecer **Reportar problema visual**, com categorias curtas, horário e contexto disponível. Captura de tela automática não é necessária; screenshot anexado explicitamente pode ser uma etapa posterior.

A Unity 2017 dispõe de `Application.logMessageReceivedThreaded`, mas nosso APK não é recompilado a partir de um projeto completo. Integrar esse callback ao IL2CPP existente requer investigação e validação separadas. Não assumir que basta adicionar uma classe Java para receber os logs Unity. Um callback futuro deve aceitar apenas erros/exceções, produzir eventos limitados e nunca fazer rede, disco ou chamadas de UI dentro do callback.

Logcat via ADB permanece ferramenta de investigação pontual. Não usar uma leitura contínua como dependência do diagnóstico de todos os testers: aumenta processamento, gera conteúdo livre e não garante acesso às mensagens relevantes em todo Android/aparelho.

## Dados recomendados

Relatório com campos permitidos e tamanhos máximos:

- `schema_version`, `event_id` aleatório, identificador aleatório de instalação e identificador da sessão do diagnóstico.
- Horário do evento, horário de recebimento pelo servidor e tempo decorrido desde a abertura.
- `versionCode`, `versionName`, identificador do build do cliente e motor.
- Fabricante/modelo, versão/API Android, build do sistema, ABI e página do kernel.
- GPU/vendor/versão gráfica quando obtidos, resolução e opções de compatibilidade usadas naquela sessão.
- Tipo do evento, motivo Android, sinal/status, RSS/PSS do registro quando disponíveis, último estado e poucos eventos anteriores.
- Classe/frames Java ou biblioteca/Build ID/offsets relativos nativos.
- Código HTTP e nome lógico validado do recurso quando observados; não URL livre com query/token.
- Contadores agregados de repetições e indicação de informação indisponível/truncada.

Não incluir senha, token de login, corpo de requisição/resposta, chat, nome de jogador, dump de memória, lista de arquivos ou logs completos do sistema. ID da instalação deve ser aleatório e próprio desse recurso, sem IMEI, MAC ou identificador do Android. Vincular conta não é necessário para comparar falhas.

No Android 11+, `setProcessStateSummary` pode preservar um resumo de até 128 bytes junto ao encerramento: versão, sessão, etapa e flags. Atualizar só em transições importantes, com tratamento de erro, porque o sistema pode limitar chamadas. Isso complementa arquivos pequenos e evita depender somente do último PID.

## Envio sem interferir no jogo

Fluxo recomendado:

1. Durante a sessão, guardar poucos eventos técnicos em um buffer limitado; persistir apenas transições importantes pelos pontos já existentes.
2. Ao ocorrer uma exceção Java fatal, registrar somente contexto local mínimo e seguir o tratamento original. Em falha nativa, deixar o Android e o motor produzirem seus registros.
3. Na próxima abertura, coletar os registros anteriores em tarefa protegida, sem bloquear o formulário de login.
4. Se o envio automático estiver habilitado e houver conectividade, transmitir um lote pequeno enquanto o login estiver visível, em conexão independente.
5. O login e a entrada no jogo não esperam resposta. Quando começar a entrada no motor, cancelar/adiar o trabalho de upload e preservar a fila.
6. Excluir pendências somente após confirmação durável do servidor. Se a resposta se perder, reenviar com o mesmo `event_id`, sem duplicar a ocorrência.

Sem conexão ou servidor disponível, continuar funcionando normalmente. Retentar em uma próxima abertura, com intervalo crescente e jitter, sem serviço permanente, polling ou loops de tentativas durante o gameplay.

Para esse launcher Java/D8, uma fila pequena em arquivos e executor temporário evita adicionar dependências AndroidX agora. WorkManager é opção se depois for necessário enviar com o app fechado; sua integração requer revisar dependências/manifesto do APK e não é necessária para o primeiro release.

Exibir em Diagnóstico **Enviar diagnóstico automaticamente**, **pendentes/enviados**, **Enviar agora**, **Reportar problema visual** e manter exportação manual. A tela informa que a coleta contém dados técnicos filtrados. Desativar o envio impede transmissões futuras sem comprometer o login.

## Limites iniciais propostos

São limites de projeto a validar, não medições já realizadas.

| Item | Limite/política inicial |
| --- | --- |
| Histórico de eventos por sessão | 64 entradas, códigos e campos pequenos; repetições viram contadores |
| Relatório final | Até 48 KiB; requisição até 64 KiB descomprimidos |
| Fila privada do APK | Até 20 eventos e 1 MiB, com política explícita de descarte e contador de truncamento |
| Rede | Uma conexão de upload por vez, até três relatórios por abertura e teto diário configurado |
| Timeouts | Conexão/leitura curtos, por exemplo 3/5 segundos, fora da UI e canceláveis ao entrar no motor |
| Falhas repetitivas não fatais | Uma amostra por assinatura/sessão e contador; nunca um envio por erro repetido |
| Tombstone | Leitura e parsing limitados, preservando motivo/frame parcial se o limite for excedido |
| Backend | Corpo/tempo/conexões/fila limitados; rejeitar excesso antes de acumular trabalho |
| Retenção inicial | 30 dias de detalhes e teto de armazenamento, com agregados históricos menores |

O orçamento de rede, limites e possibilidade de desativar envios podem vir de configuração do coletor, aplicada somente no contexto do diagnóstico. Não devem alterar autenticação ou parâmetros de gameplay.

## Servidor e painel

O servidor atual processa as rotas comuns por `WebServer.Process()` no loop do jogo. Até a leitura do corpo POST ocorre nesse caminho. Acrescentar ali uploads com gravação em disco ou consultas de banco pode introduzir atraso no mundo.

**Opção preferida:** um coletor pequeno em processo/container próprio na mesma VPS, exposto por HTTPS, com fila limitada e armazenamento persistente separado. O painel existente ganha a aba **Erros do cliente**, consultando esse coletor por integração administrativa protegida. Alternativa: listener/worker dedicado dentro do servidor, desde que requisições, corpo, persistência e consultas fiquem fora do loop e os estados compartilhados sejam seguros entre threads.

Não é necessário hospedar uma plataforma completa de observabilidade para iniciar. SQLite com índices por data, versão, tipo e assinatura atende a um volume moderado; um único writer assíncrono e limites de fila tornam o custo previsível. Backup/retenção devem preservar os dados do coletor sem misturá-los aos saves.

O APK deve conseguir enviar antes de autenticar, pois o problema pode acontecer no splash/login. Isso exige uma rota de ingestão independente da sessão do jogo. ID de instalação não prova autenticidade e nenhuma chave fixa no APK deve ser considerada segredo. Aplicar validação de esquema, limites por origem/instalação, limite global e, se adotado, ticket específico do diagnóstico. Tratar os relatos recebidos como dados do cliente, não como comandos ou fatos confiáveis do servidor.

Consulta e exportação de detalhes exigem sessão administrativa. O collector não deve aceitar o token administrativo embutido no APK. O endereço atual do gateway é HTTP; o envio de diagnóstico deve usar endpoint HTTPS separado ou proxy com TLS, sem exigir que o tráfego legado do jogo seja migrado nesta etapa.

Registrar 404/5xx do gateway como eventos estruturados com agregação em memória e persistência fora do loop. Bundles podem ser requisitados sem identidade da conta; sem um identificador de correlação realmente presente na requisição, esses registros devem aparecer como evidência global por recurso/horário. IP e coincidência temporal não comprovam que a falha pertence ao mesmo tester.

## Agrupamento e investigação

Gerar assinatura normalizada a partir de tipo de evento e evidência técnica:

- Nativo: sinal + Build IDs/bibliotecas + primeiros offsets relativos relevantes. Não usar endereços absolutos, que variam por ASLR.
- Java: classe da exceção + primeiros frames relevantes normalizados.
- Recursos: categoria HTTP + recurso validado + revisão de catálogo, quando conhecida.
- Sem pilha: agrupar por motivo/etapa como grupo amplo, indicando confiança menor.

Modelo/GPU/Android são dimensões de comparação, não parte obrigatória da assinatura. Isso permite observar a mesma falha em vários aparelhos. Comparar APKs diferentes por funções simbolizadas quando possível; sem equivalência de símbolos/build, não afirmar que offsets iguais identificam a mesma causa.

Arquivar por build os hashes/Build IDs das bibliotecas distribuídas e símbolos das nossas pontes. Isso permite localizar funções/linhas das bibliotecas próprias. Nos binários originais sem símbolos disponíveis, biblioteca e offset ainda ajudam a localizar recorrência, mas não garantem uma linha de código ou causa final.

A aba deve apresentar:

- Grupos ordenados por instalações afetadas e ocorrências, com primeiro/último registro.
- Filtros por versão, modelo, Android, GPU/driver, página 4/16 KB, etapa e opções de vídeo/gráficos.
- Distribuição dos aparelhos dentro de cada grupo, amostras da pilha e linha do tempo curta.
- Diferença entre crash confirmado, falta de memória, falha de inicialização, problema visual e motivo desconhecido.
- Estado de investigação: novo, investigando, correção em teste e resolvido; verificar recorrência nas novas versões.
- Download de amostras TXT/JSON filtradas para análise local.

Contar instalações evita interpretar 50 crashes do mesmo aparelho como 50 testers. Para calcular taxas de falha, é preciso um denominador: contadores compactos de sessões/inícios bem-sucedidos enviados em lote, também limitados. Sem isso, mostrar contagens, não porcentagens de estabilidade. Identificar taxas como relativas às sessões observadas, pois quem não reabre o app ou desativa envios pode ficar fora da coleta.

Agrupamento aponta padrões e prioridades. Por exemplo, concentração em uma GPU e no mesmo frame aumenta a suspeita de caminho gráfico; concentração em um bundle 404 favorece conteúdo ausente. Associação não é prova de causa: confirmar por reprodução e teste de uma alteração por vez.

## Entrega proposta

1. Corrigir identificação/isolamento do diagnóstico atual, JSON/fila/envio e coletor com aba de grupos: estimativa de 4–7 dias úteis de implementação para um desenvolvedor familiarizado com o projeto, depois de definir o endpoint HTTPS.
2. Validar offline, repetição, atualização do APK, falha do coletor, flood, encerramento normal e falta de pilha; testar Android 11/12+ e aparelho antigo quando disponível. Comparar tempo de abertura, memória, frame pacing e carga do servidor com a mesma base sem envio. Reservar cerca de uma semana de testes com testers; disponibilidade dos aparelhos afeta o calendário.
3. Ampliar eventos Unity/asset loading somente se os relatórios da primeira versão não forem suficientes. Integração ao IL2CPP e observação de erros não fatais têm prazo dependente da investigação; não incluídos no primeiro orçamento.

O primeiro release deve preservar motor/shaders e usar as pontes atuais apenas para códigos de evento nos pontos já instrumentados. Não substituir handlers nativos de crash nem instalar novas interceptações só para coletar logs.

## Fontes e limites desta avaliação

- [ApplicationExitInfo](https://developer.android.com/reference/android/app/ApplicationExitInfo): motivos de encerramento e disponibilidade condicional de traces/tombstones.
- [ActivityManager](https://developer.android.com/reference/android/app/ActivityManager): histórico circular, resumo de processo limitado a 128 bytes e limitação de chamadas.
- [Unity 2017: callback de logs](https://docs.unity3d.com/2017.4/Documentation/ScriptReference/Application-logMessageReceivedThreaded.html): callback pode executar em várias threads e exige processamento seguro.
- [Android: logcat](https://developer.android.com/tools/logcat): ferramenta de diagnóstico, opções dependentes do sistema e privilégios.
- [Android: WorkManager](https://developer.android.com/develop/background-work/background-tasks/persistent/getting-started/define-work): constraints, agendamento e retentativas; alternativa futura.

Arquivos examinados: `CrashDiagnostics.java`, `DiagnosticApplication.java`, `TombstoneSummary.java`, `NativeRuntime.java`, `OriginalAuthActivity.java`, `NewDawnApi.java`, `runtime_compat.c`, `WebServer.cs`, `Gateway.cs`, rotas administrativas, frontend do painel e compose de staging. Não houve alterações nesses arquivos, publicação, nova compilação ou medição de desempenho nesta etapa.
