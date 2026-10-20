[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$CheckAvailable,
    [string]$DataDirectory,
    [string]$StorageKey = 'Durango Brasil',
    [ValidateRange(1, 65535)][int]$GatewayPort = 8190,
    [ValidateRange(1, 65535)][int]$GamePort = 8191
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$serverDirectory = Join-Path $projectRoot 'Durango-CustomServer\server'
$serverDll = Join-Path $serverDirectory 'bin\Debug\net9.0\DurangoServer.dll'
if (-not $DataDirectory) { $DataDirectory = Join-Path $serverDirectory 'data' }
$originalWslEnv = $env:WSLENV

# --help carrega o programa sem abrir portas nem carregar os saves.
# Captura stderr para distinguir o bloqueio do Windows de outros erros reais.
function Invoke-RuntimeProbe {
    param([string]$Command, [string[]]$CommandArguments)
    $ErrorActionPreference = 'Continue'
    $output = & $Command @CommandArguments 2>&1
    $code = $LASTEXITCODE
    return @{ Code = $code; Text = (($output | ForEach-Object { "$_" }) -join "`n") }
}

function ConvertTo-WslPath {
    param([string]$WindowsPath)
    $result = Invoke-RuntimeProbe $script:wslCommand @(
        '--distribution', $script:distro, '--exec', 'wslpath', '-a', '-u', $WindowsPath
    )
    if ($result.Code -ne 0) { throw "Nao foi possivel converter o caminho para WSL: $($result.Text)" }
    return $result.Text.Trim()
}

try {
    # Executado tambem pelo BAT antes do build, enquanto a DLL ainda pode estar em uso.
    if ($CheckAvailable) {
        foreach ($port in @($GatewayPort, $GamePort)) {
            $connection = [System.Net.Sockets.TcpClient]::new()
            try {
                $attempt = $connection.ConnectAsync('127.0.0.1', $port)
                $occupied = $attempt.Wait(750) -and $connection.Connected
            } catch { $occupied = $false } finally { $connection.Dispose() }
            if ($occupied) {
                throw "Ja existe um servidor ou outro processo na porta $port. Encerre o servidor com Ctrl+C para salvar antes de compilar e iniciar novamente."
            }
        }
        exit 0
    }
    if (-not (Test-Path -LiteralPath $serverDll -PathType Leaf)) {
        throw 'DLL nao encontrada. Execute iniciar-servidor-local.bat para compilar primeiro.'
    }
    $DataDirectory = (Get-Item -LiteralPath $DataDirectory).FullName
    if (-not (Test-Path -LiteralPath (Join-Path $DataDirectory 'assets\constants.json'))) {
        throw "Dados do jogo nao encontrados em $DataDirectory."
    }
    $terrainDirectory = Join-Path $DataDirectory 'terrains'
    if (-not $Check) {
        foreach ($port in @($GatewayPort, $GamePort)) {
            $connection = [System.Net.Sockets.TcpClient]::new()
            try {
                $attempt = $connection.ConnectAsync('127.0.0.1', $port)
                $occupied = $attempt.Wait(750) -and $connection.Connected
            } catch {
                $occupied = $false
            } finally {
                $connection.Dispose()
            }
            if ($occupied) {
                throw "A porta TCP $port ja esta em uso. Encerre a instancia anterior com Ctrl+C antes de iniciar novamente. Nenhum save foi carregado ou gravado por esta tentativa. Painel local: http://127.0.0.1:$GatewayPort/admin/"
            }
        }
    }
    $dotnet = (Get-Command dotnet.exe -ErrorAction Stop).Source
    $nativeProbe = Invoke-RuntimeProbe $dotnet @($serverDll, '--help')
    $runtimeName = 'Windows'
    $command = $dotnet
    $prefixArguments = @()
    $runtimeDll = $serverDll
    $runtimeData = $DataDirectory
    $runtimeTerrains = $terrainDirectory

    if ($nativeProbe.Code -ne 0) {
        if ($nativeProbe.Text -notmatch '0x800711C7') {
            throw "O servidor falhou na verificacao de execucao: $($nativeProbe.Text)"
        }
        Write-Host '[AVISO] O Controle de Aplicativos bloqueou a DLL no Windows (0x800711C7).'
        Write-Host 'Verificando a execucao no WSL, com os mesmos dados e progresso...'
        $script:wslCommand = (Get-Command wsl.exe -ErrorAction Stop).Source
        $distrosResult = Invoke-RuntimeProbe $script:wslCommand @('--list', '--quiet')
        if ($distrosResult.Code -ne 0) {
            throw "Nao foi possivel acessar o WSL: $($distrosResult.Text)"
        }
        # wsl --list produz UTF-16 mesmo em alguns consoles que leem como UTF-8.
        $distros = @($distrosResult.Text.Replace([string][char]0, '').Split("`n") |
            ForEach-Object { $_.Trim() } | Where-Object { $_ -and $_ -notlike 'docker-*' })
        $script:distro = $env:DURANGO_WSL_DISTRO
        if ($script:distro) {
            if ($script:distro -notin $distros) { throw "Distribuicao WSL nao encontrada: $script:distro" }
        } elseif ('Ubuntu-24.04' -in $distros) {
            $script:distro = 'Ubuntu-24.04'
        } elseif ($distros.Count -gt 0) {
            $script:distro = $distros[0]
        } else {
            throw 'Instale uma distribuicao WSL e o runtime .NET 9 para Linux. Consulte Durango-CustomServer/docs/servidor-local.md.'
        }

        # Transfere configuracoes administrativas por ambiente, nunca pela linha de comando.
        $sharedNames = @('DURANGO_ADMINS', 'DURANGO_ADMIN_TOKEN', 'DURANGO_STORAGE_KEY')
        $env:WSLENV = (@($originalWslEnv) + $sharedNames | Where-Object { $_ }) -join ':'
        $runtimeDll = ConvertTo-WslPath $serverDll
        $runtimeData = ConvertTo-WslPath $DataDirectory
        $runtimeTerrains = ConvertTo-WslPath $terrainDirectory
        $candidates = @()
        foreach ($relativePath in @('bin\local-runtime-linux\dotnet', 'bin\test-runtime-linux\dotnet')) {
            $localRuntime = Join-Path $serverDirectory $relativePath
            if (Test-Path -LiteralPath $localRuntime -PathType Leaf) {
                $candidates += ConvertTo-WslPath $localRuntime
            }
        }
        $candidates += 'dotnet'
        $linuxDotnet = $null
        foreach ($candidate in $candidates) {
            $probe = Invoke-RuntimeProbe $script:wslCommand @(
                '--distribution', $script:distro, '--exec', $candidate, $runtimeDll, '--help'
            )
            if ($probe.Code -eq 0) { $linuxDotnet = $candidate; break }
        }
        if (-not $linuxDotnet) {
            throw "O WSL precisa de um runtime .NET 9 funcional. Consulte Durango-CustomServer/docs/servidor-local.md.`n$($probe.Text)"
        }
        $command = $script:wslCommand
        $prefixArguments = @('--distribution', $script:distro, '--exec', $linuxDotnet)
        $runtimeName = "WSL ($script:distro)"
    }

    if ($Check) {
        Write-Host "[OK] DLL executada com sucesso: $runtimeName. Dados e caminhos verificados."
        Write-Host '[OK] Nenhuma porta foi aberta e nenhum progresso foi carregado ou salvo.'
        exit 0
    }

    Write-Host ''
    Write-Host "Execucao: $runtimeName"
    Write-Host "Gateway: http://127.0.0.1:$GatewayPort"
    Write-Host "Jogo TCP: 127.0.0.1:$GamePort"
    $saveDirectory = Join-Path (Split-Path -Parent $DataDirectory) "AppData-nx\offline\$StorageKey"
    Write-Host "Progresso: $saveDirectory"
    Write-Host 'Abra Durango-OffServer\DurangoBrasil.exe e selecione o servidor local.'
    Write-Host 'Aguarde a mensagem de servidor pronto antes de entrar no jogo.'
    Write-Host 'Para encerrar e salvar, pressione Ctrl+C nesta janela.'
    Write-Host ''
    $serverArguments = $prefixArguments + @(
        $runtimeDll, '--name', 'Durango Brasil-local', '--storage-key', $StorageKey,
        '--data', $runtimeData, '--terrains', $runtimeTerrains,
        '--public-host', '127.0.0.1', '--gateway-port', "$GatewayPort",
        '--game-port', "$GamePort", '--max-players', '2'
    )
    # Mantem o processo em primeiro plano para Ctrl+C acionar o salvamento do servidor.
    & $command @serverArguments
    exit $LASTEXITCODE
} catch {
    Write-Host "[ERRO] $($_.Exception.Message)" -ForegroundColor Red
    exit 1
} finally {
    $env:WSLENV = $originalWslEnv
}
