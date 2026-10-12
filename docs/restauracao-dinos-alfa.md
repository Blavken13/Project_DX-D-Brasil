# Restauração dos encontros Alfa

As variantes originais abaixo foram ativadas na geração de fauna do servidor. Cada ilha recebe um exemplar de cada encontro configurado, dentro da população total de 40 animais. As espécies comuns continuam presentes. Os encontros nascem em terra, fora da área dos portos, no nível 60.

| Criatura | Tipo original | Ilha disponível | Vida no nível 60 |
|---|---|---|---|
| Compsognathus Alfa | 2108 / compsognathus_elite | op60te_alpha, temperada | 17.640 |
| Estegossauro Alfa | 2112 / stegosaurus_elite | op60te_alpha, temperada | 17.640 |
| Anquilossauro Alfa | 2125 / ankylosaurus_elite | ua60de_alpha, deserto | 17.640 |
| Mamute Alfa | 2109 / mammoth_elite | ua60sn_alpha, neve | 17.640 |
| Tarbossauro Alfa tropical | 2115 / tarbosaurus_elite | ua60tr_alpha, tropical | 200.000 |
| Tarbossauro Alfa nevado | 2113 / tarbosaurus_snowy_elite | ua60sn_alpha, neve | 200.000 |

O Braquiossauro comum (2004 / brachio) já aparece em `op60tr_alpha`, no nível 60, com aproximadamente 48.926 HP calculados pela fórmula original. O Tarbossauro nevado comum (2089) continua disponível junto ao novo encontro Alfa.

O Compsognathus utiliza a variante Alfa existente, conforme solicitado: modelo `Compso/CompsognathusPrefab`, escala original 0,4. A restauração não cria uma versão gigante. Não foi localizado um modelo ou tipo de Megatherium/Preguiça-Gigante nos assets disponíveis; essa criatura permanece pendente.

Os modelos, escalas, animações, ataque, defesa e regras de doma vêm do catálogo original. As seis variantes Alfa são indomáveis e usam a coleta de carcaças existente. A vida dos Tarbossauros é um ajuste explícito do nosso servidor para 200.000 HP; não é uma confirmação de balanceamento histórico. Os demais Alfa usam suas fórmulas originais. O reposicionamento nos mapas disponíveis também é uma configuração do nosso servidor.

A implementação restaura os encontros com o sistema de combate atual. Não acrescenta ataques em área, congelamento, lentidão ou sangramento específicos de chefes. A carcaça desaparece após quatro minutos e o animal reaparece após cinco minutos, com seus atributos restaurados. Reabrir a ilha gera novamente um único exemplar de cada variante configurada.

## Arquivos

- `Durango-CustomServer/server/data/restored-animal-encounters.json`: distribuição por template e ajuste de HP dos Tarbossauros; copiado pelo build/publish.
- `Support/RestoredAnimalEncounters.cs`: carregamento e validação do catálogo de encontros.
- `Support/RegionCatalog.cs`: associação aos templates do servidor.
- `Core/AnimalManager.cs`: reserva dos encontros na população, atributos e uso do ciclo existente de respawn.
- `RestoredEncountersCheck.cs`: geração repetida, unicidade, nível, terreno, portos, animações, pacote de vida, coleta, descarte e respawn.
- `Durango-CustomServer/server/tests/check-restored-animal-assets.py`: verificação dos modelos Android originais dos encontros.

## Validação local

Compilação concluída sem erros, com dois avisos já existentes. Passaram 636 verificações:

```powershell
dotnet build Durango-CustomServer/server/DurangoServer.csproj --no-restore -v quiet
dotnet Durango-CustomServer/server/bin/Debug/net9.0/DurangoServer.dll --restored-encounters-check --data Durango-CustomServer/server/data
# PASS 166
dotnet Durango-CustomServer/server/bin/Debug/net9.0/DurangoServer.dll --fauna-check --data Durango-CustomServer/server/data
# PASS 404, incluindo coleta pelo protocolo TCP
dotnet Durango-CustomServer/server/bin/Debug/net9.0/DurangoServer.dll --world-combat-check --data Durango-CustomServer/server/data
# PASS 52
dotnet Durango-CustomServer/server/bin/Debug/net9.0/DurangoServer.dll --survival-continuity-check --data Durango-CustomServer/server/data
# PASS 14
python Durango-CustomServer/server/tests/check-restored-animal-assets.py
# PASS 7 modelos Android presentes com cabeçalho UnityFS
```

Logs dos encontros e da fauna: `docs/restored-encounters-check.log` e `docs/fauna-restored-encounters-check.log`.

As alterações estão implementadas e testadas no projeto local. Não foi realizado deploy no servidor online nem teste visual no celular nesta etapa.
