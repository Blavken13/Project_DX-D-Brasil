# Aplicar os materiais desérticos no Unity 6

O OBJ importa como uma malha única com UVs repetidos 32 vezes e dois grupos de material: `Desert_Sand` e `Desert_Rock`. No Unity 6, abra os bundles Windows isolados em `../Reference_RI35DE/AssetBundles/Windows` e extraia os materiais `cliff_desert_grass` e `cliff_desert_btm` para o projeto de autoria; atribua-os aos grupos areia e rocha, respectivamente. Os bundles de material com sombras e o bundle de cliffs comuns também estão preservados junto da referência. `AridBasin_RockMask.png` pode orientar a mistura de duas camadas num shader.

A malha não recebe diretamente o shader proprietário do jogo por um OBJ/MTL. O MTL incluído mantém cores e nomes de slots para importação imediata; use os bundles fonte para obter os materiais e texturas originais. A prévia oblíqua foi renderizada com exagero vertical de 3× para destacar os platôs; a escala real está descrita no `manifest.json`.
