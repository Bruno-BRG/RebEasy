# RehabEasy

Aplicativo desktop em C# para consumir payloads da RehabEasy Transfer API, gravar os registros em SQLite local e oferecer visualizacao e busca.

## Arquitetura

- `src/RehabEasy.App`: aplicacao desktop WPF
- `src/RehabEasy.Domain`: modelos e contratos centrais
- `src/RehabEasy.Infrastructure`: cliente HTTP da API, normalizacao de payload e persistencia SQLite
- `docs/`: diagramas Mermaid da visao inicial

## Fluxo do produto

1. Sistema A publica um payload na API usando `POST /api/payloads`.
2. O RehabEasy recebe o `payload_id` e consome `GET /api/payloads/{id}` com a chave do Sistema B.
3. O payload e normalizado para registros locais.
4. Os dados sao gravados em `%LOCALAPPDATA%\RehabEasy\rehabeasy.db`.
5. A UI lista, busca e abre o detalhe dos registros persistidos.

## Variaveis de ambiente

```powershell
$env:REHABEASY_API_BASE_URL="https://telemedicinacc.vercel.app"
$env:REHABEASY_SYSTEM_B_API_KEY="SUA_SYSTEM_B_API_KEY"
```

`REHABEASY_API_BASE_URL` e opcional. Quando nao for informado, o app usa `https://telemedicinacc.vercel.app`.

## Rodar localmente

```powershell
dotnet restore
dotnet build
dotnet run --project .\src\RehabEasy.App\RehabEasy.App.csproj
```

Depois de abrir o app, cole o `payload_id` gerado pela API e clique em `Importar payload`.
