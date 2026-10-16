# Mapas do deserto

Reference_RI35DE preserva o terrain original ri35de nível 35 e bundles com cliffs, materiais e vegetação seca. A disponibilidade Android e os hashes estão em manifest.json.

Generated_Arid_Basin contém a malha OBJ autorada, heightmap de 16 bits, máscara, materiais e prévias. Os quatro platôs ficam nas bordas para abrir a área central do Giganotossauro.

A integração visível é feita pelo prefab Unity 6 Models/Maps/AridBasin/AridBasinTerrain.prefab, carregado como marco global da ilha. O client não consome whole_elevations para construir relevo. Os bundles prontos estão em Generated_Arid_Basin/Runtime/Clients, e o terrain está em Durango-CustomServer/server/data/terrains/arid_basin_60_v2.zip.

As camadas de costa e bioma são próprias, sem rios, com entrada em 128,27. A arena central permanece no plano de movimento nativo. As montanhas são malha visual periférica; navegação sobre elas ainda exige implementação específica.

O servidor cria exatamente um Giganotossauro 2213, nível 60, em 128,128, com escala 2× T-Rex. O catálogo baixa os bundles da malha e do animal pelo gateway local. Veja New Dinos/Arid Basin — Server Integration.md e Runtime/live-validation.json.
