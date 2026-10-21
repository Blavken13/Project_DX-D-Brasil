# Pin da ilha atual no mapa de viagem

Correção aplicada em 2026-10-20 ao cliente local `Lost-Horizon-PC`.

## Causa confirmada na DLL instalada

`Durango.UI.WorldRoutesViewer.Set(bool)` limpa `_isValidCurrentCursor`,
preenche as quatro áreas do mapa, calcula o layout e oculta `_currentCursor`
se nenhuma área tiver chamado `SetCurrentCursor`.

As chamadas existentes cobrem a ilha de papel `Civilized` (6), em
`WorldRoutesExpertStableArea.Set`, e a ilha pessoal em
`WorldRoutesBeginnerStableArea.SetPersonal`. As áreas restantes, incluindo
`WorldRoutesUnstableArea.Set` e `ExploreAreaNode.Set`, não associam o cursor à
região atual. Assim, ter a ilha na resposta `Routes` não basta para exibir o pin.

## Correção

O patch em `Durango-CustomServer/tools/WorldMapPin` adiciona
`RefreshCurrentRegionCursor` ao cliente e o chama após preencher as áreas,
antes de `UpdateLayout` e da decisão de ocultar o cursor.

- Preserva o cursor já definido pelo fluxo nativo.
- Consulta `GameManager.Region`, usando papel, bioma principal e nível.
- Usa os localizadores nativos `GetIslandTransform` e
  `GetUnstableRoutesTransform` para encontrar o nó correspondente no mapa.
- Chama `SetCurrentCursor`, que vincula o pin ao nó, zera sua posição local,
  ativa o objeto e marca o cursor como válido.
- Trata região ausente e objetos Unity destruídos sem inventar uma posição.

O mapa mundial agrupa ilhas instáveis por zona; o pin acompanha o nó da zona
que representa a ilha atual. O patch não altera o mapa interno da ilha.

## Aplicação e validação

Executar na raiz do projeto:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File pc-client/fix_world_map_pin.ps1
```

O script compila o patcher, preserva backup por hash, executa os testes antes
e depois de serializar a DLL, verifica idempotência e substitui atomicamente
a DLL instalada. Também está integrado a `pc-client/build_community_pc.py`.

Resultado obtido:

- Build sem erros e sem avisos.
- 11 testes executando o IL do helper com substitutos das dependências Unity:
  correspondência de papel/bioma/nível, cursor nativo existente, região ausente,
  nó ausente, objeto destruído e reabertura após viagem.
- 37.023 métodos não relacionados preservados.
- Segunda aplicação produz DLL com SHA-256 idêntico.
- SHA-256 anterior: `9db5f79b646b75c4f732328b3f29efbbd65c3cce3242f01bafbb38e4c796b479`.
- SHA-256 instalado: `b68239965da3c66cf41e7e3c4fa6c7b94970b4c1209a71f09eda52b9cb914d7b`.

Relatório e backup estão em `pc-client/work/world-map-pin/`.
Não foi realizada validação visual dentro do jogo. Para conferir, reiniciar
o cliente e abrir o mapa de viagem em uma ilha instável, viajar para uma zona
de outro nível/bioma e reabrir o mapa; conferir também o retorno à ilha domada.
Esta aplicação é local ao cliente PC; não publica uma atualização remota nem
modifica o APK Android.
