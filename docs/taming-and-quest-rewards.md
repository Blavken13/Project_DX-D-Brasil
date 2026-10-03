# Doma e recompensas de missões — Lost Horizon

## Doma

O servidor agora lê `groggy_max`, `groggy_section`, `groggy_velocity`,
`groggy_duration` e os multiplicadores por direção de
`server/data/assets/entity_types/animal.json`. Os golpes usam seu campo
`groggy` em `player_battle_actions.json`, independentemente do bônus de dano
físico. Por exemplo, a investida (`melee_tackle`) tem dano físico 0,2 e
atordoamento 5,0.

Esgotar a resistência derruba o animal vivo com o clip `Groggy` da espécie,
publica o estado e a barra correspondentes e cancela ataques pendentes. A
recuperação usa a duração da espécie. Um animal caído não anda nem ataca.
Ao entrar na área de interesse, outro jogador recebe a mesma postura.

A ferramenta de captura exige animal capturável, vivo, caído, vida de no
máximo 30%, alcance e intervalo entre tentativas. Esses valores de vida,
tempo de ferramenta e intervalo já vêm de `constants.json`. Uma tentativa
iniciada durante a queda mantém o animal imobilizado até terminar; após
falhar ou ser cancelada, ele pode recuperar-se. A captura entrega as rédeas
com animal ainda não domesticado para levar ao cercado. O animal desaparece,
sem morte, carcaça ou recursos de abate.

Relatos da época confirmam o fluxo de enfraquecer/atordoar/capturar e o uso
de armas menos letais para ter mais oportunidades. Eles não documentam
todas as fórmulas do servidor original:

- [Guia de doma publicado em 2019](https://www.reddit.com/r/DurangoWildLands/comments/bs3o3f/)
- [Discussão sobre capturar um styracosaurus](https://www.reddit.com/r/DurangoWildLands/comments/c33z1v/)

## Recompensas

Os assets originais contêm textos e objetivos, mas não a tabela de pagamentos
da Nexon. Relatos de jogadores confirmam moedas T e EXP em tarefas, e
pagamentos maiores conforme dificuldade/progresso. Não foi localizada uma
tabela completa e verificável com os valores de cada diária e conquista:

- [Relatos sobre ganhar T-Stones](https://www.reddit.com/r/DurangoWildLands/comments/bvjgim/)
- [Conquistas e tarefas diárias como fonte de moedas](https://www.reddit.com/r/DurangoWildLands/comments/bwiucp/)

Os números de missões de facção e exploração semanal citados nessas fontes
não são valores oficiais de diárias ou conquistas. A tabela implementada é
um **balanceamento configurável do alfa brasileiro**, em
`Durango-CustomServer/server/data/assets/quests/rewards_server.json`.

| Nível do personagem | Moedas T por diária funcional |
| --- | ---: |
| 1–9 | 100 |
| 10–19 | 200 |
| 20–29 | 400 |
| 30–39 | 600 |
| 40–49 | 800 |
| 50–59 | 1.000 |
| 60 | 1.200 |

Conquistas: etapas 1–12 pagam, respectivamente, 250, 500, 1.000, 1.500,
2.000, 3.000, 4.000, 5.000, 6.000, 7.500, 9.000 e 10.000 moedas T.
Etapas são ordenadas pela meta dentro da mesma família de conquistas;
o nível atual do personagem não altera essa ordem. EXP conserva o cálculo
existente do servidor por nível, com peso 8 nas diárias e peso 8 × etapa
nas conquistas, respeitando o teto de experiência.

Estão ativadas conquistas de nível do personagem e habilidades, caça,
fabricação de armas/roupas/comida, construção, coleta/mineração,
agricultura e captura de zebra. Objetivos específicos ainda sem mecanismo
no servidor (por exemplo, cursos de conselheiro e condições de clã) não
ganham progresso fictício nem são anunciados como funcionais.
Contadores de conquistas começam a acumular a partir desta atualização;
níveis já alcançados são reconhecidos ao entrar. Todos os degraus de uma
família acumulam progresso ao mesmo tempo, sem perder o total ao resgatar
uma etapa anterior.

O valor exibido na missão e o recibo são enviados pelo protocolo nativo;
o limite da carteira é respeitado. Resgates são marcados antes do pagamento
e persistidos junto com carteira e experiência, bloqueando pagamentos
duplicados, inclusive após reconectar. A virada diária mantém o horário
KST já usado no projeto e reinicia apenas as diárias; conquistas são permanentes.

## Verificação local

Compile o servidor e execute os checks com `--data` apontando para a pasta
de dados completa. Eles usam conexões TCP locais e saves temporários:

```powershell
dotnet build Durango-CustomServer/server/DurangoServer.csproj -c Release
dotnet Durango-CustomServer/server/bin/Release/net9.0/DurangoServer.dll --gameplay-check --data Durango-CustomServer/server/data
dotnet Durango-CustomServer/server/bin/Release/net9.0/DurangoServer.dll --quest-check --data Durango-CustomServer/server/data
dotnet Durango-CustomServer/server/bin/Release/net9.0/DurangoServer.dll --quest-rewards-check --data Durango-CustomServer/server/data
```

O teste de jogabilidade inclui queda viva, cancelamento de ataque, recuperação,
captura bloqueada em pé, ausência de carcaça após captura e cancelamentos
por ferramenta/alcance. O teste de recompensas cobre pagamento real pelo
protocolo, EXP, moedas, progressão, duplicação, reconexão, reset e teto da carteira.
Uma conferência visual da animação no PC e Android continua necessária.
