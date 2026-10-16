# APK 50225 — guia do mapa

Artefato: `dist/LostHorizon-alfa-gameplay-50225.apk`.
Base: `LostHorizon-conta-precarregada-50224.apk` (SHA256 `e616a8655041267944eb75d581c169855d2286a00f60da4be52e70380523a96a`).

O minimapa abre `WorldMap` pelo `MenuHelper.Open`. Apenas a tarefa do botão WorldMap passa a observar `MenuOpened`, com inscrição e remoção correspondentes. As demais tarefas conservam `MenuClicked`. A conclusão e remoção da indicação continuam no fluxo nativo.

O build conserva 1771 arquivos da base e modifica somente o manifesto, `libil2cpp.so` e o registro do build. Mantém o pré-carregamento de conta, os recursos, os diagnósticos, o atalho da Âncora e a chave de assinatura existente. Assinatura v1/v2/v3 e alinhamento de bibliotecas de 16 KB verificados.

Validação: 26 casos ARM64 nos clientes original e com a Âncora, cobrindo o botão do minimapa, argumentos da chamada, inscrição/remoção da tarefa do mapa, pilha e preservação dos demais menus. O artefato final também compara todos os arquivos com a base.

Reproduzir com `python android-client/tests/verify_guide_map.py` e `python android-client/build_gameplay_apk.py`, usando o Python e as ferramentas Android locais do projeto.

Pendente: em celular, chegar à instrução de olhar o mapa, tocar o minimapa e conferir que a mão desaparece e a orientação avança. As mudanças de fauna, coleta e cancelamento de ações exigem também o servidor atualizado.

SHA256 do APK: `c3efe2a01b36bbda1cff269d77d13f245f81f9641a4c9822e8833307eb42238c`.
