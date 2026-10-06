# Níveis dos dinossauros e da fabricação

## Fauna natural

O nível dos animais naturais é o nível exato do template da ilha: ilha 60 gera dinos 60, ilha 30 gera dinos 30. O mesmo nível é usado nas fórmulas de vida, ataque, defesa e grogue, além do pacote `AppearAnimal` recebido pelos clientes.

As faixas históricas por espécie e os sufixos das definições de manada deixam de reduzir o nível da fauna natural. A regra cobre a população inicial, a regeneração dos animais e o recarregamento da ilha. Spawns administrativos explícitos continuam usando o nível solicitado dentro da faixa da espécie.

## Ferramentas, armas e componentes

Na fabricação normal, o nível é a média arredondada dos níveis dos materiais utilizados, limitado pela proficiência da habilidade exigida na receita e pelo nível máximo 60. A proficiência inclui o nível da categoria, habilidades aprendidas, equipamentos e bônus do clã, conforme o cálculo já utilizado nas estatísticas de fabricação.

Exemplos sem bônus adicionais:

| Proficiência | Materiais | Resultado normal |
|---|---|---|
| 10 | Todos 60 | 10 |
| 40 | Todos 60 | 40 |
| 60 | Todos 20 | 20 |
| 60 | Todos 60 | 60 |
| 50 | 20, 40 e 60 | 40 |

O sucesso excelente pode usar o nível do melhor material, mantendo o limite da proficiência e do nível 60. A prévia mostra o resultado normal e sua chance de sucesso excelente.

As 216 receitas de criação de armas/ferramentas e seus 298 protótipos de saída aceitam a faixa calculada. Os limites de saída antigos (19, 24, 39, 44 e semelhantes) foram removidos. Requisitos de ingredientes, aprendizado, ferramentas e bancadas continuam definidos em cada receita. As faixas dos protótipos cobrem 1 a 60 para o item ser reconhecido pelos clientes PC/Android.

## Validação e ativação

`--fauna-check` percorre as ilhas instaladas e verifica nível no servidor/protocolo, população, espécies, terreno, atributos, respawn e recarregamento. `--progression-check` verifica todas as receitas/protótipos de equipamentos e executa fabricação real por TCP com diferentes habilidades e materiais, incluindo sucesso excelente, consumo, atributos e persistência.

Para ativar, atualizar o servidor e suas tabelas, reiniciar e reconectar os clientes para recarregar os dados. O artefato de validação desta alteração está em `Durango-CustomServer/server/bin/levels-fixes/`.
