# Auditoria das missões — 06/10/2026

**Ainda não temos tudo implementado para reativar 100% das missões com segurança.** Temos os arquivos originais do cliente e uma base funcional de diárias e conquistas. Faltam regras e sistemas no servidor para boa parte do conteúdo. Habilitar categorias na interface não resolve esses bloqueios.

Este levantamento compara o código atual, o catálogo carregado pelo servidor e os TextAssets dos clientes originais PC e Android disponíveis no projeto. Não altera o progresso dos jogadores nem publica uma nova versão no servidor.

As contagens abaixo registram o estado anterior à reativação. A implementação posterior está descrita em [Reativação de diárias, cursos e conquistas](reativacao-missoes-2026-10-06.md).

## Inventário do catálogo

| Grupo | Entradas | Habilitadas com regras no servidor | Situação |
| --- | ---: | ---: | --- |
| Diárias | 30 | 14 | As 30 aparecem na lista; 16 não têm conclusão implementada. |
| Conquistas permanentes | 541 | 117 | As demais 424 não estão habilitadas. |
| História principal (`sunset`) | 101 | 0 | Distribuídas em 22 capítulos; sem sequência funcional. |
| Outras categorias de história (`story`, `sunset_sub`) | 28 | 0 | Sem execução pelo sistema atual. |
| Semanais | 64 | 0 | Sem categoria jogável e sem regras de progresso/recompensa. |
| Retorno de jogadores | 50 | 0 | Fluxo de retorno e benefícios indisponíveis. |
| Eventos e temporadas — outras 31 categorias | 572 | 0 | Exigem análise e implementação por evento. |
| **Total — 38 categorias** | **1.386** | **131** | **1.255 entradas sem habilitação funcional.** |

As 131 entradas representam 9,45% do catálogo com regras habilitadas, não uma medida de fidelidade ao jogo original. Duas diárias já habilitadas têm aproximações conhecidas: `daily_cooking_b_02` conta a categoria geral de culinária; `daily_process_b_01` compartilha a categoria de processamento com outra diária. As recompensas dessas missões usam o balanceamento alfa do servidor, não uma tabela original de pagamento por missão.

Sistemas adicionais, que não devem ser somados ao catálogo como missões distintas sem verificar sobreposição:

| Sistema | Dados encontrados | Execução atual |
| --- | --- | --- |
| Facções | 7 facções, diálogos, níveis, pré-requisitos e recompensas parciais | Níveis ativados são persistidos e Lama libera o menu do guia; recomendação, aceitação e entrega de missões indisponíveis. |
| Arquipélagos | 71 definições de conjuntos, 251 entradas de região e 710 ocorrências de objetivos, com 256 IDs distintos | Sem distribuição, relatório de conclusão ou avanço das missões. |
| Cooperação em arquipélagos | 9 grupos de regiões com gatilhos e referências a animais de elite | Sem integração funcional de objetivos cooperativos. |
| Guia de carreiras | 58 cursos com habilidades exigidas, itens de recompensa, títulos, pontos e dicas | Alvos vazios, curso selecionado nulo e resgate indisponível. |

## O que já temos

- Progresso persistente, atualização de interface e resgate para a parte habilitada das diárias/conquistas.
- Eventos reais de caça, coleta, fabricação, construção, cultivo e domesticação; conquistas de nível usam atualização própria. Os eventos atuais têm poucos detalhes para filtrar objetivos específicos.
- Reset diário e pontuação de diárias já existentes.
- Protocolo e interface do cliente para história, NPCs, facções, cursos e arquipélagos. A existência das mensagens não significa que seus manipuladores executem as regras.
- Catálogo original, capítulos, diálogos, instruções do guia, definições de arquipélagos, facções, títulos e cursos.

Foram inventariados 280 TextAssets no PC original e 280 no Android original, com 35 nomes relacionados a missões, guias, facções ou história em cada plataforma. Os 13 arquivos de apoio comparados têm conteúdo original igual entre PC e Android. No servidor, 12 desses arquivos são idênticos em bytes; `advices.json` contém os ajustes de dicas realizados anteriormente.

