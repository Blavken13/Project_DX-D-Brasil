# Viabilidade de reconstruir o cliente na Unity

Análise de 03/10/2026, baseada no checkout local e na documentação oficial consultada nessa data.

## Parecer

É tecnicamente plausível construir um projeto Unity editável que reaproveite parte significativa do cliente e gere Android e Windows. A viabilidade do jogo completo ainda depende de uma prova de integração. O cliente atual já é Unity; o trabalho necessário é reconstruir seu projeto de desenvolvimento e modernizá-lo.

Não existe no material examinado um projeto original completo, com cenas, fontes de shaders, metadados de importação e configurações pronto para abrir e recompilar. Copiar o APK ou a pasta do executável para um projeto novo não recria esse projeto. Trocar somente as bibliotecas do motor também não substitui a reconstrução.

A recomendação é manter a manutenção do APK atual e iniciar uma prova isolada com Unity moderna: personagem renderizado, trecho de mapa, autenticação e conexão ao servidor. Só ampliar a migração depois de validar esses quatro pontos em Android real e Windows.

## Evidências locais

| Material | Constatação | Implicação |
| --- | --- | --- |
| `Durango original/` | APK extraído com DEX, bibliotecas ARM64, metadados IL2CPP e arquivos Unity serializados | É entrada compilada; não é o projeto do editor |
| `android-client/build_original_apk.py` | Copia a base, compila launcher/ponte, modifica recursos selecionados, empacota e assina | O build atual não recompila o jogo inteiro pelo editor Unity |
| `android-client/work/original-nexon/verification.json` | Motor identificado como `2017.4.34f1` | A versão comercial 5.2.1 não identifica o motor |
| `Durango-OffServer/player.log` | Inicialização com Unity `2017.4.34f1` | O cliente PC também usa o motor legado |
| `Durango-OffServer/_client_src/` | 3.765 arquivos C#, aproximadamente 10,65 MiB | Base relevante para recuperar a lógica do cliente; não certifica compilação moderna nem equivalência ao Android |
| `Assembly-CSharp.csproj` | `net35`, `LangVersion=12.0`, referências às DLLs Unity antigas | Precisa adaptar referências, linguagem e APIs ao editor escolhido |
| Fontes C# | 3.277 arquivos com declaração `namespace Nome;` | Esse formato exige C# 10; o projeto de teste do editor instalado usa C# 9 |
| `Durango.System/Platform.cs` | Inicialização explícita com `new Platform_PC()` | Selecionar Android como destino não adapta sozinho autenticação e plataforma |
| `Platform_Android.cs` | Implementação local limitada à solicitação de permissões | É necessário construir e validar a integração mobile de conta, sessão, interface e ciclo de vida |
| `android-client/dist/assetbundles-android-5.2.1.json` | 2.153 bundles esperados, 1.608 disponíveis, 545 ausentes; 603.598.424 bytes disponíveis | O conteúdo Android local é parcial |
| Mesmo inventário | Zero bundles ausentes classificados diretamente com prioridade >= 500, mas 166 dependências ausentes desse grupo | `required_missing=0` não significa conteúdo completo |
| `native-unity6-results.json` e `native-summary.json` | 62 bundles candidatos carregados no editor `6000.6.0f1`, zero erros, 99 texturas decodificadas; renderização não testada | Evidência de reaproveitamento de uma amostra, sem comprovar imagem, animação, execução do jogo ou APK |
| `android-client/shaders/` | Uma fonte de shader do jogo: `Floor2AlphaUV.shader`, além do script de build | Não foi encontrado o conjunto completo de fontes dos shaders originais |
| Instalação local | Unity `6000.6.0f1`, módulos Android e Windows presentes | Já existe infraestrutura inicial; SDK/NDK/JDK, licença e toolchain devem ser verificados no primeiro build |
| `android-client/work/unity-bundle-tests/` | Projeto Unity de teste com configuração e script de inspeção | Podemos automatizar testes de conteúdo; ele não contém o jogo reconstruído |

Os números de recursos vêm do relatório local existente, conferido nesta análise. Não houve nova auditoria integral dos hashes nem consulta ao inventário efetivamente publicado no servidor remoto.

