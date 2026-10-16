# Cliente PC Lost Horizon — comunidade 3.1.9

O cliente adaptado está em `Lost-Horizon-PC`. Abra `Lost Horizon.exe` para usar o modal de login e cadastro atual do projeto. `Lost Horizon.cmd` executa o mesmo iniciador pelo PowerShell nos ambientes que bloqueiam o EXE sem assinatura. O runtime Unity é `Lost Horizon Game.exe`, acompanhado de `Lost Horizon Game_Data`.

A base da comunidade e o cliente antigo foram preservados nas suas pastas. Não misture arquivos das duas versões: a antiga usa Unity 2017.4.34f1, e a comunidade 3.1.9 usa Unity 6000.6.3f1.

## Adaptação feita

- Logo Lost Horizon Brasil do iniciador atual aplicada às duas logos do título, ao prólogo e à transição da comunidade.
- Splash Vision Force do projeto aplicado ao splash PC e ao splash nativo; o logo Unity adicional foi desativado.
- As duas regiões do atlas abaixo do hexágono, inglês e coreano, receberam nossa logo. A textura BC7 e todos os outros blocos do atlas foram preservados.
- Os dois rodapés de copyright do título usam exatamente o texto do cliente atual: `2016 VISION FORCE. Todos os direitos da Produtora` e `*Cliente 5.2.1`. O modal também usa os créditos Vision Force.
- Produto Unity, empresa, janela e executáveis identificados como Lost Horizon / Vision Force. O ícone personalizado fica no iniciador; o runtime mantém os bytes exatos do executável da comunidade para preservar sua reputação no Controle Inteligente de Aplicativos.
- Gateway definido como `http://179.197.72.129:8190`, o mesmo configurado no iniciador PC e no APK do projeto. A lista local contém somente Lost Horizon Brasil.
- O login e cadastro usam a implementação existente em `pc-client/LauncherAuth.cs`. A nova ponte importa a sessão por variáveis de ambiente, limpa essas variáveis e injeta o token somente nos formulários `/accounts` e `/sessions` do gateway autenticado. O fluxo original de seleção de personagem e entrada no mundo continua recebendo a sessão de jogo emitida pelo servidor.
- Catálogo `Languages/pt_BR/messages.po` e `messages.mo` copiado do cliente atual para o leitor de idiomas da comunidade.

## Validação

As verificações dos assets confirmaram os mesmos IDs e a alteração somente dos objetos previstos. Foram conferidos por SHA-256 2.375 arquivos preservados e, por instruções IL, 37.016 métodos preservados. O patch de login/servidor/janela altera oito métodos. O runtime restaurado foi verificado byte a byte e por SHA-256 contra o executável original da comunidade.

Passaram os 23 testes de autenticação e sessão contra um gateway HTTP local, mais os testes da ponte com sessão válida, expirada, token inválido, token ausente e isolamento por endereço. Login e cadastro foram renderizados e inspecionados sem autenticar contas reais.

O teste de inicialização do runtime renomeado carregou Unity 6000.6.3f1, bundles, áudio e a cena de título, sem erros de carregamento de assembly. O encerramento automático por `-quit` não ocorreu em 60 segundos, e apenas o processo de teste foi encerrado. Não foi realizada entrada autenticada no mundo do servidor real.

As evidências locais estão em `pc-client/work/community-3.1.9/validation.json`, `login.png`, `register.png` e `unity-smoke.log`.

## Atualizações identificadas na comunidade

A DLL nova contém os serviços `OffServerSeasonPass`, `OffServerVoice` e `OffServerVoiceStream`, as mensagens `VoiceUp/VoiceDown`, interface de tingimento individual/em lote, compartilhamento de itens, emojis, vínculo Discord, troca de carteira, configuração de atalhos, opções de jogabilidade, contador FPS, progresso de carregamento, transições, carregamento de bundles embutidos/fallback e remapeamento de shaders. A comparação de tipos e o C# recuperado estão em `docs/community-v3.1.9-audit`.

A reconstrução muda também instruções geradas pelo compilador, tipos aninhados e bibliotecas Unity. Portanto, a contagem bruta de métodos diferentes não equivale à quantidade de correções de gameplay. As funções acima foram identificadas diretamente nos símbolos e código da nova versão; a adaptação preserva seu código.

## Ativação do Season Pass

