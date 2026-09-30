# Português brasileiro no Durango Brasil

A tradução utiliza o catálogo brasileiro original encontrado no cliente, com
33.065 entradas, e completa os textos adicionados pela comunidade. O resultado
tem 33.220 entradas: interface, itens, receitas, missões, habilidades, lojas,
diálogos, tutoriais e notificações. Nomes próprios de pessoas, lugares e espécies
mantêm sua grafia quando apropriado.

O servidor prioriza `pt_BR`, `pt-BR` e `pt` nas traduções dos dados. O cliente
seleciona `pt_BR` na primeira execução desta atualização, preservando as demais
preferências. Uma mudança de idioma feita depois pelo jogador será respeitada.

## Publicar a atualização

Atualize o servidor e seu diretório `data`, incluindo
`data/locales/pt_BR/LC_MESSAGES/messages.mo`. Atualize também estes arquivos no
cliente distribuído aos jogadores:

- `Durango-OffServer/Languages/pt_BR/messages.po`
- `Durango-OffServer/Languages/pt_BR/messages.mo`
- `Durango-OffServer/DurangoV2_Data/Managed/Assembly-CSharp.dll`
- `Durango-OffServer/DurangoBrasil.exe`

Apenas atualizar o servidor não traduz os menus e os diálogos locais de um
cliente antigo. Os fontes correspondentes foram atualizados junto dos binários.

## Manter a tradução

Os arquivos JSON deste diretório guardam os complementos em português.
`catalog-pt_BR.json` completa entradas do catálogo original que ainda estavam em
inglês; `additional-pt_BR.json` acrescenta textos de produtos e rótulos locais;
`server-pt_BR.json`, `client-pt_BR.json` e `launcher-pt_BR.json` guardam textos
diretos dos respectivos programas. As chaves são os textos originais e os valores
são as traduções. Os escopos do cliente indicam classe, método e, quando
necessário, ocorrência do literal para preservar comparações e identificadores.

Regenere e confira os catálogos com Python 3:

```powershell
python localization/build_pt_br.py
python localization/build_pt_br.py --check
```

O script usa apenas a biblioteca padrão, extrai os catálogos nativos de
`resources.assets` sem modificar esse arquivo e verifica contextos, marcadores de
formatação, compilação MO e igualdade dos arquivos gerados.

Para trabalhar nos textos diretos, compile `LocalizationTools` com .NET 9. Os
comandos `apply-source` e `apply-client-source` alteram apenas tokens de texto e
conferem a sintaxe C#. `patch-client` produz uma cópia traduzida de um assembly e
confere todas as instruções originais; a única inclusão de lógica no cliente do
jogo é a inicialização do idioma brasileiro. `verify-client` compara o binário
final com uma cópia anterior usando o mesmo mapa de traduções.

```text
apply-source <pasta-do-servidor> <server-pt_BR.json>
apply-client-source <pasta-_client_src> <client-pt_BR.json>
patch-client <assembly-original> <mapa-json> <assembly-saida>
verify-client <assembly-original> <assembly-final> <mapa-json>
```

Guarde uma cópia do assembly original antes de substituí-lo. Os diretórios
`bin` e `obj` deste utilitário são locais e ficam fora do controle de versão.
Quando a Proteção Inteligente de Aplicativos impedir a execução do utilitário
compilado no Windows, o runtime Linux existente pode executá-lo pelo WSL. Nesse
caso, use `DOTNET_ReadyToRun=0`, pois as dependências Roslyn vieram do SDK Windows.

## Verificação realizada

`DurangoServer --localization-check --data <diretorio-data>` verifica o catálogo,
as prioridades de idioma, os produtos e os textos de todos os protótipos e dados
referenciados: 40.346 verificações passaram. Os testes existentes de mundo,
jogabilidade, economia, cultivo, missões, efeitos de habilidades e efeitos
visuais somaram mais 2.165 verificações aprovadas.

O cliente foi iniciado em uma cópia isolada, sem conexão ao staging. Os botões e
textos da tela de entrada foram conferidos em execução e por captura de tela.
Isso confirma o carregamento da atualização; não substitui uma revisão visual
de todas as telas, missões e situações possíveis do jogo.

Identificadores de protocolo, chaves dos dados, enums, placeholders e marcação
dos textos foram preservados. Comentários e mensagens de diagnóstico não
exibidas aos jogadores permanecem no idioma original.

Depois desta tradução, a atualização de pacotes e acampamento acrescentou alterações de jogabilidade ao cliente. Use `verify-client` para verificar somente a etapa de tradução, antes de `patch-gameplay`. O comando `verify-gameplay` confere as três funções de jogabilidade no assembly final. Consulte `Durango-CustomServer/docs/ajustes-farm-e-acampamento.md` para as regras e a publicação dessa atualização.