O Git não contém `ProjectVersion.txt`, cenas `.unity` ou arquivos `.meta` do projeto completo do jogo. A busca local nos diretórios de clientes encontrou o projeto isolado de teste. Alguns diretórios de ferramentas nativas tiveram acesso negado; não foram considerados evidência de um projeto recuperável.

## Fechamentos e personagem preto

Os relatos não permitem atribuir todos os sintomas à mesma causa.

- **Fecha após login ou splash:** investigar biblioteca da pilha nativa, etapa de inicialização, vídeo, driver gráfico, falta de memória e tamanho real da página do kernel. O APK é ARM64 e tem motor/plugins antigos. Android recente pode usar páginas de 4 ou 16 KB; a versão do sistema sozinha não determina isso.
- **Personagem preto:** neste projeto há histórico documentado de modelos, materiais e texturas ausentes, corrigidos por recuperação e publicação de bundles. Para o relato atual, ainda é preciso distinguir download/cache/dependência ausente de incompatibilidade de shader ou GPU.
- **Funciona em outro aparelho:** não elimina nenhuma dessas hipóteses. Conteúdo já presente no cache, GPU, driver, sistema e configuração de memória podem diferir.

A revisão 50209 documenta pontes próprias corrigidas para páginas reais, instalação antes do UnityPlayer, liberação do WebView, perfil gráfico e exportação do fechamento anterior. Essas medidas não modernizam `libunity.so`, `libmain.so` e todos os plugins legados. Não basta alinhar o ZIP do APK para garantir suporte a 16 KB.

Antes de modificar novamente o APK, obter os relatórios em **Diagnóstico → Compartilhar relatório**, com versão instalada e opções gráficas/vídeo utilizadas. Para a aparência preta, registrar personagem/roupa/cabelo e conferir os pedidos exatos de bundles e suas dependências. Comparar os mesmos passos nos aparelhos que funcionam.

O log histórico `work/unity6/crash-native.log` contém um SIGSEGV de um cliente Unity `6000.6.3f1`, em outro pacote, em 01/10. Ele comprova que a experiência anterior também teve falha; não identifica a causa dos relatos atuais do APK Unity 2017. O README registra problemas visuais na experiência Unity 6.

## O que pode ser preservado

| Parte | Reaproveitamento proposto | Condição |
| --- | --- | --- |
| Servidor e regras já implementadas | Manter servidor .NET separado do cliente Unity | Preservar contratos e protocolo |
| Contas, personagens, inventários e missões salvos | Continuar usando os mesmos dados do servidor | Manter IDs, autenticação e significado das mensagens; não exige transportar o banco para Unity |
| Rede C# | Adaptar conexão TCP, cabeçalhos, mensagens, MessagePack e Snappy existentes | Verificar dependências, reflexão, AOT/IL2CPP e compatibilidade de bytes |
| Modelos, texturas, animações e prefabs disponíveis | Extrair/importar ou testar carregamento controlado dos bundles | Recuperar referências, scripts associados e validar por plataforma |
| Tradução, tabelas, identidade visual e vídeos | Reutilizar os arquivos existentes compatíveis | Reconstruir ligações e substituir o player antigo quando necessário |
| Interface e lógica C# | Portar gradualmente | Adaptar NGUI, APIs antigas, plataforma e dependências |
| Patches ARM64 e offsets IL2CPP | Reimplementar o comportamento em código do novo cliente | Endereços e instruções do binário antigo não são portáveis |
| Recursos ausentes | Recuperar equivalentes verificados ou recriar | Uma engine nova não produz os 545 bundles que faltam |

O caminho mais promissor começa no cliente PC: ele tem assemblies gerenciados e uma grande base C# legível. O Android tem o jogo compilado em IL2CPP; seus metadados ajudam a comparar classes e referências, mas não equivalem a fontes completas dos métodos. O PC também contém adaptações próprias e não deve ser considerado uma cópia integral da implementação mobile.

## Principais dificuldades

