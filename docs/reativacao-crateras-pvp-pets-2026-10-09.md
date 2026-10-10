# Reativação de crateras, PvP e assistência dos pets

Alterações no código local do servidor. Nenhum servidor em execução foi reiniciado ou atualizado.

## Crateras

Ao carregar a ilha, crateras do tipo 7037 existentes sem o estado `Crack` recuperam a interação de investimento e o custo original. Crateras ativas conservam estado e recursos; as que venceram enquanto o servidor estava desligado são fechadas no carregamento.

O fluxo de indução usa os parâmetros de `constants.json`: custo `max(1, int(nível * 0.2))`, investimento de quatro segundos e recursos temporários durante dez minutos. A cobrança ocorre ao concluir; interrupção, distância e concorrência continuam validadas. A expiração remove os recursos temporários e suas filas de renovação, permitindo novo investimento.

## PvP

A regra corresponde a `CombatSystem.IsPvPEnabled` e `Durango.Logic.Explore.Region.IsPvpIsland` do cliente original: `Role.Outpost`, ou `Role.Instance` com a tag `pvpisland`. Os terrenos disponíveis `op60te_alpha` e `op60tr_alpha` utilizam essa regra. Ilhas instáveis comuns, urbanas, domadas, tutorial e refúgio permanecem protegidas. O cliente conserva sua opção de habilitar PvP.

O servidor valida alvo vivo, mesma instância de ilha, grupo confirmado, mesmo clã e alianças. As relações são verificadas novamente no impacto, incluindo combos. Jogadores hostis oferecem a interação de ataque. Dano básico e habilidades usam o agendamento e a geometria de impacto das correções anteriores, com posição interpolada do alvo, armadura, precisão, esquiva e desgaste.

Os multiplicadores de `constants.json` são aplicados à curva de dano do servidor: `0.15 * 0.8` para melee e `0.15 * 0.35` para ranged. Essa integração com a curva atual é uma reconstrução local; o cálculo completo do servidor comercial original não está disponível.

## Dinos domados

O pet convocado acompanha o inimigo do dono, tanto ao atacar quanto ao entrar em combate por agressão. O servidor envia `BattleBegun`, movimento de perseguição, clipe nativo de ataque, dano atribuído ao pet e `BattleEnded` para devolver o controle ao acompanhamento do cliente.

O ataque usa os atributos derivados do próprio pet, sem consumir a arma do dono. O impacto é agendado na janela de 0,35 segundo usada atualmente pelo combate dos animais, ajustada pelo playback do pet; não ocorre ao iniciar a animação. No impacto são reavaliados alvo, alcance e deslocamento em relação à posição visada. Essa janela requer conferência visual no cliente para cada animação.

Respeita `is_fightable`, vida, fome, montagem, pastagem, recolhimento, saída do combate, morte do dono e distância máxima de acompanhamento de 12 tiles. Os limites de comida são lidos de `pet.battle`: 50% para começar e mais de 0% para continuar. Em PvP aplica as mesmas proteções sociais do dono. O catálogo possui 64 tipos aptos para combate com deslocamento e clipes nativos disponíveis.

## Validação

- Compilação `dotnet build ... --no-restore`: passou, com os dois avisos preexistentes (`SYSLIB0014` e `CS0169`).
- `--world-check`: 2.795 verificações passaram, incluindo investimento, reserva, persistência, expiração e reativação das crateras.
- `--combat-bags-check`: 2.399 verificações passaram.
- `--pet-feeding-check`: 87 verificações passaram.
- `--gameplay-check`: 58 verificações passaram.
- `--social-check`: 97 verificações passaram. O teste de retorno ao enclave foi atualizado para concluir o timer já existente no código local.
- `--world-combat-check`: a execução final passou 52 verificações de PvP, crateras e pets, incluindo expiração no carregamento, catálogo completo, ataque básico TCP e janela de esquiva. O bloqueio temporário do Windows observado durante a implementação não impediu a validação final.
- As correções posteriores de sobrevivência, lava e recursos minerais das crateras passaram 204 verificações adicionais. Detalhes em [correcoes-vulcao-crateras-2026-10-09.md](correcoes-vulcao-crateras-2026-10-09.md).

Os testes usam conexões TCP locais e saves temporários. Não houve validação visual no Android nem publicação destas alterações.
