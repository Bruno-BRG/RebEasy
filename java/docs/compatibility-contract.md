# Contrato de compatibilidade

## API remota

O desktop Java consome a API existente sem alterar o servidor:

- `GET /api/payloads/{id}`
- `GET /api/payloads/next`
- Header `X-API-KEY` com a chave do Sistema B.
- `404` em `/next` significa que nao ha payload pendente.
- Respostas de sucesso possuem `id`, `payload` e opcionalmente `pdf_url`.

O detalhe do contrato e os formatos de payload permanecem em
`../../vercel-api/docs/api-integration-guide.md`.

## Dados locais

O arquivo continua em `%LOCALAPPDATA%\RehabEasy\rehabeasy.db`.

As tabelas mantidas pelo cliente sao:

- `records`: registros normalizados, JSON bruto, identificador de paciente,
  tipo de teste e caminho local do PDF.
- `patient_clinical_notes`: prontuario atual por paciente.
- `patient_clinical_note_history`: versões salvas do prontuario.

Na inicializacao, o cliente cria tabelas e indices ausentes e adiciona somente
as colunas de metadados que foram introduzidas depois da primeira versao:
`patient_id`, `test_type` e `pdf_local_path`.

## Regras clínicas preservadas

- `CvTUG`: tempos Normal/Motora/Cognitiva, dual-task cost e velocidade.
- `Equilibrio`: indices posturograficos, Romberg, dependencia visual e avisos.
- `Index-Index`: distancia final, limiar, oscilacoes e assimetria entre maos.

As regras sao verificadas por fixtures e testes JUnit antes do empacotamento.
