# Clique de expansão gratuita sem resposta no PC e Android

## Causa confirmada

O cliente de PC distribuído em `Lost-Horizon-PC` chama o diálogo legado
`Durango.UI.EstateGridGroup.ExpandEstate` para domínios `OwnerType.Player`,
assim como o Android. O patch de outro cliente PC, que desviava desse diálogo,
não está presente nessa versão distribuída.

Antes de abrir a confirmação, o diálogo calcula:

```csharp
long upkeep = costs.Estate.GetExtendingCost(license.Type, license.Size + 1)
    * (license.Size + 1);
if (upkeep <= 0) return;
```

Essa condição também aparece no Android em `0x139c33c`/`0x139c340`.
O commit `06ad4b3e` zerou `extending_cost`, além do preço de compra
`expanding_cost`, reintroduzindo o retorno silencioso. O endpoint do staging
`/assets/costs` confirmou esse catálogo zerado durante a investigação.
O clique não envia `ExpandEstate`, portanto correções no handler do servidor
não alcançam essa falha.

## Correção

`extending_cost` passa a `1` para `Player` e `ClanEstate`: é o valor mínimo
positivo que permite abrir o diálogo legado. `PersonalPlayer` mantém seu
caminho direto. `expanding_cost` permanece `0` para os três tipos, e o servidor
continua sem cobrar expansão ou debitar esse crédito de compatibilidade.
As licenças já fornecem os dois campos de depósito exigidos pelo cliente,
com crédito suficiente para a confirmação mesmo com carteira zerada.
O limite pessoal continua sendo oito lotes.

O diálogo legado pode mostrar manutenção de compatibilidade; esse valor
não é cobrado pelo servidor. Não é necessário distribuir outro APK ou cliente
PC. Depois da publicação, os jogadores precisam fechar e abrir o jogo para
recarregar `/assets/costs`.

## Regressão

`--polish-check` agora verifica a condição anterior ao envio do pedido,
reproduzida a partir do cliente distribuído, além de testar o protocolo TCP.
A versão anterior do catálogo falha com um domínio de um lote; com o catálogo
corrigido, a confirmação é verificada de um a sete lotes para jogador e clã,
com saldo zero e tempo decorrido desde a emissão da licença.
Também são verificados os próximos cliques com as licenças retornadas,
a expansão até oito, a rejeição do nono e a preservação de domínios legados.

Validação em uma cópia isolada do commit publicado, com apenas esta correção:

- Build Release concluído.
- `--polish-check`: 1270 verificações passaram.
- `--social-check`: 98 verificações passaram, incluindo a licença real do
  enclave recebida por TCP e a preservação de carteira e fundo do clã.
- O mesmo teste do clique falhou com o catálogo anterior, confirmando a
  reprodução da regressão antes da correção.

Não foi realizado teste visual em um celular ou na janela do jogo de PC.
