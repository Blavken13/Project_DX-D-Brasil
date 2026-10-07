# Reativação de diárias, cursos e conquistas

## Conteúdo implementado

- **8 diárias de caça por bioma**, elevando as diárias habilitadas de 14 para **22**. Exigem cinco abates em ilha instável do bioma correspondente: floresta temperada, tropical, tundra, pradaria, pântano, deserto, neve ou vulcânico.
- **58 cursos do Guia de Carreiras**, com seleção, cancelamento, progresso calculado pelas habilidades aprendidas, conclusão, itens/títulos de recompensa e pontos do guia persistentes.
- **240 conquistas adicionais**, elevando as conquistas habilitadas de 117 para **357**: espécies específicas, recursos, fabricação por receita, construções, plantio, cursos e domínio.
- **379 entradas do catálogo habilitadas** no total. Os cursos são contabilizados separadamente desse total; há conquistas relacionadas aos cursos dentro das 357 conquistas.

Nomes, descrições, objetivos, dicas, nomes dos conjuntos de recompensa e títulos desse conteúdo foram convertidos para português brasileiro nos assets servidos aos clientes. As descrições das conquistas de cursos usam o nome efetivo do curso para evitar divergência entre a lista de missões e o guia.

## Como o progresso é validado

`quests/objectives_server.json` define 248 objetivos explícitos: 8 diárias e 240 conquistas. O servidor usa os IDs de espécies, protótipos, receitas e cursos; não tenta deduzir o alvo a partir do texto traduzido. As condições de bioma e tipo da ilha vêm do template real do mundo em que ocorreu o abate.

Os eventos são emitidos após o resultado da ação: animal abatido, itens coletados, fabricação concluída, construção concluída, semente plantada ou domínio declarado/expandido. Receita incorreta, outro animal, outro recurso, ilha particular e abate já contabilizado não satisfazem um objetivo específico. Variantes de carne crua, couro, ossos da perna, galhos e troncos foram mapeadas para suas famílias correspondentes.

Os cursos usam requisitos originais de habilidades e nível de categoria. O cliente não pode conceder conclusão enviando contadores próprios. Habilidades já aprendidas contam para os cursos; as habilidades e os pontos SP existentes são preservados. A conclusão fica registrada mesmo após cancelar a seleção ou retirar uma habilidade, e a recompensa só pode ser recebida uma vez.

O resgate prepara todos os itens e verifica o espaço na mochila antes de alterar o inventário. Itens, pontos do guia e registro do resgate são salvos no mesmo contexto do personagem. Os pontos do guia são separados dos SP. Os títulos recebidos podem ser equipados; apenas o título selecionado aplica seus bônus. Os limites reais de vida/energia e os valores exibidos permanecem consistentes.

## Compatibilidade e limites

- O formato de progresso das missões existentes foi mantido. O campo `learning_guide` é adicional e inicializado para saves antigos quando necessário.
- A leitura de quantidades aceita separadores de milhar em português: `1.000` continua representando mil ações. As metas existentes foram comparadas com o inventário anterior à tradução.
- As duas aproximações já existentes de diárias de processamento/culinária não foram alteradas nesta etapa.
- Recompensas de diárias e conquistas mantêm o balanceamento alfa já utilizado pelo servidor. Cursos usam os itens, títulos e pontos definidos em `advices.json`.
- Missões de facção, história, arquipélagos, PvP e eventos dependentes desses sistemas continuam fora desta liberação. Conquistas com requisitos ainda não identificados ou sistemas incompletos não foram habilitadas por aproximação.
- A abertura do menu do guia usa o desbloqueio de Lama já existente. Esse desbloqueio não implementa o ciclo de missões das facções.

## Verificação

O teste novo `--quest-reactivation-check` passou **1.703 verificações na imagem final Linux**, incluindo validação de todas as skills e recompensas dos 58 cursos, filtros de objetivos, abates reais em três tipos de ilha, curso bloqueado, contador forjado, mochila cheia, cancelamento, títulos, preservação de skills, resgate duplicado e persistência após reconexão. Também verifica que resgatar outro curso restaura a seleção ativa depois do callback do cliente original. Os saves utilizados nos testes são temporários.

Também passaram as regressões existentes de catálogo, recompensas, progressão, gameplay, bugs relatados, comida no vulcão, SP, mundo/recursos, fauna, economia, premium, social, cupons de EXP, indução, efeitos de status e apresentação. A compilação concluiu sem erros, com dois avisos preexistentes.

As **18 suítes passaram na validação local inicial**. A imagem final repetiu sete suítes em contêineres Linux isolados, sem montar os saves dos jogadores. O Windows bloqueou a execução da DLL recompilada pela política de controle de aplicativos (`0x800711C7`); a compilação local passou e a validação final executável foi feita em Linux. Os resultados estão registrados no [resumo de validação](quest-reactivation-2026-10-06/validation-summary.json).

O comportamento foi verificado no servidor e pelo protocolo TCP/MessagePack. Não foi realizado um percurso visual de todos os cursos e conquistas no aplicativo do jogo.

## Arquivos para revisão

- [Objetivos habilitados e contagens](quest-reactivation-2026-10-06/enabled-objectives.json)
- `server/Support/QuestCatalog.cs` e `server/data/assets/quests/objectives_server.json`: definição e filtros de objetivos.
- `server/Core/Player.LearningGuide.cs` e `server/Support/LearningGuideCatalog.cs`: cursos, persistência, recompensas e títulos.
- `server/QuestReactivationCheck.cs`: validação específica da reativação.
- `server/data/assets/quests/quests_for_client.json`, `advices.json` e `titles.json`: textos em português brasileiro.

## Publicação verificada

Versão final publicada em 06/10/2026 (horário de Brasília), com o servidor saudável, conexão TCP do jogo, handshakes Android/Windows e os quatro bundles do tutorial verificados. A identificação abaixo é o hash do pacote de código, incluindo as alterações locais; não representa um commit Git.

- Pacote de código: `d686b77f7d58dfc261a483462c6a1a519b030613`; imagem `durango-brasil-server:d686b77`.
- Backup anterior à versão final: `/opt/durango/backups/state-pre-d686b77-20261007T011110Z.tar.gz`. O encerramento normal salvou o estado, e todos os 229 arquivos do backup foram conferidos por SHA-256.
- **53 contas e 63 personagens preservados**. O estado persistente foi conferido antes da recriação do servidor. Jogadores podem modificar normalmente seus saves após reconectar.
- Assets públicos de missões, cursos e títulos idênticos aos arquivos da versão; **831 textos conferidos**, sem caracteres coreanos ou tailandeses no conteúdo reativado.
- Sete suítes finais aprovadas: reativação (1.703), catálogo (127), recompensas (142), progressão (104), SP (101), bugs relatados (106) e comida no vulcão (73).

Os textos são distribuídos pelo servidor aos clientes existentes. Feche e abra o jogo para recarregar o catálogo e acessar o conteúdo atualizado.

As evidências da publicação estão em [Verificação da publicação](quest-reactivation-2026-10-06/deployment-verification.json).
