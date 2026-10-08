# Tempos de coleta e preparo de metais para testers

## Coleta

- Coletas que geram itens de nível 60 são 5 vezes mais rápidas: usam 20% do tempo normal, em qualquer ilha.
- A redução também vale para esquartejamento quando o item previsto é de nível 60.
- Vale o nível do item calculado pelo servidor no início da coleta, considerando o recurso, o protótipo e a habilidade. Um personagem ou uma ilha de nível 60 não aceleram itens de níveis menores.
- Os bônus de velocidade continuam aplicados. Após calcular o tempo normal, incluindo o mínimo original, o servidor multiplica por 0,2 somente para itens de nível 60.
- A prévia de coleta informa o tempo base reduzido; o pacote `Timer` e a entrega adiada usam a mesma duração final com os bônus.
- Itens de outros níveis conservam o tempo normal de coleta.

Exemplo: uma coleta que levaria 20 segundos passa a levar 4 segundos.

## Metais nas fornalhas

Receitas de `material_process / process_metal` que exigem `kiln` ou `furnace` levam exatamente 1 segundo. Isso inclui fundição, combinação de metais, variantes de refino, ligas, pregos e hastes metálicas.

O tempo é fixo, mesmo com bônus de velocidade. Os materiais são reservados ao iniciar e o produto só é entregue ao completar o segundo. A devolução dos materiais em caso de desconexão continua usando o fluxo existente de cancelamento.

## Validação e ativação

Os testes `--polish-check` e `--gameplay-bugs-check` verificam o tempo anunciado pelo protocolo TCP, a preservação dos tempos em níveis inferiores e a reserva de metais antes da conclusão, incluindo os produtos das ligas. A matriz de coleta inclui itens de níveis 1, 10, 20, 59 e 60 na mesma ilha de nível 60, conferindo a prévia, o temporizador e a entrega adiada.

Validação local em 7 de outubro de 2026: build Release concluído sem erros, `--polish-check` aprovado com 288 verificações, `--gameplay-bugs-check` aprovado e `--fauna-check` aprovado com 404 verificações. Os testes usam saves temporários.

Atualização em 8 de outubro de 2026: a coleta passou a usar 20% do tempo normal (velocidade 5x), restrita ao nível do item previsto no início. Build Release sem erros; `--polish-check` aprovado com 303 verificações, `--gameplay-bugs-check` aprovado e `--fauna-check` aprovado com 404 verificações. A execução dos testes usou o runtime Linux existente no WSL porque o Controle de Aplicativos do Windows bloqueou a DLL. Os testes usaram saves temporários.

O teste de cupons de XP usa `PlayerContext.LockedItemIds`, que contém os bloqueios persistentes do personagem, em vez de procurar por reflexão o antigo campo privado removido na atualização anterior. Isso permite validar o commit publicado diretamente, sem adaptar o teste em uma imagem separada. O teste corrigido passou 178 verificações em 8 de outubro de 2026.

As alterações são no servidor. É necessário executar a versão recompilada para que entrem em vigor; não é necessário alterar o cliente.
