# Interrupção indevida da primeira coleta e doma

O cliente original acumula os pacotes `Move` por aproximadamente 0,5 segundo (`MoveMsgGenerator`). A ação começa antes de o servidor receber o último trecho da caminhada de aproximação. O pacote atrasado combina essa caminhada com a pose da coleta ou captura.

`Player.HandleMoveMsg` comparava somente a posição anteriormente conhecida com o destino recebido. Uma correção atrasada dessa posição cancelava as ações pendentes, mesmo quando o deslocamento tinha ocorrido antes da ação. Isso explica por que a segunda coleta funcionava: a primeira tentativa já sincronizava a posição.

## Correção

- Registrar o início de cada ação pendente e conferir os horários das amostras de movimento, incluindo lotes com várias animações.
- Atualizar a posição e manter o histórico original para alcance, impactos e lava, sem cancelar por uma simples sincronização da pose.
- Usar `Depart`, enviado imediatamente pelo cliente para movimento manual, para interromper ações sem esperar o próximo lote. Preservar o tratamento existente do descanso ao encaixar o personagem em um abrigo.
- Detectar deslocamentos novos dentro do lote, inclusive caminhar e retornar à mesma posição, e mudanças posteriores à sincronização mesmo com uma animação estacionária.
- Aplicar o cooldown de captura apenas depois de uma tentativa concluída. Uma interrupção libera a reserva do animal e permite tentar novamente com a mesma ferramenta. Uma captura concluída continua impondo o cooldown normal.

## Validação

Uma compilação isolada com o `Player.cs` anterior e os novos testes reproduziu duas falhas esperadas: primeira doma e primeira coleta de minério após a chegada atrasada. Não foram usados saves de jogadores.

A versão corrigida passou por testes TCP/MessagePack de primeira coleta e primeira doma, conclusão sem duplicação de itens, reserva do animal, preservação da ferramenta, cancelamento por movimento real, retomada imediata de captura interrompida e cooldown de tentativa concluída.

Também foram executadas as suites `reported-gameplay`, `gameplay`, `volcano-crater`, `combat-bags` e `world-combat`. A compilação local passou com os dois avisos preexistentes (`SYSLIB0014` e `CS0169`).
