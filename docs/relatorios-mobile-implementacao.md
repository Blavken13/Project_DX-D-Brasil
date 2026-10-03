# Relatórios de erros Android — implementação e testes

## Versão oficial para staging (50212)

APK assinado: `android-client/dist/LostHorizon-alfa.apk`, versão `5.2.1-losthorizon-alfa-reports3`, código `50212`. O builder usa por padrão `https://179.197.72.129/client-reports/mobile`. Login, jogo e relatórios usam o staging. A tela de diagnóstico mostra a versão instalada, o destino, pendências e o resultado da última tentativa.

O painel continua em `http://179.197.72.129:8190/admin/`, aba **Relatórios de erros**. Para testar, registre um problema visual no diagnóstico e permaneça no login até a confirmação. Se houver espera por uma tentativa anterior, use **Tentar envio agora**. Consulte os detalhes e baixe todos os registros em CSV ou JSON. Não é necessário provocar um crash.

`deploy/staging/Caddyfile` e `compose.yml` publicam somente ingestão e status por HTTPS, com validação normal de certificado, emissão e renovação ACME automáticas e volumes persistentes para os certificados. As portas TCP 80 e 443 precisam estar acessíveis. O proxy usa endereço interno fixo `172.29.91.2`; o servidor confia em `X-Forwarded-For` somente desse endereço para aplicar limites por IP. Cabeçalhos enviados diretamente ao gateway não substituem o IP real. Após subir o servidor, suba também `reports-proxy` com o mesmo arquivo Compose e ambiente.

### Aparência inicial masculina

No aparelho conectado, o personagem estava preto e o staging respondia 404 ao bundle original `models$pc$male$body$m_body_beginner_leaf.fbx.119999585ccb6817cf2ce148ba55755d.bundle`. Foi recuperado o arquivo Android original (86.110 bytes), sem alterar shaders ou inserir erros visuais. O manifesto em `android-client/recovered/upstream-android/inventory.json` registra a origem e o SHA-256 `3fb3f5df5d34664184589c060ad3edb9a566d757af9cd3bc49f87c443daebd30`.

Para reconstruir os recursos, execute `android-client/recover_starter_appearance.py` após preparar o catálogo Android. Os binários permanecem fora do Git e precisam acompanhar o deploy dos assetbundles. Validação: `android-client/tests/verify_female_appearance.py --starter-male` verifica as duas dependências, quatro texturas decodificadas e 216 referências resolvidas com o preload original. Reinicie a sessão do jogo para repetir o carregamento que havia falhado; a reposição não garante atualizar um personagem já carregado.

A implementação afeta apenas o launcher do APK original ARM64, as rotas do servidor e o painel administrativo. Não altera o cliente PC, o motor Unity, os shaders ou os protocolos de jogo. O recebimento usa o gateway existente, com tarefas e limites de concorrência separados do ciclo de simulação.

## Coleta e custo no celular

- Na próxima abertura, consulta `ApplicationExitInfo` (Android 11+) para a sessão anterior: falha Java, falha nativa, ANR, falta de memória e sinal. Fechamentos normais não viram erros automáticos.
- Android 12+ pode disponibilizar o tombstone nativo; extrai apenas sinal, biblioteca, PC relativo e BuildID da thread que falhou, com leitura limitada a 4 MiB. A disponibilidade depende do Android. Em versões anteriores, as exceções Java registradas pelo launcher continuam úteis; não existe garantia de recuperar falhas nativas ou falta de memória.
- Inclui versão do APK e metadados capturados **antes** da sessão anterior, aparelho, Android, ABI, página do kernel, RAM, perfil de compatibilidade, GPU quando disponível, última etapa, até 64 etapas e frames limitados. Não atribui os dados de uma instalação nova a uma sessão antiga sem metadados correspondentes.
- Guarda UUIDs aleatórios de instalação, sessão e evento. Não lê identificadores de hardware, contas, tickets, senha, conversas, mensagens de exceção, memória do tombstone ou logcat. Não acrescenta hooks nativos nem coleta contínua durante as partidas.
- Fila privada e durável: até 20 relatórios, 48 KiB por relatório e 1 MiB no total. Se cheia, preserva os eventos existentes; a coleta é limitada e não garante guardar todos os eventos de uma sequência muito longa sem conexão.
- Um único trabalhador com prioridade baixa, até três tentativas por processo, timeouts de conexão/leitura, espera crescente persistida até uma hora. Envia somente enquanto a tela de login está visível. Ao sair dela ou começar o jogo, cancela a requisição e impede novas tentativas.
- Só remove um relatório pendente após HTTP 200 contendo confirmação do mesmo UUID. Falhas de rede e HTTP 429/503 preservam a fila. Rejeições definitivas 400/409/413/415 vão para uma pasta privada separada, limitada a cinco arquivos, para não bloquear os seguintes.
- Diagnóstico → **Envio de relatórios** permite consultar pendências e ativar/desativar o envio automático. A coleta local continua disponível quando o envio está desativado. **Reportar problema visual** registra personagem preto, textura ausente ou outro problema visual; o registro manual não confirma uma causa.

