# Pin de localização no mapa do mundo

O cliente possui dois cursores independentes:

- `Durango.UI.Archipelago._currentCursor`: mapa interno de um arquipélago.
- `Durango.UI.WorldRoutesViewer._currentCursor`: mapa do mundo da jangada.

A correção anterior em `Archipelago.UpdateLayout()` cobre o arquipélago com uma ilha, mas não modifica o mapa do mundo. No cliente inspecionado, `WorldRoutesViewer.Set()` zera `_isValidCurrentCursor`. Apenas `WorldRoutesExpertStableArea.Set()` (região atual com Role 6) e `WorldRoutesBeginnerStableArea.SetPersonal()` chamam `SetCurrentCursor`. `WorldRoutesUnstableArea.Set()` desenha os destinos sem marcar a região atual. Ao final, o viewer oculta o pin porque o indicador continua falso. A tag NEW é independente desse fluxo.

Este patch acrescenta `WorldRoutesViewer.RefreshCurrentRegionCursor()` e o chama depois que as quatro áreas foram preenchidas, antes do layout e da rolagem automática. Se o fluxo nativo ainda não marcou a localização, busca o ponto correspondente ao `GameManager.Region` por Role, MajorBiome e Level usando os métodos de busca já existentes. Em seguida chama o `SetCurrentCursor` nativo, que prende o pin ao ponto e o ativa. No mapa do mundo, os destinos instáveis representam grupos por tipo/bioma/nível; a ilha individual continua sendo mostrada no mapa interno do arquipélago.

## Gerar e instalar

Na raiz de `Durango-CustomServer`, com o jogo fechado para a instalação:

```powershell
dotnet build tools/WorldMapPin/WorldMapPin.csproj
dotnet tools/WorldMapPin/bin/Debug/net9.0/WorldMapPin.dll '../Lost-Horizon-PC/Lost Horizon Game_Data/Managed/Assembly-CSharp.dll' tools/WorldMapPin/artifacts/Assembly-CSharp.dll
powershell -NoProfile -File tools/WorldMapPin/Install.ps1 -ClientAssembly '../Lost-Horizon-PC/Lost Horizon Game_Data/Managed/Assembly-CSharp.dll'
```

O instalador compara hashes, recusa substituir um cliente alterado desde a geração, e preserva a assembly anterior em `Assembly-CSharp.dll.world-map-pin-<data-hora>.bak`. A substituição é atômica. A correção deve ser reaplicada se uma distribuição futura substituir a assembly; este utilitário não altera o pipeline de empacotamento do cliente.

## Verificação

O utilitário verifica a posição da chamada no fluxo do mapa, preserva os corpos de todos os métodos não relacionados e a identidade da assembly, relê o arquivo produzido e executa o IL do novo método com dependências substituídas. São 11 verificações: ilhas iniciais/avançadas, outpost, domada/pessoal, marcador nativo existente, região/ponto ausente e mudança de ilha. Não substituem a confirmação visual no cliente Unity.

Validação visual: abrir o mapa na Ilha Domada, viajar para Savana Mar, abrir o mapa do mundo, viajar para Tropical Mar e abrir novamente. O pin deve acompanhar o destino atual em cada abertura, inclusive ao reabrir o mapa e após reiniciar o cliente.
