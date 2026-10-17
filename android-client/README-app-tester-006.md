# Build tester-006: expansão de domínio

Base: `losthorizon-build-tester-005.apk`, SHA-256
`0d5176163d78293fb131c63114d4d1992263ccb59863063f77de1b9148026dc2`.

A correção Android anterior (`estate_original.py`) redirecionava o clique do
diálogo legado para `EstateGridGroup.ExpandPersonalEstate`, que envia o pedido
e processa a licença retornada. Os endereços dessa build Unity 2017 não podem
ser reutilizados no executável Unity 6 atual.

O tester-006 adapta o mesmo caminho no `libtester.so`: localiza os métodos
por classe, nome e número de parâmetros no runtime IL2CPP atual, confirma que
`Point2` tem oito bytes e instala o redirecionamento na thread Unity já usada
pelo adaptador. O handler nativo mantém seu limite e callback; o servidor
continua validando proprietário, adjacência e quantidade de lotes.

## Gerar

```powershell
python android-client/build_app_tester_006.py
```

Saída: `android-client/dist/losthorizon-build-tester-006.apk`.
Instale como atualização do tester-005, com a mesma assinatura, e abra o jogo.
Não é necessário desinstalar a versão anterior.

## Verificações

- A compilação sem a nova opção reproduziu o código de máquina do adaptador
  do tester-005, antes de compilar a variante com o redirecionamento.
- Apenas `AndroidManifest.xml` e `lib/arm64-v8a/libtester.so` mudaram.
- 4.244 outros arquivos foram comparados e preservados, incluindo engines,
  assets, login, mídia e os bundles do Giganotosaurus 2213.
- Versionamento: código `6`, nome `tester-006`.
- Assinatura v2/v3 e alinhamento de 16 KB verificados; certificado igual ao tester-005.

Em `adb logcat -s AppTester`, `estate_expansion_ready` indica a instalação do
redirecionamento e `estate_expansion_free_path` indica um clique encaminhado.
O adaptador registra `estate_expansion_unsupported_point_abi` ou
`estate_expansion_hook_failed` quando detecta essas falhas. Se os métodos ainda
não estiverem disponíveis, tenta novamente no próximo tick. A presença de
`estate_expansion_ready` deve ser confirmada em uma validação no dispositivo.

Relatório: `android-client/work/app-tester-006/static-validation.json`.
Não foi executado teste visual no dispositivo nesta etapa.
