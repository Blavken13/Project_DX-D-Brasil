# GUIA DE ESTILO — Português do Brasil

## Idioma e voz

- Português do Brasil natural e fluido.
- Acordo Ortográfico vigente.
- Preservar intenção, humor, registro e classificação do original.
- Não impor um tratamento único a todos os personagens; respeitar a voz.
- Nomes próprios só mudam quando houver decisão estabelecida.

## Consistência

- Consultar `docs/GLOSSARIO.md`.
- Reutilizar termos já decididos.
- String ambígua sem contexto fica pendente.

## Elementos técnicos

Nunca remover ou alterar inadvertidamente placeholders (`{0}`, `{name}`, `%s`,
`%d`, `$VAR`), códigos de controle, quebras significativas, markup técnico,
identificadores, chaves, comandos ou nomes de arquivo.

A frase pode ser reorganizada para soar natural desde que esses elementos sejam
preservados.

### Exceção legada observada

`server-pt_BR.json` possui metassintaxe humana entre `<...>` e `[...]` já
localizada. Portanto esses delimitadores não podem ser tratados globalmente
como markup técnico nesse arquivo sem contexto. O teste usa regra conservadora
até uma etapa específica separar metassintaxe humana de markup real.

## Plural e gênero

- Evitar concordância quebrada como “1 itens”.
- Não alterar placeholders para resolver concordância.
- Quando faltarem dados dinâmicos, preferir redação neutra.

## Comprimento

Não há limites formais auditados por componente. Até medi-los:

- preferir texto conciso;
- preservar quebras intencionais;
- revisar visualmente strings longas;
- só transformar limites em regra automática depois de comprovados.

## Encoding

- JSON, Markdown, scripts de suporte e PO do projeto: UTF-8.
- MO e outros formatos binários permanecem binários.
- Não converter encoding legado sem etapa própria.

## Revisão mínima

1. formato/encoding;
2. placeholders/markup;
3. glossário;
4. comprimento quando aplicável;
5. teste visual quando não houver automação equivalente.
