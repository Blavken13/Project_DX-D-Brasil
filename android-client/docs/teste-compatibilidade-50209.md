# Teste manual — Lost Horizon 50209

Instale `LostHorizon-alfa.apk` sobre a instalação atual, sem apagar os dados.
O vídeo da seleção volta a reproduzir por padrão no Android 15 e 16, inclusive
quando o modo de gráficos reduzidos está ativado e ao atualizar a revisão 50208.

1. Faça login ou toque em Continuar. Confira o splash, o vídeo do fundo e os personagens.
2. Entre no mapa e confira aparência, estruturas e controles.
3. Feche o aplicativo e repita a abertura para conferir a conta salva.

Diagnóstico → Modo de compatibilidade agora oferece opções independentes:

- Gráficos reduzidos: solicita 30 FPS e resolução menor. Continua ativado por
  padrão no Android 15 ou superior.
- Desativar vídeo da seleção: inicia desmarcado. Use apenas para comparar uma
  falha antes da seleção com e sem o plugin antigo de vídeo nessa tela.
  Quando marcada, a seleção fica sem vídeo; cenas do tutorial permanecem.

Se o jogo fechar, abra-o novamente, aguarde alguns segundos na tela de login e
use Diagnóstico → Compartilhar relatório. Também é possível visualizar o texto
ou salvar em Downloads pelo seletor do Android. Informe qual opção estava
ativada no momento da falha. Não é preciso USB ou acesso à pasta Android/data.

Essa revisão mantém as correções de inicialização, páginas de memória,
liberação do WebView, renderização e relatórios da 50208. O vídeo foi separado
do perfil gráfico; a estabilidade do Poco X6 ainda depende da validação manual.
