# Cupons do Luiz

Cada unidade é um consumível de uso único com 10.000 XP fixos, sem multiplicadores. O nome mantém a grafia solicitada: `Cupon do Luiz - (habilidade)`.

| Habilidade | Referência no servidor |
|---|---|
| Sobrevivência | `bonus_xp_sobrevivencia` |
| Combate corpo a corpo | `bonus_xp_combate_corpo_a_corpo` |
| Combate à distância | `bonus_xp_combate_a_distancia` |
| Defesa | `bonus_xp_defesa` |
| Abate | `bonus_xp_abate` |
| Coleta | `bonus_xp_coleta` |
| Culinária | `bonus_xp_culinaria` |
| Fabricação de armas | `bonus_xp_fabricacao_de_armas` |
| Fabricação de armaduras | `bonus_xp_fabricacao_de_armaduras` |
| Construção | `bonus_xp_contrucao` |
| Agricultura | `bonus_xp_agricultura` |
| Processamento | `bonus_xp_processamento` |

`bonus_xp_contrucao` conserva a referência pedida; o nome exibido usa **Construção**.

## Uso e progressão

- Concede XP somente à categoria indicada, atualizando nível, barra e receitas durante a sessão.
- Os cupons avançam pelas etapas de pesquisa, inclusive quando há uma pesquisa em andamento. A progressão normal continua usando os limites e tempos originais.
- O nível máximo é 60. O excedente é descartado, sem saldo armazenado. Com a tabela atual, um cupom basta para levar qualquer uma das 11 categorias independentes ao nível 60.
- No nível máximo, o uso é recusado e o cupom permanece na mochila. Itens bloqueados manualmente também não são consumidos.
- Sobrevivência acompanha o nível do personagem no jogo original. Seu cupom concede 10.000 XP ao personagem, sem bônus premium, de clã ou alfa, preservando essa regra.
- Repetir uma solicitação com o ID de uma unidade já consumida não concede XP novamente.
- Consumo e progresso são salvos juntos no personagem.

## Restrições

Os cupons não podem ser vendidos, transferidos para recipientes, armazéns ou animais, nem colocados no chão. A validação é feita no servidor, inclusive para requisições diretas.

Para **apagar**, use o descarte normal, sem escolher um local. Esse comando elimina o item definitivamente e não concede XP. O cliente original permite essa operação para itens não negociáveis. A opção de selecionar um local, caso exibida pelo cliente, será recusada pelo servidor.

`trade_locked` é verdadeiro e `Tradable` é falso. `dump_locked` permanece falso porque, no cliente existente, esse campo também impediria apagar o item. O servidor bloqueia especificamente o descarte que inclui um local.

## Compatibilidade e entrega

Todos os cupons usam o sprite existente `icon_exp`, identificado nos dados dos clientes Android e PC em `(2565, 980)`, tamanho `38 × 32`. O `ItemIconTex` resolve o sprite pelo gerenciador comum de atlas; vários protótipos podem usar a mesma imagem sem criar ou substituir sprites.

Os nomes, descrições, ícone e tag existente `usable` são enviados pelo servidor. Não é necessário alterar os clientes ou os atlas.

Os itens ficam disponíveis para anexos do painel administrativo por `prototype_id`. Exemplo de anexo:

```json
{ "prototype_id": "bonus_xp_contrucao", "quantity": 1, "level": 1 }
```

Esta alteração cadastra os itens e seu uso; não adiciona distribuição automática, loot ou receitas de fabricação. A instalação em um servidor em execução requer atualizar os arquivos do servidor.

## Verificação

```powershell
dotnet build Durango-CustomServer/server/DurangoServer.csproj --no-restore
dotnet Durango-CustomServer/server/bin/Debug/net9.0/DurangoServer.dll --xp-coupon-check --data Durango-CustomServer/server/data
```

O teste usa TCP e MessagePack reais, em personagens e arquivos temporários, e verifica XP, consumo unitário, repetição, nível máximo, pesquisa, persistência, nomes, anexos administrativos e restrições de movimentação. Também confirma que XP normal mantém o limite por ação e o bônus premium.
