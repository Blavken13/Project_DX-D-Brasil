# Correções de receitas, recursos e descobertas — 06/10/2026

## Comportamento corrigido

- **Bolinho de carne:** a receita `meatball_01` recusa o próprio produto, inclusive quando modificado. Dois ingredientes de carne, peixe ou um de cada continuam produzindo dois bolinhos. A recusa acontece antes de reservar materiais ou conceder experiência. A tag `meat` do alimento foi preservada para outras receitas.
- **Spots parcialmente coletados:** a primeira coleta agenda renovação em todas as ilhas, usando o tempo já configurado: `NaturalRegrowSeconds` para ilhas comuns e `TutorialNaturalRegrowSeconds` para o tutorial. Não é necessário colher as folhas restantes para restaurar o caule. Saves antigos com coleta parcial e sem fila recebem a fila ao carregar. A renovação mantém tipo, coordenadas e proteção contra conclusão de coletas anteriores à renovação.
- **Recursos ausentes:** as variantes de junco recebem caule; rio e lago usam respectivamente `stem` e `stem_tough`, independentemente da tradução. Spots `dump_*` recebem o esterco `dump` existente no jogo. Todos os tipos coletáveis dos terrenos disponíveis foram auditados; portais e barreiras não são recursos de coleta.
- **Níveis:** o toque informa o nível real do recurso. Postos avançados, como `op60te_alpha`, usam o nível da ilha em vez dos overrides antigos de nível 1. O nível dos itens entregues continua limitado pela habilidade do personagem e pelo prototype.
- **Guia de carreiras:** sete guias básicos tinham `hints: []`, deixando a ajuda textual vazia quando a imagem não carregava. Essas sete listas foram preenchidas em português. PC e Android mostram diretamente as dicas textuais do guia, sem depender das imagens e suas texturas. As dicas e os demais campos das outras 51 carreiras foram preservados. Os outros menus com CardNews mantêm seu fluxo de imagens. Não altera recompensas ou a seleção de carreiras.
- **Dinossauros descobertos:** espécies são salvas por personagem e template de ilha. A descoberta exige um animal real, próximo e pertencente à fauna do template. Consultas do mapa retornam os retratos descobertos e a taxa correspondente; respawn e reconexão não apagam o registro. PC e Android atualizam o cache nas consultas ao entrar na ilha e abrir as descobertas na navegação.

As notificações antigas não eram persistidas. Depois da atualização, o personagem precisa reencontrar essas espécies uma vez para gerar o registro permanente. Não há dados confiáveis para reconstruir as descobertas antigas.

## Validação

- `--tester-bugs-check`: **106 verificações**, incluindo carne/peixe, recusa sem XP ou consumo, variantes de junco, renovação após restart, migração de saves, auditoria de todos os terrenos disponíveis, descobertas completas após reconexão e isolamento por personagem.
- `--gameplay-bugs-check`: **PASS**, cobrindo receitas, bancadas, descarte, construções e respawn.
- `--progression-check`: **104 verificações** de fabricação e Defesa.
- `--gameplay-check`: **58 verificações**, incluindo tutorial e coleta pendente durante renovação.
- PC: comparação de todos os corpos de métodos; somente `MapSystem.OnReady`, `MapSystem.GetDiscoveryInfos` e `LearningGuideGroup.ShowHintPopup(Advice)` mudaram.
- Android: três instruções ARM64 de quatro bytes; todos os demais bytes da biblioteca preservados. Entradas inesperadas são recusadas.
- APK: assinatura v1/v2/v3 e alinhamento de 16 KiB verificados. Além da biblioteca e da versão no manifesto, os **1.770 arquivos** do APK anterior foram preservados, com os mesmos modos de compressão. Conferência dos conteúdos repetida após assinar.

Compilação .NET 9 concluída; dois avisos preexistentes. Ainda não foi feito teste visual em um aparelho ou publicação no servidor público.

Um comando com nome incorreto de teste tentou inicializar a instância local antes de falhar na abertura da rede. A regravação dos quatro personagens foi revertida usando os backups daquela operação e verificada por SHA-256. O mundo não teve alteração de conteúdo. Os comandos corretos de teste usam saves temporários.

## Arquivos de atualização

- Android: `android-client/dist/LostHorizon-alfa-gamefix1-50218.apk`, versão `5.2.1-losthorizon-alfa-gamefix1`, com o SHA-256 ao lado.
- PC: `pc-client/dist/LostHorizon-PC-descobertas.zip`, com o SHA-256 ao lado. Feche o jogo e extraia sobre `Durango-OffServer`.
- Servidor: `Durango-CustomServer/dist/LostHorizon-servidor-gamefix1.zip`. Contém os arquivos executáveis publicados e `data/assets/advices.json`; não contém saves, contas ou configurações do ambiente. Aplicar na pasta publicada do servidor, com a instância encerrada normalmente para concluir os saves.

As correções de receita, renovação, níveis e persistência dependem do servidor atualizado. As correções de cache dependem também do cliente atualizado. Os pacotes foram preparados localmente; não foram publicados nem instalados automaticamente.

## Reproduzir os testes

```powershell
dotnet build Durango-CustomServer/server/DurangoServer.csproj -c Release --no-restore
dotnet Durango-CustomServer/server/bin/Release/net9.0/DurangoServer.dll --tester-bugs-check --data Durango-CustomServer/server/data
dotnet Durango-CustomServer/server/bin/Release/net9.0/DurangoServer.dll --gameplay-bugs-check --data Durango-CustomServer/server/data
dotnet Durango-CustomServer/server/bin/Release/net9.0/DurangoServer.dll --progression-check --data Durango-CustomServer/server/data
dotnet Durango-CustomServer/server/bin/Release/net9.0/DurangoServer.dll --gameplay-check --data Durango-CustomServer/server/data
```

O fluxo Android em `build_original_apk.py` inclui a correção `discovery_original.py` e sua verificação. O patch PC pode ser reproduzido sem modificar o cliente instalado:

```powershell
dotnet build pc-client/Patcher/Patcher.csproj -c Release --no-restore
dotnet pc-client/Patcher/bin/Release/net9.0/Patcher.dll --gameplay-ui Durango-OffServer/DurangoV2_Data/Managed/Assembly-CSharp.dll pc-client/work/Assembly-CSharp.discovery.dll
```
