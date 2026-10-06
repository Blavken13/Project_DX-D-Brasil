# Clique de expansão gratuita do enclave

O relato do tester corresponde a um bloqueio no cliente, antes do envio TCP.
`EstateGridGroup.ExpandEstate` retorna quando o custo diário da área seguinte é
zero. As tabelas brasileiras zeram tanto a manutenção quanto a compra; por isso
o botão exibe gratuidade, mas o diálogo antigo não chega a abrir. Os campos de
depósito da licença não resolvem esse segundo bloqueio. Os testes anteriores de
expansão enviavam o pedido diretamente e não cobriam essa decisão da interface.

A revisão Android 50217 redireciona a chamada de expansão para o caminho gratuito
original `ExpandPersonalEstate`, já usado pela ilha pessoal. Esse caminho envia
o mesmo `ExpandEstate`, converte a posição por quatro e atualiza a licença pelo
callback original. O limite recebido em `LargestClanEstateSize` habilita a
expansão até o limite do nível do clã. A alteração tem quatro bytes no
`libil2cpp.so`; identidade dos métodos, metadados e demais instruções permanecem
iguais. O PC recebe a mesma mudança de chamada por Mono.Cecil, com comparação de
todos os métodos restantes e preservação das correções do cliente atual.

Artefatos: `android-client/dist/LostHorizon-alfa-enclave1-50217.apk` e
`pc-client/dist/LostHorizon-PC-enclave1.zip`, acompanhados de SHA-256. O APK usa a
mesma assinatura do projeto e preserva os APKs anteriores. O pacote PC deve ser
extraído sobre a pasta do cliente com o jogo fechado.

Validação: `verify_estate_expansion.py` resolve os endereços reais pelo registro
IL2CPP e verifica a chamada, ABI, conversão, limite, envio e APK assinado. O
patcher PC recusa qualquer alteração de outro método. `--social-check` cobre
expansão real no enclave público, saldo pessoal zero, carteira e fundo
inalterados, recusa de jogador sem permissão e persistência após recarga. A
regressão de terrenos agora confere as tarifas reais zeradas, em vez de uma
fórmula antiga de manutenção.

Resultados: os seis verificadores do APK passaram (expansão, apresentação,
runtime, estabilidade mobile, dependências do tutorial e jangada/K); os testes
sociais passaram 96 verificações e as regressões gerais, 251. O patch PC passou
a comparação de todos os métodos, com alteração restrita ao clique de expansão.

O staging já aceita a expansão gratuita; a ativação desta correção exige instalar
o novo APK ou o patch PC e reconectar. Não há mudança de regra em produção no
servidor nesta revisão. Não foi realizado um toque real de expansão no celular
do tester; essa validação deve confirmar que uma célula adjacente livre é
incorporada ao enclave por um líder/oficial com permissão.
