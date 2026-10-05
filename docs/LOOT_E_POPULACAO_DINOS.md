# Loot e população dos dinossauros

Implementação de 05/10/2026. O [catálogo por espécie e variante](CATALOGO_LOOT_DINOS.md) lista os materiais efetivos, os nós de habilidade exigidos e a população de cada ilha instalada.

## Análise dos dados

O servidor reconstruía os materiais a partir de `item/recipes.json`, que só cita recursos usados pelas receitas do cliente. Muitas carcaças não aparecem nessa lista. A implementação anterior completava todas com o mesmo conjunto de carne, couro comum, osso de perna e gordura, deixando outras partes ausentes ou genéricas.

As fontes nativas confirmam, por exemplo, couro blindado de anquilossauros/euoplocéfalos, penas de celófises, ossos grandes e crânio de protoceratops, chifre de bonusaurus/chasmosaurus, couro com pelo de skunkodus e tendão de megaloceros. Esses vínculos foram preservados. As partes ausentes foram reconstruídas por família, tamanho e anatomia usando exclusivamente itens existentes em `prototype_data.json`. As tabelas completas de drops, atributos extras e probabilidades do servidor original não estão no dataset do cliente; essa complementação é uma regra do servidor customizado.

As famílias agora podem fornecer carne, couro adequado, ossos de perna, costelas, crânio, gordura, órgãos e tendões. Animais com penas recebem penas; os blindados recebem couro blindado; estegossauros e outros animais com placas recebem placas ósseas; animais com chifres recebem chifres; mamutes/elefantes recebem marfim. Cortes especiais existentes foram incluídos conforme família e tamanho. Animais de evento que representam papel, melancia, recrutas ou o boneco de treino não recebem esse conjunto anatômico.

## Progressão do esquartejamento

O nível dos itens e de suas tags é limitado por `min(nível do animal, nível de esquartejamento)`, respeitando também os limites do item. O nível informado pelo cliente não concede itens mais fortes. Quantidade, esforço, duração base e ferramentas continuam calculados a partir da carcaça, independentemente do jogador que a consulta.

Cada material exige a recompensa exata do nó aprendido. Não basta ter desbloqueado alguma habilidade da categoria. Isso corrige casos como gordura (liberada por `meat_02`, não por uma recompensa de gordura sem nó ensinável), tendões, couro blindado, crânios, dentes e chifres. Também evita liberar costelas ao aprender osso grande: `bone_03` é ensinado antes de `bone_02`.

| Material | Esquartejamento mínimo para aprender o nó |
|---|---|
| Carne e osso de perna comum | 1 |
| Couro comum/com pelo | 10 |
| Osso grande de perna | 15 |
| Penas | 20 |
| Costelas e dentes | 25 |
| Chifres | 30 |
| Gordura e órgãos | 40 |
| Couro blindado e crânio comum | 45 |
| Cortes especiais e marfim | 50 |
| Tendões e placas ósseas | 55 |
| Crânio grande | 60 |

As partes ainda bloqueadas permanecem visíveis no menu. Ao tentar coletá-las, o servidor informa a habilidade necessária sem consumir materiais. Coletar carne não esgota nem remove os outros tipos de loot. O cadáver continua sujeito ao prazo de desaparecimento já existente.

## População das ilhas

`data/config.json → Animals.TargetAnimalsPerRegion` define a população inicial, com padrão de **40 animais** e proteção pelo teto `MaxAnimalsPerRegion`. O multiplicador antigo `SpawnScale` foi substituído pela população desejada.

O spawn usa pontos nativos válidos e completa os que faltam com posições de terra seca, distribuídas pelo mapa, sem duplicar pontos nem ocupar a proximidade dos portos. O algoritmo refina a grade nos pântanos quando falta terra seca. Cada espécie do template recebe ao menos um animal quando há vagas suficientes; as demais vagas seguem as proporções do template. Isso conserva também as espécies raras.

Foram confirmados 40 animais em cada uma das **25 ilhas de caça instaladas**. `ri30td01` tem 40 animais de cinco espécies. O teste também remove temporariamente seus pontos de herd e confirma que a população ainda é completada.

No dataset local, a ilha 30 já contém pontos e espécies cadastrados. O relato de ausência visual no servidor em uso não foi reproduzido por esse teste de dados. A mudança garante a população no servidor reconstruído; a observação pelo cliente depende de aplicar a versão e visitar as áreas de spawn. As regras existentes das ilhas particulares, tutorial e refúgio são preservadas.

## Validação e aplicação

- Compilação .NET 9 sem erros; dois avisos preexistentes.
- `--fauna-check`: **347 verificações** de população, terra seca, espécies nativas, fallback sem herd, materiais válidos e únicos, recompensas ensináveis e coleta pelo TCP.
- `--polish-check`: **203 verificações** de recursos iniciais, coleta e esquartejamento.
- `--world-check`: **2.787 verificações** do mundo e da coleta, incluindo os testes de níveis de habilidade que já estavam no workspace.
- `--gameplay-check`: **58 verificações** de captura, caça e demais regressões de gameplay.

O Controle de Aplicativo do Windows bloqueou a DLL final. A validação final foi executada pelo runtime Linux existente no projeto, no WSL, sem alterar a política do Windows.

As alterações estão no código e na configuração locais. Para aparecerem no servidor em uso, é necessário publicar a compilação com a configuração atualizada e reiniciar as instâncias das ilhas. Este trabalho não publicou nem reiniciou o servidor remoto.
