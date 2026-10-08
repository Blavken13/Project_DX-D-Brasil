# Âncora mobile e durabilidade de roupas — 50221

## Entrega

APK: [`LostHorizon-alfa-ancora-50221.apk`](../android-client/dist/LostHorizon-alfa-ancora-50221.apk), ARM64, 317.670.231 bytes.

SHA-256: `b907863d1a9b66226d9dd3542fa68afb533950a792da04d5056b3742762a7bb9`.

Assinado com a mesma chave da versão 50220. Mantém os diagnósticos da 50219 e a correção de ABI da 50220. A comparação com a 50219 verificou 1.769 arquivos preservados: as alterações limitam-se ao manifesto, identificação do diagnóstico, reparo de `libnd.so` e menu de mapa em `libil2cpp.so`.

O APK precisa destas alterações também no servidor para receber os portos disponíveis e a durabilidade restaurada. A publicação no staging é registrada no relatório de deploy da respectiva release.

## Botão de âncora

O menu original omitira `WarpToPort` em ilhas de caça e no refúgio. A nova versão acrescenta o botão original com `icon_map_port`, sua ação e sua condição de disponibilidade. Prioriza a âncora também nas ilhas rurais, urbanas e pessoais. Os outros atalhos continuam acessíveis no seletor original do mesmo botão. Postos avançados já possuíam a âncora e mantêm seu menu.

O servidor informa como conhecidos somente os portos reais do terreno, permitindo usar a âncora antes de caminhar até a jangada. Não revela crateras, fendas ou outros pontos ainda não explorados, não duplica portos conhecidos e não altera o histórico salvo de descobertas.

Na ilha Âncora do tutorial, os portos são excluídos da resposta que habilita o botão, inclusive descobertas antigas. O servidor também recusa `WarpToPort` nessa ilha. A sequência do tutorial e o modo offline original são preservados. Ilhas sem porto real não oferecem esse teleporte.

O reparo nativo verifica o SHA-256 da biblioteca original e as instruções antes de alterá-las. Usa o espaço vazio ao final do segmento executável, amplia esse segmento sem mudar offsets das demais estruturas e preserva o tamanho do arquivo. Não altera metadados gerenciados nem recursos visuais.

## Durabilidade

A classificação antiga reconhecia armas e ferramentas, mas ignorava roupas e acessórios com a tag nativa `armor`. Agora reconhece roupas funcionais e esses acessórios; itens com `equipment_avatar` continuam cosméticos, sem desgaste de defesa.

Os dados originais disponíveis trazem categorias, materiais e deltas de desgaste, mas não os máximos históricos de durabilidade por peça. Portanto, os valores abaixo são uma restauração pelo modelo de capacidade já adotado pelo servidor, e não uma reprodução de números originais confirmados pela wiki.

| Família/material | Capacidade base | Máximo no nível 60 |
| --- | ---: | ---: |
| Roupa inicial simples (`clothes_novice`) | 40 | 158,4 |
| Outras roupas funcionais; osso, chifre e presa | 80 | 222,4 |
| Metal, bronze/latão, ferro e aço, identificados pelo protótipo | 120 | 286,4 |

Fórmula: `(base + clamp(nível, 1, 70) - 1) × 1,6`. A identificação de material usa os mesmos termos do identificador de protótipo já adotados para armas/ferramentas. A tabela aplica-se somente a níveis permitidos para cada protótipo.

O [catálogo por item e faixa de nível](durabilidade-roupas-armaduras.csv) foi exportado pelo próprio teste do servidor. Contém 393 variantes funcionais e 146 cosméticas, com categoria, família, limites de nível e capacidade em cada limite. O valor 1 nos cosméticos é a representação anterior preservada, sem desconto por defesa.

Cada golpe que acerta desconta `0,064`, a delta `durability.deltas.defense` do `constants.json` original, de cada peça funcional equipada. Esquivas não aplicam esse desgaste. Roupas guardadas na mochila não sofrem desgaste de defesa. Uma mesma peça não é descontada duas vezes se um save a referencia em mais de um slot.

A normalização existente restaura roupas antigas no inventário, bolsas de animais e demais caminhos que já normalizam itens, preservando a fração de durabilidade. Peças que já possuem capacidade real não são redefinidas. Peças quebradas ficam em zero e continuam disponíveis para reparo com kit. Reconectar não renova a durabilidade.

## Validação

- Build Release do servidor: passou; dois avisos anteriores ao ajuste.
- `--polish-check`: 1.249 verificações, incluindo catálogo completo, migração, persistência, desgaste, reparo pelo TCP e bloqueio da ilha Âncora.
- `--world-check`: 2.795 verificações de mapa, viagens, interação e combate.
- `--fauna-check`: 404 verificações.
- `--gameplay-check`: 58 verificações, incluindo continuidade do tutorial.
- Âncora ARM64: oito casos executados em Unicorn, verificando ordem dos botões, manutenção dos atalhos, limites dos arrays, retorno ao código original e preservação de stack/frame.
- ABI dos diagnósticos: 88 verificações, mantendo a correção da 50220.
- APK: integridade ZIP, comparação completa dos arquivos, assinatura v1/v2/v3 e alinhamento ZIP de 16 KiB verificados.

Não havia aparelho conectado por ADB. A aparência do menu e o toque na âncora ainda precisam de teste físico usando este APK e o servidor atualizado.
