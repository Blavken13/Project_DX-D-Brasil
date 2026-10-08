# Habilidades de combate: aprendizado e troca de arma

## Pesquisa do jogo original

A [Durango Wiki, Melee](https://durango.fandom.com/wiki/Melee) distingue aprender uma acao (primeiro nivel) de melhorar seu dano, precisao ou outra caracteristica (niveis seguintes). Tambem separa acoes de uma mao, duas maos e combate desarmado, incluindo Body Tackle utilizavel com arma. A [pagina geral de habilidades](https://durango.fandom.com/wiki/Skills) lista as categorias Melee, Ranged e Defense, mas tem pouca documentacao.

Existem diferencas historicas: uma [nota de desenvolvimento de junho de 2019 reproduzida pela comunidade](https://www.reddit.com/r/DurangoWildLands/comments/c3csd2/developer_note_combat_changes_warp_rush_and/) anuncia joystick em combate e remocao da reserva de habilidades. Por isso, nao usamos nomes, custos ou posturas de guias antigos para substituir as tabelas desta versao do cliente.

Os detalhes desta correcao foram confirmados no material nativo do projeto:

- `skill/skills.json`: nos aprendidos e recompensas por nivel.
- `skill/rewards.json`: recompensas `type: 8`, com `action_ids` para cada variante de arma. Uma recompensa pode liberar a mesma tecnica para espada, machado e arma contundente.
- `tag_allow_actions.json`: ataques basicos (`default_actions`) e tecnicas compativeis (`skill_actions`). Compatibilidade nao significa aprendizado.
- `player/player_battle_actions.json`: custos, cooldowns e slots/prioridades de cada acao.
- `Durango-OffServer/_client_src/CombatSystem.cs`, `UpdateActionSlots`: a barra escolhe a acao de maior `slot.order` em cada `slot.id`. `FillPlaceholderActions` mostra possibilidades, incluindo botoes indisponiveis; nao concede habilidades.
- `Durango-OffServer/_client_src/EquipSystem.cs`: arma principal em `main`, arma de duas maos em `both`, equipamento secundario em `sub`; duas maos nao pode coexistir com as outras. O cliente procura `main` antes de `both` ao determinar a arma.

## Causas e correcao

O servidor unia ataques basicos e todas as `skill_actions` da arma, sem ler `action_ids` das recompensas aprendidas. A barra nao era atualizada ao aprender/desaprender ou equipar. Alem disso, equipar em `both` mantinha `main` e `sub`, misturando acoes de armas diferentes.

Agora a lista autorizada e composta pelos ataques basicos da arma e pela intersecao entre acoes aprendidas e acoes compativeis. Os nos gratuitos continuam seguindo o mecanismo existente de aprendizado automatico; ataques basicos permanecem disponiveis. Melhorias nao concedem tecnicas de outra arma e remover apenas uma melhoria conserva a acao aprendida no primeiro nivel.

Mudancas no save de habilidades e nos equipamentos publicam `Actions` quando a lista muda. A execucao valida o estado atual, independentemente da ultima lista enviada, antes de consumir stamina ou iniciar cooldown/combate. Cooldowns existentes permanecem ao atualizar a barra.

Equipar uma arma de duas maos desocupa `main`/`sub`; equipar em `main` desocupa `both`. Um pedido para ocupar `sub` com `both` equipado e rejeitado. Os itens permanecem no inventario. Saves antigos com `main` e `both` simultaneos conservam `main`, conforme a precedencia do cliente; somente a referencia conflitante de equipamento e removida.

A selecao da barra segue os slots nativos automaticamente ao trocar de arma. Esta correcao restaura a autorizacao e atualizacao dessas acoes; nao recria posturas de versoes antigas nem altera formulas de dano ou o sistema de presets.

## Validacao

`CombatSkillsCheck`, incluido em `--progression-check`, usa o protocolo TCP para verificar as nove familias de armas antes/depois do aprendizado, atualizacao imediata da barra, variantes de espada/machado/contundente/arco/besta/lanca, tecnica universal, melhoria e desaprendizado, troca de arma, itens preservados, cooldown mantido e reconexao com save conflitante. Tambem verifica pedidos de execucao invalidos antes do consumo de stamina e cooldown.

Resultados: compilacao sem erros (dois avisos preexistentes); `--progression-check` passou 148 verificacoes; `--polish-check`, 253; `--safehouse-missions-check`, 47; `--gameplay-bugs-check`, incluindo os filtros de todas as receitas e atributos de coleta, passou.

Para aplicar ao jogo, encerre o servidor com Ctrl+C, aguarde o salvamento e execute o BAT local. Teste aprendendo uma tecnica e equipando uma arma compativel; apenas trocar de arma nao aprende tecnicas novas. A verificacao automatizada nao substitui o teste visual no cliente Unity.
