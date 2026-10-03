# Investigação: POCO X6 / Android 16

Data: 02/10/2026. Sintoma informado pelo tester: fecha após o splash, antes da seleção de personagens. Aparelho indisponível para ADB. Auditoria estática; nenhuma reprodução da falha nesse modelo e nenhuma alteração no APK nesta etapa.

## Artefato auditado

- APK: `android-client/dist/LostHorizon-alfa.apk`.
- SHA-256: `49d033837d3566abd22dd6409233ff954f66849f3e4919ccd66017e1598f5780`.
- Tamanho: 317.444.865 bytes; versionCode 50207; versionName `5.2.1-losthorizon-alfa`.
- Pacote: `com.nexon.durango.global`; bibliotecas ARM64 exclusivamente.
- Motor efetivo: Unity **2017.4.34f1**, obtido dos arquivos Unity. O nome comercial do cliente 5.2.1 não identifica a versão do motor.
- Manifesto: minSdk 16, targetSdk 28; tráfego HTTP permitido. O launcher injetado requer APIs mais novas que esse mínimo declarado; isso deve ser ajustado ao definir o suporte a Android antigo, mas não explica o caso Android 16.

O POCO X6 tem Snapdragon 7s Gen 2, GPU Adreno e variantes de 8/12 GB de RAM. Falta de potência não é a primeira hipótese. A tela 2712 × 1220 pode elevar o custo de renderização, mas a resolução efetiva do jogo nesse aparelho ainda não foi medida. [Especificações oficiais](https://www.po.co/global/product/poco-x6/specs/).

## Evidências e hipóteses

### 1. Compatibilidade com páginas de memória de 16 KB

Todos os oito arquivos `.so` estão armazenados sem compressão e alinhados em 16 KB no ZIP. O alinhamento interno dos segmentos ELF é diferente:

| Biblioteca | Alinhamento de LOAD | Resultado estático |
| --- | --- | --- |
| libunity.so | 4 KB | Não atende ao alinhamento ELF de 16 KB |
| libmain.so | 4 KB | Não atende ao alinhamento ELF de 16 KB |
| libnd.so | 4 KB | Não atende ao alinhamento ELF de 16 KB |
| libbr.so | 16 KB | Atende a esse requisito |
| libil2cpp.so | 64 KB | Atende a esse requisito |
| libBlueDoveMediaRender.so | 64 KB | Atende a esse requisito |
| libAkSoundEngine.so | 64 KB | Atende a esse requisito |
| libsnappy.so | 64 KB | Atende a esse requisito |

Atender ao alinhamento LOAD não certifica a compatibilidade completa de uma biblioteca. As bibliotecas legadas de áudio/compressão também merecem revisão da proteção RELRO e das suposições de memória em execução.

A desmontagem de `libnd-auth.so`, entrada usada para gerar `libnd.so`, confirmou valores fixos no instalador da interceptação de autenticação: alocação de 4096 bytes (`worker`, endereço 0x4154), alinhamento do endereço do código a 4096 (`0x434c`) e tamanhos 4096/8192 para `mprotect` (`0x4350–0x4378`). O alinhamento de `mprotect` precisa respeitar a página real do sistema. Algumas operações falham e o código aborta a instalação da interceptação; isso não comprova por si só que o processo encerra. Não confundir esses valores com a página arquitetural de 4 KB usada pela instrução ARM64 ADRP, que continua correta.

**Android 16 não significa automaticamente páginas de 16 KB.** É necessário medir o kernel do tester. O fato de o splash aparecer mostra que parte da inicialização já funcionou; uma falha exclusivamente ao carregar `libunity.so` não explica necessariamente esse estágio. O carregamento de IL2CPP, dependências e a ponte de autenticação continuam relevantes.

A documentação exige alinhamento ZIP, alinhamento ELF e revisão do código nativo. O modo de compatibilidade permite que alguns aplicativos antigos funcionem, sem garantir todos os casos. [Guia oficial Android](https://developer.android.com/guide/practices/page-sizes).

### 2. Motor, gráficos e vídeo legados

O jogo utiliza Unity 2017 e plugins nativos antigos, incluindo `libBlueDoveMediaRender.so`, ligado a EGL/OpenGL ES. O momento informado coincide com a preparação da tela de seleção e seu vídeo. Uma falha de renderização, de decodificação/interação com o plugin ou da inicialização nativa é plausível, especialmente se o kernel usar 4 KB. Não há registro de SIGSEGV ou nome de biblioteca culpada disponível.

As configurações auditadas permitem resolução nativa e renderização multithread. A qualidade Mobile já desativa sombras e antialiasing. O manifesto exige no mínimo OpenGL ES 2, mas isso não comprova qual API o jogo efetivamente usa no aparelho. Não há evidência suficiente para afirmar que Vulkan está causando a falha.

### 3. Launcher WebView e SDKs antigos

`OriginalAuthActivity.java` cria WebView sem recuperação de erro de inicialização e sem `onRenderProcessGone`. É uma fragilidade confirmada: a morte do renderizador pode derrubar um aplicativo que não a trata. Porém, o launcher é encerrado ao abrir Unity, e o tester passa pelo splash; a hipótese tem prioridade menor neste caso. [Tratamento oficial de encerramento do WebView](https://developer.android.com/develop/ui/views/layout/webapps/handle-termination).

Inicializadores de Firebase, Facebook e marketing permanecem no aplicativo original. Removê-los exige verificar referências e registros de exceção; não há evidência para atribuir a eles o fechamento atual. targetSdk antigo também não é um diagnóstico suficiente, nem justifica aumentar o target sem revisar as mudanças de comportamento.

## Soluções propostas, por ordem de execução

1. **Diagnóstico sem USB.** Preparar uma revisão que consulte o histórico de encerramentos na próxima abertura, registre etapa de inicialização, versão do sistema/HyperOS, ABI, tamanho real da página e dados gráficos quando disponíveis. Incluir exportação manual do relatório, com exclusão de senhas e tokens. `ApplicationExitInfo` informa motivo/sinal; desde Android 12 pode disponibilizar o tombstone nativo, embora não seja garantido que o registro permaneça disponível. Isso distingue erro Java, falha nativa, falta de memória e encerramento solicitado. [API oficial](https://developer.android.com/reference/android/app/ApplicationExitInfo).
2. **Teste controlado da seleção.** Gerar uma variante que substitua somente o vídeo da seleção por fundo estático, preservando recursos, shaders e autenticação. Se funcionar, concentrar o trabalho no plugin de vídeo e seu ciclo de criação/destruição. A transição após o trem não é executada nessa etapa e não deve ser tratada como causa direta sem evidência.
3. **Tratar páginas de memória.** Se forem 16 KB, experimentar o modo de compatibilidade do Android, quando disponível. Para uma correção durável, recompilar/substituir a ponte de autenticação por uma implementação equivalente usando `sysconf(_SC_PAGESIZE)`, proteções de memória verificadas e NDK atual; auditar todas as dependências. O código-fonte completo dessa ponte pré-compilada não está no projeto. Alterar apenas cabeçalhos ELF ou reaplicar zipalign não resolve. `libunity.so`/`libmain.so` exigem um motor compatível ou uma solução de compatibilidade validada.
4. **Perfil gráfico de compatibilidade.** Se os registros apontarem EGL/GLES/driver, avaliar resolução reduzida, limite de 30 FPS e renderização sem múltiplas threads, mantendo os shaders compatíveis com o cliente original. Mudanças de API gráfica devem ser testadas individualmente, sem aplicar uma troca global às cegas.
5. **Fortalecer o launcher e validar a cobertura.** Recuperar a perda do renderizador WebView e oferecer fundo estático/formulário funcional quando o vídeo ou WebView falhar. Validar em aparelhos Adreno e Mali, incluindo Android 16 com páginas de 4 e 16 KB, antes de promover uma revisão padrão.

Uma migração futura do motor requer recursos/shaders, IL2CPP e plugins compatíveis. A documentação atual da Unity confirma suporte a Android API 36 nas versões modernas, mas trocar somente `libunity.so` em um APK Unity 2017 não é uma migração segura. A tentativa anterior com Unity 6 já apresentou incompatibilidades visuais neste projeto. [Compatibilidade Android da Unity](https://docs.unity.com/en-us/engine/6000.0/manual/platform-specific/android/introducing/requirements-and-compatibility).

## Teste imediato para o tester

Em Informações do aplicativo → Avançado, procurar a opção de execução com compatibilidade de tamanho de página e ativá-la, **somente se estiver disponível**. Essa opção é documentada para sistemas com páginas de 16 KB; nome/local podem variar na interface do fabricante. Reabrir e informar se passa da seleção. Não é necessário apagar contas ou dados. Se a opção não aparecer, a ausência por si só não substitui uma medição do kernel.

Obter também a versão exata do APK e a compilação do Android/HyperOS. Sem o relatório do encerramento, as causas permanecem hipóteses; os defeitos estáticos descritos acima são reais, mas não identificam isoladamente a causa deste aparelho.

## Comparação com os fechamentos anteriores do projeto

Revisão adicional em 02/10/2026, a pedido do usuário. Foram consultados os scripts preservados da versão Unity 6, o registro de encerramento daquela revisão e a documentação da correção da K na versão original.

| Ocorrência anterior | Evidência preservada e correção | Relação com o POCO X6 |
| --- | --- | --- |
| Unity 6: encerramento na entrada do motor, após login | `work/unity6/crash-native.log` registra SIGSEGV imediatamente após `IL2CPP: JNI_OnLoad`, no Redmi Note 12/Android 15. A versão corrigida de `native/unity6_auth_bridge.c` instala a autenticação depois de retornar de `il2cpp_init`, na thread do motor. Na inicialização tardia, verifica o domínio e registra a thread com `il2cpp_thread_attach`. | É o precedente mais parecido em estágio. Sustenta revisar a sincronização da ponte atual, mas o registro antigo sem backtrace completo não demonstra que o POCO tenha a mesma causa. São motores e pontes diferentes. |
| Unity 6: seleção abre, mas personagens não aparecem | O histórico de testes confirma correção da autenticação antecipada e do gateway. Não carregar personagens e encerrar o processo são sintomas distintos. | Verificar que a ponte está pronta antes da primeira requisição evita conta vazia; por si só isso não explica um crash nativo. |
| Unity original: encerramento ao entrar no mapa, durante a criação da K | `docs/tutorial-ancora-investigation.md` registra SIGSEGV ao usar `il2cpp_field_set_value`: referências eram passadas como endereço de uma variável temporária, em vez do objeto gerenciado. Strings, request e array foram corrigidos. O usuário confirmou conversa e avanço do tutorial. | A correção está no código atual. A manipulação de K acontece no Update do tutorial; o POCO fecha antes da seleção. Não é a primeira candidata para esse estágio. |
| Aparências pretas, cenário ausente e HUD travado | Dependências de bundles e compatibilidade de shaders foram corrigidas; o usuário confirmou a recuperação. | Um recurso ausente pode prejudicar a seleção, mas não há evidência de que as correções de texturas anteriores expliquem o fechamento atual. Não trocar o motor ou shaders que já funcionam como primeira tentativa. |

### O que a comparação revelou no cliente atual

Não é correto dizer que `libnd.so` nunca registra sua thread no IL2CPP: a desmontagem confirma a resolução de `il2cpp_thread_attach`, espera por `il2cpp_get_corlib` e uso de `il2cpp_domain_get`. Entretanto, a rotina ainda usa uma espera fixa de aproximadamente 100 ms após obter o domínio, e modifica as instruções de um método a partir de uma thread auxiliar. A existência da biblioteca/domínio não certifica que todo o estado necessário esteja pronto. Esse caminho merece uma sincronização baseada em estado, equivalente em objetivo à correção da Unity 6, sem reutilizar os offsets ou a biblioteca daquele motor.

O worker de `tutorial_bundles.c` começa quando encontra `libil2cpp.so` carregada e os símbolos exportados. Ele instala duas interceptações escrevendo instruções e endereço de destino em etapas. Isso não é prova de uma falha atual, mas existe uma janela potencial se outra thread executar o método durante a escrita. As chamadas que manipulam a K já ocorrem na thread Unity; essa proteção deve ser mantida.

### Mudanças preventivas prioritárias

1. **Inicialização por estados reais:** aguardar o término efetivo do runtime, preparar a ponte e liberar as requisições de conta somente quando estiver pronta. Uma operação única de instalação deve impedir repetição/concorrência. Em caso de incompatibilidade ou timeout, retornar ao login com mensagem, sem enviar autenticação incompleta. Não solucionar apenas aumentando sleeps.
2. **Intercepções instaladas com segurança:** validar versão, assinatura e limites do método; preparar o trampolim completo antes de publicar o destino; executar a instalação em um ponto conhecido e seguro da inicialização. Conferir todos os retornos de proteção de memória e evitar deixar código parcialmente alterado. Uma falha numa melhoria opcional deve preservar o fluxo original quando possível; a autenticação obrigatória deve impedir a entrada com mensagem.
3. **Vídeo com alternativa estática:** liberar o player do login antes da reprodução na seleção. O launcher atual solicita pause via JavaScript, inicia a Activity Unity e só destrói o WebView em onDestroy; a pausa não confirma a liberação do decodificador. Há uma possível sobreposição que merece eliminar, sem afirmar que ocorra no POCO. Verificar a preparação/erro do player da seleção e permitir fundo estático sem impedir a consulta da conta. Fazer primeiro um teste A/B sem esse vídeo, pois um sinal nativo não é recuperado por um simples try/catch Java.
4. **Corrigir suposições de páginas:** a ponte nova deve usar o tamanho real do kernel para mmap/mprotect. Isso é independente da correção de threads e também precisa de alinhamento/revisão das bibliotecas pré-compiladas.
5. **Registrar o estágio e a falha anterior:** obter histórico de encerramentos no próximo início e exportá-lo manualmente. Isso permite distinguir uma regressão na ponte de uma falha no plugin de vídeo/driver, sem depender de USB.

Prioridade revisada: para esse sintoma específico, começar pela sincronização e integridade das pontes e pelo teste controlado do vídeo. A hipótese de 16 KB permanece condicional à configuração efetiva do aparelho. Nenhuma dessas propostas foi aplicada ao APK nesta investigação.

## Implementação posterior — revisão 50208

Em 02/10/2026, após autorização do usuário, foi compilada a revisão
`5.2.1-losthorizon-alfa-compat1`, código Android `50208`.
As conclusões anteriores descrevem a investigação antes dessas alterações.

- Autenticação e pontes do tutorial instaladas antes da criação do UnityPlayer,
  sem workers alterando código durante a execução. As chamadas gerenciadas de
  autenticação acontecem na thread da requisição original, após a inicialização.
- Validação de instruções e proteção de memória com o tamanho real da página;
  pontes próprias recompiladas com alinhamento ELF/RELRO de 16 KB.
- Liberação do vídeo e destruição do WebView antes da entrada no motor;
  formulário nativo disponível se o WebView falhar.
- Modo de compatibilidade ativado inicialmente no Android 15 ou superior:
  fundo estático na seleção, solicitação de 30 FPS e resolução limitada a
  1280 pixels no maior eixo. O tester pode alterar esse modo em Diagnóstico.
- Renderização multithread desativada por alteração de um único byte de
  PlayerSettings. Binários do motor e recursos de shaders preservados.
- Relatório privado da etapa de inicialização e do encerramento anterior,
  consultado na próxima abertura. Compartilhamento e salvamento são manuais.
  O tombstone, quando fornecido pelo Android, é filtrado para sinal e pilha
  da thread afetada; mensagens, memória, logs brutos e credenciais são omitidos.

Os testes de autenticação, páginas de 4/16/64 KB, relocação ARM64, parser de
tombstone, empacotamento, tutorial/K, apresentação e assinatura passaram.
O teste em execução no Poco X6 permanece pendente. Unity 2017/libmain continuam
legados: a declaração `pageSizeCompat` não comprova compatibilidade universal
com kernels de 16 KB e não substitui uma recompilação do motor.

APK: `android-client/dist/LostHorizon-alfa.apk`, 317506305 bytes.
SHA-256: `986168152983109fc014a1503de61a89d4a9797f882236f03d8b404ab8ce6fb0`.
Instruções para o tester: [teste-compatibilidade-50208.md](teste-compatibilidade-50208.md).

## Vídeo restaurado — revisão 50209

O usuário encontrou fundo preto na seleção durante o teste da revisão 50208.
A causa no código era a supressão automática do vídeo vinculada ao perfil de
compatibilidade do Android 15/16. Não havia uma imagem estática substituta nessa tela.

A revisão 50209 separa a opção de desativar o vídeo do perfil gráfico. O vídeo
é habilitado por padrão em todos os aparelhos e permanece assim ao atualizar
da 50208. Gráficos reduzidos e demais proteções continuam disponíveis. A opção
manual de desativar o vídeo serve para comparar fechamentos no Poco X6; seu
estado aparece no relatório. A apresentação em execução aguarda teste manual.

SHA-256 do novo APK:
`5af7a4abddd9fab1e6f6c6bff33b7e68db54a0c4cf4b50aca945f769ce5b9e0d`.
Instruções atuais: [teste-compatibilidade-50209.md](teste-compatibilidade-50209.md).
