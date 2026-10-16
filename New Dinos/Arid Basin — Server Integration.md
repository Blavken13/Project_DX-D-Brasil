# Arid Basin — integração local

Arid Basin e ua60de_alpha continuam no arquipélago desértico nível 60. A ilha personalizada usa o terrain arid_basin_60_v2; o template anunciado ao client é ua60deMain01, já conhecido pelo Unity 6. O nome da ilha e o spawn são resolvidos pelo ID próprio do terrain.

## Terreno visível

O client não solicita whole_elevations para construir sua geometria. Alterar somente esse arquivo não modifica o relevo visível. A malha de Desert Maps/Generated_Arid_Basin/AridBasin_Terrain.obj agora é compilada como Models/Maps/AridBasin/AridBasinTerrain.prefab e carregada pelo sistema nativo GlobalLandmarks.

O terrain registra a biblioteca 60000 e preserva esse marco na construção de World. Os platôs vêm da malha autorada, com texturas extraídas dos bundles originais de deserto. A região central usa o piso nativo plano para o encontro; as montanhas periféricas são geometria visual. Navegação sobre montanhas e colisão 3D escalável ainda não fazem parte desta integração.

As camadas de bioma e oceano têm uma costa própria. Garden, marcos e rebanhos copiados do RI35DE foram retirados. A entrada está em 128,27; não há rios.

## Giganotossauro

O spawn usa o ID do terrain, independente do template compartilhado com o client. A ilha cria apenas um EntityType 2213, nível 60, no tile 128,128. AppearAnimal.Display.BaseScale envia duas vezes a escala normal do T-Rex. As outras ilhas seguem seus rebanhos normais.

## Download

O bundle visual foi compilado para Windows e Android, com Priority 750. Os fragmentos e arquivos estão em Desert Maps/Generated_Arid_Basin/Runtime/Clients. Os índices instalados ficam no client PC e em Durango-CustomServer/server/assetbundles/android. O gateway serve catálogos e bundles via /live/windows/ e /live/android/.

O prefab usa MeshFilter/MeshRenderer e materiais, sem scripts novos no client. O carregador nativo baixa e instancia o asset registrado nos metadados. O cadastro e os dois bundles do Giganotossauro também precisam permanecer presentes no catálogo.

## Reconstrução e verificação

Na raiz do projeto, execute python tools/build-arid-basin-content.py build e compile DurangoServer.csproj em Debug e Release. O iniciador local usa bin/Debug/net9.0 com --data e --terrains apontando para os dados fonte.

O comando dotnet Durango-CustomServer/server/bin/Debug/net9.0/DurangoServer.dll --arid-basin-check --data Durango-CustomServer/server/data cria mundos temporários e verifica rota única, template cliente, costa distinta, marco 3D serializado, único animal 2213 central e escala. Os 11 checks passaram.

Runtime/live-validation.json registra a verificação HTTP dos seis bundles necessários e o spawn do servidor ativo. Runtime/save-validation.json compara os saves com o backup. Para conferir HTTP novamente, execute python tools/restart-arid-basin-local.py --verify-only.

Reabra o client e selecione Server Test para recarregar catálogo e definições. A validação visual numa sessão do jogo ainda é necessária; os testes confirmam assets, servidor e downloads.
