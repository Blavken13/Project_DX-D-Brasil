# Lost Horizon 50226 — login no motor

APK de teste: `dist/LostHorizon-login-no-jogo-50226.apk`.
SHA-256: `6a1c420dcbfb67fc7a961ded81a0c33b862bf33ae9f0fbbdad850d6a19ab1679`.

O aplicativo abre diretamente `CompatGameActivity` e inicia a Unity uma única vez.
O login aparece como um modal da própria Unity sobre a seleção de personagem.
Após a autenticação, a consulta original de personagens é retomada na mesma
atividade e na thread Unity. A lista real substitui a seleção vazia e o modal fecha.

Estão disponíveis entrar, criar conta com confirmação de senha, continuar com
a conta salva, trocar de conta e compartilhar diagnóstico. O teclado de senha
usa o modo seguro e o modal exibe a senha com asteriscos. A senha não é gravada
nas preferências. A sessão salva usa o mesmo armazenamento da versão anterior.

## Implementação

- `src/com/newdawn/launcher/EngineLogin.java`: transporte HTTP em segundo plano,
  mensagens de erro, sessão salva e diagnóstico. Usa a API de autenticação existente.
- `native/original/engine_login.c`: desenho do modal por Unity IMGUI, teclado
  TouchScreenKeyboard, retenção de objetos gerenciados e retomada de RequestAccounts.
- `engine_login_original.py`: resolve as entradas pelo metadata do IL2CPP original
  e confere seus bytes contra o APK de base antes da compilação.
- `build_engine_login_apk.py`: compila somente a integração, altera a entrada do
  manifesto e o inicializador de CompatGameActivity e assina com a chave existente.

Enquanto não existe autenticação, RequestAccounts entrega somente uma conta
local vazia, sem identidade, personagens ou slots, para montar o fundo da seleção.
A consulta remota só acontece depois que o gateway autentica. UICamera e OnConfirm
bloqueiam a interação de fundo enquanto o modal está aberto. A mudança para a
seleção acontece no próximo evento da Unity, evitando reentrar no inicializador.

O inicializador legado exige um buffer de 43 caracteres. A integração limpa esse
buffer antes de criar o UnityPlayer; o valor provisório não vai ao gateway.
O token real só é aplicado na thread Unity depois da resposta de autenticação.
Na troca de conta a credencial ativa é invalidada e a lista anterior é substituída
pela conta local vazia. Falha na consulta de personagens mantém o modal disponível.

Arquivos alterados em relação ao APK 50225: `AndroidManifest.xml`, `classes2.dex`
e `assets/durango-br/diagnostics/build.json`. Adicionado `libenginelogin.so`.
`classes.dex`, `libnd.so`, `libbr.so`, `liblhaccount.so`, `libil2cpp.so`, `libunity.so`,
os recursos e os assets do jogo permanecem idênticos byte a byte à base.

## Validação de 10/10/2026

Assinaturas v1/v2/v3, mesmo certificado da base e alinhamento ZIP de 16 KB conferidos.
Compilação nativa com `-Wall -Wextra -Werror` e segmentos ELF com alinhamento de 16 KB.
Treze cenários executados com Unicorn usando o ARM64 extraído do APK; APIs
gerenciadas e HTTP simuladas. Resultado em `dist/engine-login-50226-native-tests.json`.

Teste físico autorizado no Xiaomi/Redmi modelo 23021RAAEG, Android 15/API 35,
patch de segurança 01/07/2026, páginas de 4 KB. Toques realizados pelo usuário
porque o Android rejeitou a injeção de eventos por ADB com INJECT_EVENTS.

- A primeira revisão iniciou Unity, autenticou, carregou personagens e entrou no
  tutorial no mesmo processo PID 19808, sem fechamento observado.
- O APK final, SHA acima, iniciou no PID 23199. A autenticação carregou os três
  personagens da conta na mesma atividade. Captura: `work/engine-login-50226/device/final-modal.png`.
- Trocar de conta mostrou o modal sobre a seleção vazia (0/0), sem os personagens
  da conta anterior. Senha incorreta produziu ENGINE_AUTH_REJECTED e a mensagem
  “Usuário ou senha incorretos”, com senha oculta, mantendo o processo 23199.
  Captura: `work/engine-login-50226/device/wrong-password.png`.

O aparelho conectado é diferente do tester do ZIP com Android 16/API 36. Estes
resultados confirmam o fluxo no aparelho testado, mas não confirmam a correção
do SIGSEGV daquele tester. O APK ainda usa as bibliotecas e os hooks legados;
a integração também instala hooks nativos verificados. Mover a interface não
elimina, por si só, possíveis restrições do sistema a alterações de código nativo.

Cadastro de uma conta nova, sessão expirada e opções de diagnóstico não receberam
teste físico nesta sessão. A recuperação de falha da consulta de personagens foi
validada por emulação ARM64, não por interrupção da rede no aparelho.

## Reproduzir

```powershell
python android-client/build_engine_login_apk.py --resume
python android-client/build_engine_login_apk.py --verify-only
```

Sem `--resume`, o construtor exige um diretório de trabalho novo e confere o SHA
da base. A evidência física fica separada do carimbo de compilação, que registra
“pending” porque o APK é assinado antes do teste. Não há alteração de servidor.
