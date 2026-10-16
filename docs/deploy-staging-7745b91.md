# Deploy do servidor de staging — 7 de outubro de 2026

Deploy concluído. O servidor `179.197.72.129` foi atualizado de `0f28eee` para
`7745b91fd5069ea54f3c9caa58e8a3bb901a53f2`, correspondente ao `main` sincronizado
com `origin/main` no momento da publicação.

## Alterações publicadas

- `6dd89a4`: bloqueios de inventário persistentes e ciclo de coleta, expiração e renovação da fauna.
- `e9d2ca6`: missões de caça e rádio, níveis das receitas e propriedades dos recursos coletados.
- `7745b91`: autorização das habilidades aprendidas e atualização das ações ao trocar de arma.

O pacote foi criado com `git archive` do commit, sem incorporar alterações locais.
Os ajustes de coleta a 10% nas ilhas nível 60 e de processamento de metais em
1 segundo continuam somente neste PC. O APK e os diagnósticos locais foram preservados.

## Publicação e preservação dos dados

- Imagem: `durango-brasil-server:7745b91`.
- ID: `sha256:2cf00a2e2f874f8a45ef8b5e420e6e741cba255c6b3d97436ed442ea6a270821`.
- Release: `/opt/durango/releases/7745b91fd5069ea54f3c9caa58e8a3bb901a53f2`.
- Backup: `/opt/durango/backups/state-pre-7745b91-20261008T025104Z.tar.gz`.
- Backup dos 231 arquivos persistentes verificado por tamanho e SHA-256, após encerramento normal do servidor.
- Volume mantido: `durango-brasil-staging-state`; chave de storage: `Durango Brasil`.
- Credenciais administrativas, configuração externa e assets Android anteriores preservados.
- Imagem anterior conservada como `durango-brasil-server:pre-7745b91` para rollback.

| Cluster persistente | Contas preservadas | Personagens preservados |
| --- | ---: | ---: |
| Durango Brasil | 45 | 52 |
| Durango Brasil - STAGING | 9 | 12 |
| Total | 54 | 64 |

## Validação

Compilação Docker Release e 15 comandos de regressão concluídos com sucesso antes
da troca da imagem: missões de rádio/caça, problemas reportados pelos testers,
gameplay, polimento, indução, recompensas de quests, mundo, social, premium,
economia, progressão/combate, fauna, cupons de XP e correio.

Alguns resultados: missões 47 verificações; progressão/combate 148;
economia 304; fauna 404; cupons de XP 178; correio 42.

O teste original de cupons de XP falhou inicialmente ao usar `GetField` em
`_lockedItemIds`, que virou propriedade no commit `6dd89a4`. A publicação parou
antes do reinício. Esse teste foi adaptado para aceitar campo ou propriedade
**somente em uma imagem separada de validação**, mantendo as verificações de uso,
bloqueio, consumo e persistência. A diferença de fontes entre as imagens foi
limitada a `SkillXpCouponCheck.cs`; o ID da imagem de produção permaneceu igual.
O teste corrigido passou, e toda a preparação foi executada novamente antes do deploy.
O teste desatualizado continua no commit original; a adaptação está nos scripts
locais de validação, sem commit adicional ou push.

Após a publicação foram confirmados:

- Container `healthy` e `/status` com `ok: true`.
- Acesso público deste PC a `http://179.197.72.129:8190/status` e TCP 8191.
- Handshake Android e Windows 5.2.1, catálogo e quatro bundles de tutorial por SHA-256.
- Assets Android anteriores mantidos, com 1609 arquivos `.bundle`.
- Arquivos do painel iguais aos da release e rotas administrativas protegidas contra acesso anônimo.
- Catálogo da loja, configuração administrativa e hashes dos 12 terrenos adicionais.

## Pendência anterior ao deploy

HTTPS não está disponível. A inspeção encontrou `durango-brasil-reports`
parado havia quatro dias, sem listener nas portas 80/443. Os logs anteriores
registram timeout na validação externa do certificado. O proxy não foi alterado
por este deploy do servidor do jogo. A checagem de HTTPS recebeu conexão recusada;
o acesso HTTP 8190 e a conexão do jogo 8191 foram confirmados externamente.

Evidências detalhadas: [deploy-staging-7745b91-validacao.json](deploy-staging-7745b91-validacao.json).
