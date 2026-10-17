# Plano técnico

## Contexto
Implementa a especificação do encurtador de links, respeitando a constituição
do projeto.

## Arquitetura
- API HTTP em Minimal API, um arquivo de endpoints por recurso
- Camada de aplicação com LinkService, dona das regras RN-01 a RN-04
- Camada de dados com EF Core e um repositório por agregado
- Endpoint só valida entrada, chama o serviço e traduz o resultado em resposta

## Decisões
- D-01: SQLite como armazenamento, porque o RF-08 exige persistência
  entre reinícios
- D-02: contador incrementado por UPDATE atômico no banco, e não lido e
  reescrito na aplicação, para atender a RN-04 sob acesso concorrente
- D-03: código do link gerado com alfabeto sem caracteres ambíguos, para
  reduzir erro de digitação em link lido em voz alta
- D-04: unicidade do código garantida por índice único no banco, além da
  checagem no serviço, porque a checagem sozinha tem corrida
- D-05: chave de gerenciamento da DC-03 armazenada como hash, nunca em texto puro

## Dependências e execução da T-02
- `Microsoft.EntityFrameworkCore.Sqlite` 10.0.5: provedor EF Core para a
  persistência SQLite exigida pela constituição e por RF-08.
- `SQLitePCLRaw.bundle_e_sqlite3` 3.0.5: atualização explícita do runtime
  nativo transitivo do provedor, evitando a versão 2.1.11 sinalizada pelo
  NuGet com a vulnerabilidade GHSA-2m69-gcr7-jv3q.
- `Microsoft.EntityFrameworkCore.Design` e ferramenta local `dotnet-ef`
  10.0.5: geração e aplicação versionada das migrações da T-02. O pacote
  de design fica privado ao projeto, sem propagação para consumidores.
- `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.4 e
  `Microsoft.NET.Test.Sdk` 17.14.1: testes unitários e de persistência
  executáveis por `dotnet test`, conforme a constituição.
- SQLite é um banco embarcado, sem servidor separado. Docker executa
  a migração em um contêiner com o arquivo em volume persistente compartilhado
  com a API local. A migração é explícita, sem alterar o schema na inicialização
  HTTP. O serviço de aplicação e o repositório serão implementados com os casos
  de uso nas tarefas seguintes; a T-02 não expõe operações HTTP.

## Modelo de dados
Link
- Id, Code (único), TargetUrl, CreatedAtUtc, ExpiresAtUtc (nulo), AccessCount,
  ManagementKeyHash

## Contratos
- POST /api/links entrada: targetUrl, code opcional, expiresAtUtc opcional
- POST /api/links saída: shortUrl, code, managementKey
- GET /{code} redireciona com 302, ou 404 se não existe, ou 410 se expirou
- GET /api/links/{code}/stats exige header com a chave de gerenciamento

## Riscos
- Concorrência na criação de código próprio, mitigada por D-04
- Crescimento da tabela de links sem política de limpeza, fora de escopo por ora
