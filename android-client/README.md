# Cliente Android Lost Horizon

APK atual: `dist/LostHorizon-alfa.apk` (02/10/2026, aproximadamente
303 MiB), versão Android `50209` / `5.2.1-losthorizon-alfa-compat2`.

## Compatibilidade e relatório de fechamento — revisão 50209

A autenticação agora usa `native/original/runtime_compat.c`, compilado como
`libnd.so`. As pontes são instaladas de forma síncrona em `CompatGameActivity`,
antes da criação do UnityPlayer. As APIs gerenciadas são usadas no fluxo Send
do jogo após a inicialização; não há polling com sleeps nem patch por worker.
As duas pontes próprias têm alinhamento ELF e RELRO de 16 KB e conferem os
retornos das proteções de memória. O motor e os shaders originais são preservados.
A opção de renderização multithread é desativada mudando um único byte de
PlayerSettings, sem reserializar o arquivo global inteiro.

O vídeo do login é desacoplado e o WebView destruído antes de abrir o jogo.
Uma falha do WebView oferece formulário Android com login/cadastro e diagnóstico.
No Android 15/16, o modo de compatibilidade fica ligado por padrão e solicita
30 FPS/resolução reduzida, quando suportado pelas APIs originais. Desde a revisão
50209, o vídeo da seleção volta a reproduzir por padrão, independentemente desse
perfil. Diagnóstico → Modo de compatibilidade oferece duas opções separadas:
gráficos reduzidos e desativação do vídeo da seleção para investigar fechamentos.
A segunda opção inicia desmarcada, inclusive ao atualizar a revisão 50208;
quando marcada, deixa a seleção sem vídeo. Prólogo e vídeos de outros mapas
continuam no player original. Altere as opções antes de entrar no personagem.

Diagnóstico oferece Ver último fechamento, Compartilhar relatório e Salvar relatório.
Os cinco registros mais recentes ficam em `files/diagnostics/reports/`, na área
privada do aplicativo. O compartilhamento usa URI temporária somente de leitura;
Salvar abre o seletor de documentos do Android. Não exige permissão de armazenamento.
No Android 11+, o histórico do sistema informa o motivo; no Android 12+, pode
fornecer a pilha nativa. Só sinal, bibliotecas e frames do thread que falhou são
exportados: logs brutos, mensagens de exceção, memória, senhas e tokens são omitidos.
Quando o sistema não disponibiliza o motivo/pilha, o relatório registra essa ausência.

O manifesto declara mínimo Android 5/API 21, mantém target 28 e habilita
`android:pageSizeCompat="enabled"`. Unity 2017/libmain ainda são binários legados
de 4 KB; o modo do Android é uma medida de compatibilidade, sem garantia universal.
O POCO X6/Android 16 permanece pendente de validação manual pelo tester.

Regressões: `tests/verify_runtime_compat.py`, `verify_presentation.py`,
`verify_tutorial_bundles.py` e `verify_raft_k.py`. O primeiro executa testes nativos
dos mesmos helpers da ponte e testa o parser de tombstone sem precisar de USB.

## Discord e transição após o trem

Os ícones de megafone e porta da seleção de personagens abrem
`https://discord.gg/nFHKbS7De`. A porta usa o mesmo manipulador do aviso;
esse clique não limpa a conta nem executa o logout antigo.

Após o trem, o player original reproduz `Movie/Mobile/warping.mp4`, já incluído
no APK, em lugar da URL externa da Nexon. O vídeo permanece sem compressão para
o `AssetManager.openFd` do player Android. A conclusão do vídeo e a ação de
pressionar e segurar para pular mantêm seus callbacks originais.

`presentation_original.py` modifica somente três literais dos metadados e uma
instrução ARM64 de quatro bytes do callback da porta. Os offsets, definições de
métodos e demais literais são preservados. `tests/verify_presentation.py` confere
os endereços pelos metadados/registro IL2CPP, o destino da instrução, o conteúdo
do APK assinado e a leitura local do vídeo sem compressão. Esta revisão foi
validada por inspeção e testes de integridade; a reprodução no celular aguarda
o teste manual do jogador.

