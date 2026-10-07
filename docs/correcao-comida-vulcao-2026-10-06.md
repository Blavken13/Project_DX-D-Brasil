# Comida e vida na ilha Vulcão Mar nível 60

## Causa e correção

O efeito original `volcanic_storm` reduz `health` em 10 unidades por segundo, descontada a regeneração base. `health` limita a vida. A curva de vida era calculada usando apenas o início e o fim de um horizonte de 600 segundos, mesmo quando a saúde esgotava muito antes. Isso deixava a vida acima da saúde máxima. Comer, descansar ou realizar outra ação que reenviava as barras cortava a diferença de uma vez.

Reprodução anterior à correção, com vida e saúde inicialmente em 300: após 10 segundos, saúde 200,012 e vida 295; recalcular as barras mudava a vida instantaneamente para 200,012. Depois da correção, a vida acompanha continuamente o teto de saúde e permanece em 200,012 antes e depois da atualização nesse instante.

`Core/SurvivalState.cs` agora preserva os trechos da curva da barra que define o teto, inclusive quando atinge seu piso ou máximo. A mesma regra atende stamina/energia. O dano da tempestade, os efeitos dos alimentos e os valores dos debuffs não foram alterados.

## Validação

- `--volcanic-food-check`: 73 verificações; continuidade durante tempestade, restauração repetida de energia, vida abaixo do teto, descanso, dano direto, stamina e desconexão. Consumo de alimento pelo protocolo TCP nas ilhas vulcânica nível 60, deserto nível 60, floresta temperada nível 60 e ilha pessoal. Na vulcânica, cinzas, prenúncio e tempestade foram exercitados separadamente.
- `--tester-bugs-check`: 106 verificações dos ajustes anteriores.
- `--gameplay-check`: 58 verificações de combate, captura, descanso e renovação.
- `--progression-check`: 104 verificações de progressão e Defesa.
- `--gameplay-bugs-check`: aprovado, incluindo comida preparada e crafting.
- `--se-check`: 45 verificações dos efeitos de status.
- Compilação Release .NET 9 concluída; dois avisos preexistentes.

Os testes usam saves temporários. Ainda não foi feita validação visual dentro do jogo nem implantação no servidor público.

## Pacote

`Durango-CustomServer/dist/LostHorizon-servidor-gamefix2.zip`, acompanhado de SHA-256, inclui esta correção e os ajustes anteriores de receita, recursos, níveis, dicas e descobertas. Contém executáveis, dependências e `data/assets/advices.json`; não contém contas, saves ou configurações do ambiente.

Aplicar na pasta publicada do servidor com a instância encerrada normalmente para concluir os saves. Os clientes de gamefix1 continuam compatíveis com esta correção. O pacote foi preparado localmente.

Para reproduzir o teste específico:

```powershell
dotnet build Durango-CustomServer/server/DurangoServer.csproj -c Release --no-restore
dotnet Durango-CustomServer/server/bin/Release/net9.0/DurangoServer.dll --volcanic-food-check --data Durango-CustomServer/server/data
```
