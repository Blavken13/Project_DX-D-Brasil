# Ganho de pedras de portal

## Identificação e fontes

A pedra usada para ativar crateras é o voucher `voucher_resource_induced_stone`, não `Currency.Gem` (gema de teleporte) nem `Currency.WarpMatter`. Os dados originais confirmam:

- `data/assets/constants.json`, `crack.required_voucher_id`: voucher gasto na ativação.
- `data/assets/purchaser/vouchers.json`: limite de 240 e descrição de obtenção por missões ou presença.
- `Durango-OffServer/_client_src/Durango.Logic.Interactions/ArtifactInteractions.cs`, `InvestToCrack`: o próprio diálogo aponta para `ui://Quest` e `ui://Event` como fontes.
- `RewardInfo.Vouchers`, `QuestScoreReward` e `AttendanceReward.Voucher`: formatos nativos de entrega ao cliente.

O gasto em crateras já funcionava, mas as missões pagavam apenas EXP e moedas T, os pontos de missão sempre retornavam zero e o calendário não publicava recompensas. Os arquivos recuperados não contêm as quantidades nem calendários de pagamentos da Nexon. A configuração abaixo é balanceamento do projeto, não uma reprodução comprovada dos valores originais.

## Fluxos restaurados

| Caminho | Valor configurado |
| --- | --- |
| Resgate de missão diária funcional | 2 pedras |
| Resgate de conquista funcional | 2 por etapa, até 12 |
| Bônus diário por 20 / 50 / 100 pontos | 2 / 4 / 6 pedras |
| Presença diária | 5 pedras |
| Bônus ao completar 28 presenças | 20 pedras |

Os valores ficam em `Durango-CustomServer/server/data/assets/quests/induction_rewards_server.json`. Cada diária resgatada soma os dez pontos já anunciados pelo servidor. Os bônus dependem de pontos calculados no servidor e só podem ser recebidos uma vez por dia. O reset segue o calendário KST (UTC+9) já usado pelas missões: 12h no horário de Brasília UTC−3.

O calendário de presença é pessoal: uma recompensa por dia, sem pular ou perder a sequência quando o jogador não entra. Usa `CategoryType.Event1`, pois esse widget original também mostra o botão do bônus final; o widget mensal não mostra esse botão. Após completar o ciclo e receber o bônus, outro ciclo começa no próximo dia de presença. Não existe compra de dias ausentes neste calendário; pedidos de restauração são recusados.

Saldos e marcadores ficam no mesmo save do personagem. Reconectar ou reiniciar não libera de novo uma recompensa. Saves antigos recebem apenas os campos novos: saldos são preservados e missões anteriormente resgatadas não são pagas novamente. Missões incompletas ou ainda sem implementação não geram pedras. Não foram criadas fontes fictícias por matar animais, colher, comprar pacotes ou acionar comandos comuns.

O limite original continua sendo 240. A prévia/recibo da missão mostra a quantidade que cabe na carteira. Para presença e bônus, se não couber a recompensa inteira, o servidor recusa o resgate e conserva a oportunidade até haver espaço. A carteira é atualizada pelo protocolo nativo `WalletUpdated`, e os ganhos aparecem no log como `[pedras-portal]`.

## Teste local

1. Entre com o cliente apontando para o servidor local atualizado.
2. Abra **Missões**, conclua uma diária e resgate: confira as duas pedras e os dez pontos.
3. Ao alcançar vinte pontos, resgate o primeiro bônus na barra de pontos.
4. Abra **Eventos → Presença** e resgate as cinco pedras do dia.
5. Use as pedras em uma cratera. O custo e a duração continuam sendo os valores originais de `constants.crack`.
6. Reconecte e confira o saldo e os marcadores de recompensa recebida.

Validação: `--induction-check` exercita TCP/MessagePack, missões, conquistas, pontos, calendário, bônus, carteira cheia, duplicatas, adulteração de índices, reset diário, reconexão e ciclo seguinte com saves temporários. `--quest-rewards-check` verifica a preservação de EXP/moedas T; `--world-check` cobre o gasto e o funcionamento das crateras.
