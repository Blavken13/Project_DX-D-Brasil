# Correções de sobrevivência, lava e minérios — 09/10/2026

Implementado no código local do servidor. A aplicação ao servidor em execução depende da publicação desta compilação.

## Morte com vida ou saúde zeradas

A atualização de sobrevivência agora verifica morte antes de processar ações e antes/depois de reconstruir as barras. A verificação não depende de um ataque nem de uma nova mensagem `SurvivalUpdated`.

`SurvivalState.ValueAt` considera alterações pendentes. Isso evita ignorar um `Set(life, 0)` e impede que a regeneração transforme zero em um pequeno valor positivo antes da verificação de morte. Saúde zerada também dispara morte, pois ela é o limite máximo da vida.

A morte cancela coletas e demais ações pendentes, preserva o cancelamento existente das reservas e congela a vida em zero. Pedidos de coleta de um personagem morto são rejeitados; o renascimento continua recuperando as barras.

## Lava

O servidor integra o tempo de exposição ao longo do trajeto, dividindo cada segmento nos limites dos tiles. Passar por um canal de lava entre dois pontos fora da lava também causa dano. O valor vem do efeito original `lava`: 70 de vida por segundo de exposição, com a regeneração normal calculada separadamente.

O caminho recebido por TCP é preservado antes de atualizar a posição usada pelo save. O primeiro ponto do pacote original deixa de ser sobrescrito pelo último. Combate e efeitos de terreno consultam esse caminho preservado; teleporte e mudança de ilha invalidam o caminho anterior.

O cliente original acumula movimento por 0,5 segundo. O cálculo mantém o histórico recebido e aguarda uma margem de 0,6 segundo para assumir que um ponto ainda não atualizado permaneceu parado. Trechos confirmados pelo pacote são calculados imediatamente. Repetir um pacote não repete o dano. Morte e troca de posição/ilha limpam o histórico para evitar dano acumulado depois de renascer ou viajar.

As botas com a propriedade original `immune_lava` e os efeitos temporários de imunidade à lava são respeitados.

## Recursos das crateras

Spots de pedra/rocha no bioma deserto a até 24 tiles de uma cratera oferecem obsidiana. O gerador original `obsidian` entrega `stone_obsidian`, sem substituir o minério por pedra comum. Fora dessa área, pedras comuns continuam usando seus próprios recursos.

Minérios reconhecidos deixam de receber ferro ou pedra adicionais pelo fallback genérico. As variantes de basalto, granito e ferro negro sem gerador direto recebem os aliases dos itens nativos.

A consulta direta `GetCollectible` também encontra o recurso pelo tile, sem depender de um clique anterior registrado no cache.

A migração `crater_mineral_ecology_version` complementa os spots que faltam ao redor das crateras. Ela preserva construções, recursos existentes, remoções e filas de regeneração. O carregamento seguinte não repete a migração. A indução gera os minérios encontrados no entorno nativo da cratera, usando os minérios do mapa como fallback. As tabelas completas de biocoms não estão disponíveis no pacote local; a seleção usa os tipos e itens presentes nos dados originais do mapa.

## Validação

Compilação `dotnet build Durango-CustomServer/server/DurangoServer.csproj --no-restore`: sem erros; dois avisos já existentes (`SYSLIB0014` e `CS0169`).

| Verificação | Resultado |
| --- | --- |
| `--volcano-crater-check` | 204 verificações aprovadas |
| `--world-combat-check` | 52 verificações aprovadas |
| `--gameplay-check` | 58 verificações aprovadas |
| `--reported-gameplay-check` | 107 verificações aprovadas |

Também passaram, durante a implementação, `--world-check` (2.795 verificações) e `--combat-bags-check` (2.399 verificações).

O teste específico cobre os mapas disponíveis, ativação das crateras, persistência sem duplicação, travessia TCP com o movimento enviado em lotes, imunidade, morte, renascimento, bloqueio/cancelamento de coleta, menus e entrega real de obsidiana, estanho e ferro negro ao inventário no nível da ilha, além da regeneração dos spots.

Os testes usam saves temporários. O servidor em execução ainda precisa receber esta versão para que as mudanças apareçam no jogo.
