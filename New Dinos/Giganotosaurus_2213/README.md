# Giganotossauro — EntityType 2213

Pacote para acrescentar o novo animal aos clientes atuais Lost Horizon em Unity 6 (6000.6.x), com bundles próprios para Windows e Android. O modelo usa `Giganotosaurus/GiganotosaurusPrefab`, framework `Giganotosaurus` e clips `Giganotosaurus-*`, conforme o cadastro 2213. Os bundles não se destinam aos executáveis Unity 2017.

## Arquivos para a futura inclusão nos clientes

- `Clients/Windows/AssetBundles/`: bundles Windows com sufixo de conteúdo.
- `Clients/Android/AssetBundles/`: bundles Android, compilados separadamente, com shaders GLES3/Vulkan.
- `Clients/<plataforma>/Info.5.2.1.additions.json`: entradas para mesclar em `FileList` e `ItemList` do catálogo existente. Esse arquivo é um fragmento; não substitui o catálogo completo.
- `Clients/<plataforma>/manifest.json`: nomes, tamanhos e SHA-256 dos arquivos.
- `Server/entity_types/animal.2213.json` e `Server/derived/animal_motions.2213.json`: cópias apenas do registro 2213, para mesclar em outras instalações do servidor. Não substituem as tabelas completas.
- `UnityProject/Assets/Models/Animals/Giganotosaurus/`: FBX, malha corrigida, materiais, texturas, clips individuais, framework e prefab final.
- `Metadata/`: contrato de componentes, aliases das ações provisórias do T-Rex, relatório dos assets e validação dos bundles.
- `Previews/`: imagens de repouso, mordida e pose final da morte, renderizadas a partir do bundle Windows.

Para incluir no PC ou em uma árvore extraída do APK, execute sobre a pasta que já contém `Info.5.2.1.json`:

```powershell
python install-assets.py --platform Windows --assetbundles "C:\caminho\cliente\Lost Horizon Game_Data\StreamingAssets\AssetBundles"
python install-assets.py --platform Android --assetbundles "C:\caminho\apk-extraido\assets\AssetBundles"
```

O caminho Android é ilustrativo: use a pasta real do catálogo no cliente que será empacotado. Acrescente `--dry-run` para conferir sem gravar. O instalador valida os hashes, copia os dois bundles e mescla o índice, preservando o preload e os demais registros. Ele guarda uma cópia do índice anterior. No Android, a inclusão deve ocorrer antes do empacotamento e assinatura do APK. Se o cliente buscar os bundles pelo servidor, o mesmo fragmento precisa ser mesclado no catálogo Android servido pelo gateway.

## Contrato com o servidor

O prefab contém `AnimalBehavior`, referência ao `AnimalFrameworkResource`, `Animation` Legacy no modelo, mapeamento dos ossos de cabeça/corpo/patas/cauda, sombras e componentes auxiliares do cliente. A escala de metros para centímetros fica no filho do prefab; a raiz permanece em escala 1, pois o cliente aplica `Display.BaseScale` recebido do servidor.

Os 43 clips do FBX foram preservados com o prefixo `Giganotosaurus-`. Foi acrescentado `Giganotosaurus-Die`, derivado de `Torpid-In`, com retenção do último quadro. Essa queda de torpor foi aprovada como animação de morte. O registro `dead` do servidor foi ajustado para esse nome. O torpor normal continua usando `Giganotosaurus-Torpid-Idle`.

As curvas de animação ficam no bundle de framework, compartilhadas por referência com o prefab. A validação rejeita curvas duplicadas entre os dois bundles. O verificador de catálogo Android do servidor também foi atualizado para reconhecer os builds Unity 6000.6.0f1 e 6000.6.3f1 usados pelos clientes atuais.

As animações de deslocamento mantêm o movimento dos ossos e removem curvas no Transform externo do modelo. O deslocamento da entidade vem do jogo/servidor. A sequência de passeio e o áudio contínuo da demo não integram o prefab de gameplay. Os eventos de animação estão preparados com listas vazias; sons específicos ainda precisam de uma integração com o sistema de áudio do cliente.

O framework preserva os identificadores de ação e conjuntos usados por `trex_ai`, que o cadastro provisório 2213 ainda reutiliza. As ações de ataque usam a mordida fornecida, e o rugido usa `Roar`; não foram criadas animações novas de cauda ou pisoteio. Balanceamento, IA, loot e retrato seguem os valores provisórios do T-Rex. O cadastro não cria spawns naturais. Depois da inclusão dos arquivos, o servidor precisa usar as tabelas com 2213 e os clientes precisam atualizar seu cache de definições.

Ao aplicar essas tabelas em outra instalação, mescle o registro 2213 e reinicie o servidor para recarregar o cadastro e o mapeamento de animações.

## Reconstruir

Os bundles distribuídos usam **LitSphere/DualMode/Animal** e **litsphere_dino_giant** do `preload` de cada plataforma, com a albedo original `giga_d_colorized.png`. Não usam Standard, emissão ou a textura experimental clareada. O material no projeto isolado é uma prévia matcap portátil; `finalize-native-materials.py` liga o material aos assets compilados reais do jogo antes de empacotar. Essa finalização funciona sem abrir o editor e valida as referências do shader e do matcap. `Metadata/native-materials.json` registra os resultados.

O Giga caminha a 320 cm/s na escala 1 (1.280 na escala 4, escala atual), conforme a velocidade das patas no FBX. Seu tamanho é quatro vezes o T-Rex (dobrado em relação à escala 2 anterior). A mordida dura 1,25 s e impacta aos 0,65 s. O servidor usa a posição atual interpolada e o raio do corpo no alcance do ataque.

Na raiz do repositório:

```powershell
python tools/prepare-giganotosaurus-2213.py build
python tools/prepare-giganotosaurus-2213.py archive
```

O build usa o Unity 6000.6.0f1 instalado com os módulos Android e Windows, gera dois bundles por plataforma e confere clips, nomes, plataforma de serialização, dependências e hashes. O relatório fica em `Metadata/validation.json`. A validação dos assets não equivale a um teste dentro do cliente conectado ao servidor.

Neste computador, o CoreModule de referência do módulo Windows instalado estava truncado. A ferramenta usa uma cópia local do editor e os metadados completos do CoreModule da mesma versão para compilar os schemas. Esse ajuste não altera a instalação do Unity e não acrescenta DLLs de engine ao pacote dos clientes. Os detalhes ficam em `Metadata/build-environment.json`.

`UnityProject/Assets/ClientSchema` contém somente schemas de serialização extraídos dos assets do jogo para autoria. Os bundles referenciam as classes do `Assembly-CSharp` que já existe no cliente. **Não copie o Assembly-CSharp gerado por esse projeto de autoria para os clientes.**
