# Status premium — análise e proposta

**Proposta histórica, superada pela implementação descrita em [status-premium.md](status-premium.md).** A ativação por item foi cancelada pelo usuário; a liberação agora é exclusiva pelo admin.

Análise do servidor no commit `f77ea50196a4aa3858925b8d1d5af52df157d506`.
Este documento descreve uma implementação proposta; o recurso ainda não foi implementado.

## Comportamento proposto

- Premium por personagem, inicialmente. Escopo por conta depende de confirmação.
- Prazo contado em tempo real, inclusive offline, usando UTC do servidor.
- XP de personagem aumentado; percentual configurável. XP de categorias de habilidades é uma configuração separada, inicialmente sem bônus.
- Bônus de quantidade na coleta: chance configurável de um item extra, com atributos e qualidade normais. Inicialmente apenas coleta natural; pesca, agricultura e esfolamento precisam de decisão explícita.
- Fadiga igual a zero durante toda a vigência. Energia, stamina, fome, dano e vida mantêm suas regras atuais.
- Ativação ou renovação pelo admin e pelo consumo de um passe premium.
- Renovações somam tempo, sem multiplicar os benefícios.
- Expiração automática, inclusive durante uma sessão de jogo.

Valores de referência para discussão: XP +50%, chance de coleta extra 25%, passes de 1, 7 e 30 dias. Esses números não são requisitos aprovados.

## Evidências no código atual

| Área | Implementação atual | Integração proposta |
| --- | --- | --- |
| Persistência | `Core/PlayerContext.cs`, `Save()` | Salvar vencimento e identificação das operações premium no estado persistente |
| XP | `Core/Player.Skills.cs`, `AddExp()`, `AddExpForAction()`, `PreviewActionExp()` | Aplicar a mesma fórmula ao ganho real, à prévia e ao indicador; respeitar teto de nível |
| Coleta | `Core/Player.Gathering.cs`, `ScheduleCollectFinish()`, `CompleteCollectAfterDelay()` | Gerar bônus somente ao concluir uma coleta válida |
| Bônus existente | `Core/Player.SkillEffects.cs`, `RollGatherBonus()` | Manter bônus de habilidade e sortear o premium separadamente |
| Fadiga | `Core/SurvivalState.cs`, `Set()`, `Add()`, `ValueAt()`, construção das curvas | Garantir valor e curva de fadiga zerados enquanto premium está ativo |
| Fadiga ambiental | `Core/Player.Fatigue.cs`, `SyncFatigueVelocities()` | Sincronizar velocidades zeradas e recalcular na ativação/expiração |
| Uso de item | `Core/Player.Inventory.cs`, `HandleUseItemMsg()` | Tratar passes antes do caminho de comida |
| Lista administrativa | `Core/Gateway.Admin.cs`, `/admin/players` | Expor atividade, vencimento e tempo restante para online e offline |
| Interface admin | `admin/app.js`, `renderPlayers()` | Coluna Premium, filtro e ações de concessão/renovação/revogação |
| Expiração online | `Core/Player.cs`, `Process()` | Sincronizar transições antes de processar ações e curvas |

O multiplicador global de XP já existe e exclui `reason = cheat`. O premium deve compor com ele e também excluir comandos de ajuste de XP. O XP de habilidade é concedido separadamente em `AddExpForAction()`.

Na coleta atual, os itens são preparados no início, mas entregues depois de um timer. Para o premium, consultar a vigência no momento da conclusão e atualizar os itens de `Collected` e `InventoryUpdated` juntos. Uma coleta cancelada não concede bônus. Verificar espaço considerando o bônus: o método atual `AddItems()` apenas adiciona os itens à lista, sem fazer a validação de capacidade.

Os efeitos temporários de `Player.StatusEffects.cs` vivem em um dicionário na sessão. Não podem ser a única fonte do prazo premium, pois esse estado não fornece persistência própria para o novo direito.

## Prazo e renovação

Salvar `premium_expires_at` como timestamp UTC em segundos. Ausência ou zero significa sem premium. Ativo quando `agora < vencimento`.

```text
novo_vencimento = max(agora_utc, vencimento_atual) + duração
```

Exemplo: restam 3 dias e o jogador usa um passe de 7 dias; passa a ter 10 dias. Se já venceu, ganha 7 dias a partir do consumo. O prazo continua correndo durante logout e reinício do servidor.

Usar um serviço comum, por exemplo `PremiumService`, para validação, renovação, revogação e consulta. A mesma regra atende admin e item. Configuração do benefício fica no servidor, por exemplo `premium.json`; o cliente nunca escolhe duração ou multiplicador.