## Base original do jogo

O cliente usa a pasta `Durango original`, com Unity **2017.4.34f1**. Essa pasta
é entrada somente de leitura; a compilação usa `work/original-nexon/client/`.
Os vídeos, shaders, `resources.arsc`, DEX do jogo original e `libunity.so`
continuam intactos. A identidade visual altera somente as imagens da marca,
os créditos da seleção e o nome instalado. Os demais recursos originais são
preservados byte a byte. O teste anterior foi confirmado pelo jogador:
acesso ao mapa e shaders carregando corretamente.

As alterações internas permanecem restritas ao modo online, gateway brasileiro,
carregamento da ponte de requisições e substituição da autenticação NPA
antiga. O Alfa 3 incluiu as dependências do resgate de K e Pia; o Alfa 4 restaura
a aparência da K no NPC junto à jangada. No servidor,
a árvore decorativa que obstruía o resgate é filtrada somente no terreno Âncora.
O banco e os arquivos do cliente de PC não foram modificados.

## Tutorial Âncora: Alfa 3

Os quatro bundles de `bundled/tutorial/` completam materiais e texturas de K e
o efeito usado por Pia. Foram recuperados do cache Android, com Unity 2017,
CABs e PathIDs correspondentes aos prefabs originais. O manifesto registra
tamanho, CRC, hash do catálogo e SHA-256; a compilação confere os arquivos.

`native/original/tutorial_bundles.c` compila uma ponte ARM64 `libbr.so` que
redireciona somente esses quatro nomes para os StreamingAssets do APK. As
demais requisições continuam pelo fluxo original. A ponte depende de `libnd.so`
para manter a autenticação brasileira. O motor e os shaders originais permanecem
intactos. É necessário o Android NDK, indicado por `ANDROID_NDK_HOME` ou encontrado
na instalação local `28.2.13676358` do SDK.

No celular, os registros confirmaram as quatro leituras locais e a cena avançou:
personagem de pé, Pia visível, HUD e controle de movimento presentes, objetivo
“Para a Jangada” ativo. `tests/verify_tutorial_bundles.py` valida as referências,
os hashes do servidor e as seis texturas. A remoção da acácia AF01, posição
`49,48`, é feita em `World`, inclusive para saves antigos e renovação dos recursos.
As verificações de regressão do servidor cobrem a remoção e a preservação das
árvores vizinhas e de outros mapas.

A publicação do servidor `000dd44` foi conferida no staging. Na nova cena do
celular, K realiza o resgate com Pia ao lado, sem a árvore obstruindo a câmera.
Contas e personagens foram preservados com backup verificado antes da troca
da imagem. A recuperação adicional de 112 dependências originais restaurou os
demais elementos do mapa, confirmados pelo usuário no celular. A preparação
atual registra 1.287 recursos disponíveis e 866 ainda ausentes.

## K junto à jangada: Alfa 4

`native/original/tutorial_raft_k.c` usa o modelo
`Models/NPC/F_NPC_K_Story.prefab` e o ToDo original
`talk_npc_raft_ancora.meet_chief`. No APK original, o NPC interativo 502 não foi
criado no cenário testado. A ponte acrescenta K enquanto esse objetivo está
ativo, com o mesmo posicionamento relativo à jangada usado no PC. O NPC fica
no mesmo nível da jangada na hierarquia: colocá-lo dentro da estrutura fazia
o seletor de cliques priorizar a montagem da jangada. Handles acompanham a
existência dos dois objetos para remover K ao descarregar a jangada. Um handle e a
verificação do NPC impedem duplicatas. Se um NPC 502 já existir, somente sua
aparência é substituída, preservando posição, collider e conversa.
Os componentes de interação por quests do modelo de K são retirados para
não disputar o clique com o ToDo original. Nenhum objetivo é concluído pela ponte.

A ponte usa o carregador assíncrono de dependências já existente e executa no
Update do tutorial, na thread Unity. O prólogo nativo é conferido antes de instalar
a ponte. Não modifica o cliente de PC, motor, shaders nem objetivos do servidor.
`tests/verify_raft_k.py` confere o NPC original, ToDo, modelo, seis dependências
e APIs mantidas no APK. A confirmação visual e da conversa é feita no celular.

