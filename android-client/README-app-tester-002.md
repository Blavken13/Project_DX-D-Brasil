# Lost Horizon - Build tester — 002

APK: `dist/losthorizon-build-tester-002.apk`.
SHA-256: `22eb7b4ce354b1c55aa451481156ae66bca49bd4a6cef8a59149f7da1a66524d`.
Tamanho: 987.650.956 bytes.

Atualização do tester-001, pacote `com.losthorizon.tester`, código de versão 2,
versão `tester-002` e mesmo certificado. Instalar como atualização preserva os
dados e a sessão salva. O construtor usa uma pasta isolada `work/app-tester-002`
e verifica o SHA-256 do APK anterior antes de gerar a atualização.

## Alterações

- Nome Android: **Lost Horizon - Build tester**, em todas as traduções de `app_name`.
- Ícone Android: `../icon2.png`, redimensionado para o recurso existente. A imagem
  entregue foi preservada, sem geração de uma nova arte.
- Identificação do modal: `Build tester · Entre na sua conta`.
- A confirmação de personagem retém o ID e o slot escolhidos pelo usuário.
  Quando a consulta de conta substitui o DTO, o adaptador aplica a escolha ao
  novo DTO antes de atualizar a interface. Isso evita o retorno ao personagem
  recomendado originalmente, inclusive quando o usuário confirma o segundo ou
  terceiro personagem.

`GetRecommendedPlayer` também chama `ApplyRecommendedPlayer` ao calcular seu
padrão; essas chamadas são diferenciadas das confirmações explícitas da UI.
A escolha é retida com GC handle e descartada ao aplicar uma nova autenticação.
Um personagem removido da lista não é restaurado. A escolha explícita de criar
um personagem, representada por ID vazio, também é preservada.

## Validação

`work/app-tester-002/static-validation.json` registra:

- Assinaturas APK v2/v3, mesmo certificado e alinhamento ZIP de 16 KB.
- Nome e versão conferidos com `aapt2 dump badging`.
- Pixels do ícone conferidos contra o redimensionamento de `icon2.png`.
- CRC ZIP e comparação SHA-256 de 4.224 entradas preservadas do tester-001.
- Bibliotecas principais do motor, assets do jogo e vídeos preservados.
- Wrapper do tick executado em ARM64 com endereço fixo e relocação ASLR.
- Código ARM64 compilado do adaptador executado com APIs gerenciadas simuladas:
  três slots, padrão implícito, conta nula, personagem removido, consulta durante
  autenticação e escolha de criar personagem, em dois endereços de carga.

A versão 001 foi testada no Xiaomi 23021RAAEG conectado e o defeito foi reproduzido:
o segundo personagem foi marcado, mas após a confirmação houve nova consulta
de conta e o título voltou a mostrar o primeiro. Evidências da reprodução ficam
em `work/app-tester-001/instrumentation/second.png`, `confirm-second.png` e nos
eventos de atualização da conta.

**Teste físico da versão 002 pendente.** A instalação em streaming falhou sem
detalhe. A segunda tentativa transferiu o APK completo, mas o Android retornou
`INSTALL_FAILED_USER_RESTRICTED: Install canceled by user`. Não houve remoção do
aplicativo nem limpeza dos dados. A instrumentação separada está instalada e
preparada para conferir os slots 1 e 2 (segundo e terceiro personagens) assim
que o aparelho permitir a atualização. Testes emulados não substituem essa
validação no motor real.

## Reproduzir

```powershell
python android-client/build_app_tester_002.py
python android-client/tests/verify_app_tester_002.py
adb -s 725b9a01 install -r android-client/dist/losthorizon-build-tester-002.apk
python android-client/tests/run_app_tester_002_usb.py build
python android-client/tests/run_app_tester_002_usb.py selection 1
python android-client/tests/run_app_tester_002_usb.py selection 2
```

O pacote de instrumentação é separado do APK entregue. Seus testes restauram
a conta já salva, selecionam e confirmam o slot informado, capturam as telas e
entram no jogo. As capturas exigem inspeção para confirmar a identidade no mundo;
o resultado automatizado verifica a retenção da escolha nos eventos do adaptador.
