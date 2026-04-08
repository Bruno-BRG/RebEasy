# RebEasy

Aplicativo desktop em C# para conectar a uma conta Gmail, sincronizar mensagens para cache local e oferecer visualizacao, busca e exportacao.

## Arquitetura inicial

- `src/RebEasy.App`: aplicacao desktop WPF
- `src/RebEasy.Domain`: modelos e contratos centrais
- `src/RebEasy.Infrastructure`: implementacoes de cache, sincronizacao e integracoes externas
- `docs/`: diagramas Mermaid que definem a visao inicial

## Fluxo do produto

1. Usuario conecta a conta Gmail via OAuth Desktop.
2. App executa sincronizacao inicial e persiste dados locais.
3. Atualizacoes incrementais usam `historyId`.
4. UI apresenta lista, detalhe, busca e exportacao.

## Stack proposta

- .NET 8
- WPF para interface desktop Windows
- SQLite para cache local
- Gmail API para leitura de mensagens

## Estrutura prevista de entrega

1. Base da solucao e navegacao inicial.
2. Fluxo de autenticacao OAuth.
3. Sincronizacao inicial e persistencia local.
4. Atualizacao incremental.
5. Busca, filtros e exportacao.

## Como testar a conexao com Gmail

1. Instale o `.NET 8 SDK`.
2. Gere um cliente OAuth Desktop no Google Cloud.
3. Salve o JSON em `secrets/google-oauth-client.json`.
4. Execute os comandos abaixo na raiz do projeto.

```powershell
dotnet restore
dotnet build
dotnet run --project .\src\RebEasy.App\RebEasy.App.csproj
```

Na primeira conexao, o navegador sera aberto para autenticar sua conta Google.

## Proximo passo recomendado

Trocar o cache em memoria por SQLite e implementar a sincronizacao incremental real via `historyId`.
