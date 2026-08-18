# RehabEasy Desktop Java 25

Implementacao Java 25 + JavaFX do desktop RehabEasy. A API FastAPI/Python em
`../vercel-api` continua sendo o contrato externo do sistema.

## Requisitos

- JDK 25 LTS.
- Maven 3.9 ou superior, ou o Maven Wrapper incluído.
- Windows para o perfil atual de dependencias JavaFX (`win`).

## Configuracao

As variaveis seguem o desktop C#:

```powershell
$env:REHABEASY_API_BASE_URL = "https://telemedicinacc.vercel.app"
$env:REHABEASY_SYSTEM_B_API_KEY = "rehabeasy-system-b"
```

Na ausencia das variaveis, esses mesmos valores padrao sao usados. A base
local permanece em `%LOCALAPPDATA%\RehabEasy\rehabeasy.db` e os PDFs em
`%LOCALAPPDATA%\RehabEasy\pdfs`.

## Desenvolvimento

```powershell
.\mvnw.cmd test
.\mvnw.cmd javafx:run
```

O cliente aceita payloads JSON livres e preserva os aliases já suportados pelo
desktop atual. As fixtures em `src/test/resources/fixtures` cobrem payload
generico, CvTUG, Equilibrio e Index-Index.

## Empacotamento Windows

```powershell
.\scripts\package-windows.ps1
```

O script gera um app-image em `dist/RehabEasy`. A visualizacao embutida dos
PDFs usa PDFBox para renderizar as paginas dentro do JavaFX, evitando depender
do WebView2 do C#.

O código C# original permanece no repositório durante a fase de aceite. A
remoção dele e a troca final do atalho de produção devem ocorrer somente após
validar a base SQLite real, o fluxo com a API e a instalação em uma máquina
Windows limpa.
