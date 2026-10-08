# Nível do metal refinado nas receitas

O processamento `refine` usava `Increase` para a propriedade `purity_high`,
gravando nível 1 no primeiro refinamento mesmo em metal nível 60. A receita
`bowstick_metal_01` exige `purity_high: 55`. Cliente e servidor comparavam
corretamente esse atributo, mas recebiam um valor incorreto do processamento.

`purity_high` é um atributo `major` sem intensidade visível e agora acompanha
o nível do material. A correção foi centralizada para todos os atributos `major`
gerados por processamento, inclusive impermeabilização e secagem. Os atributos
`minor` dos demais tratamentos continuam
incrementando sua intensidade. A validação de receitas continua exigindo todos
os grupos de propriedades, formas e níveis originais.

Na normalização já executada ao carregar o inventário, os atributos `major`
antigos são reparados somente se estão presentes, têm modificação registrada e
processamento realizado. O reparo não concede propriedades ausentes, não
transforma material não processado e não altera nível, identidade, quantidades
ou usos restantes.

O teste `--gameplay-bugs-check` cobre material 54 recusado, 55 e 60 aceitos na
receita real, reparo de save e idempotência, preservação de atributos minor e
refinamento pelo TCP. A auditoria percorre as 720 receitas e seus 1.756 slots,
com 12.040 verificações de requisitos, incluindo alternativas OR, grupos
obrigatórios AND e propriedades abaixo/iguais/acima do mínimo. Todas as receitas
de processamento também são verificadas para geração e reparo de propriedades
`major`. A compilação e a suíte passaram.

Para aplicar localmente, encerre o servidor com Ctrl+C, aguarde o salvamento,
execute `iniciar-servidor-local.bat` e entre novamente no personagem. Os metais
e materiais afetados no inventário serão reparados no carregamento.
