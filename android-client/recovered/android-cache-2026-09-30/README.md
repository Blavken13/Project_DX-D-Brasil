# Recursos Android recuperados do celular

Origem: aplicativo `com.primalcolony.game`, Android 15, aparelho modelo `23021RAAEG`.
Diretório de origem: `/sdcard/Android/data/com.primalcolony.game/files`.
A recuperação apenas leu e copiou arquivos. Nenhum cache foi apagado no aparelho,
nenhum APK foi alterado e nenhum arquivo foi publicado no servidor.

## Arquivos recuperados

- `Info.5.2.1.json`: índice Android original, com 2.152 arquivos na `FileList`
  e 4.302 entradas na `ItemList`.
- `UnityCache.tar`: cópia do cache, incluindo `__data` e seus metadados `__info`.
  O arquivo preserva os caminhos originais, que excedem o limite de algumas
  ferramentas Windows.
- `bundles-android/`: cópias dos 1.170 payloads `__data` com os nomes esperados
  pelo servidor (`<nome>.<CRC>.bundle`), mais uma cópia do índice.
- `inventory.json`: proveniência, tamanhos reais, hashes SHA-256 e relação
  entre caminhos do cache e nomes de arquivo do servidor.
- `source-sha256.txt`: hashes calculados nos arquivos originais do celular.
- `missing-bundles.json`: os 983 arquivos do índice ausentes desse cache.
- `SHA256SUMS.txt`: hashes dos arquivos de recuperação e dos relatórios.

Os dados de autenticação e o cache genérico de requisições HTTP não foram copiados.
O cache de recursos, o índice e a pasta de bundles ficam ignorados pelo Git.

## Verificação

Todos os 1.170 payloads recuperados têm assinatura `UnityFS` e versão Unity
`2017.4.34f1`, a mesma versão usada pelo APK brasileiro.
Todos os hashes SHA-256 das cópias coincidem com os arquivos originais do celular.
As cópias em `bundles-android/` também foram conferidas após a gravação.

Payloads recuperados: **571.064.880 bytes**, aproximadamente **545 MiB**.
O TAR que inclui diretórios e metadados tem **578.472.960 bytes**, aproximadamente
**552 MiB**. A pasta contém ambas as representações para preservação e uso futuro.

O índice prevê **2.153 bundles incluindo o preload**. Temos **1.170**, faltam
**983**. O preload recuperado é:

```text
preload.09fced165c9bf5ee2bb14ea9906b0c26.bundle
Hash: 45fc886f6a6238364c01a91381dc0ab9
```

Todos os arquivos de prioridade 500 ou maior estão presentes. Os ausentes têm
prioridades 495 (467 arquivos), 494 (22), 493 (172), 491 (1) e 490 (321).
Isso cobre os pré-requisitos definidos pelo cliente; não confirma que todos os
itens, aparências, mapas ou situações de jogo carregarão com o conjunto parcial.

## Uso futuro

`bundles-android/` fornece uma estrutura candidata à opção já existente
`--assetbundles-android` do servidor. Ela ainda não foi instalada ou testada
contra o APK em uma sessão de jogo.

Não descarte `missing-bundles.json`: publicar o índice completo com esse conjunto
parcial ainda pode resultar em HTTP 404 para os arquivos ausentes.
Recuperar um cache mais completo ou os bundles Android do servidor da comunidade
permitirá completar essa lista antes da distribuição ampla.

Os tamanhos originais do índice são informações lógicas do catálogo; não foram
usados para truncar os arquivos físicos do cache. Os payloads foram preservados
integralmente, com os tamanhos reais e hashes registrados no inventário.

## Nova busca após aguardar na seleção de personagens

Verificação em 30/09/2026, às 22:43 (horário de Brasília): o aparelho continua
com os mesmos 1.170 payloads. Os hashes SHA-256 de todos eles e do índice original
permanecem iguais. Nenhum dos 983 bundles ausentes foi encontrado, e o diretório
temporário do UnityCache está vazio. Não houve novos recursos para copiar.

A listagem atual, os hashes do aparelho e o relatório da comparação estão em
`recheck/device-paths.txt`, `recheck/source-sha256.txt` e `recheck/comparison.json`.
