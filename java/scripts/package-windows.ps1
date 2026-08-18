#Requires -Version 5.1
[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$JavaRoot = Split-Path -Parent $PSScriptRoot
$Target = Join-Path $JavaRoot "target"
$InputDirectory = Join-Path $Target "package-input"
$Distribution = Join-Path $JavaRoot "dist"
$JarName = "rehabeasy-desktop-1.0.0-SNAPSHOT.jar"
$JarPath = Join-Path $Target $JarName

$maven = Get-Command mvn -ErrorAction SilentlyContinue
$mavenExecutable = if ($maven) {
    $maven.Source
} else {
    Join-Path $JavaRoot "mvnw.cmd"
}
if (-not (Test-Path -LiteralPath $mavenExecutable) -and -not $maven) {
    throw "Maven nao encontrado e o Maven Wrapper nao esta disponivel."
}
if (-not (Get-Command jpackage -ErrorAction SilentlyContinue)) {
    throw "jpackage nao encontrado. Use um JDK 25 completo, nao apenas um JRE."
}

Push-Location $JavaRoot
try {
    if (-not $SkipBuild) {
        & $mavenExecutable clean package
        if ($LASTEXITCODE -ne 0) {
            throw "O build Maven falhou."
        }
    }

    if (-not (Test-Path -LiteralPath $JarPath)) {
        throw "JAR principal nao encontrado: $JarPath"
    }

    if (Test-Path -LiteralPath $InputDirectory) {
        Remove-Item -LiteralPath $InputDirectory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $InputDirectory | Out-Null
    Copy-Item -LiteralPath $JarPath -Destination $InputDirectory
    Copy-Item -Path (Join-Path $Target "lib\*") -Destination $InputDirectory -Force

    if (Test-Path -LiteralPath $Distribution) {
        Remove-Item -LiteralPath $Distribution -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Distribution | Out-Null

    & jpackage `
        --type app-image `
        --name RehabEasy `
        --dest $Distribution `
        --input $InputDirectory `
        --main-jar $JarName `
        --main-class com.rehabeasy.Main `
        --java-options '--module-path $APPDIR --add-modules javafx.base,javafx.graphics,javafx.controls,javafx.fxml,javafx.swing --enable-native-access=javafx.graphics,ALL-UNNAMED'
    if ($LASTEXITCODE -ne 0) {
        throw "O empacotamento jpackage falhou."
    }

    Write-Host "App-image criado em: $(Join-Path $Distribution 'RehabEasy')"
}
finally {
    Pop-Location
}
