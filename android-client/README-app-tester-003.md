# Lost Horizon - Build tester — 003

APK: `dist/losthorizon-build-tester-003.apk`.
SHA-256: `95562bfc200c3893eec47adecf99fdb7e9c57cb1b2667e35d1e4c1ce96252c2d`.
Tamanho: 987.642.764 bytes.

Atualização do tester-002 validado pelo usuário. Mesmo pacote
`com.losthorizon.tester`, mesmo certificado, nome **Lost Horizon - Build tester**,
código de versão 3 e versão `tester-003`.

## Correção do ícone

O manifesto aponta para `@mipmap/app_icon`. O recurso `mipmap-anydpi/app_icon.xml`
é um ícone adaptativo que referencia `ic_launcher_background` e
`ic_launcher_foreground`. Alterar apenas `mipmap-mdpi/app_icon.png` não troca a
imagem usada pelo launcher em Android moderno.

A imagem `../icon2.png`, agora com **512×512 pixels**, foi aplicada ao ícone
legado e às seis densidades do fundo adaptativo. Os recursos de primeiro plano
são transparentes. A versão anterior do APK permanece arquivada.

## Verificação

O construtor verifica o SHA-256 da base antes de criar uma cópia isolada em
`work/app-tester-003`. O APK final passou por verificação de assinatura v2/v3,
certificado igual ao anterior, alinhamento ZIP de 16 KB e integridade ZIP.
Os 13 recursos PNG do ícone foram verificados contra a imagem fornecida ou a
camada transparente correspondente. O XML adaptativo compilado, o nome e o
código de versão foram conferidos com `aapt2`.

A comparação com o tester-002 confirmou somente oito entradas diferentes:
manifesto (versão), ícone legado e seis fundos adaptativos. Todas as 4.236
entradas restantes foram conferidas, incluindo os DEX, o adaptador nativo, as
bibliotecas e os assets do jogo. A correção de personagens confirmada pelo
usuário permanece byte a byte igual à versão 002.

Relatório: `work/app-tester-003/static-validation.json`.
Não foi feita instalação da versão 003 no celular nesta revisão.

```powershell
python android-client/build_app_tester_003.py
```
