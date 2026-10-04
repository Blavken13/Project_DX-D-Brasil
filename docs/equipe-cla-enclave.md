# Equipes, clãs e enclave

Enclave significa o território compartilhado do clã. A implementação usa a ilha persistente `clan_<id>` e os protocolos originais do cliente.

## Diagnóstico

Antes desta alteração, equipes, alianças e pesquisas respondiam com dados vazios ou recusas. O clã possuía um cadastro básico, mas o JSON não correspondia ao contrato nativo de membros e cargos. A ilha do clã já existia; seus direitos eram amplos demais, a expansão consumia a carteira pessoal e faltava recuperação conjunta do território e do fundo após uma interrupção.

## Comportamento implementado

| Área | Comportamento |
| --- | --- |
| Equipe | Criar, convidar entre ilhas, aceitar, recusar, expulsar, sair e transferir liderança. Saída do líder elege outro integrante confirmado. |
| Presença | Vida, energia, nível, localização e estado online dos integrantes; restauração de equipes ao reconectar e reiniciar. |
| Chat | Canais privados de equipe e clã entre ilhas, com vínculo validado pelo servidor e identidade do remetente definida no servidor. |
| Clã | Criação paga, busca e detalhes, solicitação de entrada, aprovação, convite, recusa, saída, expulsão, edição de informações, emblema e transferência de liderança. |
| Cargos | Cargos personalizados, ordenação, permissões delegadas e migração de membros ao excluir cargo. A hierarquia impede promover a si próprio e alterar integrantes acima do próprio cargo. |
| Fundo | Doações em T-Stones, consulta de saldo, gastos em território e pesquisas; valores negativos, moedas diferentes e saldos insuficientes são recusados. |
| Progressão | XP normal dos membros alimenta o clã; níveis, capacidade, benefícios e blueprints seguem as tabelas originais. XP de comandos administrativos não alimenta o clã. |
| Alianças | Proposta, aceitação, recusa, negociação de encerramento e rompimento unilateral, com registros bilaterais e capacidade por nível. |
| Pesquisa | Laboratórios concluídos, categoria e nível corretos, proximidade, autorização por cargo, pagamento pelo fundo e prazo/cooldown persistentes. Um laboratório não executa pesquisas simultâneas. |
| Enclave | Declaração, expansão contígua, redução sem fragmentação, remoção, licença de acesso e renovação de ativação. |
| Estruturas compartilhadas | Acesso por cargo e por estrutura, armazenamento compartilhado e quotas diárias de retirada. Contadores enviados pelo cliente não substituem os contadores do servidor. |
| Expulsão | Perda imediata de direitos sobre o enclave, inclusive estruturas construídas pelo próprio expulso; retorno à ilha pública e bloqueio de reconexão na ilha do antigo clã. |

## Valores e regras

- Criação: **10.000 T-Stones**, de `assets/constants.json`.
- Níveis: até **25**, de `assets/clan.json`. Capacidade inicial: **10 membros**.
- Enclave: desbloqueio no **nível 5**, com **12 células**; os limites seguintes vêm da tabela original. A primeira célula custa zero pela fórmula original `10000 * territory_count`; expansões seguintes custam 10.000 vezes a área atual. Recuperar área previamente paga até `LargestSize` não cobra novamente.
- Pesquisas: 15 definições de `assets/clan_research.json`, com custos, laboratórios, duração e restrição geográfica originais. Pesquisas básicas custam **80.000 T-Stones**; avançadas, **150.000**.
- Efeitos são removidos ao expirar, sair do clã ou sair da área exigida. Coleta, fabricação, combate, saúde máxima, recuperação de vida e fadiga usam os modificadores aplicáveis no servidor; o cliente recebe os efeitos nativos.
- Equipe: **5 integrantes**, contando convites pendentes; convite vence em **5 minutos**. São políticas deste servidor, pois não foi encontrada tabela original de capacidade.
- Convites de clã e propostas de aliança vencem em **24 horas**; rompimento unilateral bloqueia o slot por **24 horas**. São políticas do servidor. Slots de aliança usam os valores originais.
- Quotas de retirada reiniciam à meia-noite **UTC**. `-1` significa ilimitado; ausência de autorização significa retirada proibida.

## Persistência e compatibilidade

`parties.json` e `clans.json` ficam junto aos saves dos personagens. Saves antigos de clãs recebem os cargos e coleções padrão ao carregar. O contrato HTTP agora retorna `members` como pares `[id, cargo]`, `appliers` como IDs e `role_infos` com permissões numéricas, conforme o cliente original.

Criação, doação, pesquisa e alterações do enclave usam o journal econômico existente. Cada operação é gravada antes da aplicação e da confirmação ao cliente. Checkpoints no clã, no personagem e na ilha impedem duplicação ao recuperar snapshots antigos. A recuperação da ilha reaplica também geometria e permissões, mesmo que o fundo já tenha sido atualizado.

## Validação e limites

Execute `dotnet run --project Durango-CustomServer/server -c Release -- --social-check`. O teste exercita os protocolos TCP em personagens de ilhas diferentes, o contrato JSON nativo, cargos, chat privado, fundo, alianças, enclave, laboratório, quotas, expulsão, recarga dos arquivos e recuperação do journal. Há verificações de falha de gravação para evitar alterações apenas em memória.

Resultado em 04/10/2026: build Release sem erros (dois avisos preexistentes), **80 verificações sociais** aprovadas no runtime Linux do projeto. As regressões executadas nesta implementação também passaram: premium (38), gameplay (58), economia (304), progressão (35) e correio (42). O Windows bloqueou o binário final pelo Controle de Aplicativo; a validação final usou WSL, sem modificar essa política.

O enclave desta entrega não é a guerra por outposts (`ClanWarphole`): captura competitiva, horários de guerra e recompensas PvP exigem outro sistema. Pesquisas limitadas a outposts não concedem efeitos em um enclave comum. Rotas de equipe usam o catálogo de navegação já disponível no servidor. Benefícios de desconto de viagem e prevenção de perda na morte não alteram custos que o servidor atualmente mantém gratuitos ou perdas que ele já não aplica. A pesquisa de ecologia envia os modificadores nativos; os sistemas simplificados de furtividade/captura ainda não reproduzem todas as fórmulas históricas.

As alterações estão locais. Os testes de protocolo não substituem a conferência das telas e fluxos no cliente Android. Esta entrega não realizou um novo deploy em staging.
