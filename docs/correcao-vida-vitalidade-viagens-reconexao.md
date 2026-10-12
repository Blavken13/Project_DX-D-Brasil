# Continuidade de vida e vitalidade nas viagens e reconexões

## Problema e correção

O teste TCP reproduziu a perda de valores na reconexão antes da alteração.
`SurvivalState` reconstruía os medidores com um dicionário de bônus vazio tanto
em `PlayerContext.Initialize` quanto no construtor de `Player`. Os limites
básicos de saúde (300) e energia (100) cortavam os valores persistidos, incluindo
vida e stamina, antes do recálculo dos bônus de nível, atributos, título e clã.

A inicialização agora recupera os bônus dos limites estáticos já presentes nos
medidores, antes do primeiro `Rebuild`. O recálculo normal do personagem continua
autoritativo. O formato atual dos saves já contém esses limites; nenhum campo
novo ou migração manual é necessário. Os valores atuais são preservados, sem
preencher barras danificadas ou reviver personagens.

O aumento real de um bônus também materializa a curva antes de somar a diferença
e marca a alteração como pendente. Assim, a coleta dos valores antigos durante
`Rebuild` não sobrescreve o aumento. Reaplicar o mesmo bônus é uma operação sem
efeito e não duplica os valores.

## Validação local

Compilação `net9.0` concluída com zero erros e dois avisos preexistentes.
`git diff --check` passou.

| Suíte | Verificações aprovadas |
| --- | ---: |
| `--survival-continuity-check` | 14 |
| `--estate-return-check` | 88 |
| `--volcanic-food-check` | 73 |
| `--quest-reactivation-check` | 1.703 |
| Total | 1.878 |

A regressão de continuidade usa saves temporários e conexões TCP locais reais:
reconexão em memória, recarga do arquivo, primeiro `AppearPlayer` e viagem
`ri35te` → `ri55tu`. No cenário de nível 60, os valores iniciais foram mantidos:
vida 596,3, saúde 626,3, stamina/vitalidade 198,62 e energia 218,62; os limites
permaneceram 636,3 e 228,62. A comparação usa o início da curva enviada, permitindo
que a regeneração online normal continue depois da entrada.

As verificações de domínio exercitam retorno pelo protocolo, save/recarga,
chegada na Ilha Domada e Ilha Particular e teleporte na mesma ilha. Os casos de
barras abaixo dos limites básicos, personagem novo, personagem morto e aumento
ou reaplicação de bônus também passaram.

```powershell
dotnet build Durango-CustomServer/server/DurangoServer.csproj --no-restore
dotnet Durango-CustomServer/server/bin/Debug/net9.0/DurangoServer.dll --survival-continuity-check --data Durango-CustomServer/server/data
dotnet Durango-CustomServer/server/bin/Debug/net9.0/DurangoServer.dll --estate-return-check --data Durango-CustomServer/server/data
dotnet Durango-CustomServer/server/bin/Debug/net9.0/DurangoServer.dll --volcanic-food-check --data Durango-CustomServer/server/data
dotnet Durango-CustomServer/server/bin/Debug/net9.0/DurangoServer.dll --quest-reactivation-check --data Durango-CustomServer/server/data
```

## Publicação

Correção implementada e compilada na workspace. Ainda não publicada no servidor
online. A validação acima é local, sem alteração dos saves de jogadores reais.
