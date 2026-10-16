# Correção e restauração do armazenamento no staging

Aplicação concluída no servidor `179.197.72.129`, da release `d1a245f` para
`9ed8e54c24f23026fc5c9accb5f7bbef17fe6b22`, publicada na branch `main`.

## Resultado

- Regra de normalização corrigida: os níveis de armazenamento antigos mais altos são preservados.
- **28 itens restaurados: 24 bolsas e 4 trajes.**
- As 12 mochilas `bag_back` voltaram de armazenamento 15 para 60.
- Os demais itens recuperaram seus níveis individuais comprovados no backup anterior.
- A recuperação localizou os itens por ID, incluindo os transferidos para armazéns após a auditoria.
- Somente o atributo `pocket` foi alterado em 13 arquivos de estado atuais.
- Nenhum item ausente, nenhum item recriado e nenhum conflito.
- Os 28 IDs e seus níveis foram verificados após o reinício e novamente na validação final.

## Release e backups

- Imagem: `durango-brasil-server:9ed8e54`.
- ID: `sha256:101b493d1a3098aacada87947d7d0688c57db4e1345601b37c31be7726e72475`.
- Release: `/opt/durango/releases/9ed8e54c24f23026fc5c9accb5f7bbef17fe6b22`.
- Backup imediatamente antes da recuperação: `/opt/durango/backups/state-pre-9ed8e54-20261010T014736Z.tar.gz`.
- Backup de referência dos níveis anteriores: `/opt/durango/backups/state-pre-d1a245f-20261010T012030Z.tar.gz`.
- O backup atual dos 243 arquivos foi verificado por tamanho e SHA-256 após encerramento normal do servidor. O SHA-256 do backup histórico também foi confirmado.
- Volume e chave de storage mantidos: `durango-brasil-staging-state`, `Durango Brasil`.
- Imagem anterior preservada como `durango-brasil-server:pre-9ed8e54`.
- Assets Android anteriores preservados, com 1609 bundles; credenciais e configuração externa preservadas.

| Cluster | Contas preservadas | Personagens preservados |
| --- | ---: | ---: |
| Durango Brasil | 49 | 57 |
| Durango Brasil - STAGING | 9 | 12 |
| Total | 58 | 69 |

## Validação

A imagem publicada foi compilada em Release e validada antes do reinício.
Passaram os testes de combate e bolsas (2415 verificações), gameplay reportado
(107), vulcão e crateras (204), crateras/PvP/pets (52), mundo, missões, economia,
progressão, fauna, social, premium, cupons e correio (42). Os cinco testes Python
de recuperação também passaram na release, incluindo preservação de progresso,
idempotência, transferência de itens e rollback de gravações parciais.

O teste antigo de cupons foi adaptado somente em uma imagem separada de
validação, como nos deploys anteriores. A imagem publicada permaneceu inalterada.

Após a aplicação foram confirmados container `healthy`, `/status` com `ok: true`,
acesso público HTTP 8190 e TCP 8191, handshake Android/Windows, catálogo e quatro
bundles de tutorial por SHA-256, painel administrativo e proteção das rotas,
catálogo da loja e hashes dos 12 terrenos adicionais.

O proxy HTTPS permanece com a pendência de certificado anterior ao deploy.

Evidências: [deploy-staging-9ed8e54-validacao.json](deploy-staging-9ed8e54-validacao.json).
