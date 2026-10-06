# Nível dos itens coletados

O nível entregue é o menor entre o nível do recurso na ilha, o nível atual da categoria **Coleta** e o máximo permitido pelo protótipo do item. O nível geral do personagem, a qualidade da ferramenta e o nível enviado pelo cliente não elevam esse resultado.

Exemplos: ilha 60 e Coleta 10 entregam item 10; ilha 60 e Coleta 20 entregam item 20; ilha 10 e Coleta 60 entregam item até 10. Se o protótipo exigir um mínimo acima da habilidade, a coleta fica indisponível.

A prévia e a criação usam o mesmo cálculo. Antes da entrega, o servidor confere novamente o limite e ajusta nível e atributos caso a habilidade tenha mudado durante a animação. O item extra do premium herda o nível final. A XP da atividade é concedida depois da entrega, portanto não aumenta retroativamente o nível do item dessa coleta.

O caminho principal já aplicava o limite de habilidade. Esta revisão centraliza o cálculo, reforça a entrega final e impede overrides de recurso acima do nível da ilha. O relato do tester não informou recurso, ilha ou versão em execução, então não foi possível identificar o caso original específico.

Validação TCP em `--polish-check`: personagem nível 60, Coleta 1/10/20/60, folhas, madeira e pedra em ilha 60; ilha baixa; nível inválido enviado pelo cliente; prévia; atributos; habilidade alterada durante a coleta; premium; persistência e reconexão. Regressões de carcaças usam a categoria própria, Esquartejamento.

Para disponibilizar aos testers, atualizar/reiniciar o servidor. O build de verificação está em `Durango-CustomServer/server/bin/gather-level-fixes/`.
