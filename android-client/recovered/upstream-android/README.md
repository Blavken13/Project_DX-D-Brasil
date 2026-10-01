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

Com esta recuperação, o conjunto preparado passa a 1.171 bundles, com 982 ainda
ausentes. Nenhum índice foi reduzido e nenhum bundle Windows foi usado.