Os relatórios de crash são recuperados na reabertura do aplicativo: não se tenta fazer rede enquanto o processo está falhando. Erros internos tratados pelo Unity e defeitos visuais silenciosos não são automaticamente exceções Java; a opção visual e os dados de GPU ajudam a investigá-los.

## Rotas

| Método e rota | Uso |
| --- | --- |
| `POST /client-reports/mobile` | Ingestão Android sem login, inclusive falhas anteriores ao login |
| `GET /admin/client-reports` | Listagem paginada, estatísticas e grupos |
| `GET /admin/client-reports/{event_id}` | Relatório completo |
| `GET /admin/client-reports/export?format=csv` | Tabela completa de todos os registros armazenados |
| `GET /admin/client-reports/export?format=json` | Todos os registros estruturados |

Formato de ingestão: `{"schema_version":1,"report":{...}}`. Um evento por requisição. Sucesso: `{"accepted":["uuid-do-evento"]}`. Conteúdo diferente usando o mesmo UUID retorna 409. O recebimento confirma somente depois de gravar, sincronizar e renomear atomicamente o arquivo.

Todas as rotas administrativas usam a sessão existente (`X-Admin-Session`) e a verificação de origem do painel. A ingestão aceita JSON UTF-8 sem compressão, até 64 KiB por requisição, profundidade limitada e campos explícitos. Existem dois trabalhadores de ingestão e dois administrativos; corpos lentos expiram em cinco segundos. O excesso de concorrência retorna 503. Eventos novos têm limites de 10/minuto por instalação, 60/minuto por IP e 600/minuto globais; retransmissões já confirmadas são idempotentes.

Filtros da listagem: `event_type`, `version_name`, `model`, `signature`; `offset` controla páginas de 100 registros. Os grupos mostram até 100 assinaturas mais frequentes dos resultados filtrados, com contagem de instalações, modelos e versões. Assinaturas de falhas nativas usam biblioteca, PC relativo e BuildID, sem o endereço absoluto. A mesma assinatura indica semelhança, não prova a causa.

CSV usa UTF-8 com BOM, separador `;`, todas as colunas, JSON em células de listas, aspas e neutralização de fórmulas de planilha. CSV e JSON sempre exportam **todos os registros retidos**, independentemente da página e dos filtros. Nenhum evento é agregado ou descartado na exportação.

Persistência: `AppData-nx/mobile-reports`, fora das tabelas públicas e dos saves dos personagens. Retenção de 30 dias, até 10.000 eventos ou 128 MiB; ao atingir limites, remove os mais antigos. No staging, o volume existente de `AppData-nx` preserva esses arquivos entre deploys.

## Teste local preparado

APK: `android-client/dist/LostHorizon-diagnostico-local.apk`, versão `5.2.1-losthorizon-alfa-reports2`, código `50211`. O destino dos relatórios nesta compilação é `http://192.168.1.4:8190/client-reports/mobile` (IP Wi-Fi atual do computador). O login e o jogo mantêm os endereços já existentes; esta compilação direciona **somente os relatórios** ao servidor local. Portanto, personagens conectados ao staging não aparecem como jogadores online no painel local.

1. Abra o painel em `http://127.0.0.1:8190/admin/` e entre com as credenciais administrativas locais existentes. A aba é **Relatórios de erros**.
2. Instale o APK no aparelho conectado à mesma rede Wi-Fi. Abra `http://192.168.1.4:8190/status` no navegador do celular para verificar alcance do servidor.
3. Na tela de login do APK, use Diagnóstico → Reportar problema visual → Personagem preto. Aguarde alguns segundos sem iniciar o jogo. Consulte a aba do painel e abra os detalhes.
4. Baixe **todos (CSV)** e **todos (JSON)** e confira os campos completos. Repita com mais de um aparelho para comparar modelos, versões, GPU e assinaturas.
5. Para verificar a fila offline, desligue a rede, registre outro problema visual, volte a conectar e reabra o aplicativo. Se houve falha de envio, a espera crescente pode adiar a tentativa. Confira o contador em Envio de relatórios.
6. Após um fechamento real inesperado, reabra o APK e permaneça na tela de login para a coleta/envio. Fechar normalmente ou forçar parada não deve ser usado como prova de crash capturado.