O usuário confirmou K visível, conversa funcional e avanço da missão no Redmi
Note 12. O fechamento da revisão intermediária foi corrigido: a API de campos
do Unity 2017 recebe referências diretamente, enquanto valores numéricos usam
o endereço do payload. A requisição do prefab e as strings agora seguem esse
contrato. A versão final não inclui o tratador de sinais usado no diagnóstico.

## Login e conta salva

A tela utiliza `ui/index.html`, `ui/mobile.js` e a logo brasileira em um WebView
local. O painel é centralizado, preto e translúcido. O vídeo de fundo é exatamente
`assets/Movie/Mobile/title.mp4`, também utilizado na seleção de personagens.
O arquivo permanece original e tem apenas uma cópia dentro do APK.

Ao abrir o aplicativo com uma sessão salva, a tela mostra **Continuar** e
**Trocar de conta**. Não abre o Unity automaticamente. A sessão é validada no
gateway somente depois do toque em Continuar; se expirada, mostra o formulário.
Trocar de conta apaga o token local e permite entrar ou cadastrar outro usuário.
Um login ou cadastro solicitado explicitamente abre o jogo após autenticar.

Use o usuário e a senha da mesma conta brasileira do PC. A senha não é salva;
o token e sua validade permanecem nas preferências privadas, com backup Android
desativado. O WebView não permite navegação externa nem acesso a outros arquivos.
O vídeo para quando o login deixa de estar visível e é liberado ao abrir o jogo.

Gateway: `http://179.197.72.129:8190`. A ponte ARM64 acrescenta `token` aos POSTs
`/accounts` e `/sessions`, mantendo os cabeçalhos das sessões do jogo.

## Compilar e validar

```powershell
python android-client/build_original_apk.py
```

Requer Java do Android Studio e os utilitários APKtool/D8/assinatura em `work/`.
A ponte é compilada dos fontes de `native/original/`; `libnd-auth.so` é mantida
somente como referência da implementação anterior.
As fontes da tela e da autenticação estão em `src/com/newdawn/launcher/`.

A compilação confere os recursos originais, verifica assinatura e alinhamento,
e compara os arquivos dentro do APK assinado com o cliente validado. O relatório
fica em `work/original-nexon/verification.json`. Não registra senhas ou tokens.

No Redmi Note 12 conectado, o painel centralizado, a transparência e o vídeo
foram conferidos por captura de tela. O jogador confirmou que a abertura com
conta salva permanece no login e que Continuar abre os personagens normalmente.

O pacote é `com.nexon.durango.global`, ARM64, nome no aparelho **Lost Horizon**,
com a mesma chave de teste brasileira da versão anterior. Atualiza esse APK por
instalação normal, preservando a sessão e os recursos já baixados. Não substitui
uma instalação oficial Nexon assinada com outra chave.

## Identidade visual Lost Horizon

`branding_original.py` usa as três imagens na raiz do projeto: `icon.png`,
`logo.png` e `credits_splash_nexon_what.png`. O ícone é aplicado às 30 variantes
de densidade e formato. A logo substitui as versões inglesa e coreana da seleção,
a imagem do login e as duas regiões do atlas usadas abaixo do hexágono.
O splash Android recebe diretamente a imagem da Vision Force.

O atlas mantém ETC2 RGBA8, dimensões, tamanho e referências dos sprites.
Apenas 423 dos 524.288 blocos comprimidos foram alterados; os demais são idênticos
ao original. Os objetos Unity fora da marca permanecem byte a byte iguais,
inclusive materiais e shaders. Os créditos exibem:

```text
2016 VISION FORCE. Todos os direitos da Produtora
*Cliente 5.2.1
```

A versão antiga em outro rótulo é ocultada para evitar duplicação.
O relatório `work/original-nexon/branding-verification.json` registra os hashes
das imagens, regiões do atlas e verificações. O nome Android é alterado no
manifesto sem reconstruir a tabela de recursos. Pacote, chave de assinatura,
autenticação e correção do tutorial continuam compatíveis com o Alfa 4.

