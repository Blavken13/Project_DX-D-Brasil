# Correções de atributos, alimentação, fabricação e enclave — 2026-10-13

Alterações locais em `Durango-CustomServer/server`. Compilação de validação em `bin/gameplay-fixes`. Esta execução não publicou nem reiniciou o servidor online.

## Causas e correções

- **Habilidades:** os bônus de saúde e energia eram aplicados ao pacote de estatísticas e depois sobrescritos pelos limites antigos das barras. O cálculo dos limites agora inclui modificadores aprendidos, atributos básicos modificados, títulos, equipamentos e efeitos ativos. A regeneração por segundo passa a alimentar as curvas reais de sobrevivência.
- **Defesa e outros atributos:** modificadores de status e valores das armaduras agora entram no cálculo do personagem. Os mesmos valores de defesa são utilizados no combate. Reduções de estamina e cooldown, redução de dano, vigor, capacidade de transporte e bônus de velocidade entram nos consumidores correspondentes.
- **Buffs:** efeitos de sobrevivência são avaliados no nível efetivamente aplicado, em vez do nível mínimo do template. Amplificadores de aumento e diminuição afetam as velocidades das barras e os custos correspondentes. A expiração remove os bônus. Benefícios do clã entram na mesma fonte de modificadores, evitando aplicar regeneração duas vezes.
- **Alimentação:** o consumo aplica a energia expressa pelo alimento, recuperações percentuais de vida, saúde e energia, os modificadores temporários do bloco de alimento e os efeitos removidos pelo alimento. O preparo recalcula os tags a partir da base, remove campos residuais do estado cru e atualiza nome e ícone quando a receita fornece uma aparência de preparo. As cores definidas na receita continuam sendo utilizadas.
- **Fabricação:** o produto e a prévia usam a mesma função de construção. Propriedades adicionais e pureza dos ingredientes são transferidas ao produto e salvas; tags estruturais continuam descrevendo o protótipo de saída. Repetir um atributo entre ingredientes não soma cópias ilimitadas. A pureza principal respeita o nível do produto, enquanto propriedades adicionais mantêm sua intensidade. Cores são transferidas dos slots ou materiais especificados na receita. Os bônus numéricos seguem as fórmulas existentes no catálogo de tags.
- **Equipamentos:** o recálculo preserva bônus já salvos que não são substituídos por um tag ativo. Ataque e capacidade de mochila deixam de aplicar novamente um tag já calculado na performance do item.
- **Água:** a capacidade do recipiente considera sua performance salva e usa os dados do protótipo como fallback. A regressão verifica coleta real por `DrawWater`, entrega do item com o tag `water` e aceitação como ingrediente de fervura.
- **Enclave:** `VisitEstate` agora registra `PendingEstateArrival` quando muda de ilha. Na mesma ilha, teleporta diretamente para uma posição pertencente ao enclave. A chegada persistida continua verificando a propriedade do clã.

## Validação

Os testes usam mundos e personagens temporários. O Windows bloqueou uma DLL recompilada pelo Controle de Aplicativo; as regressões finais foram executadas pelo runtime .NET Linux local no WSL.

| Opção | Resultado | Cobertura |
| --- | --- | --- |
| `--attributes-check` | PASS | 29 verificações |
| `--gameplay-bugs-check` | PASS | catálogo de 720 receitas, processamento, bancadas e protocolo |
| `--estate-return-check` | PASS | 96 verificações |
| `--progression-check` | PASS | 148 verificações |
| `--combat-bags-check` | PASS | 2415 verificações |
| `--volcanic-food-check` | PASS | 73 verificações |
| `--survival-continuity-check` | PASS | 14 verificações |

SHA-256 da DLL validada: `010ad99a110896d0eb73a6b1eec6b9415dc9e9580af89605c3259769f1827649`.

Compilação sem erros. Os dois avisos existentes são o uso obsoleto de WebRequest em SelfTestPackages e um campo não utilizado em Gateway. A verificação de whitespace do Git passou.

## Limites práticos

A herança vale para novas fabricações. Itens antigos que já perderam propriedades não guardam os ingredientes usados e não podem ter esses atributos reconstruídos automaticamente. Propriedades sem fórmula numérica no catálogo são conservadas como propriedades do material; não foram inventadas conversões adicionais de balanceamento.

A aparência foi validada nos campos enviados pelo servidor e nas referências de ícone das receitas. Não foi realizada uma sessão visual com conta real no PC ou no APK nesta execução.

## Reprodução

`dotnet build Durango-CustomServer/server/DurangoServer.csproj --no-restore -o Durango-CustomServer/server/bin/gameplay-fixes`

`dotnet Durango-CustomServer/server/bin/gameplay-fixes/DurangoServer.dll --attributes-check --data Durango-CustomServer/server/data`

As outras suítes usam o mesmo executável com as opções listadas na tabela de resultados.
