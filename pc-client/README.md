# Cliente PC Lost Horizon

Correção da expansão gratuita do enclave: execute `python pc-client/patch_estate_expansion.py`
para gerar `dist/LostHorizon-PC-enclave1.zip` a partir do cliente atual. Feche o jogo
e extraia o pacote sobre `Durango-OffServer`. O patch altera somente a chamada do
clique de expansão e verifica todos os demais métodos contra a cópia anterior.
O limite da licença e a validação do servidor continuam ativos. Essa correção
também é aplicada pelo build completo do iniciador.

O iniciador abre o login com o vídeo `DurangoV2_Data/StreamingAssets/Movie/PC/title.mp4`, a logo Lost Horizon e um painel preto semitransparente. Somente ao entrar ou clicar em **Continuar** o jogo Unity é iniciado: login → splash existente → seleção de personagens.

## Abrir o jogo

Na pasta `Durango-OffServer`, abra `DurangoBrasil.exe`. Em computadores onde o Controle de Aplicativos do Windows bloqueia esse EXE sem assinatura, use `LostHorizon.cmd`. Ele executa a mesma implementação pelo Windows PowerShell e pelo .NET Framework, sem alterar as políticas do Windows.

O servidor escolhido em `offserver.txt` continua disponível; havendo mais de um, a seleção aparece no login. Cadastro e autenticação usam os endpoints existentes do servidor. A conta anterior é oferecida por **Continuar** e **Trocar de conta**, sem entrar automaticamente. A sessão fica criptografada com DPAPI, vinculada ao usuário do Windows e ao gateway, em `%LOCALAPPDATA%\Vision Force\Lost Horizon\PC`. Senhas não são salvas. O token é passado ao processo Unity por variáveis de ambiente e consumido na inicialização; não é colocado na linha de comando.

`DurangoV2.exe` continua sendo o executável Unity. Abrir esse arquivo diretamente mantém o fluxo anterior; use o iniciador para o novo fluxo.

## Identidade visual e compatibilidade

- A seleção de personagens usa `logo.png` da raiz do projeto e os créditos `2016 VISION FORCE. Todos os direitos da Produtora` / `*Cliente 5.2.1`.
- As duas variantes de idioma do logo abaixo do hexágono de carregamento usam a mesma imagem.
- O splash existente permanece intacto.
- As logos mantêm os IDs dos objetos Unity. O atlas mantém DXT5 e somente os blocos comprimidos das duas regiões substituídas são alterados.
- O helper `LostHorizon.PC.dll` importa a sessão para a autenticação já existente. O patch modifica seis métodos do iniciador/título e a chamada de expansão em `Assembly-CSharp.dll`; o restante do código do jogo, materiais e shaders é verificado contra a cópia anterior.

## Compilar e verificar

Execute `python pc-client/build_pc_client.py` na raiz do projeto, com Python, UnityPy 1.25.3/Pillow, .NET Framework e SDK .NET 9 disponíveis. As bibliotecas Python são carregadas de `android-client/work/python-deps`. O verificador Mono.Cecil é executado por WSL com o runtime Linux local `Durango-CustomServer/server/bin/test-runtime-linux/dotnet`, permitindo a compilação neste computador com SAC ativo. Esses runtimes e diretórios de trabalho não são versionados.

Antes de alterar os binários, feche o jogo. `pc-client/work` guarda uma cópia da versão anterior para verificar a preservação dos objetos, métodos e splash. Para iniciar uma nova base após outras atualizações do cliente, arquive esse diretório de trabalho e deixe o script criar um novo.

`python pc-client/tests/test_launcher.py` executa os testes de login, cadastro, falhas de sessão, isolamento por gateway, cache DPAPI e transferência ao processo Unity contra um servidor HTTP local temporário. Não cria nem modifica contas no servidor real.

Para inspecionar somente a aparência, use `powershell.exe -NoProfile -STA -File Durango-OffServer/Launcher/Start-LostHorizon.ps1 -Preview`. A prévia não autentica nem inicia o jogo.

Se o iniciador por PowerShell apresentar um erro, o diagnóstico fica em `Durango-OffServer/Launcher/launcher-error.log`, ignorado pelo Git.
