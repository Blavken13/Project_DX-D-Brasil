# Arid Basin

AridBasin_Terrain.obj preserva a malha autorada de 257 × 257 vértices, com extensão de 51.200 × 51.200 unidades e platôs afastados do centro. AridBasin_Heightmap16.png, RockMask e prévias documentam essa geometria.

## Integração Unity 6

Runtime/Clients contém o prefab real compilado para Windows e Android. O build usa a malha OBJ, texturas originais do deserto e um shader próprio. Os platôs periféricos são renderizados como marco global; o centro fica livre e no piso nativo para o encontro. O projeto de autoria compartilhado fica em New Dinos/Giganotosaurus_2213/UnityProject, com o editor Assets/Editor/BuildAridBasin.cs.

O client não usa o endpoint whole_elevations para renderizar relevo. A geometria agora vem do bundle, enquanto bioma, costa e entidades continuam sendo enviados pelo servidor. O comando de reconstrução é python tools/build-arid-basin-content.py build, seguido de compilação do servidor em Debug e Release.

O terrain arid_basin_60_v2 usa o template cliente ua60deMain01 e o nome Arid Basin. O único Giganotossauro aparece no centro, tile 128,128. A entrada da ilha fica em 128,27. Navegação sobre montanhas e colisão 3D escalável ainda precisam de implementação própria; a arena central usa a movimentação existente.

Runtime/live-validation.json comprova os downloads pelo servidor ativo, e Runtime/save-validation.json registra a auditoria dos saves. As prévias PNG originais são da malha autorada; não são capturas da sessão do jogo.