1. **Cenas e referências:** recuperar prefabs/cenas serializados, componentes, campos, identidade de assemblies/classes e relações entre objetos. Um modelo importado não recupera sozinho comportamento ou missões.
2. **Código e bibliotecas:** substituir as referências às DLLs Unity 2017 pelas do editor, converter sintaxe incompatível, portar APIs e avaliar dependências externas. Não copiar `UnityEngine.dll`, `mscorlib.dll` e DLLs `System.*` antigas para o novo projeto.
3. **Renderização:** recuperar ou reconstruir shaders LitSphere, terreno, personagem e efeitos; garantir variantes e formatos de textura. Começar com Built-in é uma hipótese de menor retrabalho. A migração para URP/HDRP seria outro projeto e precisa de justificativa própria.
4. **Plugins:** avaliar Wwise/AkSoundEngine, EasyMovieTexture/BlueDove, Snappy e integrações Windows como `user32.dll`, Dwarf e FFmpeg. DLL Windows não serve como biblioteca Android. Um adaptador para `VideoPlayer` é candidato para substituir o vídeo legado, preservando eventos de conclusão e pular.
5. **Android real:** validar suspend/resume, voltar, toque, orientação, vídeo e rede. Compilar sem erros não garante compatibilidade de GPU ou memória.
6. **AOT:** testar serializadores e caminhos de reflexão no IL2CPP, retenção de tipos e dependências nativas. Um teste Windows/Mono não certifica Android/IL2CPP.
7. **Catálogo e cache:** se bundles forem recompilados, mudam hashes/CRCs e possivelmente referências. Versionar catálogos do novo cliente e manter o legado durante a transição; não publicar bundles novos sob nomes/hashes antigos.

AssetRipper é uma opção a avaliar para recuperar assets em formatos do editor. Sua exportação precisa ser inspecionada e integrada aos scripts; não é uma promessa de recuperar automaticamente um jogo compilável. UnityPy já está presente no fluxo local e é útil para inventário e extração, sem substituir reconstrução de projeto.

## Alternativas

| Caminho | Viabilidade | Complexidade | Avaliação |
| --- | --- | --- | --- |
| Manutenção do APK Unity 2017 | Já funciona em parte dos aparelhos | Menor para problemas localizados; limitada para incompatibilidade do motor | Adequado para continuar testes e diagnosticar os relatos |
| Recuperar projeto em Unity 2017 e depois atualizar | Condicional à qualidade da recuperação | Muito alta; enfrenta editor/toolchain antigos e migração posterior | Usar uma versão antiga como referência quando ajudar a recuperar conteúdo, sem tomá-la como solução de compatibilidade moderna |
| Reconstruir gradualmente em Unity moderna | Plausível, ainda sem validação do jogo completo | Alta | Melhor candidato para cliente sustentável Android/Windows |
| Migrar para outra engine | Possível como reimplementação | Muito alta | Acrescenta conversão de scripts/componentes, renderização, interface e ferramentas; não há vantagem demonstrada para este material |

Para a prova, usar a instalação Unity 6 disponível evita instalação preliminar. Para o projeto duradouro, escolher e fixar uma versão suportada após testar shaders/plugins; a versão instalada não foi classificada aqui como LTS nem como a melhor opção definitiva.

## Terminal e automação

É possível criar pastas/configurações, transformar fontes, importar assets, gerar cenas por scripts de editor, compilar bundles, APK/AAB e Windows por terminal. A Unity continua sendo o compilador/editor executado em modo batch. Não basta `dotnet build` para gerar um Player Unity.

Exemplo conceitual para uma etapa futura; `unity-client` e `ClientBuild.Android` ainda não existem:

```powershell
& 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe' `
  -batchmode -quit `
  -projectPath 'C:/code/ProjectDX/Project_DX-D-Brasil/unity-client' `
  -buildTarget Android `
  -executeMethod ClientBuild.Android `
  -logFile 'C:/code/ProjectDX/Project_DX-D-Brasil/unity-client/android-build.log'
