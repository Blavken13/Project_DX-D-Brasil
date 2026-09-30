# Ajustes de combate, captura, descanso e recursos

## Comportamento implementado

- **Aviso do ataque:** o dino envia `notice_attack` com a hora futura do golpe. O cliente original dispara o brilho dois segundos antes dessa hora (`AnimalBehavior.AttackNotice`). A animação começa depois desse aviso; o impacto é calculado 0,35 segundo depois. O golpe mira a posição anunciada: sair dela ou do alcance evita o dano. Morte e saída do alvo cancelam ataques pendentes.
- **Captura:** a tentativa aguarda os três segundos definidos nos dados nativos. O servidor valida novamente alvo, distância, vida do jogador e ferramenta antes de concluir. Quando tem sucesso, remove o animal com `DisappearEntity` e entrega as rédeas com `Domesticated = false`. Não envia `EntityDied`, não cria uma carcaça e rejeita tentativas de coleta desse animal. Inventário cheio não remove o animal; capturas simultâneas não duplicam a entrega. A etapa seguinte continua sendo a domesticação no cercado, pelo sistema existente.
- **Descanso:** `RestOn` usa o abrigo informado pelo cliente e ativa imediatamente o descanso e a recuperação de fadiga. O movimento atrasado que encaixa o personagem no assento mantém o descanso; caminhar encerra-o. A existência e o estado de construção do abrigo são verificados. `AppearArtifact.IsAlive` não indica se uma estrutura pode ser usada: estruturas válidas do projeto têm esse campo falso.
- **Recursos do tutorial:** a primeira coleta de cada spot agenda sua renovação para dali a 120 segundos, mesmo se ainda restarem outros recursos. Coletas seguintes e o esgotamento completo não adiam o prazo. A renovação limpa os contadores compartilhados, remove a representação anterior e recria o recurso original. A fila é salva; spots parcialmente coletados em saves antigos também recebem agendamento. Recursos informados pelo cliente que não constam em `whole.garden` são lembrados para renovação.

A renovação parcial se aplica às ilhas com papel `Tutorial`. Agricultura, carcaças e construções não entram nesse wipe. Fora do tutorial, mantém-se a renovação existente após o esgotamento completo.

As coletas de recursos naturais agora terminam no loop do jogador. Cada operação lembra a geração do spot; se houver renovação durante a coleta, a operação antiga é cancelada sem entregar itens ou remover o recurso recém-criado.

## Referência para captura e domesticação

O fluxo histórico separa capturar o animal vivo, recebê-lo no inventário e domesticá-lo no cercado. Esse encadeamento é descrito no [guia de domesticação de 2019](https://www.touchtapplay.com/durango-wild-lands-guide-how-to-tame-animals/) e corresponde às rédeas e mensagens presentes no cliente deste projeto. Os valores usados pelo servidor continuam vindo dos dados desta versão: vida de captura até 30%, tentativa de três segundos e intervalo de cinco segundos. O guia descreve uma versão antiga e não substitui esses dados nativos. O consumo da ferramenta e as regras de habilidades não foram alterados neste ajuste.

## Verificação automatizada

Na raiz do repositório, com .NET 9:

```powershell
dotnet build Durango-CustomServer/server/DurangoServer.csproj -o Durango-CustomServer/server/bin/gameplay-check --no-restore
dotnet Durango-CustomServer/server/bin/gameplay-check/DurangoServer.dll --gameplay-check --data Durango-CustomServer/server/data
dotnet Durango-CustomServer/server/bin/gameplay-check/DurangoServer.dll --economy-check --data Durango-CustomServer/server/data
```

`--gameplay-check` usa conexões TCP com as mensagens reais e saves temporários. O relógio das operações é avançado nos testes para conferir as janelas de ataque, captura e renovação sem esperar dois minutos. O servidor em execução e os saves dos jogadores não são usados por esses testes.

Validação em 29/09/2026: compilação sem erros; 41 verificações de gameplay, 304 de economia, 42 de agricultura, 126 de missões, 45 de efeitos de status e 67 de efeitos e recompensas passaram (625 no total). A DLL compilada foi testada no Ubuntu do WSL com o runtime .NET 9.0.20. A última execução nativa no Windows foi bloqueada pelo Smart App Control (`0x800711C7`, evento 3077); suas configurações não foram modificadas. A validação visual com o cliente ainda é necessária.

## Validação com o cliente

Após recompilar e reiniciar o servidor de testes:

1. Provocar um ataque e confirmar brilho → animação → dano; repetir saindo da posição marcada.
2. Capturar um dino enfraquecido. Confirmar a espera, o desaparecimento sem cadáver, as rédeas no inventário e a possibilidade de seguir para o cercado.
3. Descansar em fogueira e barraca com fadiga alta: a recuperação deve começar no primeiro clique e parar ao caminhar.
4. Coletar apenas parte de um spot do tutorial. Após 120 segundos desde a primeira coleta, confirmar a reposição para dois jogadores; repetir esgotando-o completamente e mantendo uma coleta em andamento durante o reset.

Estas alterações no repositório não fazem publicação automática no staging.