## Aparência feminina

Em 02/10/2026, o staging retornava 404 para roupas e cabelos femininos,
mantendo a aparência provisória preta. Foram recuperados 321 bundles Android
originais para completar as 366 aparências e suas 376 dependências.
`tests/verify_female_appearance.py` valida modelos, materiais, 379 texturas,
referências CAB/PathID e o preload. O pacote de recursos foi atualizado para
1.608 bundles disponíveis; 545 outros recursos do catálogo ainda estão ausentes.

Os arquivos adicionais são servidos pelo gateway; o APK atual continua válido.
Não é preciso recompilar, reinstalar ou limpar dados. Depois da publicação,
feche o jogo e entre novamente para refazer os pedidos que falharam.
O inventário em `recovered/upstream-android/` permite recuperar os arquivos
ignorados pelo Git. Os shaders originais e as correções de K e Pia foram
conferidos e preservados.
Os 321 arquivos foram publicados no staging e verificados pela rota HTTP;
o usuário confirmou a aparência feminina normal após reconectar no celular.

## Limpeza das versões anteriores

Foram removidas da raiz `DurangoBrasilApk` e `Durango-Brasil-v5.2.1`, após preservar
a ponte de autenticação e conferir os recursos da identidade visual em `branding/`.
A base atual `Durango original`, o servidor, o cliente de PC e o diretório `deploy`
foram mantidos. `localization` continua necessário à manutenção da tradução e
`tools` ao iniciador local do servidor.

Os scripts dos clientes antigos e da experiência Unity 6 permanecem como referência
histórica; o comando de compilação ativo é `build_original_apk.py`. As bibliotecas,
recursos e fontes da identidade visual em `branding/` podem ser usados em futuras
personalizações, sem reintroduzir os motores Unity modificados pelos outros APKs.

## Recursos Android para os testes

Na verificação de 30/09/2026, `/knock?platform=Android&version=5.2.1` respondeu
com `compatible: true`, porém o índice que ele anuncia,
`/live/android/Info.5.2.1.json`, retornou **HTTP 404**.

Recuperamos do celular 1.170 bundles Android, incluindo o preload, e o índice
original. Eles somam aproximadamente 545 MiB. Faltam 983 dos 2.153 bundles
previstos. O pacote parcial permite testar os recursos disponíveis, mas não
garante o carregamento de todo o jogo: também faltam dependências de alguns
bundles de prioridade 500 ou maior, incluindo materiais e texturas.

Para preparar os arquivos locais e um pacote de transferência para staging:

```powershell
python android-client/prepare_android_assets.py
```

O script confere os hashes recuperados, a assinatura UnityFS e a versão Unity,
copia os arquivos para `Durango-CustomServer/server/assetbundles/android/` e
gera `dist/assetbundles-android-5.2.1.tar.gz`, seu SHA-256 e um relatório JSON.
O índice de origem é mantido integralmente como referência. No catálogo preparado,
somente as quatro dependências integradas de K e Pia recebem os metadados das
revisões recuperadas; os demais nomes e referências permanecem intactos. O script
também inclui esses quatro arquivos, para não desfazer a correção em nova preparação.
O cache grande e o pacote de transferência são ignorados pelo Git. As quatro
dependências e o catálogo corrigido são versionados; o restante do cache ainda
precisa ser transferido separadamente ao staging.

O servidor detecta automaticamente `assetbundles/android/` ao lado da pasta
`--data`, quando o índice existe. A inicialização pelo BAT local usa essa detecção.
Para usar outro diretório, a opção explícita mantém a precedência:

```text
--assetbundles-android <diretorio-dos-bundles-android> --public-host 179.197.72.129
```

No Docker Compose do staging, o diretório local é montado como somente leitura
em `/app/assetbundles/android` e passado explicitamente ao gateway. Transfira o TAR para a máquina de staging e
extraia-o dentro de `Durango-CustomServer/server` **antes de recriar o serviço**:

