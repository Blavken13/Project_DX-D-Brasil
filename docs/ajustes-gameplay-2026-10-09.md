# Ajustes de gameplay — 09/10/2026

Na Safehouse, a população padrão de 50 Sebrossaurus (2027) foi substituída por 50 raptores medrosos (2051). Os 50 compsognatos, nível da ilha e comportamento defensivo foram preservados. `World.SafehouseRaptorCount` configura a quantidade; configurações antigas com `SafehouseZebraceratopsCount` continuam fornecendo a quantidade como fallback.

Os pontos pequenos e grandes de basalto e granito passam a oferecer o mineral correspondente, sem acrescentar o seixo genérico da família de pedras.

Movimento real interrompe fabricação manual em sequência, coleta, colheita, construção, captura, indução de cratera, teleporte e viagens entre ilhas. A troca de animação sem deslocamento conserva a ação. O cancelamento da fabricação devolve os materiais completos e encerra sua sequência de respostas com `Abort`, impedindo o próximo craft automático. A construção preserva os materiais depositados, e a colheita preserva a planta. Efeitos de beber água e banho são concedidos ao concluir o tempo da ação. Animações de reparo e observação também recebem interrupção.

Construções, colheitas, viagens e retornos agora concluem pelo loop do jogador, evitando uma conclusão concorrente com o movimento em outra thread. A reserva inicial de um local é removida se a ocupação for interrompida.

O guia do mapa mobile foi corrigido no APK 50225. Detalhes e SHA256 em `android-client/README-50225.md`. A alteração mobile exige instalar o APK; alterações do servidor exigem atualizar binário/configuração e reiniciar o servidor. A fauna é montada ao carregar a ilha.

Validações concluídas em saves temporários:

- Build .NET: zero erros; dois avisos preexistentes.
- `--reported-gameplay-check`: 107 verificações, incluindo os minerais, devolução de materiais, ausência de produto após cancelar, retomada, construção, colheita, teleporte e efeitos de ações.
- `--safehouse-missions-check`: 49 verificações, incluindo população e ambas as missões de caça.
- `--gameplay-check`: 58 verificações de captura e coleta.
- `--estate-return-check`: 73 verificações de retorno e persistência entre ilhas.
- `--polish-check`: 1249 verificações, incluindo reparo, viagens e recursos das ilhas.
- `--gameplay-bugs-check`: aprovado, incluindo fabricação e construção adiada.
- Guia mobile: 26 casos em emulação ARM64 nas bases original e com a Âncora; APK final assinado, alinhado e comparado com a base 50224.

As validações de servidor usam `dotnet Durango-CustomServer/server/bin/Debug/net9.0/DurangoServer.dll <opção> --data Durango-CustomServer/server/data`.

Pendente: validação visual da mão do guia em celular e aplicação das alterações no servidor em execução. Não houve deploy nem reinício nesta tarefa.
