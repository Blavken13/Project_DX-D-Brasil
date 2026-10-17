# Build tester-007: ativação da expansão no Android

## Causa identificada no aparelho

No Redmi conectado por USB, o tester-006 registrou:

```text
port: prologue not relocatable (b40000c8)
estate_expansion_hook_failed
```

O instalador de hooks da ponte comunitária não consegue realocar o `CBZ`
no início de `EstateGridGroup.OnExpandEstateClick`. Assim, o redirecionamento
da expansão não era instalado, embora o APK compilasse e tivesse assinatura válida.

O tester-007 substitui a entrada completa desse handler por um salto ARM64.
Como o handler anterior não é executado, não precisa de trampoline nem de
realocação das instruções originais. O destino continua sendo o método
`ExpandPersonalEstate` do próprio cliente, com seu callback e limite local.
O servidor mantém a validação da expansão.

Os métodos são localizados no runtime IL2CPP atual. O adaptador confirma o
tamanho de `Point2`, usa o tamanho de página do aparelho e restaura a proteção
de execução após escrever o salto. O código de login permanece o mesmo.

## Gerar e instalar

```powershell
python android-client/build_app_tester_007.py
python android-client/tests/monitor_estate_usb.py --serial SERIAL install
python android-client/tests/monitor_estate_usb.py --serial SERIAL launch
python android-client/tests/monitor_estate_usb.py --serial SERIAL logs --label expansion
```

Saída: `android-client/dist/losthorizon-build-tester-007.apk`.
Instale como atualização, preservando os dados do aplicativo.

## Evidências

- Base tester-006: SHA-256 `87a8cd7f0770b133385a3ddd58272cb43cd94bcd8c62ed1439de5417adc497fb`.
- APK tester-007: SHA-256 `ff87712abd10295bf380f974561428d1837e6d9b9d9200e90138fff6f23d159f`.
- Mesma assinatura, versão `7` / `tester-007`, alinhamento de 16 KB.
- Apenas o manifesto e `lib/arm64-v8a/libtester.so` mudaram; 4.244 arquivos preservados.
- A compilação da variante anterior reproduziu o código de máquina do adaptador 006.
- No aparelho, `estate_expansion_direct_entry_ready` e `estate_expansion_ready`
  confirmaram a instalação do redirecionamento na build 007.
- `estate_expansion_click` registra posição, tamanho e limite local; seguido
  de `estate_expansion_free_path` quando o clique é encaminhado.
- Teste USB em 17/10/2026, Redmi 23021RAAEG, Android arm64: instalação da
  atualização e login concluídos. Os cliques registraram tamanho `2`, `3`,
  `4`, `5`, `6`, `7` e `8`, com limite `8` e proprietário do tipo `0`.
  Novos cliques no limite continuaram registrando tamanho `8`.
- O usuário confirmou: "agora funcionou". A expansão efetiva de dois para
  oito lotes foi acompanhada pelos logs do cliente.

Relatórios locais: `android-client/work/app-tester-007/static-validation.json`
e `android-client/work/app-tester-007/device-725b9a01/`.
Log funcional: `device-725b9a01/tester007-success.log` dentro dessa pasta.