```bash
sha256sum -c assetbundles-android-5.2.1.tar.gz.sha256
tar -xzf assetbundles-android-5.2.1.tar.gz -C Durango-CustomServer/server
docker compose -f deploy/staging/compose.yml up -d --build
```

Os comandos presumem execução na raiz do checkout e o TAR/checksum nessa pasta.
A preparação local não publica arquivos nem reinicia o servidor remoto.

O gateway envia os recursos em `/live/android/Info.5.2.1.json` e
`/live/android/<nome>.<CRC>.bundle`. Arquivos ausentes retornam 404 e são
registrados no log; o cliente pode tentar baixá-los novamente em segundo plano.
O índice é entregue como JSON sem cache persistente; bundles usam cache com ETag
e são enviados em streaming fora do loop do jogo. O servidor exige o nome e CRC
exatos: não substitui um bundle por outra revisão nem troca bancos de voz sob o
hash solicitado. Arquivos temporários e relatórios não são publicados nessa rota.
Não há substituição por arquivos vazios. Bundles Windows não substituem Android.
Para completar o pacote, acrescente à pasta de origem arquivos compatíveis com
o mesmo índice e rode o script novamente. Bundles novos ficam disponíveis no
gateway sem recompilar o APK; se mudar o catálogo, valide a nova versão e o índice.

O teste `tests/check_android_assets_gateway.py`, executado no WSL, usa saves
temporários e verifica o índice, os hashes de todos os bundles enviados por HTTP,
as rotas alternativas, o retorno 404 e a separação da rota Windows. A validação
da entrada no mundo e de seus recursos precisa ser feita no aparelho.

### Auditoria de completude

Antes de anunciar o pacote como completo, execute no servidor:

```text
dotnet DurangoServer.dll --android-assets-check --assetbundles-android /app/assetbundles/android
```

No checkout local também funciona `--android-assets-check --data <pasta-server/data>`.
Essa opção não inicia gateway/TCP nem carrega contas ou saves. O relatório JSON
informa arquivos ausentes, cabeçalhos inválidos e as dependências transitivas dos
pré-requisitos de prioridade **maior que 500**, conforme o cliente original.
Código de saída 0 significa catálogo completo e cabeçalhos válidos; 3 significa
pacote incompleto/incompatível; 2 significa configuração ou catálogo inválido.
Isso não substitui a validação do conteúdo pelo Unity em um aparelho real.

Na auditoria de 01/10/2026, os 1.170 bundles disponíveis tinham cabeçalhos válidos;
faltavam 983 arquivos, incluindo 301 dependências dos pré-requisitos. O servidor
entrega o conjunto disponível, mas o pacote ainda não permite garantir 100% do jogo.
O índice original continua íntegro para receber os arquivos restantes depois.

`tests/check_android_assets_catalog.py` verifica essa auditoria com o catálogo real
e casos isolados de dependência transitiva ausente, cabeçalho inválido e JSON malformado.

## Compilação e assinatura

`build_apk.py` recompila o adaptador com Java/D8, decompila e recompila o DEX do
launcher com Apktool 2.12.1 e mantém `classes.dex`, `libil2cpp.so` e as demais
bibliotecas do jogo intactos. As alterações nativas conferem as instruções
originais antes de aplicá-las. O TextAsset de servidores e as tabelas binárias
do manifesto/recursos mantêm seus tamanhos e offsets.

Pré-requisitos locais usados nesta máquina:

- Java do Android Studio em `C:/Program Files/Android/Android Studio/jbr/bin`;
- `work/apktool.jar`, distribuição oficial 2.12.1;
- `work/android.jar`, `work/d8.jar`, `work/apksigner.jar` e `work/zipalign.exe`,
  copiados do SDK Android instalado;
- Python 3.

```powershell
python android-client/build_apk.py
```

