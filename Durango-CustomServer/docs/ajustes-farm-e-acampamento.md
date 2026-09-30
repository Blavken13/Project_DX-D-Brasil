# Pacotes, construções, ferramentas e retorno à jangada

Esta atualização precisa do servidor recompilado, do `data/config.json` atualizado
e do novo `Durango-OffServer/DurangoV2_Data/Managed/Assembly-CSharp.dll` no cliente.
A DLL preserva a tradução brasileira aplicada anteriormente.

- Pacotes perdidos oferecem coleta de suprimentos, sementes e, nas caixas
  correspondentes, armas ou roupas básicas. Como o pacote da comunidade não
  trouxe a tabela original de loot, a seleção usa somente protótipos existentes
  e exclui equipamentos premium e de eventos.
- Construções de jogadores em ilhas selvagens de farm expiram 24 horas depois de
  serem colocadas. A expiração inclui locais ainda em construção e conteúdo do
  armazenamento; portos, crateras e portais do sistema permanecem. O prazo é salvo
  e não reinicia ao reconectar ou reiniciar o servidor. Construções anteriores à
  atualização recebem 24 horas completas quando a ilha é carregada.
- Ilhas domadas, particulares e assentamentos de clã ficam protegidos. A
  classificação lógica é definida antes de carregar o mundo, inclusive quando
  um assentamento usa o arquivo de terreno de uma ilha selvagem.
- A safehouse recebe até 40 animais pequenos, espalhados fora do centro. Eles
  continuam defensivos e usam o respawn existente.
- Expandir o acampamento individual em uma ilha de nível 10 é gratuito e funciona
  mesmo sem saldo. Em níveis superiores, os custos seguem `assets/costs.json`.
  Não há nova cobrança para recuperar o tamanho já pago após reduzir o terreno.
  A validação de propriedade, limite e espaço acontece antes da cobrança.
- Armas e ferramentas usam durabilidade em pontos. Os presets originais de
  capacidade não constam do pacote; a capacidade usa o balanceamento por material
  do servidor, acrescido do nível, e o desgaste usa as deltas originais de
  `assets/constants.json`: coleta, fabricação e ataque. A prévia da fabricação e
  o item final usam a mesma unidade. Itens antigos são migrados preservando seu
  percentual de desgaste. Itens quebrados permanecem na mochila para reparo;
  ferramentas quebradas não podem ser usadas em coleta ou fabricação.
- Recursos de ilhas selvagens acompanham o nível da ilha. A projeção respeita os
  intervalos válidos de cada protótipo e não altera os caches compartilhados
  entre mapas. Materiais já coletados não ganham níveis retroativamente.
- O botão de âncora é habilitado no HUD das ilhas selvagens e da safehouse. Ele
  usa a ação existente de retorno ao porto, com teleporte e validação pelo
  servidor, sem exigir que o personagem esteja perto da jangada.

As opções `World.WildStructureLifetimeSeconds` (86400) e
`World.SafehouseAnimalCount` (40) permitem ajustar o prazo e a quantidade de fauna.
Reinicie o servidor depois de alterar essas opções.

## Validação

O comando `--polish-check --data <pasta-data>` verifica coleta de pacotes pelo TCP,
nível de recursos, migração/desgaste/reparo de ferramentas, expansão gratuita e
paga, proteção de assentamentos, persistência da expiração e retorno ao porto.
São utilizados apenas saves temporários. Essa suíte e as suítes existentes de
mundo, combate/captura/descanso, economia, cultivo, missões e efeitos passaram,
totalizando 2.200 verificações.

As alterações do cliente estão também nos fontes de `WorldMapGroup.cs` e
`EstateGridGroup.cs`. O utilitário `localization/LocalizationTools` dispõe do
comando `patch-gameplay <assembly-anterior> <assembly-saida>` para aplicar o mesmo
ajuste ao assembly. O processo confere que todos os demais métodos preservam
suas instruções. O comando `verify-gameplay <assembly-final>` também valida a
pilha de execução e os destinos dos saltos nas três funções alteradas do cliente.
