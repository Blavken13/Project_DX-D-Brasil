# Resultado dos testes dos bundles recuperados

Testes locais em 01/10/2026. O servidor, o catálogo de produção e o APK não foram alterados.

**Os 62 candidatos são recursos reais de mesmo nome e caminho interno que faltam
no catálogo atual, com evidência favorável de aproveitamento.** A diferença de CRC
e Hash, isoladamente, não demonstrava incompatibilidade: era necessário examinar
o conteúdo e testar seu carregamento. Ainda não foi comprovada a renderização
no APK brasileiro/Unity 2017.4.34f1 em aparelho real.

## Evidências

| Verificação | Resultado |
|---|---|
| Bundles recuperados íntegros e analisados | 200/200 |
| Arquivos atuais conferidos contra seus SHA-256 | 1.171/1.171 |
| Candidatos cujo nome interno e caminho de asset são os esperados | 62/62 |
| TypeTrees preservados | 62/62 |
| Esquemas de serialização iguais aos controles atuais | 191/191 árvores |
| Texturas decodificadas com dimensões e dados válidos | 99/99 |
| Referências dos modelos atuais resolvidas nos candidatos | 785/785 |
| Referências internas e externas dos candidatos resolvidas no conjunto de teste | Todas |
| Dependências iguais ao catálogo, descontando o preload implícito | 62/62 |
| Carregamento nativo no editor Unity 6000.6.0f1 | 62/62 |
| Materiais carregados pelo Unity com shader e texturas vinculados | 62/62 |
| Texturas decodificadas encontradas também nas ligações nativas dos materiais | 99/99 |
| Erros/exceções de carregamento no teste nativo | 0 |

As 785 referências vêm de 41 bundles atuais. Em 61 candidatos, um consumidor já
disponível referencia exatamente seu CAB e seus PathIDs. O 62º é
`models$pc$male$beard$m_beard_large_short.fbx.bundle`: possui o asset esperado no
catálogo, sem consumidor presente nessa amostra. Sua malha de 51 vértices e seu
material carregaram no teste nativo. Essa distinção explica por que o campo
`missing_static_pass` do relatório vale 61, apesar de os 62 carregarem no Unity.

## O que pode ser aproveitado

São 61 bundles de materiais/texturas e um de modelo de barba. Há recursos para
Brachiosaurus, Compsognathus, Edmontosaurus, Phenacodus, vegetação, terrenos,
objetos e personagem. Os nomes completos estão em
`missing-resource-candidates.json`; referências e hashes estão em `test-results.json`.

- **26 candidatos foram gravados com Unity 2017.4.34f1**, a mesma versão do APK.
  São o primeiro grupo indicado para um teste de integração no cliente.
- **36 candidatos foram gravados com Unity 2017.4.7f1**. Todos preservam TypeTrees
  e usam os mesmos esquemas dos controles atuais, mas precisam de validação no
  cliente 2017.4.34f1 antes da integração. O audit atual do servidor exige
  2017.4.34f1 e rejeitaria esses cabeçalhos; não devem ser falsificados.
- Os **138 bundles com nomes já disponíveis** serviram como controles.
  36 são idênticos byte a byte e 48 têm todos os objetos serializados idênticos.
  Os demais contêm diferenças, apesar de muitos compartilharem CAB e PathIDs.
  Não há motivo para substituí-los pelos arquivos da outra comunidade.

Os candidatos não têm os identificadores do catálogo atual. Uma integração deve
registrar corretamente os novos recursos e sua procedência, atualizar os
metadados pertinentes e testar download, cache, carregamento e aparência no APK.
Renomear os arquivos para os CRCs antigos ou mudar a versão do cabeçalho não
substitui esse trabalho.

Se os 62 forem integrados e aprovados, a disponibilidade projetada passa de
1.171 para 1.233 bundles, e os ausentes de 982 para 920. **Essa integração não foi
realizada nesta etapa.**

## Limites dos testes

O teste nativo usou o editor instalado, **Unity 6000.6.0f1/WindowsEditor**, em
projeto separado, sem interface e sem GPU (`-batchmode -nographics`). Carregou os
bundles Android, seus materiais, texturas e shaders, mas não renderizou cenas.
Isso demonstra que os arquivos são utilizáveis por esse editor; não prova a
aparência final ou o comportamento do runtime Android/Unity 2017.4.34f1.

A própria [documentação técnica da Unity](https://unity.com/blog/engine-platform/unity-asset-bundles-tips-pitfalls)
explica que TypeTrees preservados ajudam no carregamento entre versões, e que
compatibilidade exige testes do conteúdo no runtime pretendido.

## Reprodução e relatórios

Execute com o Python utilizado no projeto, que tem acesso às dependências UnityPy:

```powershell
python android-client/tests/analyze_recovered_bundles.py
python android-client/tests/analyze_bundle_schemas.py
python android-client/tests/run_recovered_unity_tests.py
```

O último comando utiliza o Unity instalado em
`C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe`, configurável por
`--unity` e `--editor-version`. Cria apenas o projeto de teste ignorado pelo Git,
`android-client/work/unity-bundle-tests`.

- `test-results.json`: comparação completa dos candidatos e controles resumidos.
- `schema-tests.json`: presença e correspondência de cada TypeTree.
- `native-unity6-results.json`: resultado produzido diretamente pelo Unity.
- `native-summary.json`: conferência das texturas e materiais, sem renderização.
- `android-client/work/unity-bundle-tests/native-test.log`: log local do editor.
