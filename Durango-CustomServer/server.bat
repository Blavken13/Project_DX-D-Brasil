Durango LastHuman
Servidor privado do jogo Durango: Wild Lands, criado a partir do código do servidor que a NEXON embutiu no próprio jogo.
Portado para .NET 9 e renomeado de Durango.Offline → Durango.Online, pois este projeto foca apenas na versão online.

Estrutura
text

server/          Servidor .NET 9
  Core/          Núcleo do servidor (Host, GameServer, Gateway, World, Player, ...)
  Support/       Shims para substituir o que está no Unity (Yaml/prototype/cor/tradução)
  GameCode/      Camada de protocolo + dados do jogo (Messages/ 852 TypeCodes idênticos ao original 100%)
  Shims/         Shim do UnityEngine (Mathf/Vector/Random/Debug)
  data/          terrain 14 mapas · assets (prototype/recipe/artifact/pet) · config.json
client/          Código-fonte do jogo (Assembly-CSharp) original da NEXON, 3.755 arquivos
game/            O jogo jogável de fato (não incluído no git — copiado de um pacote distribuído)
tools/           Scripts de build / iniciar servidor / escanear protocolo
docs/           ROADMAP.md · ROADMAP-NEXT.md · TODO.md · NEXON-SERVER-ARCHITECTURE.md · ITEM-SYSTEM-MAP.md · protocol-coverage.md
Como começar
Dê um duplo clique em เปิดเซิร์ฟ.bat (iniciar servidor) e escolha uma opção no menu:

Menu
O que faz
1	Inicia o servidor (gateway 8190 / jogo 8191)
2	Compila (build) novamente e inicia
3	Para o servidor
4	selftest — verifica o handshake (o servidor deve estar em execução)
5	Abre o jogo
6	Visualiza os logs recentes

Ou execute manualmente pela linha de comando:

bash

dotnet build .\server\DurangoServer.csproj -c Release
dotnet build client -c Release
powershell -File tools\build-client.ps1     # compila o código-fonte do jogo e coloca a DLL na pasta game\
Portas
Porta
Para que serve
8190	HTTP gateway — /knock /sessions /entry /players /terrains
8191	TCP game — handshake GetClock → Auth → Ready e entra no mundo

O arquivo game/server.txt deve apontar para 127.0.0.1:8190 (o padrão já está correto).

Se quiser permitir que outras máquinas (celular/amigos na mesma LAN) entrem, execute uma vez como Administrador:

bash

netsh http add urlacl url=http://*:8190/ user=Everyone
Caso contrário, o servidor fará um fallback para escutar apenas no loopback (o log avisará: wildcard bind denied, falling back to loopback).

Descobrindo o que falta no servidor
bash

python tools/scan-protocol.py          # Exibe o resumo na tela, dividido por sistemas
python tools/scan-protocol.py --md     # Gera o arquivo docs/protocol-coverage.md
O script lê da fonte real: Send(new X{..}) no código do jogo em comparação com Recv(delegate(X msg, ..)) no servidor.
Durante o jogo, o servidor também imprime no log: [conn] sem handler para type=NNNN.
Para descobrir o nome pelo número: grep -l "TypeCode = NNNN" server/GameCode/Messages/*.cs

Status
Servidor compila sem erros · selftest aprovado (handshake + entity + chunk streaming)
Código-fonte do jogo compila · o jogo abre normalmente
Recebe 42 tipos de mensagens das 391 que o jogo envia — para os próximos passos veja docs/ROADMAP.md
Próxima tarefa acordada: Sistema de navegação/arquipélago (é necessário refatorar o servidor para suportar múltiplas regiões primeiro)
Roteiro mais recente
Lista de tarefas: docs/TODO.md
Plano da próxima fase: docs/ROADMAP-NEXT.md
Arquitetura Nexon (referência): docs/NEXON-SERVER-ARCHITECTURE.md
Histórico/detalhes anteriores: docs/ROADMAP.md
Mapa do sistema de itens/nível/atributos/buff: docs/ITEM-SYSTEM-MAP.md

para iniciar o servidor:
dotnet run --project .\server\DurangoServer.csproj -c Release --no-build -- `
  --data ".\server\data" `
  --terrains ".\server\data\terrains" `
  --name "Durango Brasil" `
  --gateway-port 8190 `
  --game-port 8191 `
  --public-host 127.0.0.1

.

Faça backup do offserver.txt e gere um novo ID:

[guid]::NewGuid().ToString()


dotnet run --project ".\Durango-CustomServer\server\DurangoServer.csproj" -c Debug --no-build -- --name "Durango Brasil" --data ".\Durango-CustomServer\server\data" --terrains ".\Durango-CustomServer\server\data\terrains"