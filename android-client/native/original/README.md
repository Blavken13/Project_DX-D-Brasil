# Ponte de autenticação do cliente Unity 2017

`libnd-auth.so` é a biblioteca ARM64 adaptada anteriormente para o token de
43 caracteres do gateway brasileiro, preservada antes da remoção dos APKs
antigos da raiz. SHA-256 da entrada:

```text
fe2673ea8e450887bf6fa62b8d79d568cafa404cb83f239af200ac610a1f7c32
```

`build_original_apk.py` confere esse hash, remove as mensagens que imprimiam
credenciais e copia a biblioteca para `lib/arm64-v8a/libnd.so` no APK final.
A biblioteca lê o token do Intent e o acrescenta às requisições de autenticação
do gateway. É específica dos metadados e da biblioteca IL2CPP original
2017.4.34f1; não deve ser utilizada com a versão Unity 6.

`tutorial_bundles.c` é a fonte da ponte adicional `libbr.so`, compilada pelo
Android NDK. Ela depende de `libnd.so` e intercepta `AssetBundleItemInfo.GetCrcName`
da biblioteca original, com instruções e SHA da entrada conferidos. Somente os
quatro nomes do manifesto `bundled/tutorial/manifest.json` retornam URLs locais
`StreamingAssets/durango-br/tutorial/`; as demais chamadas usam o método original.
O carregamento permanece na coroutine e no UnityWebRequest originais. Os
cabeçalhos Unity, materiais, texturas e shaders não são regravados.
