# Investigação do tutorial Âncora — 01/10/2026

Base analisada: APK original Nexon 5.2.1, Unity 2017.4.34f1 (Alfa 2 brasileiro).
Investigação feita com o Redmi Note 12 conectado por ADB e com fontes do cliente
PC usadas somente como referência. Não foram alterados o servidor, seu banco,
o terreno original ou o cliente de PC nesta investigação.

## Bloqueio do HUD

Foi reproduzida a ausência de interface no tutorial. Instrumentação temporária
no cliente Android original mostrou esta sequência real:

- `intro_to_mmo`: contexto aberto, movimento/interface bloqueados pela cena;
- a transição de carregamento termina e `PauseUpdate` volta a falso;
- `intro_come_running_k` executa seu temporizador de 17 segundos;
- os diálogos e a ressuscitação lógica avançam com toques;
- `system_tip_life_ancora` permanece esperando a explicação da vida ser fechada.

Portanto, não é uma falha de autenticação nem a hipótese inicialmente examinada
de um evento de carregamento perdido. O código ARM64 já contém a proteção para
a transição que começou antes da inscrição do callback.

A NPC K e o cachorro dependem de quatro bundles ausentes no catálogo servido:

| Dependência | Consumidor |
| --- | --- |
| `models$npc$materials$npc_k_body.mat.bundle` | `NPC_KBikePrefab` |
| `models$npc$materials$npc_k_hair2.mat.bundle` | `NPC_KBikePrefab` |
| `models$npc$materials$npc_k_skin2.mat.bundle` | `NPC_KBikePrefab` |
| `particle$fx_materials$durango_common$fx_common_glow_05.mat.bundle` | `DogPrefab` |

`AssetBundleManager.TryLoadBundleFile` aguarda as dependências antes de carregar
o prefab. Com estas dependências faltando, os prefabs não são disponibilizados.
O personagem continuou deitado nas capturas mesmo depois do evento lógico de
ressuscitação do servidor.

A recuperação visual e a liberação da interface dependem da NPC:
`NpcAI_KBike.CoBeginCPR()` chama `PlayGuideSystem.Command.StandUp()` após a
animação. `CustomCommand.StandUp()` aplica `Bike_Getup` e retira a chave
`BeginMMO` dos componentes ocultados no começo da cena. Sem K, essa chamada não
acontece. O guia chega ao destaque de uma barra de vida que continua oculta,
sem completar essa etapa e sem chegar à liberação normal do movimento.

O cachorro também participa do enquadramento inicial: a câmera recebe o zoom
2.2 por uma ação aguardando a criação de `NpcAIDog`. Essa dependência ausente
impede o enquadramento previsto, além da ausência do cachorro.

Referências consultadas (sem alterações):

- `Durango-OffServer/_client_src/AssetBundleManager.cs`
- `Durango-OffServer/_client_src/Durango.Logic.PlayGuide/CustomCommand.cs`
- `Durango-OffServer/_client_src/NpcAI_KBike.cs`
- `Durango-OffServer/_client_src/Durango.UI/DialogueGroupBase.cs`
- `Durango-OffServer/_client_src/Durango.UI/PlayGuideHelperGroupBase.cs`

## Recursos recuperados que podem completar a cena

As quatro cópias existem em
`android-client/recovered/offserver-cache-2026-10-01/bundles-android/`.
Não são bundles Unity 6: os três materiais de K são Android 2017.4.7f1 e o
material do efeito é Android 2017.4.34f1.

Além do nome lógico, foi conferida a correspondência entre os CABs e os PathIDs
referenciados pelos prefabs atuais e os recursos recuperados:

| Material | CAB | PathID |
| --- | --- | --- |
| Corpo de K | `2ed55120e9f3eda87f716d4b4791deef` | `8637032030502495052` |
| Cabelo de K | `5f33da317a5771df4797b60613269732` | `2309374952275864763` |
| Pele de K | `69d0b028fcb85e237d08c3116cfb87ec` | `-3085039788844458069` |
| Efeito do cachorro | `774a6a714c22fa4984d65d5eb14f3d20` | `-8111886238630369642` |

Essas referências coincidem. O CRC/nome físico e o Hash de cache divergem do
catálogo atual; a integração deve anunciar os arquivos reais recuperados no
catálogo Android, com seus próprios valores e tamanhos. A auditoria do servidor
hoje aceita somente cabeçalhos 2017.4.34f1; deve permitir também o patch 2017.4.7f1
validado, sem alterar os cabeçalhos dos arquivos para fingir outra versão.

A correspondência de referências e os testes estruturais anteriores são
positivos. A renderização da cena completa ainda precisa ser validada no APK
original após disponibilizar esses recursos. Isso não exige trocar o motor,
modificar os shaders originais ou alterar o catálogo de PC.

