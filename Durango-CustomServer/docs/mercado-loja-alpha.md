# Mercado e loja no Alpha brasileiro

## Mercado dos jogadores

O mercado usa anúncios reais compartilhados entre as ilhas do mesmo cluster. O vendedor escolhe os itens e o preço em T-Stones; os itens saem do inventário e ficam sob custódia até a compra, cancelamento ou expiração. O comprador recebe os mesmos IDs, modificações, cores e dados adicionais dos itens. A receita fica disponível para o vendedor receber, inclusive se estava offline na hora da venda.

Estão implementados busca por nome, categoria, subcategoria, tags, prototype, preço e nível; ordenação e paginação; favoritos persistentes; histórico de anúncios, compras e vendas; cancelamento, retirada dos expirados e recebimento individual ou de todas as vendas. Itens equipados, vinculados/protegidos e com menos de 90% de durabilidade são recusados. O anúncio pode durar até 24 horas; o cliente original solicita essa duração ao anunciar.

Os itens antigos de inventários, bolsas de pets e armazéns recebem uma migração única de negociabilidade. O dump de prototypes não contém toda a informação original de vínculo: a regra disponível usa `trade_locked`, `dump_locked`, a cápsula explicitamente vinculada e o tag de avatar. Esse ponto deve ser conferido durante o Alpha, sobretudo com recompensas e cosméticos especiais.

As taxas vêm de `server/data/assets/constants.json`, em `market`. O arquivo fornecido atualmente define todas como zero. O cálculo reproduz o cliente: truncamento da taxa de anúncio e taxa de venda por faixas, abaixo/acima do limiar. Isso preserva os valores desta distribuição; não comprova as taxas do servidor oficial de dezembro de 2019.

## Loja do servidor

O catálogo vem de `server/data/assets/purchaser/commodities.json`, em `posted_commodities`, sem alterar os preços originais. Há 154 definições suportadas, das quais 121 estão disponíveis em 29/09/2026, respeitando os períodos promocionais registrados nos assets. Pacotes antigos cuja promoção terminou permanecem indisponíveis.

São suportados itens, pacotes, caixas aleatórias, bundles, cápsulas de construções e conversão de Coins em Warp Gems. Exemplo original: o produto `item_fatigue_drug_01_store` custa 150 Warp Gems; `gem_900` custa 300 Coins e entrega 900 Warp Gems. A loja usa os saldos persistentes de T-Stones, Warp Gems, Durango Coins, pontos da loja e Warp Matter; não processa dinheiro real.

O tag original `AcceptAutomatically` entrega o conteúdo junto com a compra. Outros produtos ficam na área de recebimento da loja. Se não houver espaço para uma entrega automática, a compra é recusada antes do débito. Se uma compra pendente não couber, o recibo permanece disponível. Resultados aleatórios são sorteados uma vez e persistidos. Limites por personagem e por período são verificados no servidor; neste Alpha, limites com `reset_type: weekly` renovam segunda-feira, 00:00 UTC. Os demais limites periódicos usam uma janela móvel de dias.

Produtos com pagamento externo, cupons da Nexon, DLCs, assinaturas, recompensas condicionais ou conteúdos ainda não implementados não são oferecidos. Isso inclui produtos que misturam itens com benefícios que o servidor não conseguiria entregar. Transferência de Durango Coins entre jogadores continua indisponível. A compra e a entrega foram verificadas; os efeitos de uso de cada consumível/pet/construção dependem dos respectivos sistemas e ainda precisam do teste visual no cliente.

## Persistência e operação

Cada cluster ganha `economy.json` na mesma pasta dos saves `.player` e `.world`, usando a chave de storage, não o nome exibido. O ledger é gravado atomicamente antes de alterar itens ou saldos. Cada personagem salva um checkpoint; operações posteriores são reaplicadas ao iniciar/reconectar, sem novo débito ou nova entrega. Anúncios, receitas pendentes e recibos sobrevivem ao restart.

Faça backup da pasta completa do cluster com o servidor parado. Não apague `economy.json` nem restaure apenas esse arquivo separadamente dos personagens. Se o ledger estiver inválido, faltar apesar de haver backup, ou for anterior ao checkpoint de um personagem, o servidor interrompe a inicialização para preservar os saves. O histórico de efeitos ainda não possui compactação: tamanho do arquivo e tempo de gravação devem ser acompanhados antes de ampliar o Alpha.

Para aplicar a atualização local, saia do cliente, encerre o servidor com **Ctrl+C** e execute `iniciar-servidor-local.bat` novamente. O script recompila o servidor e usa o mesmo storage.

## Verificação

Da raiz do repositório, a suíte abaixo usa saves temporários e portas TCP locais escolhidas automaticamente; ela não altera personagens reais:

```powershell
dotnet build Durango-CustomServer/server/DurangoServer.csproj --no-restore -o Durango-CustomServer/server/bin/economy-check
dotnet Durango-CustomServer/server/bin/economy-check/DurangoServer.dll --economy-check --data Durango-CustomServer/server/data
```

Ela verifica anúncio/compra/retirada/pagamento, concorrência, saldo e capacidade, propriedade dos recibos, replay após crash, entrega automática, limites semanais, conversões, geração dos produtos disponíveis e preservação de dados adicionais após JSON e MessagePack. As verificações TCP passam pelos handlers reais de `Player` e pela conexão do jogo.

As suítes existentes de quests, agricultura, efeitos de estado e efeitos de recompensa também passaram. A verificação geral `--check-data` conserva três falhas anteriores relativas à expectativa de `stem` em plantas que retornam `reed`; não são falhas introduzidas pela economia.

## Referências e limite histórico

- [Relato da comunidade sobre o mercado, em junho de 2019](https://www.reddit.com/r/DurangoWildLands/comments/bvjgim/): compra/venda por T-Stones e taxas de anúncio e venda.
- [Discussão sobre Warp Gems, em maio de 2019](https://www.reddit.com/r/DurangoWildLands/comments/bpf464/): uso em caixas e outros benefícios.
- [Discussão sobre casas e pacotes de decoração, em junho de 2019](https://www.reddit.com/r/DurangoWildLands/comments/c0tzvm/).
- [Guia de T-Stones e comércio](https://www.touchtapplay.com/durango-wild-lands-how-to-make-money-fast-t-stones/).
- [Cronograma do encerramento do jogo](https://gamingonphone.com/news/durango-wildlands-is-shutting-down/): compras com dinheiro real foram encerradas antes do fechamento do serviço em dezembro de 2019.

Essas fontes documentam o funcionamento de 2019, mas não fornecem uma tabela completa e verificável da última versão. Os preços, formatos dos recibos e mensagens da implementação usam os assets e o código do cliente presentes neste projeto como referência técnica.
