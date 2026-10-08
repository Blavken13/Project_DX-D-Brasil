# Build do APK 50221

Esta pasta contém os scripts de montagem da versão com âncora mobile e os testes ARM64 da correção de ABI dos diagnósticos. O servidor correspondente é descrito em [âncora e durabilidade](../docs/ancora-mobile-durabilidade-50221.md).

O build usa o APK `LostHorizon-alfa-diagnostico-completo-50219.apk` na raiz do projeto, validado pelo SHA-256 `0ea3ff42413a5d3fc4b1bd955ad730b239d4b01e074ca7b6b944cf6ca3cb82ad`. Esse binário é uma entrada externa e não faz parte do Git.

No ambiente Windows usado para a release, são necessários Python 3.12, o JBR do Android Studio, Android SDK build-tools 36.0.0 e a plataforma android-36. O código em `build_apk.py` define os caminhos locais dessas ferramentas. Disponibilize Apktool em `work/apktool.jar`, o executável zipalign e suas DLLs em `work/`, e Unicorn no Python ou em `work/python-deps/`.

Execute a partir da raiz do projeto:

```powershell
python android-client/build_anchor_apk.py
```

O script extrai a 50219, valida os hashes de origem, aplica o reparo de ABI da 50220, executa os testes de ABI e do menu, atualiza somente o manifesto/identificação da versão, monta, alinha, assina e compara os arquivos do APK final. Não requer um build anterior da 50220.

Para testar somente o menu nativo:

```powershell
python android-client/tests/verify_anchor.py
```

A saída está em `dist/LostHorizon-alfa-ancora-50221.apk`, acompanhada do SHA-256 e do relatório `.build.json`. `work/` e `dist/` são ignorados pelo Git. Os assets do jogo são preservados no APK, sem serem adicionados ao repositório.

Para distribuir uma atualização sobre a 50220, use a mesma chave local `durango-br-test.jks` e seu arquivo `.pass`. Essas credenciais são ignoradas pelo Git e precisam ser transferidas separadamente de maneira privada. Se faltarem, o empacotador cria uma chave de teste, que não substitui instalações assinadas com outra chave.

A versão atual foi validada por emulação ARM64 e verificações do pacote. O teste visual requer um aparelho Android e o servidor atualizado.