O script preserva os arquivos modificados originais em `work/original/` e pode
ser executado novamente sem aplicar duas vezes as alterações.
Os nove arquivos-base necessários para reaplicar os patches ficam versionados
em `base/`, com hashes em `base/manifest.json`. A compilação prioriza esses arquivos,
permitindo reconstruir o cliente após clonar o repositório sem depender dos backups
locais de `work/`. As imagens personalizadas continuam em `branding/resources/`.
`work/`, `dist/`, chaves de assinatura e caches de bundles não são enviados ao GitHub.
A assinatura de teste é local, RSA 2048, com esquemas Android v1, v2 e v3.
O APK é alinhado antes da assinatura, incluindo alinhamento de 16 KiB para as
bibliotecas nativas. Os arquivos de assinatura, sua senha, os intermediários
e o APK gerado estão ignorados pelo Git. Preserve a chave para atualizar esta
mesma instalação de teste em compilações posteriores.

## Verificações

- Assinaturas v1/v2/v3 e alinhamento conferidos com as ferramentas Android.
- 2.988 verificações de integridade, empacotamento, preservação dos binários
  do jogo e configuração de conexão aprovadas por `verify_apk.py`.
- Pacote, atividade inicial, ARM64 e permissão de HTTP conferidos no APK final.
- 24 verificações do adaptador Java contra uma cópia real do gateway com contas
  temporárias: cadastro/login, erro de senha, usuário duplicado, restauração,
  expiração, token rejeitado, status e formulário nativo com token legado vazio.
- A assinatura do método IL2CPP `HTTPRequest.AddField(string, string)` foi
  confirmada nos metadados desse APK, preservando a chamada da ponte nativa.
- As contas usadas nos testes foram apagadas com o ambiente temporário.
- Os 1.170 bundles recuperados foram enviados pelo gateway real em um teste
  isolado, com hashes e tamanhos conferidos. O índice permaneceu idêntico.
  Rotas alternativas, nomes codificados, bloqueio de caminhos inválidos,
  resposta 404 e separação da rota Windows foram validados.
- O TAR de transferência foi lido integralmente: os hashes dos 1.170 bundles
  e do índice correspondem aos arquivos preparados. O Docker Compose foi validado.
- Em 01/10/2026, a entrega HTTP dos 1.170 bundles e do índice foi novamente
  conferida, incluindo cache 304, rejeição de CRC incorreto e arquivos temporários.
  Os 77 arquivos de tabelas foram recebidos com hashes idênticos, e as rotas de
  terreno do tutorial, safehouse e ilha selvagem foram verificadas. O handshake
  HTTP do PC respondeu durante quatro downloads móveis simultâneos.

### Correções do alfa 3

- O loading utilizava o sprite inglês `bg_loading_ment_en`, com a arte Primal Colony.
  A arte brasileira estava somente em `bg_loading_ment_kr`. O atlas agora aponta
  ambos para o retângulo brasileiro, preservando a textura, os outros ícones,
  os nomes, as referências e os tamanhos serializados.
- O ícone do aplicativo utiliza `icon.png`, preservado em `branding/app-icon.png`.
  As 30 variantes dos recursos drawable/mipmap foram substituídas, incluindo
  `app_icon_global` e as variantes round em todas as seis densidades. Para importar
  outra edição, execute `python android-client/prepare_app_icon.py` antes de compilar.
- O personagem preto resultava da ausência de `m_body_vine.prefab.bundle`.
  O recurso foi recuperado do gateway público original com os mesmos metadados
  do catálogo e publicado no staging, sem modificar saves ou shaders. O usuário
  confirmou no celular que o personagem voltou ao normal após reconectar.
  Sua proveniência fica em `recovered/upstream-android/inventory.json`.
  `prepare_android_assets.py` agora inclui os recursos adicionais verificados:
  o conjunto tem 1.171 bundles disponíveis e 982 ausentes. O índice permanece intacto.
- `tests/check_additional_android_assets.py` confirma que esse tipo de recuperação
  entra no pacote e rejeita hashes, metadados ou índices divergentes.

Os logs e a tela do aparelho foram usados para diagnosticar a silhueta preta.
Os arquivos e referências da logo e do ícone são conferidos antes de entregar o APK;
a validação visual dessas duas alterações deve ser feita após instalar o alfa 3.

Referências das ferramentas: [Apktool](https://apktool.org/blog/apktool-2.12.1/),
[apksigner](https://developer.android.com/tools/apksigner) e
[zipalign](https://developer.android.com/tools/zipalign).
