# Flores nos recursos naturais

O item genérico `flower` (Flor) está em `data/assets/item/prototype_data.json`, categoria `food/medicine`, com a tag de material `flower`. A receita `capture_tool_02`, ferramenta de captura intermediária, pede três flores com nível mínimo 20; seu `source_info` aponta para `bush_lilac` / `flower_lilac`.

## Plantas originais

| Planta | Tipo natural | Gerador de coleta | Biomas do catálogo |
| --- | --- | --- | --- |
| Roseira silvestre | 11008 | `dogrose_flower` | Floresta temperada, tropical e pradaria |
| Lilás | 11020 | `flower_lilac` | Floresta temperada, tundra e pradaria |
| Lavanda | 11091 | `lavender_flower` | Floresta temperada |
| Lavanda, variante de pradaria | 14063 | `lavender_flower` | Pradaria |
| Capim florido | 14064 | `wiregrass_flower` | Pradaria |

Fontes: `entity_types/natural.json`, `item/generator_client_data.json` e `item/recipes.json`. Os nomes dos geradores de roseira, lavanda e capim florido não são IDs de itens e não correspondem ao nome genérico do prototype. O servidor os descartava na resolução dos recursos. Agora eles entregam `flower`, preservando o ID original usado pelo protocolo e o nome/ícone da planta no menu. Lilás já resolvia por prefixo, mas quase não existia nos mapas: havia sete spots em cada uma das duas tundras e nenhum nas ilhas de pradaria ou floresta temperada.

## Restauração dos mapas e saves

Ao carregar cada mundo, `PopulateFlowerResources` completa até oito spots por tipo floral, quando há espaço livre nos biomas permitidos. As posições são determinísticas e distribuídas pelo mapa. A rotina preserva naturais, landmarks, construções, entrada, espaços removidos e recursos aguardando renovação. Não força plantas em deserto, neve, pântano ou vulcão incompatíveis com o catálogo.

A migração é registrada como `flower_ecology_version: 1` no save. Funciona também com um `garden` antigo persistido e preserva coletas anteriores. Os novos spots ficam em `added_natural` e usam o sistema existente de coleta e renovação. Não há varredura adicional durante os frames do jogo nem reposição imediata a cada reinício. O tutorial conserva seus 43 spots originais de roseira; apenas a resolução do item coletado foi corrigida.

O nível dos itens continua sendo o nível efetivo do recurso naquela ilha. Flores de ilhas abaixo do nível 20 não atendem à receita intermediária. Para testá-la, use lilás na ilha temperada `ri35te` (nível 35) ou nas tundras `ri30td01` e `ri55tu`.

## Teste local

1. Use um cliente conectado ao servidor local e entre novamente no mapa após atualizar o processo do servidor.
2. Toque em lilás, roseira ou lavanda e confira a opção de coletar flores.
3. Colete três flores de nível 20 ou superior e use-as no espaço de flores da ferramenta intermediária.
4. Reinicie o servidor e confira que as plantas e o estado da coleta foram preservados.

Validação automatizada em build Release: `--world-check` cobre os 20 terrenos, coleta por TCP/MessagePack, recebimento de `flower`, nível da ilha, filtro real da receita, save antigo e reinício. `--gameplay-check` cobre captura, tutorial, coletas pendentes e renovação de recursos. Os testes usam saves temporários.
