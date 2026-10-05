# Névoa, viagens, loja e montaria — 2026-10-04

## Alterações

- A névoa agora usa a exploração de cada personagem em cada ilha. Ao entrar e caminhar, o servidor revela os chunks próximos à posição (vizinhança de 3 × 3), preservando o histórico no save. A prévia de uma ilha ainda não visitada permanece coberta. Viagens e teletransportes revelam a vizinhança do destino. O antigo envio do mapa inteiro não gravava exploração; saves sem histórico começam com a área próxima ao personagem revelada.
- As rotas incluem a ilha atual, evitando modais vazios em áreas com uma única ilha. O nível mínimo para viajar acompanha a regra de disponibilidade do cliente, mantendo o nível próprio do terreno e as restrições de acesso às regiões privadas.
- Foram acrescentados 12 terrenos para áreas anunciadas no mapa mundi que não tinham terreno instalado, incluindo postos avançados. Eles reutilizam a geografia de terrenos locais do mesmo bioma, com o template da área de destino. Não são os mapas originais recuperados. Origem, template, nível e SHA-256 estão em `data/terrains/alpha-variants.json`; o gerador está em `scripts/prepare-travel-islands.py`.
- Apenas `posted_commodities.item_skill_reset_ticket_store` passou de 900 para 0. O catálogo aceita preço zero especificamente para esse produto. Outros itens, pacotes e ofertas especiais mantêm seus preços. A compra e o recebimento foram testados com saldo zero.
- O uso do bilhete agora redefine as habilidades aprendidas, devolve todos os pontos gastos e zera o contador de retiradas. As habilidades gratuitas são reaplicadas conforme o progresso das categorias. O nível, a experiência e as pesquisas das categorias são preservados. Não exige nível mínimo, saldo, pontos previamente gastos ou tempo de espera. Consome exatamente um bilhete da mochila; repetir o pedido para um bilhete consumido é rejeitado. Atualiza habilidades, estatísticas, receitas e plantas no cliente.
- Montar agora envia tanto o estado do pet quanto `PlayerDisplay.BoardingOn` e `VehicleEntityId`, necessários para o cliente executar a montaria. Desmontar e recolher o pet limpam o vínculo. A reconexão limpa montarias de pets recolhidos. As validações de vida e de espécie montável permanecem.

## Validação

Build Release concluído. Verificações executadas com o runtime Linux no WSL:

- `--world-check`: 2.787 verificações; inclui cobertura das áreas anunciadas, viagem permitida/bloqueada por nível, névoa persistente por personagem/ilha, compra gratuita, uso do reset com pontos gastos e por personagem de nível 1, persistência do reset e mensagens de montar/desmontar/recolher.
- `--economy-check`: 304 verificações.
- `--progression-check`: 47 verificações.
- `--gameplay-bugs-check`: passou.
- `--polish-check`: 174 verificações.
- `--gameplay-check`: 58 verificações.

Não houve validação visual em um cliente PC ou Android nesta etapa.

## Teste local

Reiniciar o servidor com os arquivos de dados atualizados e reabrir o jogo para carregar o catálogo. Não é necessário gerar outro APK para estas alterações de servidor.

1. Entrar em uma ilha, abrir o mapa e caminhar até novas áreas. Reconectar e verificar que a exploração foi preservada.
2. Selecionar áreas no mapa mundi com personagem de nível elegível; conferir a lista de ilhas e viajar. Com nível insuficiente, conferir o bloqueio.
3. Comprar e receber o bilhete de redefinir habilidade com saldo zero; conferir os preços dos demais produtos. Usá-lo e verificar a devolução dos pontos e os desbloqueios, inclusive após reconectar.
4. Invocar um dino montável, montar, desmontar e recolhê-lo enquanto montado. Reconectar e conferir que o personagem não mantém uma montaria inexistente.
