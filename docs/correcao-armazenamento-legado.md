# Preservação e recuperação do armazenamento dos itens antigos

A migração de `pocket` introduzida em `d1a245f` substituía o atributo salvo pelo
padrão do protótipo quando seu valor era igual ao nível do item. Isso reduzia
bolsas e trajes que já tinham um atributo mais alto, como `bag_back` 60 → 15.

A normalização agora só corrige valores menores que o padrão. A criação de
itens novos e as melhorias de bolso até nível 60 mantêm o comportamento atual.

O módulo `deploy/staging/restore_pocket_levels.py` permite recuperar os valores
confirmados em um backup com SHA-256 verificado. A recuperação seleciona os
itens por ID, confirma protótipo, nível e a assinatura da redução, e altera
somente o nível do atributo `pocket` nos documentos atuais. Itens ausentes são
reportados sem recriação. Divergências interrompem a operação antes da gravação.

O deploy deve executar a recuperação com o servidor parado, depois de criar e
verificar um novo backup. As gravações são atômicas, preservam permissões e dono
dos arquivos, e são revertidas se uma gravação falhar durante a operação.
`verify_restored` confirma os valores após o reinício.

Validação da correção: compilação Release; 2415 verificações de combate e bolsas;
107 verificações de gameplay. A suíte Python cobre simulação sem gravação,
preservação de progresso, idempotência, transferência para armazém, divergências
no atributo ou no backup e reversão de gravações parciais.
