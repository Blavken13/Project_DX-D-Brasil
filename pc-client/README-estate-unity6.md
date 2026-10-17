# Expansão de domínio no PC atual

O `Lost-Horizon-PC/AplicarPatch.exe` é um instalador nativo que contém uma DLL
completa e a copia para `DurangoV2_Data/Managed/Assembly-CSharp.dll`, criando
um backup. A DLL foi extraída para análise sem executar esse instalador.

Na versão embutida, `EstateGridGroup.OnExpandEstateClick` envia diretamente
`EstateSystem.ExpandEstate` quando o domínio é `OwnerType.Player`. A conversão
de posição para célula divide por quatro e o retorno usa `OnSuccess`.
Esse prefixo não existia na DLL atual de `Lost Horizon Game_Data/Managed`.

O novo patch porta somente esse prefixo para a DLL atual. Sua sequência de IL
foi comparada com a DLL embutida e é equivalente instrução por instrução,
normalizando apenas o índice da variável local. Os demais tipos de domínio
mantêm os caminhos que já existiam no cliente atual.

## Gerar e instalar

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File pc-client/build_estate_expansion.ps1 -Install
```

Requer .NET SDK 9 e o jogo fechado. Sem `-Install`, apenas gera o pacote.
O script salva a DLL anterior em `pc-client/work/estate-unity6/<sha256>` e,
na instalação, também cria `Assembly-CSharp.dll.dominio-backup-<hash>` junto
à DLL do jogo. Não sobrescreve um backup de conteúdo diferente.

O pacote `pc-client/dist/LostHorizon-PC-dominio-unity6.zip` pode ser extraído
na raiz de outro cliente da mesma build. Para outras builds, gere novamente
o patch a partir da DLL correspondente.

## Verificações

- Apenas `OnExpandEstateClick` mudou; 36.418 outros métodos foram preservados.
- Identidade da assembly, referências Unity, conversão de célula e callback preservados.
- O prefixo instalado coincide com o pedido direto do patch fornecido pelo usuário.
- Reaplicar a ferramenta à DLL já corrigida não altera seu SHA-256.
- Proprietário, adjacência e teto de oito lotes continuam validados no servidor.

A validação foi feita no binário; não houve sessão visual do jogo nesta etapa.
Não use o instalador antigo para substituir a DLL inteira de outra build.
