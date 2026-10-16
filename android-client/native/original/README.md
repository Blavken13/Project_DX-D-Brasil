# Pontes do cliente Unity 2017

A revisão de compatibilidade 50208 compila `runtime_compat.c` como `libnd.so`,
com `runtime_math.h` e `runtime_compat.h`. A ponte tem código-fonte próprio,
alinhamento ELF/RELRO de 16 KB e usa o tamanho de página informado pelo sistema.
`CompatGameActivity` chama a preparação antes de criar UnityPlayer. Não há
worker alterando instruções enquanto o motor inicia nem sleeps para esperar o VM.
As APIs gerenciadas da autenticação são resolvidas na primeira chamada Send,
já no fluxo gerenciado do jogo. Apenas `/accounts` e `/sessions` da origem
brasileira recebem o campo token; headers de jogo e demais rotas são preservados.

Os instaladores do tutorial compartilham o trampolim validado da ponte própria.
Referências da K e chamadas do tutorial continuam no Update do motor. A instalação
recusa prólogos inesperados, instruções PC-relative não suportadas e falhas de
proteção. Falhas da autenticação impedem a entrada e geram diagnóstico; falhas
opcionais preservam o fluxo original quando possível.

O perfil de compatibilidade aplica 30 FPS e resolução reduzida quando as icalls
originais estão disponíveis. Na revisão 50209, a supressão do vídeo é uma opção
independente, desativada por padrão, passada como quarto argumento de prepare.
Somente o componente de mídia da seleção pode ser suprimido; o player do prólogo
e dos mapas é preservado. A configuração serializada de MTRendering é desativada
por uma alteração de um único byte no build.

`libunity.so` e `libmain.so` originais continuam com segmentos de 4 KB. O manifesto
habilita pageSizeCompat do Android 16; não há certificação de suporte nativo
completo a 16 KB para o motor antigo nem validação no POCO X6 nesta etapa.

## Biblioteca antiga preservada para comparação

`libnd-auth.so` é a biblioteca ARM64 adaptada anteriormente para o token de
43 caracteres do gateway brasileiro, preservada antes da remoção dos APKs
antigos da raiz. SHA-256 da entrada:

```text
fe2673ea8e450887bf6fa62b8d79d568cafa404cb83f239af200ac610a1f7c32
```

Antes da revisão 50208, o build conferia esse hash, removia mensagens que imprimiam
credenciais e copiava a biblioteca para `lib/arm64-v8a/libnd.so` no APK final.
A biblioteca lê o token do Intent e o acrescenta às requisições de autenticação
do gateway. É específica dos metadados e da biblioteca IL2CPP original
2017.4.34f1; não deve ser utilizada com a versão Unity 6.

`tutorial_bundles.c` é a fonte da ponte adicional `libbr.so`, compilada pelo
Android NDK. Ela depende da nova `libnd.so` e intercepta `AssetBundleItemInfo.GetCrcName`
da biblioteca original, com instruções e SHA da entrada conferidos. Somente os
quatro nomes do manifesto `bundled/tutorial/manifest.json` retornam URLs locais
`StreamingAssets/durango-br/tutorial/`; as demais chamadas usam o método original.
O carregamento permanece na coroutine e no UnityWebRequest originais. Os
cabeçalhos Unity, materiais, texturas e shaders não são regravados.
