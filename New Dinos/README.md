# Recursos visuais de dinossauros — APK comunitário v2.2 Lite

## O que foi preservado

Foram extraídos **226 Unity AssetBundles** (77.998.892 bytes, cerca de 74,4 MiB), organizados por família do jogo. O conjunto cobre 16 famílias de animais usadas como dinos no Durango, além das dependências comuns de sela e capacete. Os pacotes contêm prefabs e referências a malhas, rig/animações e materiais/texturas. O manifesto original do APK e inventários estão em `Metadata/`.

Os bundles mantêm os nomes/hash do APK; isso conserva as referências cruzadas e permite localizar a origem. Use `Metadata/inventory.csv` para ver nome lógico, hash, tamanho e dependências. `Metadata/Info.5.2.1.json` é o catálogo original do APK.

## Comparação e identificação

Comparei o catálogo do APK com `Durango-CustomServer/server/assetbundles/android/Info.5.2.1.json`, referência do projeto. O APK não declara espécies novas nos caminhos: os arquivos continuam usando os nomes antigos do jogo. `spinosaurus`, `spino`, `giganotosaurus` e `giganoto` não aparecem como nomes de bundles. Portanto, o manifesto por si só **não confirma outras espécies novas**: para confirmar a identidade visual do conteúdo substituído é preciso abrir/renderizar os prefabs e inspecionar as malhas.

Estes itens têm grandes diferenças de tamanho em relação ao catálogo local e são bons candidatos para inspeção visual. Diferença de tamanho indica mudança do pacote, mas não prova que a malha seja uma espécie nova; a referência local também pode vir de outra revisão do cliente.

- `ankylosaurus/materials/euoplocephalus.mat.bundle`: 35093 → 251018 bytes (+615.3%).
- `stegosaurus/materials/stegosaurus-texture1.mat.bundle`: 46060 → 275914 bytes (+499%).
- `phenacodus/phenacodus_paper_guardprefab.prefab.bundle`: 25499 → 127013 bytes (+398.1%).
- `stegosaurus/materials/kentrosaurus.mat.bundle`: 34799 → 153608 bytes (+341.4%).
- `stegosaurus/materials/tuojiangosaurus.mat.bundle`: 36158 → 158363 bytes (+338%).
- `brachio_junior/materials/brachio_junior.mat.bundle`: 121529 → 488930 bytes (+302.3%).
- `phenacodus/materials/phenacodus_savana.mat.bundle`: 58296 → 207637 bytes (+256.2%).
- `tricera/materials/styracosaurus.mat.bundle`: 60231 → 186544 bytes (+209.7%).
- `ornithomimus/gallimimus_smallunstableprefab.prefab.bundle`: 65653 → 201603 bytes (+207.1%).
- `dimetrodon/lizard_poisonousprefab.prefab.bundle`: 85116 → 259683 bytes (+205.1%).
- `ornithomimus/gallimimus_smallprefab.prefab.bundle`: 65567 → 198977 bytes (+203.5%).
- `phenacodus/materials/phenacodus-texture1.mat.bundle`: 49637 → 150546 bytes (+203.3%).
- `tricera/materials/chasmosaurus.mat.bundle`: 64002 → 191522 bytes (+199.2%).
- `ornithomimus/ornithomimus_smallprefab.prefab.bundle`: 58070 → 165642 bytes (+185.2%).

Como prioridade para conferir possíveis trocas de malha, os maiores aumentos entre prefabs de dinossauros foram: T-Rex (`trexprefab`, +165%), lagarto venenoso (`lizard_poisonousprefab`, +205%), variantes de Gallimimus (até +207%), Ankylosaurus (variantes mutante/elite, +92%/+108%) e Styracosaurus (`styracosaurus_deerprefab`, +147%). Isso ainda é apenas uma triagem por tamanho; não identifica a espécie representada.

A comparação completa está em `Metadata/comparison-to-project-reference.csv`. Nenhum arquivo solto `.d3` ou `.anim` existe na entrada: os recursos vêm empacotados em Unity AssetBundles. Esta pasta preserva os pacotes originais com dependências, sem converter modelos/animações para arquivos independentes.

## Conteúdo

- `AssetBundles/<família>/`: prefabs, variantes, frameworks de rig/animação e bundles de materiais/texturas.
- `AssetBundles/Common Dependencies/`: recursos compartilhados exigidos pelas referências dos prefabs.
- `Metadata/inventory.csv`: índice dos arquivos extraídos.
- `Metadata/inventory.json`: índice estruturado e dependências.
- `Metadata/comparison-to-project-reference.csv`: tamanhos e hashes comparados com o catálogo do projeto.
- `Metadata/Info.5.2.1.json`: manifesto comunitário original.

## Famílias incluídas

Allosaurus, Andrewsarchus, Ankylosaurus, Apatosaurus, Brachio, Brachio Junior, Compsognathus, Dimetrodon, Ornithomimus, Pachycephalosaurus, Parasaurolophus, Phenacodus, Raptor, Stegosaurus, T-Rex e Triceratops.


## Giganotossauro convertido — ID 2213

O pacote de integração fica em [Giganotosaurus_2213](Giganotosaurus_2213/README.md), com prefab próprio, framework, animações individuais e bundles separados para os clientes atuais Unity 6 de Windows e Android. A queda de torpor foi adotada como animação de morte.

Os dois bundles do Giganotossauro já foram adicionados aos catálogos Windows e Android com prioridade de preload 750. O gateway local serve os bundles para as duas plataformas; o cliente os baixa ao iniciar o carregamento de assets, antes de precisar renderizar o animal.

## Mapas desérticos

A referência original `ri35de` e o terreno procedural sem rios `Arid Basin` estão em [Desert Maps](Desert%20Maps/README.md). O mapa foi registrado no servidor como ilha desértica de nível 60; sua topografia foi empacotada em `Durango-CustomServer/server/data/terrains/arid_basin_60.zip`. O servidor cria um único Giganotossauro nível 60 no centro, com escala duas vezes maior que a escala base do T-Rex.

Veja [integração e teste local](Arid%20Basin%20%E2%80%94%20Server%20Integration.md) para os arquivos registrados e o fluxo de download dos clientes.