Se nada aparecer, abra **Diagnóstico → Envio de relatórios**. A versão `reports2` exibe versão instalada, destino, opção de envio, pendências, última tentativa, resultado e espera para nova tentativa. **Tentar envio agora** elimina a espera e solicita uma tentativa adicional (até três manuais por abertura), mantendo a restrição de enviar somente durante o login. Esse botão não cria erros fictícios: sem pendências, use Reportar problema visual para testar. Uma tentativa cancelada pela entrada no jogo preserva o relatório.

A conectividade deve ser verificada **no navegador do celular**. Uma resposta obtida no próprio PC não comprova que o firewall aceita conexões vindas do Wi-Fi. Na inspeção deste computador, as regras de Node encontradas estavam habilitadas para o perfil Público, enquanto o Wi-Fi usa o perfil Privado; esse ponto ainda precisa ser confirmado pelo teste no aparelho. Não foram alteradas regras de firewall automaticamente.

Para iniciar novamente o servidor, use `iniciar-servidor-local.bat` / `tools/iniciar-servidor-local.ps1`. A sessão de teste atual pode usar o runtime Linux pelo WSL, que passou na integração HTTP. Logs do processo local: `tools/reports-server.stdout.log`, `tools/reports-server.stderr.log`; PID do processo de lançamento: `tools/reports-server.pid`.

Neste computador o WSL encaminha localhost, mas não o IP Wi-Fi. Foi preparado um encaminhador local limitado às rotas de diagnóstico e `/status`; ele não abre o painel pela rede Wi-Fi. Para iniciá-lo novamente com o servidor ativo:

```powershell
node android-client/tests/local-report-proxy.mjs --listen 192.168.1.4 --port 8190 --upstream http://127.0.0.1:8190
```

Logs: `tools/reports-proxy.stdout.log`, `tools/reports-proxy.stderr.log`; PID: `tools/reports-proxy.pid`. Não é parte do servidor de produção e não exige alterar a configuração de rede do Windows. Se o IP do computador mudar, atualize o endereço do encaminhador e recompile o APK.

Para recompilar para outro IP, use o Python e o SDK Android já configurados para este projeto:

```powershell
$env:LH_DIAGNOSTICS_ENDPOINT='http://192.168.1.4:8190/client-reports/mobile'
$env:LH_DIAGNOSTICS_LOCAL='1'
$env:LH_APK_OUTPUT='LostHorizon-diagnostico-local.apk'
$env:PYTHONPATH=Join-Path $PWD 'android-client/work/python-deps'
python android-client/build_original_apk.py
```

HTTP só é permitido por esta configuração explícita de teste com IP privado/local. Para uma compilação distribuída a testers fora da rede, remova `LH_DIAGNOSTICS_LOCAL` e use o destino HTTPS padrão de staging ou configure outra URL **HTTPS** em `LH_DIAGNOSTICS_ENDPOINT`. Definir essa variável como uma string vazia desativa o destino de upload, preservando a coleta/fila local. Não há segredo estático de autenticação dentro do APK.

## Validação

- `dotnet build Durango-CustomServer/server/DurangoServer.csproj -c Debug --no-restore -v minimal`.
- `node Durango-CustomServer/server/tests/admin-panel-check.mjs` (com `DURANGO_ADMIN_TEST_WSL=Ubuntu-24.04` e `DURANGO_ADMIN_TEST_CONFIGURATION=Release` neste ambiente, após compilar Release): 94 verificações HTTP em dados temporários, incluindo persistência, deduplicação, limites de envio, permissões, paginação, exportação de 118 eventos, fórmulas CSV, corpos lentos e falha de disco. O build Debug neste runtime Linux encontrou `InvalidProgramException` na prévia de anexos do sistema de emails; o build Release passou na mesma operação.
- `MobileReportsTest.java`: 35 verificações da fila e transporte HTTP real em loopback, incluindo desconexão ao entrar no jogo, limites e quarentena.
- `TombstoneSummaryTest.java`: sinais, frames, BuildIDs longos, exclusão de campos sensíveis e protobuf inválido.
- Compilação Java e D8 para API mínima 21; APK assinado e verificação de conteúdo preservado pelo builder original.

Os testes de host não reproduzem drivers, versões do Android ou o runtime Unity no aparelho. A validação em celulares reais é o próximo teste; esta implementação coleta evidências para investigar as falhas e não altera a renderização para tentar corrigi-las.
