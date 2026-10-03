# Durango Brasil — servidor e cliente OffServer

Servidor personalizado do Durango Brasil, com cliente de PC, recursos Android e painel administrativo em português.

## Endereço do servidor

Os endereços abaixo seguem o arquivo [`Durango-OffServer/offserver.txt`](../Durango-OffServer/offserver.txt) usado pelo cliente:

| Ambiente | Gateway | Nome no cliente |
| --- | --- | --- |
| Servidor público | `http://179.197.72.129:8190` | Durango Brasil |
| Desenvolvimento local | `http://127.0.0.1:8190` | Durango Brasil-local |

O cliente também usa a porta TCP **8191** para entrar no mundo do jogo. A chave de armazenamento do progresso é **Durango Brasil**.

## Como jogar

1. Extraia o cliente em uma pasta onde seu usuário possa gravar arquivos.
2. Na pasta `Durango-OffServer`, abra **DurangoBrasil.exe**. O iniciador alternativo **LostHorizon.cmd** usa a implementação em PowerShell disponível no projeto.
3. Escolha o servidor **Durango Brasil**, entre na sua conta ou faça o cadastro e crie seu personagem no primeiro acesso.

O iniciador mantém a seleção de servidor e oferece **Continuar** ou **Trocar de conta**. A sessão local fica criptografada e vinculada ao usuário do Windows e ao gateway; a senha não é salva. Veja os detalhes em [Cliente PC](../pc-client/README.md).

## Atualizações e offserver.txt

O iniciador consulta as atualizações publicadas pelo servidor. Quando uma atualização estiver disponível, siga a opção apresentada pelo iniciador.

`offserver.txt` define os gateways e os nomes exibidos na seleção. O projeto inclui o servidor público e a opção local. O endereço público atual é **179.197.72.129:8190**, substituindo o endereço antigo da documentação. A ausência desse arquivo permite o fluxo offline original do cliente.

## Conta e troca de computador

Use o login e a senha da sua conta no novo computador para acessar os personagens do mesmo servidor. O identificador da conta, sozinho, não autentica o jogador nem permite assumir personagens de outra conta. Não compartilhe credenciais ou tokens de sessão.

## Painel administrativo

- [Painel do servidor público](http://179.197.72.129:8190/admin/)
- [Painel local](http://127.0.0.1:8190/admin/)

O usuário administrativo configurado é **blavken13**, com a senha definida pelo responsável. A senha não é documentada em texto aberto. O painel apresenta jogadores, economia, configurações, ilhas, dados do jogo e operações. O link público exige que a versão atualizada do servidor esteja publicada no VPS.

Na aba **Operações**, o administrador pode enviar emails no jogo para um personagem ou para todos os personagens existentes, incluindo jogadores offline. O conteúdo e os anexos são informados em JSON. A prévia valida os itens antes do envio; ao abrir a mensagem no jogo e clicar em **Resgatar/Receber**, o jogador recebe os anexos na mochila. Se faltar espaço, a mensagem continua disponível para resgate posterior.

Veja [Painel administrativo](docs/painel-administrativo.md) e [Correio e anexos](docs/correio-administrativo.md) para o formato JSON, persistência, categorias e testes.

## Executar o servidor local

O projeto requer o SDK .NET 9 para compilar:

```powershell
dotnet run --project Durango-CustomServer/server/DurangoServer.csproj -- --name "Durango Brasil-local" --storage-key "Durango Brasil" --data Durango-CustomServer/server/data --terrains Durango-CustomServer/server/data/terrains
```

Execute esse comando na raiz do repositório, com as portas 8190 e 8191 livres. O inicializador local existente também pode ser usado. Encerre com **Ctrl+C** para salvar o progresso. Consulte [Servidor local](docs/servidor-local.md) para o caminho WSL quando o Windows bloquear a DLL.

Os saves ficam em `server/AppData-nx/offline/Durango Brasil`. O Docker Compose em `deploy/staging/compose.yml` mantém o estado em um volume persistente. A atualização remota deve preservar esse volume e a chave de armazenamento.

## Problemas frequentes

- **O iniciador não abre:** consulte `Durango-OffServer/Launcher/launcher-error.log`, o `player.log` do jogo e o log do iniciador em `%TEMP%` quando disponível.
- **O Windows bloqueia o executável:** use o iniciador alternativo documentado em [Cliente PC](../pc-client/README.md), mantendo as políticas de segurança do sistema.
- **Não entra no mundo ou fica carregando:** confira o servidor selecionado, a disponibilidade do gateway e das portas do jogo e os logs do servidor.
- **Porta já em uso / erro 10048 ao iniciar:** encerre a instância anterior com **Ctrl+C** e aguarde o salvamento. O servidor recusa inicializações duplicadas antes de carregar o progresso; consulte [Servidor local](docs/servidor-local.md).
- **Email sem espaço para os itens:** libere espaço na mochila e tente resgatar novamente. O resgate repetido de uma entrega já aceita não duplica os itens.
