#Requires -Version 5.1
[CmdletBinding()]
param(
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$JavaRoot = Split-Path -Parent $PSScriptRoot
Push-Location $JavaRoot
try {
    $maven = Get-Command mvn -ErrorAction SilentlyContinue
    $mavenExecutable = if ($maven) {
        $maven.Source
    } else {
        Join-Path $JavaRoot "mvnw.cmd"
    }
    if (-not (Test-Path -LiteralPath $mavenExecutable) -and -not $maven) {
        throw "Maven nao encontrado e o Maven Wrapper nao esta disponivel."
    }

    if (-not $SkipTests) {
        & $mavenExecutable test
        if ($LASTEXITCODE -ne 0) {
            throw "Os testes Java falharam."
        }
    }

    & $mavenExecutable javafx:run
    if ($LASTEXITCODE -ne 0) {
        throw "A inicializacao do RehabEasy Java falhou."
    }
}
finally {
    Pop-Location
}
