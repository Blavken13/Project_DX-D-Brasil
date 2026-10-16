# Giga: material, dano e movimentação

## Alterações

- Material dos bundles: `LitSphere/DualMode/Animal`, `_MainTex` com a textura original `giga_d_colorized.png` e `_LitSphereTex` com `litsphere_dino_giant` do preload da plataforma. As referências foram resolvidas contra os assets reais; não há Standard, Lambert ou emissão no material utilizado em runtime.
- A finalização usa shaders já compilados do cliente, sem depender da licença do editor. Outros objetos do prefab foram comparados byte a byte e preservados.
- Alcance da mordida calculado com o raio do corpo e a escala do animal. Giga em escala 2: alcance de 630 unidades. O golpe usa a posição interpolada do jogador e do animal, não uma posição antiga do caminho.
- Mordida do Giga: clip de 1,25 s, impacto aos 0,65 s após o início da animação. Não aplica dano duas vezes e não pode ser interrompida por uma ordem de caminhada durante a recuperação. Sair da área durante o aviso permite desviar.
- Perfis de caminhada extraídos dos frameworks dos 215 tipos de animais. O deslocamento usa a velocidade do clip, a cadência e a escala exibida. Giga: 320 cm/s na escala 1, 640 na escala 2; a medição foi feita pelo deslocamento das patas no FBX.
- Aparecer no campo de visão durante uma caminhada passa a enviar a posição atual e o caminho restante. Interromper a caminhada também limpa seu temporizador pendente.

## Instalação local

- Windows: bundles e catálogo em `Lost-Horizon-PC/Lost Horizon Game_Data/StreamingAssets/AssetBundles`.
- Android: bundles e catálogo em `Durango-CustomServer/server/assetbundles/android`.
- O catálogo Android foi alinhado ao APK `android-client/dist/losthorizon-build-tester-003.apk`, preservando as adições customizadas. O preload usado é `527ec31c957201159d83da5dcb3c396a`. Os arquivos de áudio ausentes do próprio APK continuam registrados no relatório de sincronização; essa ausência não foi introduzida pela alteração do Giga.
- Novos hashes fazem o download reconhecer a atualização. O executável do APK não precisou ser reempacotado.
- Cópias dos catálogos anteriores foram preservadas junto aos respectivos índices.

## Verificação

- Builds Debug e Release: concluídos sem erros.
- `--arid-basin-check`: **22 verificações aprovadas**, incluindo perda real de vida, alcance junto ao corpo, interpolação, esquiva, ausência de dano duplicado, recuperação da mordida e continuidade da caminhada.
- Bundles Windows e Android: 44 clips por plataforma, referências Legacy, morte e dependências conferidas. Material e matcap resolvidos nos preloads reais.
- Catálogos instalados comparados aos pacotes e MD5 dos bundles.
- A suíte `--gameplay-check` passou nas verificações de combate, mas falhou depois em `renovacao parcial cobre ilhas comuns sem exigir esgotar todas as partes` (`GameplayRegressionCheck.cs`). A mecânica de renovação de recursos não foi alterada nesta tarefa.
- A aparência final ainda precisa da conferência dentro do jogo após recarregar os novos bundles; não foi feito um novo teste visual do cliente nesta sessão.

Logs e medições estão em `tools/giga-video-analysis`. Os pacotes e metadados estão em `New Dinos/Giganotosaurus_2213`.

## Testar

Feche completamente o cliente, inicie o servidor local e abra novamente o PC em **Server Test**. Aguarde o download na seleção de personagens e viaje para Arid Basin. O servidor local foi deixado encerrado, com as portas 8190/8191 livres.
