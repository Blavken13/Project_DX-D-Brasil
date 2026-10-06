# Ajustes para os testers

- Atividades normais concedem 12 pontos de XP à habilidade correspondente. O teto por ação também é 12 (`skill.exp_increase_limit`). A fórmula de XP geral do personagem e os cupons de 10.000 XP permanecem independentes.
- Pesquisas, nível máximo 60 e coeficientes próprios de Defesa são preservados.
- Pedidos `DeclareEstate` com `ClanEstate` na Ilha Domada pública criam o enclave do clã, sem converter o pedido em domínio pessoal. Nível 5 do clã e autoridade de Líder/Oficial continuam necessários.
- Declaração, expansão e renovação de lotes pessoais e enclaves são gratuitas. As tabelas recebidas pelos clientes PC/Android também informam custo zero.
- Licença, construção, uso das instalações e retorno reconhecem o enclave público e preservam as permissões por cargo. Enclaves existentes na Ilha de Clã continuam funcionando.
- Capacidade da mochila soma base, premium e armazenamento das peças equipadas (`armor.bag_size`, bolsos naturais/melhorados e `modifiers.carry_capacity`). Uma peça repetida no save conta uma vez.
- Equipar, remover, reformar e mudar o premium atualizam a capacidade enviada ao cliente. Retirar um bônus preserva os itens já guardados; novas entradas respeitam a capacidade atual.

Validação: build e testes TCP `--progression-check`, `--social-check`, `--premium-check`, `--xp-coupon-check`, além das regressões de território, economia e fabricação. Os fluxos usam o protocolo comum aos clientes; esta validação não substitui um teste manual no celular e no PC.

As alterações exigem atualização/reinício do servidor e reconexão dos clientes para receber as tabelas. Não exigem novos binários dos clientes.
