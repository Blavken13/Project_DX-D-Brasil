# Atributos aleatorios na coleta

A coleta criava os itens somente com as tags fixas do prototipo. O servidor nao tinha sorteio de propriedades neste caminho.

`GatheredItemProperties` agora aplica propriedades minor nativas ao concluir a coleta, depois de corrigir o nivel e validar espaco na mochila. O mesmo caminho atende recursos naturais, pesca e esquartejamento, incluindo itens extras da coleta premium. A resposta `Collected` e o inventario recebem os mesmos atributos.

Os grupos de materiais e probabilidades estao em `server/data/assets/item/gathered_properties_server.json`: chance base de 35%, chance de 25% de uma segunda propriedade quando ha mais de um grupo disponivel e intensidade de 1 a 3, limitada pelo nivel do item. Estes valores sao uma configuracao deste servidor; a tabela de probabilidades original nao esta disponivel. Habilidades/titulos `random_tag_mul` e ferramenta `random_tag_mul_on_tool` modificam a chance base.

Ossos, madeira, pedra, minerio e metal podem receber dureza, peso e densidade. Couro, fibras, penas, folhas e flores recebem propriedades de fibra/peso; alimentos recebem textura. Propriedades fixas e seus opostos sao protegidos. O sorteio nao concede tags major de processamento, como pureza elevada, nem gasta usos de processamento. Itens antigos nao sao sorteados novamente ao carregar o save.

Verificacao: `--gameplay-bugs-check` inclui sorteios deterministas em todos os grupos e niveis 1/10/60, persistencia, bonus, conflitos e uma amostra de 2000 ossos. `--fauna-check` verifica ossos coletados pelo protocolo TCP, com ferramenta e habilidade exigidas, no inventario e na resposta enviada ao cliente. `--polish-check` verifica niveis e cancelamento de coleta; `--premium-check` cobre coleta extra e capacidade.

Para aplicar ao jogo, encerre o servidor com Ctrl+C e aguarde o salvamento; depois execute `iniciar-servidor-local.bat`. Teste com novas coletas: a chance nao garante propriedade em todos os itens.