O arquivo `quests_for_client.json` é idêntico ao original. Seus campos descrevem apresentação, categoria, ordenação e mensagens: `auto_finish`, `category`, `chapter_subject`, `description`, `display_on_hud`, `icon`, `last_quest`, `order`, `quest_end_messages`, `quest_start_messages`, `quest_type`, `subject`. Ele **não fornece uma definição completa de condições, filtros, pré-requisitos e pagamentos por missão**. O servidor atual reconstrói objetivos a partir de IDs, texto e classificação manual.

Também há roteiros `play_guide_flow`/`play_guide_event`, úteis para recuperar etapas e interações; eles não substituem a validação de progresso e recompensas pelo servidor. A tabela original `rewards`, com 1.321 entradas, descreve resultados e desbloqueios de recursos/habilidades; não é um mapa de pagamento indexado pelas 1.386 missões. `season2_rewards_client` contém recompensas específicas da temporada. Não foi encontrada, nos arquivos examinados, uma configuração original completa do servidor para todas as missões.

## Bloqueios concretos

### Diárias e conquistas

Das 16 diárias sem conclusão, 8 exigem caça por bioma, 5 dependem de missões de facção e 3 de PvP. A caça atualmente emite `Hunted` sem espécie, bioma ou nível da região. Fabricação emite categoria de receita; coleta distingue coleta comum de carcaça, mas não informa ao sistema de missões toda a identidade do recurso. Isso impede filtrar muitos objetivos específicos corretamente.

As conquistas restantes incluem cursos, animais e recursos específicos, condições de ilha, exploração, clãs, mercado, relações sociais e outros sistemas. Algumas mecânicas de base existem, mas não estão conectadas ao progresso de missões. Cada família precisa de definição explícita e evento comprovado no servidor.

### História

`QuestStore.StateOf` retorna **Finished para as 101 missões referenciadas pelos capítulos**, mesmo para um personagem sem progresso persistido. É um estado padrão que contorna a história; não comprova conclusão real.

`CustomQuestEvent` só registra o evento; `InteractWithEpicNPC` e `RequestEpicWarp` retornam indisponibilidade. Faltam sequência de capítulos, aparecimento de NPCs, validação de entregas e viagens conforme o capítulo. É necessário verificar os mapas, posicionamentos e objetos exigidos por cada etapa; ter um template não comprova que todos os recursos da missão estejam disponíveis.

**Não se deve converter globalmente esse Finished para missões pendentes.** Os jogadores existentes precisam de uma política de migração que preserve acesso às ilhas, habilidades, itens e progresso legítimo. A história reiniciada precisa ser uma decisão explícita, com caminho próprio, e não consequência de uma mudança no estado padrão.

### Facções

`GetFactions` retorna os níveis ativados e assegura o desbloqueio de Lama para o guia; essa parte foi confirmada na integração posterior. `GetMissions` retorna vazio. Recomendar, aceitar, entregar e solicitar suporte não executam um ciclo funcional. Faltam catálogo de ofertas, condições de acesso, amizade, estado de missão aceita, critérios de itens, consumo validado, recompensas e intervalos de renovação persistentes.

Os dados de facções ajudam a reconstrução, mas não oferecem sozinhos a definição completa de cada oferta. O fluxo de entrega precisa impedir perda de itens quando a missão falha e pagamento repetido após reconexão.

### Arquipélagos e guia de carreiras

O servidor não distribui os objetivos dos arquipélagos nem processa conclusão, troca de objetivos ou avanço entre regiões. A viagem para a próxima região é rejeitada. Precisamos conectar objetivos, pontos, cooperação, elites, pré-requisitos e recompensas, verificando a disponibilidade real de cada mapa.

No guia, os dados dos 58 cursos são mais completos: há requisitos de habilidades e recompensas de itens/títulos. Faltam seleção, cálculo de progresso, persistência, conclusão e resgate. Corrigir a exibição das dicas não ativa esses sistemas.

