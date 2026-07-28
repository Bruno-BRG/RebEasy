#Requires -Version 5.1
<#
.SYNOPSIS
    Restaura, compila e executa o RehabEasy (WPF).

.EXAMPLE
    .\scripts\run.ps1

.EXAMPLE
    .\scripts\run.ps1 -Build -Release

.EXAMPLE
    .\scripts\run.ps1 -ApiBaseUrl "https://telemedicinacc.vercel.app" -SystemBApiKey "minha-chave"
#>
param(
    [switch]$Restore,
    [switch]$Build,
    [switch]$NoBuild,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$ApiBaseUrl = 'https://telemedicinacc.vercel.app',
    [string]$SystemBApiKey = 'rehabeasy-system-b'
)

$ErrorActionPreference = 'Stop'

function Write-Banner {
    Write-Host ''
    Write-Host '  ======================================' -ForegroundColor Cyan
    Write-Host '           RehabEasy Runner            ' -ForegroundColor Cyan
    Write-Host '  ======================================' -ForegroundColor Cyan
    Write-Host ''
}

function Write-Step {
    param([string]$Message)
    Write-Host "  > $Message" -ForegroundColor Yellow
}

function Write-Ok {
    param([string]$Message)
    Write-Host "  OK  $Message" -ForegroundColor Green
}

function Write-Fail {
    param([string]$Message)
    Write-Host "  ERR $Message" -ForegroundColor Red
}

function Ensure-DotNet {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        Write-Fail 'dotnet SDK nao encontrado. Instale em https://dotnet.microsoft.com/download'
        exit 1
    }

    $version = (& dotnet --version 2>$null).Trim()
    Write-Ok "dotnet SDK $version"
}

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir
$Solution = Join-Path $RepoRoot 'RehabEasy.sln'
$Project = Join-Path $RepoRoot 'src\RehabEasy.App\RehabEasy.App.csproj'

Write-Banner

Write-Step 'Verificando ambiente...'
Ensure-DotNet

Push-Location $RepoRoot
try {
    if ($Restore) {
        Write-Step 'Restaurando pacotes...'
        & dotnet restore $Solution
        if ($LASTEXITCODE -ne 0) { throw 'dotnet restore falhou.' }
        Write-Ok 'Pacotes restaurados'
    }

    if ($Build) {
        Write-Step "Compilando ($Configuration)..."
        & dotnet build $Solution -c $Configuration
        if ($LASTEXITCODE -ne 0) { throw 'dotnet build falhou.' }
        Write-Ok 'Compilacao concluida'
    }

    Write-Step 'Configurando variaveis de ambiente...'
    $env:REHABEASY_API_BASE_URL = $ApiBaseUrl
    $env:REHABEASY_SYSTEM_B_API_KEY = $SystemBApiKey
    Write-Ok "API: $ApiBaseUrl"
    Write-Ok "Chave Sistema B: $SystemBApiKey"

    Write-Host ''
    Write-Step "Iniciando RehabEasy ($Configuration)..."
    Write-Host '  (feche a janela do app para encerrar)' -ForegroundColor DarkGray
    Write-Host ''

    $runArgs = @(
        'run',
        '--project', $Project,
        '-c', $Configuration
    )

    if ($NoBuild) {
        $runArgs += '--no-build'
    }

    & dotnet @runArgs
    if ($LASTEXITCODE -ne 0) { throw 'dotnet run falhou.' }
}
catch {
    Write-Host ''
    Write-Fail $_.Exception.Message
    exit 1
}
finally {
    Pop-Location
}
