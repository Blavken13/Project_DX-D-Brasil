# Recursos recuperados do servidor público

Em 01/10/2026, o personagem Blavken aparecia como uma silhueta preta: sua roupa
`Models/PC/Male/Body/m_body_vine.prefab` não estava entre os bundles recuperados
do celular. O carregador mantinha o modelo inicial quando o pedido desse arquivo
falhava. As cores do personagem estavam válidas.

O gateway público original anunciou `/assetbundles/android/` em `/knock`.
Seu índice tem exatamente o SHA-256 do índice Android já usado no projeto.
O bundle foi recuperado com o CRC e os metadados originais, conferido como
UnityFS 2017.4.34f1 e disponibilizado no staging. O usuário confirmou que pele
e roupa voltaram ao normal após reconectar, sem alterações nos saves.

`inventory.json` registra a URL pública, os metadados de catálogo, o tamanho e
o SHA-256 do arquivo. Os payloads de `bundles-android/` ficam ignorados pelo Git.
`prepare_android_assets.py` inclui automaticamente os recursos desse diretório,
apenas se o índice, os metadados e os hashes corresponderem ao inventário.

Para recuperar novamente os arquivos verificados depois de clonar o projeto:

```powershell
python android-client/recover_upstream_android_assets.py
python android-client/prepare_android_assets.py
```

O recuperador baixa somente os arquivos registrados no inventário e rejeita qualquer
conteúdo cujo tamanho, SHA-256 ou versão Unity seja diferente.

Em 02/10/2026 foram recuperadas mais 112 dependências originais do tutorial:
materiais de rochas, vegetação, animais, NPCs e props. A análise percorreu 13
bundles de entrada e suas 198 dependências, decodificou 155 texturas e resolveu
32.934 referências CAB/PathID, incluindo 41 referências aos recursos internos
do APK original. Os arquivos mantêm o catálogo original, Android e Unity
2017.4.34f1. O inventário registra essas verificações e as URLs para reprodução.

Os 112 arquivos foram publicados no staging por cópia somente de recursos
ausentes, sem reiniciar o servidor ou tocar no volume de contas. O usuário
confirmou a volta das rochas, NPCs, braquiossauros, espinheiros e jangada.
A aparência específica de K junto à jangada é tratada no Alfa 4 do APK.

O conjunto preparado passa a 1.287 bundles, com 866 ainda ausentes. Nenhum
índice foi reduzido e nenhum bundle Windows foi usado. Execute
`python android-client/tests/verify_ancora_resources.py` para conferir novamente
os payloads recuperados, catálogo, hashes, texturas e dependências do tutorial.

## Aparência feminina: 02/10/2026

O celular mantinha a personagem feminina como uma silhueta preta. Os registros
do staging confirmaram 404 para `f_body_hoody.fbx` e `f_hair_longwave_01.prefab`.
Foram recuperados mais 321 bundles originais: roupas, cabelos, acessórios e
materiais, incluindo 333 texturas nos novos arquivos. Não houve alterações
nos modelos enviados pelo servidor, nos personagens ou nos shaders do APK.

`recover_female_android_assets.py` confere o índice público contra o original,
percorre as 366 aparências femininas e suas 376 dependências, e registra os
payloads no inventário. As revisões compartilhadas de K e Pia são preservadas.
`tests/verify_female_appearance.py` confere 379 texturas, 67.937 referências
CAB/PathID, o preload original e uma referência ao motor do APK. O teste do
tutorial continua passando com suas 198 dependências e seis texturas integradas.

O conjunto disponível passou a 1.608 bundles, com 545 ainda ausentes no restante
do catálogo. Todos os recursos da aparência feminina estão presentes.
`package_female_asset_patch.py` gera um pacote de apenas 321 arquivos para
publicação aditiva. `deploy/staging/apply_android_asset_patch.py` rejeita índices,
hashes ou arquivos preexistentes divergentes, instala apenas recursos ausentes
e verifica todos os payloads pela rota HTTP. Não modifica o catálogo, o processo
do servidor ou o volume de contas. O APK Lost Horizon continua o mesmo.
No staging, os 321 arquivos responderam com HTTP 200 e SHA-256 esperado.
Após reconectar no celular, o usuário confirmou a aparência feminina normal.

Para validar novamente:

```powershell
python android-client/recover_upstream_android_assets.py
python android-client/prepare_android_assets.py
python android-client/tests/verify_female_appearance.py
```