### Semanais, retorno, eventos e temporadas

Faltam calendários, condições de entrada, resets, sequências e conexão com as mecânicas específicas. Eventos de PvP e sobrevivência dependem de sistemas de temporada ainda incompletos. Conteúdo de eventos antigos deve ter sua condição de disponibilidade definida; abrir todas as temporadas simultaneamente pode produzir regras incompatíveis.

## Ordem de recuperação proposta

1. **Definições explícitas e eventos detalhados:** acrescentar alvos, quantidade, receita/protótipo, espécie, bioma, nível da ilha, pré-requisitos e recompensas verificáveis. Preservar as diárias e conquistas existentes durante a evolução do formato de save.
2. **Primeiro conteúdo recuperável:** as 8 diárias de caça por bioma, os cursos do guia e famílias de conquistas apoiadas nas mecânicas existentes. Corrigir as duas aproximações de diárias antes de declarar fidelidade aos seus objetivos.
3. **Facções:** implementar aceitação, cancelamento, entrega e amizade; depois habilitar as 5 diárias dependentes desse fluxo.
4. **História:** recuperar sequência e NPCs, validar mapas e definir migração dos personagens atuais antes de liberar capítulos.
5. **Arquipélagos:** implementar progresso de região, cooperação, elites, pontos e viagem; validar cada conjunto de objetivos.
6. **Demais grupos:** semanais, retorno e eventos por família; PvP e temporadas somente após suas dependências funcionarem.

Para cada grupo, a condição de liberação deve ser: aceitar/iniciar, avançar somente com a ação correta, concluir, pagar uma vez e restaurar o estado após reconexão. A simples aparição na interface não conta como missão recuperada.

## Verificação e limites

- O inventário foi calculado usando `QuestCatalog` e `QuestStore` reais, carregando os assets atuais, sem criar personagem ou escrever save de jogador.
- Verificação existente de catálogo: **127 verificações aprovadas**.
- Verificação existente de recompensas: **37 verificações aprovadas**, incluindo progresso, resgate, persistência e rejeição de resgate duplicado. A primeira execução apresentou disputa temporária de arquivo no Windows; a repetição isolada passou.
- Uma compilação separada foi concluída, mas sua execução foi bloqueada pelo controle de aplicativos do Windows. Os resultados acima vêm do binário existente autorizado, correspondente às regras auditadas; nenhum comportamento novo foi liberado.
- Não foi realizado um teste visual completo no cliente de cada missão, nem um percurso no servidor online de todas as etapas. Os testes atuais validam a parte implementada; não demonstram funcionamento das 1.255 entradas restantes.
- Nenhuma categoria foi ativada, nenhum estado de personagem foi migrado e nenhuma atualização foi publicada durante esta auditoria.

## Evidências

- [Inventário por missão, com títulos e descrições traduzidos (CSV)](quest-audit-2026-10-06/quests-inventory.csv)
- [Inventário detalhado (JSON)](quest-audit-2026-10-06/quests-inventory.json)
- [Contagens e estados calculados pelo servidor](quest-audit-2026-10-06/quest-readiness.json)
- [Dados de apoio e objetivos de arquipélagos](quest-audit-2026-10-06/assets-inventory.json)
- [Comparação dos arquivos originais e do servidor](quest-audit-2026-10-06/original-asset-comparison.json)
- [TextAssets dos clientes originais](quest-audit-2026-10-06/original-textassets.json)
- [Capítulos de história](quest-audit-2026-10-06/story-chapters.json)
- [Famílias de missões e exemplos](quest-audit-2026-10-06/quest-families.json)

Referências de implementação: `Support/QuestCatalog.cs`, `Core/Player.Quest.cs`, `Core/Player.QuestEngine.cs`, `Core/Player.QuestFlow.cs`, `Core/Player.Faction.cs`, `Core/Player.Missions.cs`, `Core/Player.Travel.cs`, `Core/Player.Life.cs` e manipuladores de guia em `Core/Player.cs`.