Campos adicionais propostos: data da última concessão, origem e operação. Histórico deve registrar jogador, ação, duração, vencimento anterior/novo e administrador ou item responsável. No escopo por personagem, manter o estado no mesmo snapshot do inventário reduz a complexidade de consumir item e conceder o direito juntos. Se o escopo for por conta, desenhar uma transação entre o inventário do personagem e o direito da conta.

## Fadiga zerada

Zerar o valor na ativação e impedir aumentos de todas as origens: coleta, combate, crafting, construção, ambiente e efeitos. Garantir que as curvas enviadas ao cliente também permaneçam zeradas; zerar apenas uma vez ou apenas a fadiga da coleta é insuficiente.

Recomendação: uma política central de fadiga em `SurvivalState`, ligada à vigência premium, além da sincronização ambiental. Não desativar os outros efeitos de clima ou sobrevivência.

Ao expirar ou revogar, restaurar as velocidades normais imediatamente, mantendo o valor inicial em zero. Não cobrar fadiga retroativa. Recalcular caches de fadiga e velocidade de movimento e enviar as atualizações ao cliente somente quando houver mudança.

## Ativação pelo admin

Na lista Jogadores, mostrar coluna Premium com `Ativo`, `Expirado` ou `Sem premium`, vencimento e tempo restante. Acrescentar filtros `Todos`, `Premium ativos` e `Expirados`, com contagem dos resultados. A lista deve incluir personagens offline, como já faz a rota atual.

Ações: conceder dias, renovar somando dias e revogar. Prévia deve mostrar o vencimento resultante. Revogação deve ser uma ação separada, sem representar duração negativa.

Rotas propostas: `POST /admin/premium/grant`, `POST /admin/premium/revoke`, e opcionalmente uma rota de histórico. Reutilizar sessão administrativa, proteção de origem, validação de personagem e padrões do correio. Toda concessão recebe `request_id`; repetir o mesmo pedido retorna o resultado original, e reutilizar o ID com dados diferentes retorna conflito.

As rotas atuais são processadas pelo loop do servidor. Manter as mutações nesse fluxo para evitar duas concessões lendo o mesmo vencimento e sobrescrevendo uma à outra.

## Ativação por item

Criar passes com protótipos específicos e duração definida em catálogo do servidor, por exemplo `premium_pass_1d`, `premium_pass_7d` e `premium_pass_30d`. O admin pode enviá-los pelo correio existente após integração no catálogo.

Antes do consumo: validar posse, bloqueios, tipo permitido, duração e limites. Remover uma unidade, conceder o prazo e persistir os dois efeitos como uma operação única. Só confirmar sucesso depois de persistência comprovada; falha deve preservar item e prazo anterior. Pedidos repetidos não podem renovar novamente usando o mesmo item.

Ponto relevante: `PlayerContext.Save()` usa `SafeSave.QueueAtomic()`, uma fila assíncrona. Chamar `Save()` não prova que a gravação terminou. A implementação precisa de confirmação da gravação coordenada com essa fila ou de um registro durável recuperável. Uma escrita síncrona isolada pode ser sobrescrita por snapshots já enfileirados; não basta adicionar `WriteAtomic()` ao handler.

O cliente original decide quais ações aparecem pelos dados/tags do item. Em `Durango-OffServer/_client_src/Durango.Logic.Item/Useable.cs`, a tag `usable` habilita `UseType.Use`. Portanto, os passes precisam de definição reconhecida pelo cliente, tag adequada, nome, descrição e ícone. Validar o caminho do botão em PC e Android; apenas aceitar `UseItem` no servidor não garante um item utilizável na interface.

Existe `premium_support_package` nos dados originais de loja e status. Seus efeitos são redução de tempo e aumento de sucesso, diferentes do premium solicitado. A validação atual de `ShopCatalog` não oferece suporte geral a compra de direitos por status. Evitar ativar esse pacote automaticamente; qualquer reaproveitamento de ícone exige conferir texto e comportamento do cliente.

## Validação antes de publicação

- Concessão online/offline, renovação ativa e renovação vencida.
- Expiração com o jogador conectado e ao reconectar; manutenção do vencimento após restart.
- XP real, prévia, indicador, teto de nível e composição com multiplicador global.
- Coleta normal, bônus de habilidade junto do premium, cancelamento, geração de recursos e mochila cheia.
- Fadiga de todas as origens e curvas enviadas ao cliente; retorno normal após expiração.
- Consumo de passe, item bloqueado/ausente, repetição do pedido e falha de gravação.
- Concessões repetidas do admin, requisições sem sessão e histórico.
- Filtro premium e prazo correto na lista, com jogadores online/offline.
- Uso real do passe no cliente PC e Android, incluindo nome, ícone e feedback.

Implementar e validar localmente antes de um novo deploy. Decisões pendentes: personagem ou conta; percentual de XP; significado e percentual do bônus de coleta; categorias beneficiadas; duração dos passes; se os passes podem ser negociados.
