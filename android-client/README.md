# Cliente Android do Durango Brasil

APK de teste atual: `dist/DurangoBrasil-alfa-3.apk` (01/10/2026, aproximadamente 305 MiB).
Os APKs anteriores de `dist/` foram removidos após validar esta versão.

Gateway fixo: `http://179.197.72.129:8190`.
Pacote Android: `com.durangobrasil.apk`; nome no celular: `DurangoBrasil`.
O pacote separado permite instalar esta versão junto do aplicativo da comunidade.
O APK original fornece somente bibliotecas ARM64 (`arm64-v8a`); essa arquitetura foi preservada.

## Login

A tela AUTH existente foi reaproveitada. Informe o **usuário**, e não um e-mail,
e a mesma senha da conta usada no PC. O cadastro aceita de 3 a 32 caracteres
(`A-Z`, `a-z`, números, ponto, hífen e sublinhado), e senha de pelo menos 8 caracteres.
O botão Criar conta registra no gateway e entra automaticamente quando o cadastro termina.

O adaptador Java usa `/auth/register`, `/auth/login`, `/accounts` e `/status`.
O token `auth_token` retornado pelo login é entregue ao jogo pelo Intent existente.
A ponte nativa `libnd.so` foi adaptada para incluir esse valor no campo de formulário
`token` das requisições `/accounts` e `/sessions`, que é o contrato atual do servidor.
Os cabeçalhos `Authorization` das sessões do jogo continuam sendo gerados pelo Unity.
Nenhuma alteração no servidor é necessária para essa autenticação.

O token e seu prazo ficam nas preferências do aplicativo; a senha não é salva.
Após expirar o token ou reiniciar o servidor, o jogador precisa entrar novamente.
A identidade da conta é resolvida pelo servidor e recupera os mesmos personagens do PC.

### Aparência da tela AUTH

As fontes da interface ficam em `ui/`: login e cadastro centralizados num painel
escuro translúcido, sem cartões de notícias ou seletor de servidor. O fundo usa
o vídeo já existente `assets/Movie/Mobile/title.mp4`, em repetição e sem áudio.
A logo acima do painel foi extraída da textura `logo_eng` do próprio jogo;
`ui/logo-source.json` registra sua origem e hash. Nenhuma textura Unity foi alterada.

O cadastro inclui confirmação de senha. Depois de um login ou cadastro bem-sucedido,
a tela chama a ponte existente para abrir o jogo no gateway brasileiro. Uma sessão
restaurada mostra Continuar e Trocar de conta. A seleção de personagem permanece no Unity.

Para aplicar só a interface à pasta extraída, antes de gerar o próximo APK:

```powershell
python android-client/build_apk.py --ui-only
```

A compilação completa também copia essas fontes e ajusta o WebView para permitir
a reprodução automática do vídeo. A opção `--ui-only` não altera DEX nem gera um APK;
o ajuste nativo do WebView só entra no APK na próxima compilação completa.

A prévia local pode ser aberta com `python android-client/tests/preview_login.py`.
Login, cadastro, confirmação de senha, erro de credenciais e a chamada automática
de abertura foram conferidos no navegador com uma ponte Java simulada apenas em
`test-login.html`. O vídeo e o encaixe da interface foram conferidos em paisagem
873×393 e 800×360 e em retrato 393×873. Isso não substitui o teste no Android após
a compilação. O APK alfa 2 inclui essa interface, o vídeo e o ajuste de autoplay;
a validação no aparelho ainda precisa ser feita.

### Identidade visual e créditos

As três imagens editadas pelo usuário em `DurangoBrasilApk` foram preservadas em
`branding/resources/`, com hashes e caminhos originais em `branding/manifest.json`:

- `assets/bin/Data/bcb37f4b3d27e4a4f93a3ec965e256dc`: atlas de ícones/carregamento;
- `assets/bin/Data/c6c98a0f398372a41862a640adc466d2`: textura `logo_eng` da seleção;
- `res/drawable/unity_static_splash.png`: splash da equipe Vision Force.

Toda compilação aplica essas fontes, sem recuperar versões antigas dessas imagens
dos backups do launcher. A logo do HTML é extraída da mesma textura Unity. Caso as
imagens da pasta original sejam editadas novamente, atualize as fontes antes de compilar:

```powershell
python android-client/prepare_branding.py
python android-client/build_apk.py
```

`prepare_branding.py` usa UnityPy 1.25.3 (nesta máquina, em `work/python-deps`).
Os créditos dos dois prefabs de título passaram a exibir três linhas: **Vision Force**,
**Servidor Brasileiro** e **Versão 1.0**. O patch mantém os 62 bytes da string
serializada com marcadores NGUI invisíveis de restauração de cor, e verifica o layout
do UILabel antes de alterar o pivô inferior, a altura e a fonte. Os demais objetos,
seus identificadores, tamanhos e referências continuam intactos.

O texto **1.0** identifica a apresentação desta distribuição. A versão interna do
jogo/protocolo continua **5.2.1**, preservando o contrato do gateway e dos bundles.
O APK mantém o pacote e a mesma chave de assinatura de teste do alfa anterior.
`tests/check_android_branding.py` verifica as imagens, os dois rótulos e a preservação
dos outros 525 objetos desses prefabs. Não houve teste visual desses créditos no Android.

Os serviços de autenticação, notícias, diagnóstico e atualização do outro servidor
foram desconectados dos fluxos deste cliente. A seleção de outro gateway foi
desabilitada nessa distribuição. A lista de servidores interna do Unity contém
somente Durango Brasil, apontando para o mesmo gateway da tela AUTH.

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
O índice original é preservado integralmente; seus tamanhos lógicos não são
usados para cortar os arquivos físicos. O pacote, os bundles e o índice ficam
ignorados pelo Git. Eles precisam ser transferidos separadamente ao staging.

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
