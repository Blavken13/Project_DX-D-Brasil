# Bancadas e expansão de domínios

As receitas publicadas para PC e Android incluem criação, processamento do material principal e melhorias de equipamento. A lista continua filtrada pelos nós de skill aprendidos e inclui as 81 recompensas de receita da tabela original de proficiência (`crafting_rewards_datas.json` + `crafting_rewards.json`). Receitas de evento sem uma dessas formas de desbloqueio não são liberadas automaticamente.

A proficiência usada neste servidor é o nível pesquisado da categoria, somado aos modificadores das skills, equipamentos vestidos e bônus do clã. O mesmo valor é enviado nas estatísticas e usado para desbloqueios e avaliação do craft. Aprender/remover uma skill, subir a categoria e equipar/retirar bônus atualiza as receitas durante a sessão. Os limiares das recompensas permanecem os dos dados originais.

Os requisitos de bancada são alternativas (fogueira **ou** forno para carvão), conforme a validação do cliente nativo. A auditoria verifica que cada receita com exigência de bancada possui uma estrutura com componente `Workbench` e tags de nível suficiente.

Processamento preserva o ID, nível e demais campos do material principal, modifica suas tags e consome os materiais adicionais. Materiais novos recebem três usos de processamento; equipamentos elegíveis recebem dois espaços de melhoria. Itens antigos que nunca foram processados são normalizados no carregamento, sem repor usos de itens já modificados. As melhorias usam os atributos e intervalos de `item/tech_support.json`, com nível do efeito proporcional ao nível do equipamento; os efeitos numéricos seguem as fórmulas de `tags.json`. Estimativa e conclusão compartilham a transformação. Os materiais ficam reservados até a conclusão; desconexão devolve os originais. A reavaliação tecnológica de um espaço já preenchido continua sendo um sistema separado.

Domínios pessoais/individuais podem adquirir até oito lotes, gratuitamente em qualquer nível de ilha. As licenças informam a franquia gratuita para que PC e Android habilitem a mesma expansão. Domínios existentes maiores não são reduzidos, não perdem direitos sobre suas células e não podem adquirir novos lotes enquanto estiverem no teto ou acima dele.

Os enclaves preservam o desbloqueio e limite de tamanho por nível de clã (`clan.json`): nível 5 libera 12 lotes, chegando a 72 no nível 25. A expansão é gratuita e mantém as verificações de cargo, continuidade e alcance. Taxas de declaração/manutenção não foram alteradas.

Validação: `--gameplay-bugs-check` (catálogo, todos os processamentos/melhorias, desbloqueios, bônus de equipamento, craft/estimativa TCP, consumo e persistência), `--polish-check` (expansão gratuita, teto de oito e save legado de 12 lotes), `--social-check` e `--progression-check`.

São alterações de servidor; estas regras não exigem recompilar o APK. Para testar localmente, reinicie com `iniciar-servidor-local.bat` e reconecte o personagem para renovar os dados da sessão.
