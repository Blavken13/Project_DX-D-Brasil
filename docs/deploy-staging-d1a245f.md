# Deploy do staging — 9 de outubro de 2026

Deploy concluído no servidor `179.197.72.129`, de `b9f87f0` para
`d1a245fc2fc31ef975e14fb7b6348539719f1468`.

## Release e dados persistentes

- Imagem: `durango-brasil-server:d1a245f`.
- ID: `sha256:abcbd521b6e730bb2d02bc1053c87318c8fe553e92cb0ac67f987ee6d6226e4a`.
- Release: `/opt/durango/releases/d1a245fc2fc31ef975e14fb7b6348539719f1468`.
- Backup: `/opt/durango/backups/state-pre-d1a245f-20261010T012030Z.tar.gz`.
- Backup dos 243 arquivos persistentes verificado por tamanho e SHA-256 após encerramento normal do servidor.
- Volume preservado: `durango-brasil-staging-state`; chave de storage: `Durango Brasil`.
- Imagem anterior disponível para rollback: `durango-brasil-server:pre-d1a245f`.
- Configuração externa e credenciais administrativas anteriores preservadas.
- Assets Android anteriores preservados, com 1609 arquivos `.bundle`.

| Cluster | Contas preservadas | Personagens preservados |
| --- | ---: | ---: |
| Durango Brasil | 49 | 57 |
| Durango Brasil - STAGING | 9 | 12 |
| Total | 58 | 69 |

O pacote foi criado com `git archive` do commit publicado. Os arquivos locais de
Android e diagnósticos não foram incorporados à release. O horário UTC do backup
é 10 de outubro, 01:20:30, correspondente a 9 de outubro, 22:20:30, em São Paulo.

## Validação

Compilação Docker Release e regressões executadas antes da troca da imagem.
Os testes específicos passaram: bolsas e combate, 2399 verificações; crateras,
PvP e pets, 52; vulcão e recursos das crateras, 204; problemas de gameplay
reportados, 107. As demais regressões de gameplay, mundo, missões, economia,
progressão, fauna, social, premium e correio também concluíram com sucesso.

O teste antigo de cupons usa reflexão de campo para `_lockedItemIds`, que passou
a ser propriedade. Foi aplicada a adaptação já usada no deploy anterior,
somente em uma imagem separada de validação. Suas 178 verificações passaram;
a fonte e o ID da imagem publicada permaneceram inalterados.

Após o deploy foram confirmados:

- Container `healthy`, imagem correspondente à release e `/status` com `ok: true`.
- Acesso público HTTP 8190, TCP 8191 e handshake Android/Windows 5.2.1.
- Catálogo Android e quatro bundles de tutorial verificados por SHA-256.
- Painel administrativo correspondente à release e rotas protegidas contra acesso anônimo.
- Catálogo da loja e hashes dos 12 terrenos adicionais.

## Pendência anterior

O proxy HTTPS estava parado havia seis dias antes do deploy, com timeout de
validação externa do certificado nos logs anteriores. O deploy atualizou o
servidor do jogo; HTTPS continua pendente. HTTP 8190 e TCP 8191 foram validados.

Evidências: [deploy-staging-d1a245f-validacao.json](deploy-staging-d1a245f-validacao.json).
