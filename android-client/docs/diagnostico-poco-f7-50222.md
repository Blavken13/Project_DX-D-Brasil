# POCO F7 — variante experimental 50222

Registro de 08/10/2026. O modelo foi informado pelo usuário; ainda não foi
possível consultar as propriedades do aparelho, executar o jogo ou obter
o encerramento. **A causa da falha do jogo continua desconhecida.**

## Acesso ao aparelho

- ZeroTier deste PC ativo, com endereço `10.168.149.34/24`.
- Rota direta da VPN para `10.168.149.53`, vizinho alcançável.
- TCP `10.168.149.53:34803` aberto, mas `adb connect` retorna
  `failed to connect`; `adb devices -l` permanece vazio.
- TCP `10.168.149.53:9239` recusado.
- `177.55.224.46:9239` e `177.55.224.46:34803` expiram.
- ADB local: versão 37.0.0-14910828.

A porta aberta comprova alcance TCP, não uma sessão ADB autorizada.
É necessário confirmar a porta de conexão atual e, se este PC ainda não
estiver pareado, obter IP/porta e código da tela **Parear dispositivo com
código**. As portas de pareamento e conexão podem ser diferentes.
Referência: [depuração sem fio do Android](https://developer.android.com/studio/command-line/adb#wireless).

Não atribuir a falha do jogo a GPU, tamanho de página, vídeo ou VPN sem
o registro da tentativa. Android/HyperOS, ABI, GPU e página real do kernel
não foram medidos neste aparelho.

## APK gerado

`android-client/dist/LostHorizon-poco-f7-teste-50222.apk`

- Versão `50222` / `5.2.1-losthorizon-poco-f7-teste1`.
- Base 50221 validada por SHA-256
  `b907863d1a9b66226d9dd3542fa68afb533950a792da04d5056b3742762a7bb9`.
- Perfil de compatibilidade e desativação do vídeo da seleção ligados
  inicialmente. Ambos continuam ajustáveis no menu Diagnóstico.
- Preferências do perfil em `lh-poco-f7-test`, separadas das escolhas
  guardadas pela versão padrão. Essas escolhas anteriores são preservadas.
- O fundo da seleção pode ficar preto quando seu vídeo estiver desligado.
  A opção permite comparar o comportamento com o vídeo ligado e desligado.
- Build de teste com `debuggable=true` para coleta por `adb run-as`.
  Não promover esta variante a release de produção sem validação.
- Mesmo certificado de assinatura da 50221; permite atualização sobre
  instalações com esse certificado. Não desinstalar se houver divergência
  de assinatura: preservar os dados e analisar a instalação existente.

SHA-256 do APK:
`e919d34151281771b50b2617ad81075d5e15bcef3f276101aeadc7c69e2fdf3b`.

O modo de compatibilidade de página já presente no manifesto foi mantido;
isso não comprova suporte integral do motor legado a kernels de 16 KB.
Referência: [tamanhos de página no Android](https://developer.android.com/guide/practices/page-sizes).

## Verificação concluída

Assinaturas v1/v2/v3 e alinhamento ZIP de bibliotecas de 16 KB verificados.
1.770 arquivos da base permanecem idênticos, incluindo todas as bibliotecas
nativas, recursos do jogo e DEX original. Só manifesto, DEX do launcher e
identificação de diagnóstico mudaram.

O DEX montado foi desmontado novamente: 107 classes conferidas; somente
os quatro métodos de leitura/gravação das opções do perfil foram alterados.
O manifesto codificado contém a versão e habilitação de depuração esperadas.
O certificado do APK final foi comparado com o certificado da 50221.
Relatório: `dist/LostHorizon-poco-f7-teste-50222.apk.build.json`.

## Construir e testar após liberar ADB

Na raiz do projeto, com Python 3.12 e as ferramentas Android locais:

```powershell
python android-client/build_poco_f7_apk.py
python android-client/build_poco_f7_apk.py --verify-only
```

Primeiro capturar a tentativa com a instalação existente:

```powershell
python android-client/diagnose_poco_f7.py --serial 10.168.149.53:34803
```

O coletor consulta modelo/sistema/ABI/tamanho real de página, pacote,
histórico de saídas, crash buffer e logs do runtime. Abre o launcher normal;
pode ser necessário tocar em Entrar/Iniciar durante os 45 segundos de
observação. Abrir o launcher não comprova acesso ao motor ou ao mapa.

Após analisar a falha original, instalar a variante como atualização e
repetir a captura:

```powershell
python android-client/diagnose_poco_f7.py --serial 10.168.149.53:34803 --install android-client/dist/LostHorizon-poco-f7-teste-50222.apk
```

O coletor não desinstala, não limpa dados nem apaga os buffers anteriores.
Os registros brutos ficam apenas em `android-client/work/poco-f7-device-*`;
revisar dados privados antes de compartilhá-los. A coleta já executada nesta
sessão parou antes da abertura/instalação porque o ADB não estava autorizado.

**Validação física pendente. Não há confirmação de que a 50222 resolva a
falha do POCO F7.**
