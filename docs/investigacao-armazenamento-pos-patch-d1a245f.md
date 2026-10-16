# Investigação da redução de armazenamento após o patch d1a245f

**Atualização:** a correção e a restauração dos 28 itens foram aplicadas ao staging
na release `9ed8e54`. Os níveis persistiram após o reinício e foram verificados
novamente na validação final. [Relatório da aplicação](deploy-staging-9ed8e54.md).
Os resultados e a situação abaixo documentam a investigação anterior à aplicação.

## Causa confirmada

O commit `d1a245f` introduziu em `ItemCraftModifications.Initialize` uma
normalização do atributo `pocket`. Quando seu valor salvo era igual ao nível
do item e não havia uma modificação explícita desse atributo, a normalização
substituía o valor pelo padrão do protótipo. Uma mochila `bag_back` de nível 60
com `pocket` 60 passou a `pocket` 15.

A igualdade com o nível do item foi usada como evidência de um erro de criação
antigo. Esse critério também corresponde aos itens que os jogadores já
possuíam. A migração reduziu seus atributos ao carregar inventários e armazéns,
e os valores menores foram persistidos. Os materiais usados na fabricação
não causaram essa redução.

## Auditoria de staging

Os itens atuais foram comparados por ID e protótipo com o backup verificado
`/opt/durango/backups/state-pre-d1a245f-20261010T012030Z.tar.gz`.
Seu SHA-256 foi conferido contra o manifesto criado antes do deploy.
Cópias `.bak` foram excluídas da contagem de itens atuais.

Foram identificados **28 itens únicos reduzidos**: 27 em inventários de
12 personagens e uma bolsa no armazém de uma ilha. Todos os casos correspondem
à regra de migração do patch. Todos os IDs afetados ainda foram encontrados.

| Protótipo | Itens afetados | Armazenamento antes → depois |
| --- | ---: | --- |
| `bag_back` | 12 | 60 → 15 |
| `bag_fabric` | 5 | 12, 15 ou 20 → 4 |
| `bag_leaf` | 5 | 10, 12 ou 30 → 2 |
| `bag_cross` | 2 | 30 ou 34 → 9 |
| `clothes_builder_01` | 1 | 59 → 2 |
| `clothes_linen_01` | 3 | 29 ou 35 → 1 |

O nível geral dos itens permaneceu igual nos casos identificados. A queda
ocorreu no atributo de armazenamento. A auditoria confirma o problema nas
mochilas e também nos trajes; os trajes identificados têm os valores da tabela.

A leitura foi feita com o servidor em execução. Alterações ainda em memória
podem não aparecer nos arquivos. A recuperação definitiva deve repetir a
comparação após o encerramento normal do servidor.

## Recuperação validada

É possível recuperar os níveis anteriores dos 28 itens identificados.
A simulação em memória revalidou os IDs, protótipos, níveis e o valor atual de
`pocket`, e restaurou o valor anterior conhecido no backup. Não houve conflitos.
Foi confirmado por comparação profunda que somente `Tags[pocket].Level` mudou;
os demais dados atuais permaneceram intactos. Nenhum save persistente foi escrito.

A recuperação deve seguir esta sequência:

1. Publicar a correção que impede a normalização de reduzir bolsos existentes.
2. Encerrar normalmente o servidor e fazer um novo backup verificado do estado atual.
3. Revalidar os itens pelo ID e restaurar somente o `pocket` confirmado no backup anterior.
4. Reiniciar e verificar os valores enviados ao cliente e sua persistência após reconexão.

Essa recuperação preserva o progresso posterior ao patch e os materiais,
cores, durabilidade, melhorias e identidade atuais de cada item. Não deve
recriar itens ou substituir saves inteiros pelo estado anterior.

## Correção local preparada

`ItemCraftModifications.Initialize` agora só ajusta o bolso legado quando seu
valor é menor que o padrão do protótipo. Valores antigos maiores são preservados.
A criação de novos itens continua usando os atributos próprios do protótipo,
e as melhorias de bolso continuam permitindo nível 60 conforme os materiais.

Foram adicionadas regressões para os seis protótipos afetados, recarregamento,
save e reconexão de bolsa e traje antigos, capacidade efetiva e aplicação de
uma melhoria de bolso sem duplicar efeitos.

- Compilação local: sucesso, sem erros.
- O Controle de Aplicativo do Windows bloqueou a execução da DLL local.
- Validação Release em uma imagem Docker isolada: **2415 verificações de combate e bolsas** e **107 verificações de gameplay** passaram.
- Apenas `ItemCraftModifications.cs` e `CombatBagsCheck.cs` diferem da release nessa imagem de validação.
- Nenhum volume com dados de jogadores foi montado nos testes.
- A imagem e os saves do staging continuam na release `d1a245f`; a correção e a restauração ainda não foram aplicadas.

## Evidências

- [Auditoria por ID dos itens afetados](auditoria-armazenamento-pos-patch-d1a245f.json).
- [Simulação de recuperação](restauracao-armazenamento-simulacao.json).
- [Testes da correção](correcao-armazenamento-validacao.json).