O novo passe é separado do `SeasonSystem` original e reaproveita as telas `WarpRushGroup`, `WarpRushRewards`, `WarpRushRewardPhaseWidget` e `WarpRushRewardItem`. O novo menu é `MenuType.SeasonPass = 37`; ele exige conexão OffServer. Após Welcome, o cliente consulta o passe com atraso de dois segundos; a tela consulta novamente quando aberta.

- `POST /season/pass`: header `Authorization` com o token da sessão de jogo. Corpo inclui `lang`. A resposta usa `ok`, `active`, `now`, `season {id,name,since,until}`, `points`, `tier`, `maxTier`, `pointsPerTier`, `tierPoints`, `claimable`, `tiers`, `daily` e `weekly`.
- Cada etapa tem `tier`, `state`, `box` e `special`. Estados: 0 bloqueada, 1 resgatável, 2 resgatada.
- `POST /season/claim`: mesmo header; `tier=0` resgata todas as etapas disponíveis. A resposta contém `ok`, `reason`, `tiers` e `status` atualizado.

A interface já está presente nesta nova base e foi preservada na distribuição Lost Horizon. O gateway atual do projeto ainda não implementa esses endpoints. Alterar `GetSeasons` em `Player.Event.cs` não ativa esse passe; o backend precisa fornecer temporada, progressão, resgates e persistência. Não foram inventadas temporadas/recompensas nem alterado o servidor nesta adaptação visual.

## Reproduzir

`python pc-client/build_community_pc.py --build` monta uma pasta nova a partir da base 3.1.9; `--build --resume` reaplica a adaptação na pasta gerada. `--verify` confere assets/arquivos, e `--test-login` testa a ponte e renderiza o modal. São usados os pacotes já instalados em `android-client/work/tester-python-deps`, .NET Framework, SDK .NET 9 e o runtime Linux local do projeto pelo WSL.

## Correção do bloqueio ao clicar em Entrar

A falha informada pelo usuário ocorria ao iniciar o runtime, depois da autenticação. O executável modificado no recurso de ícone foi restaurado a partir da comunidade, mantendo o nome Lost Horizon Game.exe. O SHA-256 restaurado é `4d1e2fb109900ec816b1991843188dbd1b6bcc5aba574c3f099cf8933aa1401a`. Original, runtime adaptado e iniciador não possuem assinatura Authenticode; não houve alteração nas políticas do Windows.

Na máquina do projeto, o runtime restaurado iniciou normalmente, carregou Unity 6000.6.3f1 e chegou ao título, sem erros de assembly. Evidência: `pc-client/work/community-3.1.9/unity-runtime-fix.json`. Abra `Lost Horizon.cmd` para usar o iniciador por código-fonte, já que o EXE personalizado do iniciador é bloqueado nesse ambiente.

O modal agora mostra a causa explícita se o Windows impedir a criação do processo (códigos 577, 1260 e 4551). O servidor público respondeu normalmente a /status; a sondagem sem credenciais de /auth/login retornou o esperado 401 invalid_credentials. Não foi usada nem alterada uma conta real.

## Ícone do runtime solicitado após a correção

Os recursos RT_GROUP_ICON e RT_ICON do Lost Horizon.exe foram copiados exatamente para o grupo e idioma existentes no Lost Horizon Game.exe, com seis resoluções. As demais seções do executável permaneceram idênticas. O SHA-256 final é `1252af7516720e4da717e72469b315e6896127f63e82f554b37bdaa0748a43b2`. O runtime com esse ícone iniciou e carregou a cena de título normalmente neste computador, sem bloqueio do Windows ou erros de assembly. A cópia funcional anterior está em `pc-client/work/community-3.1.9/Lost Horizon Game.working-before-icon.exe`. A evidência está em `game-icon-validation.json` na mesma pasta. O builder aplica essa cópia exata de ícones também nas próximas montagens.

## Megafone da seleção de personagem

O botão chama `Durango.System.Platform.ShowNotice()`. Seu link Nexon foi substituído por `https://discord.gg/uHfjEAajq` no cliente instalado e no patch de montagem. A verificação confirmou que os outros 37.023 métodos do assembly instalado permaneceram idênticos nesta alteração. Feche e abra o jogo para carregar a DLL atualizada. Evidência: `pc-client/work/community-3.1.9/discord-link-validation.json`.
