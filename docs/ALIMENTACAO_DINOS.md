# Alimentação de dinossauros

A alimentação usa o `type` de `pet/pets_for_client.json`: herbívoros recebem ração herbívora e alimentos vegetais; carnívoros recebem ração carnívora, carne, peixe, ovos e vermes. Só aparecem itens com o bloco nativo `pet_food`. A preferência `preferred_food_tag`, como `soft`, não funciona mais como restrição exclusiva do cardápio.

Durante a domesticação, o alimento também precisa ter um benefício nativo de redução de tempo ou aumento da chance de sucesso. Folhas e frutas sem esse benefício alimentam animais já domados, mas não aparecem no menu de domesticação. Flores, carne e rações compatíveis continuam usando os valores originais de `performance.json`, conforme o nível do item.

Ao carregar os saves, o servidor repara os filtros dos animais e dos currais, além de reconstruir o bloco `pet_food` de alimentos antigos. Outros atributos de alimentos preparados são preservados. Os três fluxos de alimentação validam a mesma dieta; IDs repetidos não multiplicam os benefícios durante a doma.

Verificação: `dotnet Durango-CustomServer/server/bin/Debug/net9.0/DurangoServer.dll --pet-feeding-check --data Durango-CustomServer/server/data`. O teste verifica os cardápios de todas as espécies domáveis, alimentos antigos, recarga de currais e os três fluxos pelo protocolo TCP.
