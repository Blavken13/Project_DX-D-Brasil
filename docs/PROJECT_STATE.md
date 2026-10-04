# PROJECT STATE — Project DX-D Brasil

**Última etapa aplicada:** 006  
**Data da base:** 2026-10-03  
**Branch auditada:** `main`  
**Commit auditado:** `4a0a9bc2d2b0d72f554bcc879c8dbea91f4b1358`

## 1. Base consultada

- `audit_20261003_1903.zip`;
- `audit_admin_mail_20261003_200038.zip`;
- `audit_admin_mail_step001_20261003_204007.zip`;
- `audit_admin_mail_delivery_20261003_211142.zip`;
- `audit_step004_catalog_20261003_214513.zip`;
- repositório no commit acima.

Se o estado local divergir do commit ou dos hashes da Etapa 000, uma nova
auditoria é obrigatória antes de modificar o projeto.

## 2. Descoberta inicial

### Engine e componentes

- Cliente original: Unity **2017.4.34f1**, conforme `android-client/README.md`.
- Cliente PC distribuído em `Durango-OffServer`.
- Cliente Android preserva o motor/assets originais e trabalha sobre cópias.
- Servidor customizado: `Durango-CustomServer/server`, .NET 9.
- `Durango original` é entrada somente de leitura para o fluxo controlado.

### Onde estão os textos

A localização PT-BR usa:

- `localization/catalog-pt_BR.json`;
- `localization/additional-pt_BR.json`;
- `localization/server-pt_BR.json`;
- `localization/client-pt_BR.json`;
- `localization/launcher-pt_BR.json`;
- `localization/build_pt_br.py`.

Saídas gettext:

- `Durango-CustomServer/server/data/locales/pt_BR/LC_MESSAGES/messages.mo`;
- `Durango-OffServer/Languages/pt_BR/messages.po`;
- `Durango-OffServer/Languages/pt_BR/messages.mo`.

Os mapas JSON também cobrem textos diretos do servidor, cliente e launcher.

### Encoding e seleção de idioma

- JSON/Markdown/PO observados: UTF-8.
- MO: binário gettext.
- O servidor prioriza `pt_BR`, `pt-BR` e `pt`.
- O cliente seleciona `pt_BR` na primeira execução da atualização e preserva
  mudanças posteriores feitas pelo jogador.

### Como o texto chega à tela

O catálogo gettext e textos diretos do cliente alimentam a UI do cliente Unity.
A auditoria não encontrou fontes independentes suficientes para provar
automaticamente a cobertura de todos os glifos. A documentação atual confirma
testes visuais parciais, não revisão visual completa do jogo.

Pendências técnicas:

- localizar/identificar a fonte ou atlas realmente usado por cada UI;
- automatizar cobertura de glifos PT-BR;
- medir limites reais de linha/caixa por componente.

Até isso ocorrer, os testes correspondentes ficam `IGNORADO` com checklist, em
vez de assumir limites inexistentes.

### Texto embutido em elementos não textuais

Há logos, créditos, vídeos e outros recursos visuais nos clientes. Os fluxos
Android/PC possuem verificações próprias de apresentação. Alterações futuras
nesses elementos devem usar cópias/patches e etapa dedicada.

## 3. Progresso da localização

`localization/coverage-pt_BR.json` registra:

- 33.065 entradas nativas;
- 197 entradas em inglês completadas;
- 33.220 entradas no catálogo produzido.

**Cobertura do catálogo produzido pelo pipeline atual: 33.220/33.220
(100,00%).**

Esse percentual não significa que 100% das telas tenham sido revisadas
visualmente.

## 4. Distribuição e legal

O repositório possui distribuição separada para servidor, cliente PC e Android.
A auditoria não encontrou licença de projeto na raiz que conceda, por si só,
permissão de redistribuição do material original.

Até confirmação documental da permissão aplicável:

- tratar `Durango original` como somente leitura;
- distribuir apenas traduções, patches e ferramentas sobre os quais o projeto
  tenha direito;
- não incluir executáveis/assets originais em novos pacotes por suposição.

## 5. Integridade e testes

A Etapa 000 cria:

- `run_tests.py`;
- `tests/manifest.json`;
- `tests/original_hashes.json`;
- `tests/steps/test_step_000.py`.

`run_tests.py` possui dez estágios: formato, encoding, cobertura, elementos
técnicos, limites de exibição, glifos, glossário, originais, build/execução e
regressão.

## 6. Painel admin / envio de email

### Etapa 001 — correção aplicada

A auditoria `audit_admin_mail_step001_20261003_204007.zip` confirmou que o
backend `/admin/mail/preview` aceita payload válido (HTTP 200), enquanto o
frontend continuava no estado anterior à tentativa de Etapa 001.

A correção aplicada nesta etapa:

- mantém `Enviar email` clicável;
- executa `/admin/mail/preview` automaticamente ao enviar quando a prévia estiver
  ausente ou desatualizada;
- mantém `Validar e visualizar` como conferência opcional;
- alterações do JSON, alvo ou destinatário invalidam a prévia sem bloquear o
  botão de envio;
- operações realmente em andamento usam `is-busy` e `aria-busy`;
- o helper `api()` possui timeout de 20 segundos;
- `Gateway.AdminMail.cs` e o protocolo de correio permanecem inalterados.

**Checklist manual:** enviar para um personagem de teste e confirmar recebimento
e resgate no cliente.

### Etapa 003 — sincronização automática do destinatário

A auditoria `audit_admin_mail_delivery_20261003_211142.zip` reproduziu o
problema de entrega:

