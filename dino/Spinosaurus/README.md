# Spinossauro — recursos 3D isolados

Origem: pacote Android instalado localmente, com OBB incorporado e 18 arquivos `.pak` em `PakCache`.

## Conversão visual atual do cliente PC

A conversão atual foi refeita a partir de `Spino_new.psk` e dos 49 arquivos
PSA, preservando a mesma base de unidades e o mesmo esqueleto de 94 ossos.
Os **22 clipes aditivos** da Unreal são reconstruídos sobre suas poses base
antes da exportação; reproduzir esses deltas como poses completas colapsava
o corpo, especialmente na mordida. As trilhas ausentes também são restauradas
conforme os arquivos `.config` do exportador.

`Spino_Albedo_Baked.png` combina a difusa, a máscara de seis regiões e as cores
do material Unreal. O shader `Spinosaurus/TexturedAnimal` acompanha o bundle
e lê essa textura diretamente. Não é necessário executar o antigo finalizador
de material nativo depois deste build.

O tamanho usa os vértices deformados reais do Dimetrodon e do Spino,
considerando `Display.BaseScale` enviado pelo servidor (1 e 4, respectivamente).
O comprimento máximo do Spino é calibrado para **220%** do Dimetrodon,
o dobro da versão visual anterior.
A escala visual fica no nó `VisualScale`; o nó de animação e movimento `Body`
mantém posição, rotação e escala locais neutras para a lógica do cliente.

### Corpo e alcance

Os raios de seleção seguem as proporções do prefab nativo do Dimetrodon.
O servidor usa 24 seções da projeção horizontal do corpo em pose neutra,
incluindo cabeça e cauda, em `data/assets/animal_body_shapes.json` (tipo 2214).
Assim o comprimento não vira um enorme raio lateral. As coordenadas já estão
em unidades de mundo; não devem ser multiplicadas novamente por `BaseScale`.
O ataque admite até 200 unidades (2 m na convenção de 100 unidades/m) além
da borda dessas seções, tanto no início quanto no impacto. A validação dos
golpes de jogadores e pets usa a mesma área, sem a margem do ataque do Spino.
Essa é uma aproximação em pose neutra; não é colisão por triângulo a cada frame.
O gerador registra as medidas em `Converted/ActorXRepair/combat-shape.json`;
após recompilar, execute `pc-client/sync_spinosaurus_combat.py` para sincronizar
a entrada 2214 do servidor e o `bound_radius` lido pelo cliente.

O prefab tem a tag nativa `Enemy`, um Rigidbody cinemático e 24 BoxColliders
de seleção com `isTrigger = true`, todos no mesmo objeto do `AnimalBehavior`.
O filtro nativo busca esse componente no objeto atingido pelo raycast. O
`Interaction_Point` acompanha o osso do tronco. Esses triggers não bloqueiam
o jogador; o afastamento local vem de `bound_radius * Display.BaseScale`.
O raio publicado agora equivale à metade da largura do corpo (cerca de 201
unidades de mundo), substituindo o antigo valor de 1.200. Isso também impede
que o cliente inicie ataques normais a uma distância que o servidor rejeita.

O build verifica **441 poses**, UVs, textura colorida, eixos de movimento,
matrizes de bind e bounds. A amostragem restaura a pose neutra antes de salvar
o prefab. As prévias do próprio bundle são renderizadas com GPU no Unity.

- Conversor atual: `pc-client/reconvert_spinosaurus_actorx.py` (executar no Blender).
- Candidato FBX e prévias: `Converted/ActorXRepair/`.
- Prévia do bundle: `Converted/ActorXRepair/Unity/contact-sheet.png`.
- Escala medida: `Converted/ActorXRepair/scale-reference.json`.
- Verificação vinculada ao MD5: `AssetBundles/Windows/bundle-validation.json`.

