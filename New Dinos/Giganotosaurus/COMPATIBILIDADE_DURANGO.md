# Compatibilidade do Giganotosaurus com Durango

## Arquivos recebidos

O pacote veio de ARK Survival Evolved Mobile 2.0.29 / Unreal Engine 4.17. Ele traz malhas ActorX PSK, animações PSA, PNGs e metadados. Esses formatos não são bundles Unity e não podem ser colocados diretamente no cliente ou no diretório de AssetBundles do servidor.

- Malha principal: 4.118 vértices / 5.215 triângulos; LOD1: 3.127 / 3.650; LOD2: 1.095 / 1.042.
- A malha tem 110 bones ActorX; 90 são animáveis e 20 são sockets auxiliares. As 31 sequências passaram na validação do extrator; 12 montages têm metadados à parte.
- As sequências foram exportadas a 30 FPS na maior parte; as Aim são poses de 1 frame. O PSA usa o esqueleto do Giganotosaurus, não o rig de um dino Durango.
- Há uma seção/material. Texturas: diffuse 512×512, normal 512×512 e máscara RGB 256×256. O material depende do shader ARK Mobile_ColorMask, que precisa ser recriado no Unity.
- Dimensões aproximadas após UE cm → Unity m: 7,96 × 3,01 × 1,42 m, antes de corrigir eixos, pivô e escala.
- Não há clipe de morte explícito. Há Idle, Move, Eat, Attack-Bite, Charge, Hurt, Swim e estados Torpid. Groggy/morte exigem escolha de substitutos ou criação de clipes.

## Como o servidor atual representa animais

- data/assets/entity_types/animal.json registra 214 tipos; T-Rex é o tipo 2005. Giganotosaurus não existe no catálogo.
- data/assets/derived/animal_motions.json contém os nomes enviados em Movement.MotionName. Os nomes do T-Rex são TRex_* e não coincidem com os clips do Giga.
- O servidor ignora spawns cujo EntityType não existe. Os mapas definem grupos em region_templates.json, com valor codificado EntityType × 100 + sufixo.
- No modo Online, o Gateway entrega dados em /assets/* e AssetBundles Android pelo Info.5.2.1.json. O verificador exige bundles UnityFS de Unity 2017.4.34f1 ou 2017.4.7f1 e dependências declaradas.

## Trabalho necessário

1. Importar PSK/PSA no Blender com leitor ActorX, preservar o esqueleto, conferir eixos, root/pivô, pesos e LODs; exportar FBX.
2. Importar no Unity compatível com os bundles do cliente. Configurar diffuse, normal e shader da máscara de cor. Construir framework/prefab que o carregador de animais do Durango reconheça.
3. Gerar bundles Unity de prefab, framework/animações e materiais/texturas. Incluir hashes, tamanhos e dependências no Info.5.2.1.json e copiar os bundles para o diretório Android servido pelo Gateway.
4. Mapear os clips para stand, move, eat, attack, battle, groggy e dead. Os clips Giga não têm nomes TRex; será preciso renomear ao exportar ou atualizar os movimentos no servidor.
5. Como espécie nova: incluir um ID livre em animal.json, atributos, escala, faixa de nível, loot/comida, retrato e caminhos do prefab/framework; adicionar movimentos para o mesmo ID. O próximo ID livre na cópia atual é 2213. Só adicionar a region_templates.json após decidir os mapas e a frequência de spawn.
6. Como substituição visual do T-Rex: manter o tipo 2005 e os dados de combate/loot; instalar o visual no slot do AssetBundle de T-Rex ou alterar o model path. Compatibilizar também os nomes de animação.

## Registro inicial no servidor

Registrei Giganotosaurus como EntityType 2213 em data/assets/entity_types/animal.json e adicionei os estados em data/assets/derived/animal_motions.json. Ataque, vida, defesa, nível, IA, loot e escala inicial foram copiados do T-Rex 2005 como valores provisórios. O portrait também usa target_trex até existir um ícone próprio. A morte usa Torpid-Idle porque o pacote não fornece clip de morte.

O registro ainda não foi incluído em region_templates.json, então não surgirá naturalmente no mapa. Isso fica deliberadamente desativado até haver um prefab/framework Unity servido no catálogo; os arquivos atuais são PSK/PSA e PNG, não bundles Unity. Após converter e publicar os bundles, será possível testar o ID 2213 e escolher mapa, bioma e frequência.