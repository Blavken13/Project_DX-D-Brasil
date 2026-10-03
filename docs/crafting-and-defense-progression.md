# Progressão de craft e Defesa

Os limites originais das receitas, prototypes e plantas foram mantidos. Materiais de nível alto não tornam uma receita básica equivalente à versão avançada: é preciso aprender a habilidade que libera a próxima versão.

## Craft

| Família | Versão | Nível máximo do produto | Categoria exigida para aprender |
| --- | --- | --- | --- |
| Mesa de trabalho | Básica | 19 | Construção 1 |
| Mesa de trabalho | Segunda | 39 | Construção 15 |
| Mesa de trabalho | Terceira | 59 | Construção 40 |
| Mesa de trabalho | Quarta | 60 | Construção 60 |
| Arpão | Madeira | 24 | Fabricação de armas 3 |
| Arpão | Osso | 44 | Fabricação de armas 25 |
| Arpão | Metal | 60 | Fabricação de armas 45 |

A faca de pedra básica continua limitada ao nível 19. A receita seguinte, `blade_sword_stone_01`, permite produto até o nível 39 e depende do aprendizado correspondente em Fabricação de armas 20.

Correções aplicadas:

- `WorkbenchTags` aceita a chave original tailandesa `โต๊ะ` e a chave traduzida `Bancada`. Antes, os dados originais não eram carregados, deixando as bancadas sem os requisitos necessários para diversas receitas.
- Aprender, retirar ou receber habilidades atualiza tanto `Recipes` quanto `ArtifactBlueprints` durante a sessão. As mesas superiores deixam de depender de reconectar para aparecer no cliente.
- Ganhar EXP atualiza o pacote `Skills`, com nível, barra de experiência e pesquisa. `SkillCategoryExperienced` sozinho exibia apenas o indicador visual.

Os 720 registros de receitas foram auditados: as 72 bancadas carregadas fornecem todos os 19 tipos de tags exigidos. A auditoria de coleta também passou a conferir o prototype entregue, em vez de confundir seu ID com o ID de gerador usado pelo cliente (`reed` gera o item `stem`). Essa alteração afeta apenas a verificação, sem mudar a coleta.

## Defesa

As ações nativas de esquiva sem arma, com uma mão e com duas mãos possuem `defense_info`, que antes era ignorado pelo servidor. A janela de defesa agora usa o relógio do servidor e os parâmetros dessas ações: duração ativa de 1,2 segundo, custo de 35 de vigor e recarga de 7 segundos.

- Esquivar de um golpe durante essa janela aplica o fator nativo de EXP da ação, 2, uma vez por ação. Apenas apertar esquiva sem evitar um ataque não concede EXP.
- Sofrer um impacto real aplica o fator `damageable_exp.hit_factor`, 0,1. As frações são salvas entre sessões; dez impactos acumulam um ponto na configuração atual.
- O caminho já existente de defesa automática usa `auto_defense_factor`, 1. O PvP continua com a configuração anterior.
- Os fatores usam a unidade de EXP por ação já adotada pelo servidor, 1, e respeitam seu limite comum de EXP, os níveis e as pesquisas existentes. Esses fatores foram extraídos dos arquivos originais; a unidade de EXP é o balanceamento atual do servidor brasileiro.
- Uma esquiva interrompida, expirada ou repetida durante a recarga não renova a proteção. Morte, saída do combate e reconexão encerram a janela. A verificação de distância impede premiar ataques remotos.

## Validação

`--progression-check` executa 35 verificações com troca real de mensagens TCP, aprendizado de habilidades, produtos de craft, requisitos de bancada, impactos, recarga, EXP, pesquisa e persistência. Usa saves temporários.

Também passaram os testes de gameplay, recompensas de missões, economia, polimento e mapas. A compilação Release foi concluída. As verificações de execução usam o runtime .NET no WSL, alternativa já prevista pelo projeto para o bloqueio de DLL pelo controle de aplicativos do Windows. Ainda cabe validar a apresentação das ações e dos desbloqueios nos clientes durante o jogo.

Relatos contemporâneos usados para contextualizar a progressão, em conjunto com os arquivos originais do jogo:

- [Discussão de níveis dos materiais e produtos](https://www.reddit.com/r/DurangoWildLands/comments/c5wplj/)
- [Discussão de experiência em Defesa](https://www.reddit.com/r/DurangoWildLands/comments/cepxus/)
