# Pesca, refúgio, portais e mapa mundial

## Correções

- Pesca: conservar spots de rio e lago; filtrar apenas spots marinhos fora do oceano. Geradores de pesca exigem a tag `harpoon`, com validação da ferramenta e do nível no servidor. O bloqueio por `fish_01` foi removido da pesca quando nenhuma habilidade da árvore concede essa recompensa. As demais exigências de habilidades continuam sendo verificadas.
- Recursos: respeitar `collectible_levels` dos templates, inclusive os recursos de nível 1 do refúgio.
- Refúgio: adicionar até 180 spots de 24 tipos nativos, sem substituir recursos, filas de renovação ou construções. A versão da população fica no save para impedir duplicação ao reiniciar.
- Fauna: 10 Compsognathus nativos, de nível 3, fora da área central do acampamento. Eles reagem a ataques, mas não iniciam combate. A reposição usa o sistema existente de fauna.
- Portais: oferecer a ação de viagem, registrar descoberta e incluir portais de acampamento no mapa de destinos. A chegada procura terra livre perto do portal; viagens pendentes são processadas pelo loop do servidor e canceladas ao desconectar.
- POIs: converter os portais e crateras de `whole.garden` em construções interativas, com IDs persistentes, e completar os mapas públicos/refúgio com pelo menos dois portais e uma cratera quando faltarem. Tutorial e ilhas pessoais conservam seus POIs nativos.
- Mapa mundial: enviar títulos em português, usar o bioma principal reconhecido pelo cliente, informar datas atuais das ilhas persistentes e disponibilizar o preview completo dos terrenos selecionados.

## Crateras e pedras de indução

As crateras mantêm os valores de `constants.json`:

- Moeda: `voucher_resource_induced_stone`.
- Custo: `max(1, int(level * 0.2))`, usando o nível da ilha.
- Tempo de interação: 4 segundos.
- Duração da ativação: 600 segundos.
- Limite da carteira: 240 pedras, conforme `purchaser/vouchers.json`.

As pedras só são descontadas após concluir uma ativação válida. Afastamento, morte, desconexão, saldo insuficiente ou impossibilidade de gerar recursos cancelam a operação. A cratera fica reservada durante a interação para evitar duas cobranças simultâneas. Saldo, ativação e recursos são persistidos. Ao fechar, a cratera limpa seus recursos temporários, inclusive filas de renovação e registros de remoção, permitindo outra ativação.

O pacote não inclui as tabelas originais de composição dos grupos de recursos das crateras. Para o alfa, cada ativação gera até 12 spots entre os tipos coletáveis nativos do terreno e do template, em espaços disponíveis próximos à cratera. Essa distribuição é uma adaptação do servidor brasileiro, não uma reprodução exata da distribuição da Nexon.

Também não há, nos dados disponíveis de missões e presença, uma tabela de concessão dessas pedras que permita restaurar sua obtenção original. Até restaurar essas recompensas, administradores podem abastecer a carteira dos testadores sem tornar as crateras gratuitas.

No console administrativo do cliente, pela mensagem `Cheat`:

```text
voucher voucher_resource_induced_stone 20
voucher voucher_resource_induced_stone 20 id_do_jogador
```

O primeiro comando concede pedras ao próprio administrador; o segundo, a um personagem conectado. A permissão vem da lista existente `DURANGO_ADMINS`/`--admins`. Jogadores comuns não podem executar o comando.

## Verificação

O comando `--world-check --data <diretório_de_data>` usa saves temporários e TCP/MessagePack reais. Ele verifica os 20 terrenos instalados, seleção e preview de mapas, rotas públicas, 70 POIs do garden, preservação da pesca de água doce, coleta com arpão, distribuição do refúgio, descoberta e viagem por portais, ativação de todas as crateras públicas, pagamento, reservas, persistência, expiração e cancelamentos.

Também foram executadas as verificações existentes de gameplay, economia, plantio, missões, efeitos de status e efeitos de evolução. A validação visual e de navegação dentro do cliente ainda deve ser feita pelos testadores.
