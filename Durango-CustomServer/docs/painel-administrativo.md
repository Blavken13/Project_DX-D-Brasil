# Painel administrativo

Acesse `http://127.0.0.1:8190/admin/` no servidor local, ou `/admin/` na porta do gateway do servidor remoto. As rotas `/admin` e `/admin/` abrem a mesma interface.

O usuário configurado é `blavken13`. A senha definida pelo responsável fica somente como hash PBKDF2-SHA256 em `server/data/admin-auth.json`; não é incluída no JavaScript. As sessões duram oito horas, são revogadas pelo logout e pelo reinício do servidor. O login aceita até cinco tentativas por minuto por IP. As APIs administrativas e `/health` exigem uma sessão; não há acesso implícito por localhost. Ferramentas existentes podem usar um token explicitamente configurado com `DURANGO_ADMIN_TOKEN`, pelo cabeçalho `X-Admin-Token`.

`server/data/admin-auth.json` é configuração privada, ignorada pelo Git. Em um novo checkout ou deploy, provisione esse arquivo separadamente, preservando o hash configurado. Sem credenciais válidas, o servidor mantém o login administrativo bloqueado.

O painel apresenta saúde do servidor, personagens dos saves (online e offline), banimentos, saldos de T Stone, Warp Gem e Durango Coin, configurações globais e individuais das ilhas, dados JSON disponíveis, anúncios e manutenção. O inventário exclui as credenciais administrativas. Os terrenos e o catálogo Android são identificados pela disponibilidade real; um catálogo Android presente pode conter recursos incompletos.

Em **Operações**, o envio de email aceita JSON com destinatário, título, mensagem e anexos identificados por nome ou protótipo. A prévia apresenta os itens resolvidos e a quantidade de destinatários. O correio entrega a mensagem no jogo e permite resgatar os anexos na mochila, inclusive após um período offline. Consulte [Correio e anexos](correio-administrativo.md) para exemplos, categorias e regras de resgate.

As cinco ilhas de `islands.json` são configurações, não uma confirmação de servidores ativos. O catálogo de terrenos e regiões também inclui outros mapas do jogo. Salvar o catálogo ou regras de uma ilha não inicia processos nem reinicia mundos. A recarga aplica as configurações suportadas em execução; alterações de inicialização exigem reinício.

## Execução local

```powershell
dotnet run --project Durango-CustomServer/server/DurangoServer.csproj -- --name "Durango Brasil-local" --storage-key "Durango Brasil" --data Durango-CustomServer/server/data --terrains Durango-CustomServer/server/data/terrains
```

O inicializador local existente também pode ser usado. Execute apenas uma instância nas portas 8190/8191. Encerre o processo com Ctrl+C para salvar o progresso. A senha administrativa não concede privilégios de administrador a personagens dentro do jogo; esses privilégios continuam sendo configurados por `DURANGO_ADMINS`.

## Publicação e persistência

O projeto copia a interface e todos os JSON/TXT de `data/` para a compilação e a publicação. O Docker Compose de staging define `DURANGO_ADMIN_CONFIG_DIR=/app/AppData-nx/admin-config`, dentro do volume de estado existente. Alterações salvas pelo painel em `config.json`, `islands.json` e configurações individuais são persistidas nessa pasta e restauradas antes do carregamento do jogo. As tabelas do jogo continuam sendo atualizadas pela imagem. Sem essa variável, as alterações ficam nos arquivos da pasta de dados utilizada.

Uma atualização remota exige compilar a nova imagem e recriar o serviço com o mesmo volume de estado e a mesma chave `Durango Brasil`. Preserve os procedimentos existentes de backup e encerramento com SIGINT. Para acesso pela internet, use HTTPS no proxy e mantenha `/admin/` e as APIs administrativas na mesma origem.

## Validação

```powershell
dotnet build Durango-CustomServer/server/DurangoServer.csproj -c Debug
node Durango-CustomServer/server/tests/admin-panel-check.mjs
```

O teste inicia o servidor real com portas livres, dados copiados e saves temporários. Verifica login, bloqueios, revogação da sessão, limitação de tentativas, consulta de dados, arquivos protegidos, editores, saldo, anúncios, manutenção, recarga e persistência após reinício. Não usa os saves do cluster de jogo.

Se o Windows bloquear a DLL com `0x800711C7`, use o WSL já instalado e o runtime Linux em `server/bin/test-runtime-linux`:

```powershell
$env:DURANGO_ADMIN_TEST_WSL = 'Ubuntu-24.04'
node Durango-CustomServer/server/tests/admin-panel-check.mjs
```