Para repetir, exporte `Spino_new.uasset` como PSK com UEViewer em
`Converted/ActorX/`, mantendo as texturas e `.props.txt`. Execute o conversor
atual e copie o candidato `Converted/ActorXRepair/SpinosaurusBody.fbx` para
`UnityProject/Assets/Models/Animals/Spinosaurus/Meshes/SpinosaurusBody.fbx`.
Depois execute:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File pc-client/build_spinosaurus_windows_shadow_unity.ps1
# Execute sync_spinosaurus_combat.py com o Python local antes de iniciar o servidor.
powershell -NoProfile -ExecutionPolicy Bypass -File pc-client/build_spinosaurus_windows_shadow_unity.ps1 -PreviewOnly
powershell -NoProfile -ExecutionPolicy Bypass -File pc-client/install_spinosaurus_2214.ps1
```

O instalador exige a verificação desta versão e atualiza somente o cliente
`Lost-Horizon-PC/Lost Horizon Game_Data/StreamingAssets/AssetBundles` por padrão.
Os FBX e catálogos anteriores são preservados para recuperação.

## Recursos principais

Pasta: `Unreal/ShooterGame/Content/PrimalEarth/Dinos/Spino/`.

| Recurso | Arquivos Unreal |
| --- | --- |
| Corpo | `Spino_new/Spino_new` e `Spino` (cada malha com `.uasset`, `.uexp`, `.ubulk`) |
| Esqueleto | `Spino_Skeleton.uasset`, `.uexp` |
| Texturas do corpo | 9 texturas nas pastas raiz e `Spino_new/`, com seus materiais |
| Sela 3D | `Saddle_new/SK_Spino_Saddle_New_RIG.uasset`, `.uexp`, `.ubulk` |
| Texturas da sela | `Saddle/Textures/T_SpinoSaddle_Colorize_d`, `_m`, `T_SpinoSaddle_Layered`, `T_SpinoSaddle_N` (cada uma com `.uasset`, `.uexp`, `.ubulk`) |
| Material da sela | `Saddle/SpinoSaddle_Colorize_MIC.uasset`, `.uexp` |
| Interface da sela | Duas texturas HUD e `HUD_SpinoSaddle_Colorize_MIC` |
| Animações | 49 sequências e 22 montagens/controles, incluindo modos quadrúpede, bípede e natação |

O blueprint do personagem referencia a malha `Spino_new`; ambas as versões da
malha foram preservadas. A malha da sela referencia o esqueleto `Spino_Skeleton`
e o material `SpinoSaddle_Colorize_MIC`, que referencia as quatro texturas da sela.

O [manifesto para o bundle](bundle-source-manifest.json) identifica cada grupo.
O [inventário completo](copied-files.json) registra origem, tamanho e SHA-256
dos **400 arquivos** (12.530.943 bytes). Todos os hashes foram conferidos após
a extração. Os índices PAK e os manifestos originais estão em `_source/`.

## Histórico da conversão inicial para Unity 6

O procedimento abaixo registra a versão anterior e foi substituído pela
conversão visual descrita acima.

Conversão concluída e importada no Unity **6000.6.0f1**. Para importar no seu
projeto, use [Spinosaurus_Unity6.unitypackage](Spinosaurus_Unity6.unitypackage)
e instancie `Assets/Models/Animals/Spinosaurus/SpinosaurusPrefab.prefab`.
O prefab principal usa `SpinosaurusBody.fbx`, a malha 3D `Spino_new` com rig de
94 ossos e os 49 clipes PSA desse mesmo rig. O Spino selvagem fica sem sela;
o prefab isolado da variante clássica está em
`SpinosaurusClassicPrefab.prefab`. O projeto de conversão fica em `UnityProject/`.

Também foram gerados AssetBundles para Unity 6000.6.0f1 em
`AssetBundles/Windows/` e `AssetBundles/Android/`, com nome
`models$animals$spinosaurus$spinosaurusprefab.prefab.bundle`. Use o bundle
da plataforma correspondente. O bundle Windows foi aberto no Editor e o
prefab foi conferido após o build; o Android teve build concluído.

O corpo `Spino_new` tem **94 ossos**. Os 49 clipes PSA foram convertidos com
translações de centímetros para metros e são reproduzidos no rig correspondente.
O material do bundle recebe uma tonalidade castanha para que a textura difusa,
que é uma máscara em tons de cinza, não apareça sem cor. O modelo Tripo fornecido
em `spino_rigged.zip` foi preservado e convertido para uma cópia de prévia, mas
não é compatível diretamente: o GLB original marca a superfície como linhas,
tem quase 1,95 milhão de triângulos, 28 ossos e zero animações. A conversão
corrige o tipo de primitivo e reduz a malha para 77.714 faces e 38.855 vértices.
O FBX está em `Converted/Tripo/SpinosaurusTripoPreview.fbx`; ele requer retarget
das animações antes de substituir o corpo principal. O ZIP original não foi
alterado. O [relatório de conversão](Unity_conversion_report.json) contém os
nomes dos clipes e os resultados dos builds.

Os FBX estão em `UnityProject/Assets/Models/Animals/Spinosaurus/Meshes/`,
as 15 texturas PNG em `Textures/`, e os clipes `.anim` em `Animations/`.
Os glTF/PSA intermediários e o arquivo Blender ficam em `Converted/`.

**Limitação visual:** a cor castanha do material é uma tonalidade fixa porque o
cliente não aplica automaticamente as cores de espécie. As 22 montagens
e controles Unreal permanecem nas fontes; os 49 clipes de movimento do corpo
principal usam o rig correspondente e o framework do cliente.

Scripts para repetir o processo: `tools/extract_spinosaurus_bluestacks.py`,
`tools/finalize_spinosaurus_assets.py`, `tools/export_spino_ueviewer.py`,
`tools/convert_spino_blender.py`. Para reparar o GLB do Tripo e gerar a prévia,
execute `blender.exe -b --python tools/convert_spino_tripo.py`. A construção e
instalação dos bundles Windows estão descritas nos scripts
`pc-client/build_spinosaurus_windows_shadow_unity.ps1`,
`pc-client/finalize_spinosaurus_native_material.py` e
`pc-client/install_spinosaurus_2214.ps1`. O projeto Unity 6 fica em
`UnityProject/Assets/Editor/BuildSpinosaurus.cs`.

## Histórico da primeira integração do cliente PC (2214)

O prefab original de conversão não continha `AnimalBehavior`. O cliente
instanciava a malha, mas falhava em `AnimalManager.MakeAnimalObject` antes de
exibir o Spino. O projeto Unity agora adiciona o componente e o recurso de
framework. O Windows precisa dos dois bundles
`spinosaurusprefab.prefab.bundle` e `spinosaurus_framework.asset.bundle`,
da mesma plataforma, com a dependência registrada no catálogo.

Para reconstruir o bundle Windows nesta máquina, use
`powershell.exe -NoProfile -ExecutionPolicy Bypass -File pc-client/build_spinosaurus_windows_shadow_unity.ps1`. Ele executa uma cópia
local do Unity, pois a instalação original possui uma DLL Windows truncada.
Depois execute `pc-client/finalize_spinosaurus_native_material.py` para aplicar o shader nativo
e a tonalidade castanha ao corpo animado. O script usa UnityPy 1.25.4 em
`pc-client/work/spino-python-deps`; nesta máquina, ele foi instalado com o
Python 3.13 do Blender 5.2. Para instalar os bundles no cliente PC local, execute
`powershell.exe -NoProfile -ExecutionPolicy Bypass -File pc-client/install_spinosaurus_2214.ps1`.
O instalador atualiza `Info.5.2.1.json` e `OffServerEmbed.json`; nenhum bundle
é enviado ao servidor.
