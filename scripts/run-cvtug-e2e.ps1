param(
    [string]$ApiBaseUrl = "https://telemedicinacc.vercel.app",
    [string]$SystemAApiKey = "rehabeasy-system-a",
    [string]$SystemBApiKey = "rehabeasy-system-b",
    [string]$PayloadFile = "C:\Users\Chari\dev\CC\vercel-api\examples\cvtug_payload_sample.json",
    [switch]$BuildApp
)

$ErrorActionPreference = "Stop"

$repoRoot = "C:\Users\Chari\dev\CC"
$rehabEasyRoot = Join-Path $repoRoot "RebEasy"
$apiRoot = Join-Path $repoRoot "vercel-api"
$appExe = Join-Path $rehabEasyRoot "src\RehabEasy.App\bin\Debug\net8.0-windows\RehabEasy.App.exe"
$sendScript = Join-Path $apiRoot "scripts\send_cvtug_payload.py"

if ($BuildApp) {
    Write-Host "Compilando RehabEasy..."
    dotnet build (Join-Path $rehabEasyRoot "RehabEasy.sln")
}

if (-not (Test-Path -LiteralPath $appExe)) {
    throw "Executavel nao encontrado em: $appExe"
}

if (-not (Test-Path -LiteralPath $sendScript)) {
    throw "Script de envio nao encontrado em: $sendScript"
}

if (-not (Test-Path -LiteralPath $PayloadFile)) {
    throw "Payload JSON nao encontrado em: $PayloadFile"
}

Write-Host "Abrindo RehabEasy..."
$appWorkingDirectory = Split-Path $appExe
$launchCommand = @'
$env:REHABEASY_API_BASE_URL = '__API_BASE_URL__'
$env:REHABEASY_SYSTEM_B_API_KEY = '__SYSTEM_B_API_KEY__'
$proc = Start-Process -FilePath '__APP_EXE__' -WorkingDirectory '__APP_WORKDIR__' -PassThru
Write-Output $proc.Id
'@

$launchCommand = $launchCommand.Replace('__API_BASE_URL__', $ApiBaseUrl)
$launchCommand = $launchCommand.Replace('__SYSTEM_B_API_KEY__', $SystemBApiKey)
$launchCommand = $launchCommand.Replace('__APP_EXE__', $appExe)
$launchCommand = $launchCommand.Replace('__APP_WORKDIR__', $appWorkingDirectory)

$appProcess = powershell -NoProfile -Command $launchCommand

Start-Sleep -Seconds 3

Write-Host "Enviando payload CvTUG para a API..."
$sendOutput = & python $sendScript --base-url $ApiBaseUrl --system-a-key $SystemAApiKey --payload-file $PayloadFile 2>&1
$exitCode = $LASTEXITCODE

if ($exitCode -ne 0) {
    throw "Falha ao enviar payload:`n$sendOutput"
}

Write-Host $sendOutput
Write-Host ""
Write-Host "RehabEasy aberto no processo $($appProcess.Id)."
Write-Host "Clique em 'Atualizar' no app para consumir o payload pendente."
