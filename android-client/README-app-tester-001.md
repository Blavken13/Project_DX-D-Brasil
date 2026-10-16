# Aplicação Tester — 001

Arquivo: `dist/losthorizon-app-tester-001.apk` (987.646.860 bytes).

SHA-256: `d3c77f8b53edd270a39c5742b016f631090c10ba0d9411f152d7636d19c0b2d8`.

Base: `Durango-Brasil-v2.2-Lite.zip`, um APK com extensão ZIP, SHA-256 `080b8cac71e807987398056303eaca91a119304d8dcfcee8466c84b1a92306e1`.

## Comportamento

- Nome Android: **Aplicação Tester**. Pacote separado: `com.losthorizon.tester`, versão `tester-001`, código 1. Android 8 ou posterior, **ARM64**.
- O aviso de versão da comunidade foi substituído pelo login na seleção de personagens. Usa o protocolo existente do projeto para entrar, cadastrar e continuar com a conta salva. A identidade só é aplicada ao Unity após autenticação no gateway.
- Servidor único: **Lost Horizon Brasil**, gateway `http://179.197.72.129:8190`. O construtor nativo `nx_ctor`, que acrescentava o Nexus ao `offserver.txt` ao carregar `libdbr.so`, foi desativado. Os padrões Java e o cache de servidores também passaram a conter só o nosso servidor.
- Logo do projeto na seleção, transições, atlas de carregamento e compositor de fotografias. Ícone Android do projeto.
- Rodapé igual ao APK 50226: `2016 VISION FORCE. Todos os direitos da Produtora`, seguido de `*Cliente 5.2.1`.
- Vídeos MP4, `boot.config`, `libunity.so`, `libmain.so` e `libil2cpp.so` preservados byte por byte da base comunitária. Unity 6000.6.3f1.

## Implementação e validação

`build_app_tester.py` modifica uma cópia isolada do cliente. O adaptador `native/app_tester.c` é chamado na thread do Unity pelo tick já existente da comunidade; não inicia uma thread para consultar a inicialização do motor. A assinatura do GC handle segue a API de 64 bits desse IL2CPP (`uintptr_t`). Tokens são adicionados somente a `/accounts` e `/sessions` do nosso gateway, sem registrar seu conteúdo.

Assinaturas APK v2/v3 e alinhamento de páginas de 16 KB verificados. `tests/verify_app_tester.py` comparou os 4.210 arquivos preservados, verificou CRC ZIP e assinaturas SHA-1 dos três DEX embutidos, a logo nativa e a remoção da inclusão Nexus. O wrapper ARM64 foi executado em emulador de instruções com endereços fixos e relocados, preservando registradores e pilha.

Teste USB em Xiaomi 23021RAAEG, Android 15, ARM64. A seleção de servidor foi capturada mostrando somente Lost Horizon Brasil. A conta previamente salva foi restaurada e o jogo entrou no mundo. As duas rodadas finais de instrumentação terminaram com PASS, sem novo encerramento nativo; o Android registra apenas o force-stop ao finalizar a instrumentação. O carregamento mostrou nossa logo, e a fotografia `Durango_20261011_152059.png` foi salva pelo jogo em `Pictures/Durango`, com a marca Lost Horizon Brasil conferida visualmente no arquivo final (`work/app-tester-001/tester-photo.png`). Um APK de instrumentação separado foi usado para os testes de interface; ele não faz parte do APK entregue. Validação de campos e rejeição de credenciais inválidas também foram verificadas durante a adaptação, antes da última alteração de servidores. Nenhuma conta de teste foi criada.

Durante a sessão anterior à retirada do Nexus, houve um fechamento nativo ao retornar novamente à tela inicial depois de jogar (`libunity.so+0x16e40b8`). Esse registro não comprova uma causa no Nexus e não deve ser apresentado como corrigido pela remoção do servidor. A entrada inicial e a restauração de sessão do APK final foram verificadas; a estabilidade em outros aparelhos e em transições repetidas precisa de acompanhamento dos testers.

Arquivos de auditoria e capturas: `work/app-tester-001/build-report.json`, `static-validation.json`, `instrumentation/servers.png`, `instrumentation/sidebar-final.png` (logo de carregamento), `tester-final-world.png` e `tester-photo.png`.

## Recompilar neste ambiente

O ambiente local usa Java do Android Studio, SDK/build-tools 36.0.0, NDK 28.2.13676358, Apktool e módulos Python armazenados em `work/`. A compilação usa os diretórios previamente extraídos da comparação e da inspeção da base, os assets de branding do projeto e o atlas do APK 50226. A chave de assinatura existente é lida por arquivo de senha; não incluir chave, senha ou dados de autenticação nos relatórios.

```powershell
python android-client/build_app_tester.py
python android-client/tests/verify_app_tester.py
python android-client/tests/run_app_tester_usb.py install
```
