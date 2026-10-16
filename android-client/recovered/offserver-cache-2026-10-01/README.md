# Bundles recuperados do segundo APK da comunidade

Recuperação em 01/10/2026, a pedido do usuário, a partir do cache externo de
`com.durango.offserver` no celular conectado. O dispositivo foi apenas lido.
Nenhum dado de login, conta, personagem ou cache HTTP foi copiado.

**Atualização após testes:** os 62 candidatos têm os assets esperados, suas
referências resolvem e todos carregaram no editor Unity instalado. São úteis
para uma possível integração, apesar dos identificadores diferentes. Consulte
[TESTES.md](TESTES.md) para as evidências e os limites da validação no APK.

## Resultado

- 200 bundles copiados: 167,150,711 bytes (159,4 MiB).
- SHA-256 de todos os 200 payloads conferido contra o arquivo original no celular.
- Todos os bundles puderam ser analisados; plataforma Android (13).
- 138 bundles gravados com Unity 2017.4.7f1; 62 com Unity 2017.4.34f1.
- 200 nomes lógicos existem no catálogo atual do APK brasileiro.
- 62 nomes lógicos correspondem a recursos atualmente ausentes: 36 da versão
  2017.4.7f1 e 26 da versão 2017.4.34f1.
- Nenhum CRC ou Hash do cache corresponde ao recurso de mesmo nome no catálogo atual.
  Portanto, não há substituições diretas verificadas nesta recuperação. Os arquivos
  ficam separados para investigação/adaptação; versão igual, sozinha, não comprova
  que referências, dependências e conteúdo correspondem ao nosso catálogo.
- O cache atual do APK brasileiro também foi consultado: 510 payloads e nenhum
  candidato com CRC de um recurso ainda ausente no servidor.

O catálogo/servidor não foi alterado: continuam 1.171 recursos disponíveis e
982 ausentes no inventário de referência.

## Arquivos

- `bundles-android/`: 200 payloads originais, preservando nome e CRC do segundo aplicativo.
- `UnityCache.tar`: cópia do cache Unity Shared, incluindo metadados Unity; não inclui
  o cache HTTP, registros de conta ou outras pastas do aplicativo.
- `inventory.json`: caminho de origem, SHA-256, tamanho, versão, plataforma e
  comparação com o catálogo atual para cada bundle.
- `missing-resource-candidates.json`: os 62 candidatos de mesmo nome lógico que faltam.
- `source-sha256.txt` e `SHA256SUMS.txt`: verificações no celular e nas cópias locais.
- `public-asset-endpoints.json`: apenas URLs públicas de recursos, sem parâmetros,
  encontradas nos diagnósticos; não contém o log completo.

Os binários e o TAR ficam ignorados pelo Git. Não devem ser renomeados para os CRCs
atuais nem enviados ao staging como se fossem os bundles esperados. O catálogo de
referência tem SHA-256 `64b1ceab2f696a1ac07410bf7e95cc9d59c0c94f7bd0d118da369fbb19708785`.
O TAR tem SHA-256 `fa1f716e590de912056a10f7b49b685d2042cf331603e0887a7812a307bfecde`.
