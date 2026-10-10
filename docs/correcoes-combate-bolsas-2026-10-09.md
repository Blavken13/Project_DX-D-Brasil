# Combate, mochilão e bolsos — 09/10/2026

As alterações estão no código local. A compilação de depuração foi atualizada; o servidor em execução não foi reiniciado nem houve publicação.

## Bolsas

O criador de itens atribuía o nível do item a todos os atributos, incluindo `pocket`. No mochilão `bag_big`, o valor original é 20 e cada ponto fornece seis espaços: nível 1 agora concede 120 espaços extras, em vez de seis. O armazenamento próprio de outras bolsas segue sua definição no catálogo. A normalização de saves corrige atributos com evidência desse erro, preservando propriedades alteradas independentemente. A prévia de fabricação usa o mesmo cálculo.

As melhorias `reform_pocket` e `reform_pocket_t2` passam a respeitar o máximo 60 anunciado pela receita, em vez dos limites internos de intensidade 10/20. O resultado considera o nível do equipamento e a média dos materiais adicionados, limitando-se ao menor desses níveis. Equipamento e materiais 60 produzem bolso 60, com 360 espaços extras. Prévia e resultado usam o mesmo caminho; efeitos de reforma não são somados duas vezes.

Bolsos já reformados mantêm o valor salvo: a qualidade dos materiais antigos não está registrada e não permite promover essas melhorias automaticamente com segurança. Essa limitação não afeta a recuperação do armazenamento básico do mochilão.

## Dano e impactos

O ataque usa o desempenho salvo da arma ativa, fórmulas no nível do item quando faltam campos e atributos adicionais, sem somar novamente modificações já aplicadas. Slots que não representam a arma ativa não fornecem ataque nem ações.

A antiga subtração direta de defesa fazia o dano cair em 1 quando a defesa superava o ataque. A curva de balanceamento do servidor agora reduz continuamente a potência: `potência² / (potência + defesa efetiva)`, seguida do bônus individual do golpe e dos modificadores do personagem. A defesa efetiva considera penetração e a mistura de corte, impacto e perfuração. Exemplo: potência 100 contra defesa 300 causa 25; bônus 2 causa 50. Esta é uma regra de balanceamento deste servidor, não uma fórmula recuperada do servidor original. Golpes muito fracos ainda podem resultar em 1; bônus zero não vira ataque básico.

Cada entrada de `attack_info` gera um impacto independente em `damage_time`, com fallback em `attack_time`/duração e suporte a `playback_rate`. Esses tempos representam o contato de cada golpe, que pode preceder o fim da recuperação da animação inteira. Vida e mensagem `Damaged` são atualizadas juntas no impacto, sem aceitar `StartAt` do cliente como autorização para dano imediato.

O impacto usa as posições atuais interpoladas, o tamanho do animal e o círculo ou retângulo do golpe. Investidas distinguem alcance de ativação e área de impacto; afastar-se ou sair lateralmente da trajetória impede dano. Combos deixam de acertar alvos mortos/capturados. Morte do jogador, saída de combate, desconexão ou troca/perda/quebra da arma cancelam impactos pendentes. Repetições durante a animação não empilham ataques básicos.

## Validação

- `dotnet build Durango-CustomServer/server/DurangoServer.csproj --no-restore`: passou, com dois avisos anteriores (`SYSLIB0014` e `CS0169`).
- `--combat-bags-check --data Durango-CustomServer/server/data`: 2.399 verificações; 243 armas e 1.917 golpes, além de timing de todas as ações ofensivas, protocolo TCP, formas de impacto, movimento, cancelamento, capacidade, qualidade dos materiais e persistência.
- `--progression-check --data Durango-CustomServer/server/data`: 148 verificações de habilidades, fabricação e progressão.
- `--gameplay-check --data Durango-CustomServer/server/data`: 58 verificações de combate animal, captura e exploração.
- `git diff --check`: passou.

Os testes usam saves temporários. Ainda é necessária validação visual no cliente para avaliar a sincronização percebida sob latência real e o novo balanceamento durante partidas.