- antes dos testes havia 0 mensagens, 0 pendentes e 0 dispatches;
- preview com `recipient_id` vazio retornou HTTP 400 com
  `recipient_id é obrigatório para um jogador.`;
- o mesmo email com o `recipient_id` do personagem selecionado retornou HTTP
  200 para 1 destinatário;
- `blade_big_sword_bone_02` foi aceito em preview com 1 anexo;
- as prévias não criaram mensagens nem dispatches.

A causa é o desacoplamento entre os controles `mail-target`/`mail-recipient` e
o JSON enviado ao backend. A Etapa 003 sincroniza esses campos automaticamente
ao trocar alvo/personagem e novamente imediatamente antes de qualquer preview
ou envio. Assim, o POST `/admin/mail/send` só recebe um draft coerente com o
personagem selecionado.

O backend de correio, a composição do item e o `MailStore` permanecem
inalterados nesta etapa.

### Etapa 004 — presentes de loja e Pedras de Portal

A auditoria `audit_step004_catalog_20261003_214513.zip` confirmou:

- a Pedra de Portal é o voucher `voucher_resource_induced_stone`, com limite
  original de 240, e não existe como `prototype_id` de item;
- o endpoint administrativo já conhece 2.407 protótipos de item;
- 357 protótipos são referenciados pelas commodities da loja;
- a classificação ampla da auditoria somou 168 candidatos, mas dois eram
  materiais de craft (`metal_set` e `rope`) capturados pela subcategoria
  `material_clothes`; o conjunto estrito de skins/roupas/acessórios é 166;
- todos os 357 protótipos referenciados pela loja existem no catálogo de
  anexos;
- o frontend antigo escondia resultados ao limitar o seletor aos primeiros 200.

A Etapa 004:

- adiciona filtros `Skins da loja`, `Todos os itens da loja` e
  `Todos os itens do jogo`, sem o corte de 200 resultados;
- permite anexar Pedras de Portal como voucher nativo do correio;
- preserva a Pedra de Portal fora de `ShopCatalog.MakeItem`;
- persiste `AttachedVouchers`, recupera claims após reinício e atualiza a
  carteira pelo protocolo nativo `WalletUpdated`;
- recusa o resgate se a soma ultrapassar o limite de 240, sem consumir o email;
- restaura a infraestrutura `run_tests.py`/`tests/`/`CHANGELOG.md`, que estava
  ausente no estado auditado.

## 7. Histórico

### Etapa 006 — Regressões históricas compatíveis com etapas futuras

Corrige as regressões das Etapas 004 e 005 para verificarem que suas
funcionalidades e registros históricos continuam presentes, sem exigir que a
etapa testada permaneça para sempre como `Última etapa aplicada` ou como
`generated_by_step` do manifesto.

Isso preserva as verificações de catálogo, skins, Pedras de Portal e
`TestHarness`, mas permite a evolução normal do projeto.

**Strings traduzidas:** 0.  
**Progresso do catálogo:** 100,00% do catálogo produzido pelo pipeline atual.

### Etapa 005 — Correção do harness e da classificação de skins

Corrige dois problemas encontrados após a Etapa 004: o build de testes passa a
usar a configuração isolada `TestHarness`, evitando conflito com o
`DurangoServer.dll` de Debug enquanto o servidor local está em execução; e a
classificação de skins passa de 168 para 166 após remover dois falsos positivos
(`metal_set` e `rope`) causados pela regra ampla `material_clothes`.

**Strings traduzidas:** 0.  
**Progresso do catálogo:** 100,00% do catálogo produzido pelo pipeline atual.

### Etapa 004 — Presentes de loja e Pedras de Portal

Amplia o catálogo administrativo para expor todos os 357 protótipos
referenciados pela loja, com filtro específico das 166 skins/roupas/acessórios,
e adiciona entrega/resgate da Pedra de Portal como voucher nativo do correio.

**Strings traduzidas:** 0.  
**Progresso do catálogo:** 100,00% do catálogo produzido pelo pipeline atual.

### Etapa 003 — Sincronização do destinatário do correio administrativo

Sincroniza automaticamente `target` e `recipient_id` entre os controles do
painel e o JSON antes de preview/envio, eliminando o HTTP 400 que impedia o
`Dispatch`. Também torna a regressão da Etapa 002 compatível com etapas futuras.

**Strings traduzidas:** 0.  
**Progresso do catálogo:** 100,00% do catálogo produzido pelo pipeline atual.

### Etapa 002 — Correção da regressão da Etapa 000

Corrige o teste legado da Etapa 000 para validar a permanência de sua
infraestrutura e de seu histórico sem exigir que `000` continue sendo a última
etapa aplicada. Isso permite que as regressões antigas continuem válidas após
novas etapas.

**Strings traduzidas:** 0.  
**Progresso do catálogo:** 100,00% do catálogo produzido pelo pipeline atual.

### Etapa 001 — Correção do fluxo de email administrativo

Torna o envio diretamente acionável, executa preview automaticamente quando
necessário, separa estado disabled de busy e adiciona timeout. A etapa também
restaura a infraestrutura de testes da Etapa 000 que estava ausente no estado
auditado.

**Strings traduzidas:** 0.  
**Progresso do catálogo:** 100,00% do catálogo produzido pelo pipeline atual.

### Etapa 000 — Inicialização do fluxo de engenharia

Cria somente documentação e infraestrutura de testes.

**Strings traduzidas:** 0.  
**Progresso do catálogo:** 100,00% do catálogo produzido pelo pipeline atual.
