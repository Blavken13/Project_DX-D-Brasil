# Expansão de domínio no Android

## Causa

O PC possui uma alteração em `Durango.UI.EstateGridGroup.OnExpandEstateClick` que envia `EstateSystem.ExpandEstate` diretamente para domínios `OwnerType.Player`. O APK original não possui esse desvio: chama o diálogo legado `ExpandEstate` para esse tipo de domínio. O limite e a mensagem de expansão gratuita são montados antes desse diálogo, portanto aparecem mesmo quando o toque não gera um pedido ao servidor.

O diálogo retorna silenciosamente quando `EstateLicense.Deposit` ou `DepositRunsOutAt` está ausente. A ausência desses campos em `World.ToLicense` explica a diferença. A inspeção de `libil2cpp.so` arm64 confirmou o caminho: `OnExpandEstateClick` em `0x139bf14` desvia somente `PersonalPlayer` (tipo 4); os demais chamam `ExpandEstate` em `0x139c108`. Nesse método há dois retornos por campos opcionais ausentes antes da confirmação. A manutenção calculada pelo cliente também precisa ser positiva; alterar os custos legados para zero impediria novamente o envio.

## Correção no servidor

`World.ToLicense` agora fornece os campos de depósito para `Player` e `ClanEstate`. O crédito enviado é exclusivamente de compatibilidade com o diálogo legado (`int.MaxValue`, atualizado no instante da licença), suficiente para sua validação local. Não é saldo de jogador/clã: não é persistido, cobrado ou creditado nas carteiras. A data enviada usa o vencimento real do território quando houver, com prazo de compatibilidade quando não houver vencimento. Os dados são enviados também na resposta de expansão e nas grades de domínio, permitindo os próximos toques.

As regras de expansão não mudam: gratuita, limite pessoal de oito lotes, preservação de domínios antigos maiores, permissões de proprietário e regras de clã por nível. Domínios `PersonalPlayer` mantêm seu caminho próprio, sem depósito.

## Validação

- Build Release passou.
- `--polish-check`: 177 verificações. Inclui recebimento dos campos pela serialização TCP, validação equivalente à confirmação Android com saldo zero, campos preservados na resposta, expansões até oito, rejeição do nono e conservação dos lotes legados.
- `--social-check`: 81 verificações; permissões, expansão e persistência do enclave preservadas.
- Não houve teste visual com um celular nesta etapa.

É necessário reiniciar/publicar o servidor atualizado e reconectar o celular para obter as novas licenças. Esta correção não exige outro APK. O diálogo legado ainda pode mostrar informação de manutenção; a cobrança de expansão continua zero no servidor.
