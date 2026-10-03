# Correio administrativo com anexos

O correio usa o protocolo nativo `Mails`, `MailPut`, `AcceptMails`, `MarkMailsAsRead` e `DeleteMails`. O servidor envia a caixa no login; novas entregas chegam imediatamente aos jogadores conectados e ficam disponíveis no próximo acesso dos demais. Não exige SMTP: são mensagens dentro do jogo.

## Enviar pelo painel

Abra **Operações → Enviar email no jogo**, selecione o destinatário e atualize o JSON. Preencha título e mensagem, acrescente os anexos e clique em **Validar e visualizar**. Confira a quantidade de destinatários e itens antes de enviar.

Exemplo de entrega de compra para um personagem:

```json
{
  "target": "player",
  "recipient_id": "ID_DO_PERSONAGEM",
  "subject": "Sua compra chegou",
  "message": "Obrigado pela compra! Clique em Receber para resgatar os itens.",
  "type": "purchase",
  "items": [
    { "name": "fatigue_drug_store", "quantity": 2, "level": 1 }
  ]
}
```

`name` aceita o nome exato exibido no catálogo ou o identificador do protótipo. Também é possível usar `prototype_id` no lugar de `name`. Nomes ambíguos são recusados e o servidor apresenta os identificadores possíveis; o seletor de anexos já inclui o identificador correto. Cada unidade recebe um ID de item próprio, os dados do protótipo e o nível solicitado.

Para enviar a todos, use `"target": "all"` e remova `recipient_id`. Todos significa uma mensagem por personagem salvo, vinculado a uma conta e não excluído, no momento do envio. Personagens offline são incluídos; personagens criados depois não recebem esse envio. O envio é confirmado de uma vez no armazenamento do correio.

`type: "purchase"` exibe a mensagem na categoria **Loja** do correio. `type: "system"`, padrão, exibe na categoria **Sistema**. A marcação de compra organiza a entrega; não processa pagamentos. Mensagens sem anexos usam `items: []`.

Limites: título de até 120 caracteres em uma linha, mensagem de até 5000 caracteres, até 20 linhas de anexos, 1 a 100 unidades por linha, no máximo 200 itens por mensagem e níveis entre 1 e 60.

## Resgate e persistência

O jogador lê a mensagem e usa **Receber/Resgatar** para transferir os anexos à mochila. O servidor verifica a propriedade da mensagem e o espaço total antes de entregar. Um lote sem espaço não faz uma entrega parcial. Mensagens com anexos pendentes não podem ser excluídas. Leitura, resgate e exclusão são persistidos.

As mensagens, envios e o diário de resgates ficam em `AppData-nx/offline/<chave-do-cluster>/mail.json`, no mesmo volume dos saves. O campo `mail_sequence` é salvo com o inventário em `.player`. Um resgate confirmado antes de uma interrupção é reaplicado ao save antigo uma única vez. A recuperação do correio ocorre antes da economia para preservar vendas e retiradas posteriores dos itens resgatados.

Cada envio administrativo usa `request_id`. Repetir a mesma solicitação retorna o resultado anterior e não cria outras mensagens. O painel preserva esse identificador em caso de falha de rede ou recarregamento; **Novo envio** permite criar outra entrega intencionalmente. Reutilizar o identificador com conteúdo diferente é recusado.

O envio de itens entre jogadores continua separado do correio administrativo, pois precisa retirar os itens da mochila do remetente. As rotas administrativas nunca aceitam IDs de itens fornecidos pelo cliente como autoridade para um resgate.

## API

Todas as rotas exigem sessão administrativa (`X-Admin-Session`) ou o token de automação configurado (`X-Admin-Token`):

| Método | Rota | Uso |
| --- | --- | --- |
| GET | `/admin/mail/status` | Estado e contagem de mensagens |
| GET | `/admin/mail/items` | Nomes e identificadores de itens |
| GET | `/admin/mail/inbox?entity_id=<id>` | Consultar a caixa de um personagem |
| POST | `/admin/mail/preview` | Validar o campo `json` e apresentar a entrega |
| POST | `/admin/mail/send` | Enviar os campos `json` e `request_id` |

Os POST usam `application/x-www-form-urlencoded`; o campo `json` contém o documento acima. `request_id` aceita até 100 letras, números, hífens ou sublinhados. A prévia não envia mensagens nem concede itens.

## Testes

```powershell
dotnet build Durango-CustomServer/server/DurangoServer.csproj -c Debug
dotnet Durango-CustomServer/server/bin/Debug/net9.0/DurangoServer.dll --mail-check --data Durango-CustomServer/server/data
node Durango-CustomServer/server/tests/admin-panel-check.mjs
```

Os testes usam arquivos temporários e conexões reais de protocolo; não enviam emails aos jogadores do cluster de jogo. Quando necessário, execute a DLL com o runtime Linux já disponível no WSL, conforme a documentação do servidor local.
