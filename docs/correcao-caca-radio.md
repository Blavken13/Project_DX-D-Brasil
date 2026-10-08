# Missões de caça no rádio do abrigo

O servidor anunciava a interação da tenda, mas recusava `RecommendMissions` e
`AcceptMission` e devolvia sempre uma lista vazia em `GetMissions`. O tutorial
espera os IDs `sh_sq_01` e `sh_sq_02`; apenas abrir a interface não o destravava.

As duas missões de caça agora podem ser recebidas no rádio do abrigo. A primeira
exige um Compsognathus (2015), a segunda um Raptor covarde (2051). Somente o
abate após aceitar, pelo personagem e na ilha indicada, conclui a etapa. A
conclusão envia `Rewarded/MissionCompletedEffect` com o ID original para liberar
os diálogos seguintes do cliente. O rádio valida entidade, coordenadas,
proximidade e jogador vivo.

Os IDs foram conferidos em `safehouse_play_guide_event` e
`safehouse_play_guide_flow`, extraídos de `resources.assets` com UnityPy. Os
IDs dos objetivos e a opção de pular após 240 segundos estão em
`constants.skip_tutorial_missions`. A tabela original do servidor de missões
não está disponível: a meta de um abate é uma adaptação brasileira, e o prêmio
de moedas T/EXP usa `QuestRewardTuning` já existente. Não se atribuem pontos de
missão diária, vouchers ou amizade por essas etapas.

O novo campo opcional `safehouse_missions` no save conserva missão oferecida,
aceitação, região e etapas concluídas. Saves antigos continuam válidos.
Cancelamento permite receber a mesma etapa novamente; missões concluídas não
repetem o prêmio. Se a espécie exigida estiver ausente, o servidor repõe um
alvo compartilhado em um ponto de fauna existente do abrigo. As missões de
cozinha/coleta/confecção posteriores e missões comuns de outras ilhas não
foram implementadas por esta correção.

Para aplicar localmente, encerre a instância anterior com Ctrl+C, aguarde o
salvamento e execute `iniciar-servidor-local.bat`. O BAT recompila o servidor.
Entre no mesmo personagem e receba a missão na tenda.

Validação TCP com saves temporários:

```powershell
dotnet run --project Durango-CustomServer/server/DurangoServer.csproj -- --safehouse-missions-check --data Durango-CustomServer/server/data
```

Passaram 47 verificações específicas, 1.703 de `--quest-reactivation-check` e
2.795 de `--world-check`, além da compilação do servidor.

O teste verifica interação, oferta/aceitação, espécies corretas, abates reais,
cancelamento, reconexão, isolamento entre jogadores e ilhas, persistência de
saldo e bloqueio de recompensas repetidas. A continuidade visual dos diálogos
precisa ser conferida no cliente Unity.
