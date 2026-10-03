# Servidor local no Windows

Os recursos Android podem ser preparados com `python android-client/prepare_android_assets.py`,
executado na raiz do projeto. Quando existe `server/assetbundles/android/Info.5.2.1.json`,
o servidor detecta essa pasta automaticamente ao usar `server/data`, inclusive pelo BAT.
A opção `--assetbundles-android` permite indicar outro caminho. O conjunto recuperado
é parcial; a documentação e o pacote de transferência estão em `android-client/`.

Para auditar o pacote sem iniciar o servidor nem carregar saves, use
`dotnet DurangoServer.dll --android-assets-check --data <pasta-server/data>`
ou informe `--assetbundles-android <pasta>`. O código 0 indica catálogo completo;
3 indica arquivos/dependências ausentes ou cabeçalhos incompatíveis; 2 indica
configuração/índice inválido. Ainda é necessário testar o carregamento pelo Unity.
Os recursos Android ficam em rotas próprias, com CRC exato e índice atualizado;
as tabelas e terrenos do servidor continuam compartilhados com o cliente de PC.

Os scripts `iniciar-servidor-local.bat` e `tools/iniciar-servidor-local.ps1` sao mantidos somente no computador de desenvolvimento e nao sao incluidos neste repositorio. As instrucoes abaixo documentam essa configuracao local.

Execute `iniciar-servidor-local.bat` na raiz do projeto. Ele compila a versao atual e verifica se a DLL consegue executar antes de iniciar o servidor.

Se aparecer **porta já em uso** ou `SocketException (10048)`, existe outro processo usando uma das portas. Encerre a janela anterior com **Ctrl+C** e aguarde o salvamento antes de abrir novamente. O painel da instância ativa fica em [http://127.0.0.1:8190/admin/](http://127.0.0.1:8190/admin/).

O inicializador verifica as portas TCP antes de executar a DLL. O servidor também verifica TCP 8190, TCP 8191 e UDP 8191 antes de carregar os dados e mantém acesso exclusivo à pasta de saves durante toda a execução. Uma segunda instância com a mesma chave e pasta de armazenamento é recusada mesmo que use outras portas, sem carregar ou gravar o progresso. O arquivo `.server.lock` pode permanecer após o encerramento; seu bloqueio é liberado pelo sistema e não é necessário excluí-lo.

Quando o Windows bloqueia a DLL com `0x800711C7`, o inicializador usa uma distribuicao Linux do WSL. Nao altera SAC, SmartScreen, Defender, exclusoes ou politicas de execucao. Erros diferentes desse bloqueio sao apresentados normalmente.

O cliente continua conectando a `127.0.0.1:8190` (gateway) e `127.0.0.1:8191` (jogo). A chave continua sendo `Durango Brasil`, com progresso em `Durango-CustomServer/server/AppData-nx/offline/Durango Brasil`. O WSL acessa essa mesma pasta do Windows; nao cria uma segunda copia dos saves. Encerre com **Ctrl+C**, aguardando a confirmacao de salvamento.

Para compilar e verificar a execucao sem iniciar o servidor, execute:

```bat
iniciar-servidor-local.bat --check
```

## Requisitos do caminho WSL

- WSL com uma distribuicao Linux instalada. O inicializador prefere `Ubuntu-24.04`, depois a primeira distribuicao que nao seja do Docker. A variavel opcional `DURANGO_WSL_DISTRO` permite selecionar outra distribuicao instalada.
- Runtime **.NET 9 para Linux**, instalado na distribuicao (`dotnet` no PATH), ou extraido em `Durango-CustomServer/server/bin/local-runtime-linux`. O runtime de testes ja preparado em `bin/test-runtime-linux` tambem pode ser reutilizado. Essas pastas de runtime ficam fora do controle de versao.
- As configuracoes de ambiente `DURANGO_ADMINS` e `DURANGO_ADMIN_TOKEN` sao repassadas ao WSL sem expor seus valores na linha de comando.

Em uma maquina sem esses requisitos, o script informa o problema. Ele nao instala distribuicoes nem baixa ou executa instaladores automaticamente. Use as instrucoes oficiais de [instalacao do WSL](https://learn.microsoft.com/windows/wsl/install) e [instalacao do .NET no Linux](https://learn.microsoft.com/dotnet/core/install/linux).

O acesso do cliente Windows ao servidor Linux por `localhost` segue o [funcionamento documentado do WSL](https://learn.microsoft.com/windows/wsl/networking).

## Validacao realizada

Em 03/10/2026, a compilação e a verificação do inicializador passaram. A integração passou 64 verificações no Windows e 61 no WSL, incluindo recusa de portas TCP ocupadas, conflito UDP no Windows e uso da mesma pasta de saves por duas instâncias, também entre Windows e WSL nos dois sentidos. Os arquivos de progresso mantiveram os mesmos hashes após as tentativas recusadas, e a instância original continuou respondendo. A instância local de validação foi encerrada com salvamento para liberar as portas do inicializador.

Em 30/09/2026, o BAT compilou sem erros e `--check` executou a DLL no Ubuntu-24.04 apos detectar o bloqueio nativo. Uma instancia isolada respondeu HTTP 200 em `/knock`, aceitou conexao TCP pelo Windows e encerrou com Ctrl+C, confirmando o salvamento e criando os arquivos `.world` e `.player` na pasta de teste. A selecao de uma distribuicao inexistente retornou codigo 1, sem falso sucesso.
