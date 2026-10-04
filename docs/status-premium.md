# Premium por personagem

Implementado em 2026-10-04 sobre a base `f77ea50`. Ativação exclusivamente pelo painel administrativo, com duração definida pelo admin. Não foi criado item de ativação nem fluxo de compra.

## Benefícios originais restaurados

Valores extraídos de `data/assets/purchaser/commodities.json` (`posted_commodities`) e `data/assets/survival/status_effects.json` (`carry_capacity`).

| Pacote | Duração padrão | Capacidade adicional | Warp Gems diárias | Consumíveis diários | Gems na ativação |
| --- | --- | --- | --- | --- | --- |
| `day_package_1` / `7days_package` | 7 dias | 20 | 10 | 3 × `fatigue_drug_store`, nível 1 | 0 |
| `day_package_2` / `15days_package` | 15 dias | 40 | 20 | 3 × `fried_hen`, nível 1 | 150 |
| `monthly_package_1` / `30days_package` | 30 dias | 40 | 20 | 3 × `fried_hen`, nível 1 | 300 |

O admin escolhe qualquer duração de 1 a 3650 dias inteiros. O nome indica a duração original do pacote. Não foram habilitadas as vantagens distintas de `premium_support_package` (tempo/sucesso e vouchers de mapa, revive e skills), pois não integram os benefícios escolhidos nesta implementação.

Enquanto houver pelo menos um pacote ativo:

- +50% de XP de personagem, composto com o multiplicador global. Ajustes administrativos de XP não recebem bônus. XP de categorias de habilidades mantém a regra atual.
- 25% de chance de uma unidade extra na coleta natural, além do bônus de habilidade. O sorteio acontece na conclusão válida; inventário e `Collected` recebem os mesmos itens. Esfolamento e farming mantêm suas regras próprias.
- Fadiga zero, incluindo ações, ambiente e curvas de sobrevivência enviadas ao cliente. Energia, stamina e vida seguem suas regras atuais.

Os extras permanecem em +50% / 25% / fadiga zero quando há vários pacotes. Capacidade e entregas de pacotes diferentes somam. Renovar o mesmo pacote aumenta o tempo sem duplicar sua capacidade ou diária. Cada concessão/renovação credita o bônus de ativação correspondente.

## Prazo e entregas

```text
novo vencimento = max(agora UTC, vencimento atual do pacote) + duração concedida
```

O prazo corre também offline e sobrevive a reinícios. Expiração e revogação removem benefícios em execução; cada pacote pode ser revogado individualmente.

A diária segue a exigência de acesso dos pacotes originais: uma entrega por personagem, pacote e dia UTC em que o personagem entrar/estiver no jogo. Não há reposição de dias offline. A verificação ocorre a cada 30 segundos da sessão. A renovação e a reativação no mesmo dia não repetem a diária do mesmo pacote. Pacotes diferentes entregam separadamente.

Gems de ativação entram diretamente na carteira. Gems diárias e consumíveis chegam pelo correio e precisam ser resgatados. O resgate valida saldo e espaço, não entrega parcialmente e não pode ser repetido para duplicar anexos. Mensagens com anexos pendentes não podem ser apagadas; entregas anteriores continuam resgatáveis após o pacote vencer.

Quando a mochila perde capacidade premium, itens excedentes são preservados. Novas entradas sujeitas à capacidade exigem espaço. A fadiga volta a acumular a partir de zero, sem cobrança retroativa.

O XP guarda metade de ponto entre ações em `premium_exp_remainder`, salvo junto do personagem, para manter os 50% também em ganhos pequenos.

## Painel

Nova aba **Premium** com seleção de personagem salvo e vinculado a uma conta (incluindo offline), pacote, duração, benefícios, prévia do vencimento, concessão/renovação, revogação por pacote e histórico das últimas 100 operações.

A listagem tem busca e filtros Ativos, Todos, Expirados e Sem premium; mostra capacidade, gems diárias, pacotes e tempo restante. A aba **Jogadores** também mostra atividade e vencimento na coluna Premium.

Rotas com autenticação administrativa: `GET /admin/premium`, `POST /admin/premium/grant`, `POST /admin/premium/revoke`. Mutações verificam origem. `/admin/players` inclui a situação premium e a capacidade atual.

Concessões usam `request_id`: repetir os mesmos parâmetros retorna o resultado anterior; reutilizar o ID com conteúdo diferente é recusado. O formulário preserva o ID pendente após falhas de rede para permitir retry sem conceder outra vez.

## Persistência

`PremiumStore` grava `premium.json` na pasta persistente do cluster, ao lado de `mail.json` e `economy.json`. É a autoridade de direitos, prazos e histórico; a gravação atômica precede a mudança em memória. Os créditos imediatos têm journal, e o checkpoint `premium_sequence` no personagem permite recuperação sem duplicar gems se o processo encerrar antes do save assíncrono.

O correio agora suporta Warp Gems em `Money`, campo já existente no protocolo nativo. O journal e `mail_sequence` protegem os créditos após falha/reinício. Dados anteriores continuam aceitos, com padrões para os novos campos.

Os buffs nativos `7days_package`, `15days_package` e `30days_package` são enviados ao cliente. A capacidade dinâmica é usada nas mensagens de inventário e nas verificações de correio, mercado/loja, retirada de armazém, animais, jaulas, cápsulas e coleta. Direitos ficam no volume persistente preservado pelo deploy existente.

Não foram modificadas as definições originais de pacotes nem os assetbundles dos clientes. Não há ativação por item.

## Validação

- Build Release .NET 9: aprovado, com apenas os avisos preexistentes.
- `--premium-check`: 38 verificações de concessão, renovação, soma, recovery, diárias, XP e frações, coleta real, capacidade, fadiga, expiração, revogação e falha de disco.
- `tests/admin-panel-check.mjs`: 122 verificações HTTP, incluindo autorização, origem, personagens offline, idempotência e reinício.
- `tests/premium-ui-check.mjs`: handlers JavaScript com fixture de DOM; filtros, escaping, prévia, validação, envio e retry.
- `--mail-check`: 42 verificações.
- `--gameplay-check`: 58 verificações.
- `--economy-check`: 303 verificações.

A revisão visual no navegador e a validação em aparelhos PC/Android permanecem pendentes: o navegador da sessão estava indisponível. O protocolo foi exercitado por TCP local e o painel por HTTP e seus handlers JavaScript. A implementação ainda não foi publicada no staging.
