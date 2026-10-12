# Coleta repetida durante atualização da bolsa

Foi corrigido um caminho no servidor que aceitava pedidos repetidos de `Collect` enquanto a primeira coleta ainda aguardava o temporizador. O teste TCP atualiza o inventário durante a coleta e reenvia o pedido: antes da correção havia duas reservas do recurso e dois callbacks pendentes; depois há uma reserva, um temporizador e uma entrega.

`Player.Gathering.cs` identifica o recurso, o gerador e a ferramenta da coleta em andamento. Um pedido repetido recebe o tempo restante dessa mesma ação. Pedidos para outra coleta aguardam a conclusão ou o cancelamento da atual. Nível informado pelo cliente não inicia outra ação. Carcaças são identificadas pelo EntityId, inclusive quando o cliente manda o tile sentinela (-1,-1).

A entrega original continua publicando `InventoryUpdated` e `Collected` com os mesmos IDs de itens. Atualizar a bolsa não gera itens adicionais. Depois da conclusão, o jogador pode iniciar a próxima coleta normalmente. Cancelamentos devolvem a reserva pendente e liberam a proteção das carcaças.

O bônus premium permanece em **25% de chance de um item adicional** na coleta de recursos naturais. O sorteio ocorre uma vez na entrega, incluindo quando o pedido foi repetido. Também foram preservados o bônus de habilidade, o nível e as propriedades dos itens, a capacidade da mochila, a coleta de carcaças e suas regras de bônus.

## Validação

`GatheringDuplicateCheck.cs`, acionado por `--gathering-duplicate-check`, cobre bolsa atualizada antes/durante/depois da coleta, repetição com outra sequência e nível enviado, tentativa de outro gerador, quantidade de reservas, entrega única, concordância entre inventário e pacotes, premium com e sem extra, próxima coleta, cancelamento e carcaças.

Reprodução anterior: `docs/coleta-duplicada-antes.log` registra a falha com `reservas=2, pendentes=2`. O mesmo cenário passa após a correção, conforme `docs/coleta-duplicada-depois.log`.

Passaram **679 verificações locais**:

| Verificação | Resultado |
|---|---|
| `--gathering-duplicate-check` | 48 |
| `--premium-check` | 52 |
| `--gameplay-check` | 62 |
| `--reported-gameplay-check` | 113 |
| `--fauna-check` | 404 |

```powershell
dotnet build Durango-CustomServer/server/DurangoServer.csproj --no-restore -v quiet
dotnet Durango-CustomServer/server/bin/Debug/net9.0/DurangoServer.dll --gathering-duplicate-check --data Durango-CustomServer/server/data
dotnet Durango-CustomServer/server/bin/Debug/net9.0/DurangoServer.dll --premium-check --data Durango-CustomServer/server/data
dotnet Durango-CustomServer/server/bin/Debug/net9.0/DurangoServer.dll --gameplay-check --data Durango-CustomServer/server/data
dotnet Durango-CustomServer/server/bin/Debug/net9.0/DurangoServer.dll --reported-gameplay-check --data Durango-CustomServer/server/data
dotnet Durango-CustomServer/server/bin/Debug/net9.0/DurangoServer.dll --fauna-check --data Durango-CustomServer/server/data
```

Compilação sem erros, com dois avisos anteriores à alteração. As alterações estão implementadas no projeto local. Nesta etapa não houve deploy no servidor online nem reprodução visual no celular: a validação usa conexões TCP e os handlers reais do servidor, com atualizações de inventário e reenvio da coleta.

## Compatibilidade verificada antes do deploy

A validação remota de `ceb5980` encontrou uma regressão em `--polish-check`: a fila `_pendingCollects` também contém temporizadores de reparo e outras ações. A proteção inicial tratava qualquer temporizador como uma coleta ativa e bloqueava a coleta seguinte ao reparo. O deploy foi interrompido antes de substituir o servidor ativo.

A proteção agora procura somente temporizadores marcados como coleta ou colheita. Reparos e outras ações mantêm seu comportamento anterior. A coleta em andamento ainda é encontrada mesmo quando existe outro tipo de ação antes dela na fila; pedidos repetidos continuam conservando uma única reserva.

O teste de duplicação foi ampliado para esses casos. Passaram `--gathering-duplicate-check` com **57 verificações** e `--polish-check` com **1.249 verificações**, totalizando 1.306 verificações locais adicionais antes de preparar a nova release. Os logs e a tabela anteriores documentam a validação original de `ceb5980`.
