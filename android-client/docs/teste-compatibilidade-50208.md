# Teste manual — Lost Horizon 50208

Instale `LostHorizon-alfa.apk` sobre a instalação atual, sem apagar os dados.
Esta revisão mantém o pacote e a chave de assinatura usados nos testes.

1. Abra o aplicativo do zero e aguarde na tela de login. Ele deve aguardar seu toque.
2. Toque em Continuar ou faça login, confira o splash e a lista de personagens.
3. Entre no personagem, confira aparência, estruturas e controles do mapa.
4. Feche o aplicativo e repita o acesso para verificar a abertura com conta salva.

No Android 15 e 16, o modo de compatibilidade inicia ativado. A seleção usa
fundo estático para evitar inicializar o plugin antigo de vídeo nessa etapa;
o vídeo do login e as cenas do tutorial permanecem. O perfil solicita 30 FPS
e resolução reduzida. Para comparar com o perfil normal, altere Diagnóstico →
Modo de compatibilidade na tela de login antes de entrar novamente.

Se o jogo fechar, abra-o novamente e toque em Diagnóstico → Compartilhar relatório.
Escolha o aplicativo pelo qual enviará o arquivo ao responsável pelo teste.
Também pode escolher Salvar relatório e indicar Downloads no seletor do Android.
Ver último fechamento permite consultar o texto no próprio jogo.
Não é preciso USB, root nem acesso à pasta Android/data.

Os relatórios ficam na área privada do aplicativo; são mantidos os cinco mais
recentes. O relatório informa modelo, sistema, versão do APK, tamanho real das
páginas de memória, etapa alcançada e motivo do encerramento quando disponível.
A pilha nativa depende de o Android disponibilizá-la. O relatório não inclui
senhas, tokens, logs brutos ou memória do processo e não é enviado automaticamente.

Ao relatar uma falha, informe também em qual etapa ocorreu e se o modo de
compatibilidade estava ativado. Aguarde alguns segundos na tela de login antes
de exportar, para que a consulta do encerramento anterior termine.