## Vegetação na frente da cena

O terreno inicial é `tropical_event_ancora_01`, template `i01ancora180107`, com
entrada `[54,52]`. `whole.landmarks` e `whole.garden` vêm do ZIP de terreno do
servidor; `World.AssignChunkData()` divide esses dados para enviá-los ao cliente.
Há árvores grandes RTF01 (landmark 3026) na vizinhança, confirmadas também na
hierarquia do jogo em execução.

É possível retirar a ocorrência que encobre a cena no servidor, filtrando-a
somente nesse terreno antes de montar os chunks. Isso afeta Android e PC sem
editar nenhum arquivo do cliente PC. As coordenadas exatas da árvore que precisa
sair ainda não foram confirmadas: não foi aplicada uma remoção em área nem uma
remoção arbitrária de árvores próximas. Primeiro deve ser validado o
 enquadramento com K e o cachorro carregados.

`DestroyNatural` não é uma solução para retirar um landmark estático: trata
recursos coletáveis e, no tutorial, agenda renovação.

## Próxima correção indicada

1. Integrar e anunciar as quatro dependências recuperadas no catálogo Android.
2. Validar a cena desde o começo e confirmar `StandUp`, barra de vida e movimento.
3. Com o enquadramento correto, localizar a árvore obstrutiva e remover somente
   essa ocorrência dos dados de terreno enviados pelo servidor, se persistir.
4. Se necessário, acrescentar recuperação pontual para a etapa de ressuscitação
   quando uma falha de download impedir K de existir; preservar os diálogos,
   objetivos e a liberação normal do tutorial.

Os registros e scripts da instrumentação estão em
`android-client/work/original-nexon/tutorial/` (diretório de trabalho ignorado).
O APK diagnóstico não é uma versão de distribuição; o Alfa 2 permanece como
APK atual. Nenhuma correção funcional foi afirmada como concluída nesta etapa.

## Implementação e validação — 02/10/2026

O Alfa 3 integra as quatro dependências em `assets/durango-br/tutorial/`.
Os arquivos são cópias verificadas por SHA-256 do cache investigado, sem alterar
seus CABs, PathIDs ou cabeçalhos Unity. O catálogo Android anuncia CRCs, hashes
e tamanhos das revisões recuperadas. A preparação do pacote do servidor reaplica
somente essas quatro substituições. A auditoria aceita também Unity 2017.4.7f1.

Uma ponte ARM64 intercepta `AssetBundleItemInfo.GetCrcName` e encaminha esses
nomes para StreamingAssets. A tentativa inicial em `CreateTargetUrl` não era
chamada pela coroutine, pois foi incorporada pelo compilador nativo; foi
substituída antes da entrega. As demais chamadas usam o método original.

No Redmi Note 12, os registros BRTutorial confirmaram o carregamento local
das quatro dependências. A captura subsequente mostrou Pia, o personagem de pé,
o HUD, o controle de movimento e o objetivo “Para a Jangada”. O bloqueio anterior
foi superado sem pular etapas do tutorial ou alterar os shaders.

A acácia decorativa AF01, entidade 14024, em `49,48`, foi identificada na direção
da câmera diante do ponto inicial. `World` filtra essa ocorrência somente em
`tropical_event_ancora_01`, tanto do jardim original quanto de saves antigos e
de tentativas de renovação. A árvore vizinha e a mesma entidade em outros mapas
continuam presentes nos testes. O cliente de PC não foi editado.

O servidor passou 46 verificações de regressão. O teste dos recursos conferiu
as quatro dependências, as referências dos dois prefabs e seis texturas decodificadas.
A cena sem a árvore foi conferida após a publicação no staging: captura do
Redmi Note 12 mostrou K realizando a ressuscitação, Pia ao lado e a moto,
com o personagem totalmente visível e sem a acácia diante da câmera.

O servidor publicado é o commit `000dd4475914b660119f486edd05b666deea2b0b`.
Antes da troca da imagem Docker, o volume persistente foi salvo e o backup teve
todos os 92 arquivos conferidos por tamanho e SHA-256. Foram preservadas 15 contas
e 22 arquivos de personagens nos dois conjuntos de dados existentes. O gateway,
TCP do jogo, handshake Android/Windows, catálogo e os quatro bundles enviados
por HTTP passaram nas verificações posteriores.

A preparação e a auditoria confirmaram 1.175 bundles disponíveis, 978 ainda
ausentes e nenhum cabeçalho inválido. Os recursos de K e Pia deixaram de estar
ausentes; a correção do tutorial não significa que o pacote inteiro esteja completo.
