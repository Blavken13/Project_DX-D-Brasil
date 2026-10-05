# Tábua aparecendo como pilar na confecção

O processamento `board` removia as tags de forma de pilar e acrescentava as de
tábua, mas conservava `Item.Prototype`, nome, descrição e ícone do material
original. Ao usar um pilar de pedra, tanto a prévia quanto o item entregue
continuavam identificados como pilar de pedra. A prévia ainda consultava o
protótipo antes de aplicar o processamento.

A conversão agora usa os protótipos nativos `board_wood`, `board_stone`,
`board_bone` e `board_metal`, conforme o material. Atualiza os dados visuais e o
tamanho, mantendo o ID do inventário, nível, atributos do material e os limites
de processamento. Abrange `board`, `s02_board`, `board_02` e `board_03`.
A estimativa envia o nome do resultado já convertido.

A receita de tábua aceita pilares como matéria-prima. A referência
`source_info.recipe_id = pillar_stone` indica como obter essa matéria-prima;
não define o produto final. Ela foi mantida.

`WorkbenchCraftCheck` verifica pelo TCP a prévia, o produto, consumo, preservação
do material durante a estimativa e serialização em save para as quatro receitas,
seguindo seus materiais permitidos: `board` aceita os quatro materiais,
`board_02` usa madeira/pedra, e `s02_board` e `board_03` usam pedra.
Para `board`, seleciona matérias-primas nativas quando
disponíveis; as variantes com atributos de processamento usam materiais
preparados pela fixture. A suíte é executada por `--gameplay-bugs-check`.

Validação: build Release sem erros; `--gameplay-bugs-check` passou, incluindo
as oito combinações de receita/material acima; `--world-check` passou com
2.787 verificações. Os testes usam saves temporários.

Esta alteração é no servidor. A validação automatizada não substitui a
confirmação visual no APK do tester. Se a interface enviar efetivamente
`RecipeId = pillar_stone` após selecionar tábua, haverá também uma troca de
seleção no cliente a investigar; a correção aqui trata o resultado incorreto
confirmado para `RecipeId = board` e suas variantes.
