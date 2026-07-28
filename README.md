# RehabEasy

Aplicativo desktop em C# para consumir payloads da RehabEasy Transfer API, gravar os registros em SQLite local e oferecer visualizacao e busca.

## Arquitetura

- `src/RehabEasy.App`: aplicacao desktop WPF
- `src/RehabEasy.Domain`: modelos e contratos centrais
- `src/RehabEasy.Infrastructure`: cliente HTTP da API, normalizacao de payload e persistencia SQLite
- `docs/`: diagramas Mermaid da visao inicial

## Fluxo do produto

1. Sistema A publica um payload na API usando `POST /api/payloads`.
2. O RehabEasy consome automaticamente o proximo payload pendente usando `GET /api/payloads/next` com a chave do Sistema B.
3. O payload e normalizado para registros locais.
4. Os dados sao gravados em `%LOCALAPPDATA%\RehabEasy\rehabeasy.db`.
5. A UI lista, busca e abre o detalhe dos registros persistidos.

## Variaveis de ambiente

```powershell
$env:REHABEASY_API_BASE_URL="https://telemedicinacc.vercel.app"
$env:REHABEASY_SYSTEM_B_API_KEY="rehabeasy-system-b"
```

As duas variaveis sao opcionais para o fluxo padrao. Quando nao forem informadas, o app usa `https://telemedicinacc.vercel.app` e a chave estatica `rehabeasy-system-b`.

## Rodar localmente

Atalho recomendado (Windows):

```powershell
.\run.cmd
```

Ou com opcoes:

```powershell
.\scripts\run.ps1 -Build -Restore
.\scripts\run.ps1 -Release -ApiBaseUrl "https://telemedicinacc.vercel.app" -SystemBApiKey "rehabeasy-system-b"
```

Manualmente:

```powershell
dotnet restore
dotnet build
dotnet run --project .\src\RehabEasy.App\RehabEasy.App.csproj
```

Depois de abrir o app, clique em `Atualizar` para buscar e consumir automaticamente o proximo payload pendente na API.