```

O método estático teria de configurar PlayerSettings, cenas, ABI, assinatura e chamar `BuildPipeline.BuildPlayer`. Outro método faria Windows. Android requer SDK/NDK/JDK compatíveis; Windows/IL2CPP requer toolchain C++ compatível; o editor requer licença válida. Preservar a chave/package para atualização da instalação atual exige também projetar compatibilidade dos dados locais.

Não usar `-nographics` como critério de aprovação visual. Podemos iniciar builds e testes por terminal e automatizar capturas, mas personagem preto, aparência, animação, interface e estabilidade precisam ser observados em runtime com GPU e aparelhos representativos.

## Etapas e estimativas

Estimativas de engenharia, não prazos demonstrados por compilação. Referência: um desenvolvedor experiente dedicado cerca de 40 horas/semana, acesso aos arquivos locais e disponibilidade de testers. Não incluem recriar todo o conteúdo que eventualmente não possa ser recuperado.

| Entrega | Prazo indicativo | Critério de conclusão |
| --- | --- | --- |
| Diagnóstico dos relatos atuais | 2–5 dias úteis após receber evidências dos aparelhos | Classificar falhas e localizar bundles/frames relevantes; corrigir depende da causa |
| Prova isolada de migração | 1–2 semanas, podendo chegar a 3–4 com bloqueios de shaders/plugins | Projeto recompilável, personagem com texturas/animação, trecho de mapa, login e handshake no servidor local; demonstração Android e Windows |
| Alfa limitado a um mapa/tutorial | 8–16 semanas no total desde o início, se a prova passar | Entrada no mundo, movimento, UI essencial, inventário e missões do recorte, com testes em vários aparelhos |
| Migração ampla do cliente existente | 6–12 meses ou mais no total | Sistemas, mapas e plugins necessários portados e cobertura de compatibilidade; depende da recuperação de recursos |
| Outra engine com escopo semelhante | Tendência de prazo maior que a reconstrução Unity | Depende de uma investigação própria; não há base para um orçamento confiável nesta análise |

Essas linhas são marcos de escopos crescentes, não estimativas para somar. Automação ajuda nas tarefas repetitivas, mas não elimina a investigação de referências, shaders e plugins. Mais desenvolvedores não reduzem o prazo proporcionalmente.

## Prova recomendada e decisão de continuidade

1. Criar um projeto separado, com fontes transformadas por scripts e entradas originais preservadas. O `.csproj` atual do cliente PC possui um alvo pós-build que copia `Assembly-CSharp.dll` para o jogo existente; não usá-lo diretamente como teste de migração.
2. Recuperar um personagem completo e um trecho do tutorial, incluindo dependências. Renderizar em Windows e Android, com shader explícito, texturas, rig e animação.
3. Implementar login brasileiro e handshake `GetClock → Auth → Ready`, mantendo cabeçalhos, MessagePack/Snappy e IDs esperados pelo servidor local.
4. Verificar pelo menos um aparelho Adreno, um Mali e o modelo que apresenta falha; incluir ambiente de páginas de 16 KB e testes de retomada/download/cache.
5. Recompilar Android e Windows de forma repetível pelo terminal. Guardar logs e capturas, com catálogo separado quando necessário.

Avançar para o alfa se os recursos essenciais puderem ser recuperados, as dependências nativas tiverem caminho viável e o recorte funcionar nos dois destinos. Se a prova emperrar, registrar quais componentes exigem reescrita e revisar escopo/prazo antes de uma migração ampla.

## Fontes e limites

- [Unity: automação e argumentos do editor](https://docs.unity3d.com/6000.0/Documentation/Manual/EditorCommandLineArguments.html): execução batch e métodos estáticos de editor.
- [Unity: compatibilidade e scripts de AssetBundles](https://docs.unity3d.com/6000.0/Documentation/Manual/AssetBundlesIntro.html): compatibilidade entre versões é condicional; classes devem existir no Player e corresponder a assembly/namespace/nome.
- [Unity: organização de AssetBundles](https://docs.unity3d.com/6000.0/Documentation/Manual/AssetBundles-Preparing.html): bundles são específicos por plataforma; carregar no editor não certifica renderização no destino.
- [Unity 6.6: linguagem C#](https://docs.unity3d.com/6000.6/Documentation/Manual/csharp-compiler.html): linguagem padrão C# 9.
- [Unity: IL2CPP](https://docs.unity3d.com/6000.0/Documentation/Manual/il2cpp-introduction.html): assemblies gerenciados são convertidos em C++ e compilados para a plataforma.
- [Android: páginas de 16 KB](https://developer.android.com/guide/practices/page-sizes): revisar empacotamento, bibliotecas nativas e comportamento em execução.
- [AssetRipper: repositório oficial](https://github.com/AssetRipper/AssetRipper): ferramenta de análise e conversão de assets; qualidade de suporte varia.

Esta etapa fez inspeção estática, contagem de fontes e conferência dos relatórios existentes. Não instalou ferramentas, não executou uma exportação/recompilação completa do jogo, não alterou APK/cliente/servidor/saves e não reproduziu os relatos nos aparelhos afetados. O único arquivo novo é este parecer.
