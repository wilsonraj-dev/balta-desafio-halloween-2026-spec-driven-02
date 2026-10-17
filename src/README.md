# Estrutura e persistência — T-01 e T-02

Base da API do encurtador de links em C# e .NET 10, usando o SDK Web e
o modelo de hospedagem de Minimal API.

## Estrutura

```text
src/
  LinkShortener.slnx
  LinkShortener.Api/
    LinkShortener.Api.csproj
    Program.cs
    Endpoints/     # Endpoints HTTP, organizados por recurso
    Application/   # Serviços de aplicação e regras de negócio
    Domain/        # Entidades do domínio
    Data/          # DbContext, mapeamento e migrações EF Core
  LinkShortener.Tests/
    Domain/        # Teste unitário de inicialização de Link
    Data/          # Testes de migração e persistência SQLite
  storage/         # Arquivo SQLite compartilhado com Docker (ignorado pelo Git)
  compose.yaml
  Dockerfile.database
```

As pastas seguem a arquitetura definida no plano. Os arquivos `.gitkeep`
permitem versionar pastas ainda vazias.

## Compilar e executar

Requer o SDK do .NET 10. Na raiz do repositório:

```powershell
dotnet build src/LinkShortener.slnx
dotnet test src/LinkShortener.slnx
dotnet run --project src/LinkShortener.Api
```

A T-01 está concluída quando a solução compila. A aplicação inicia sem endpoints
mapeados, portanto requisições HTTP retornam 404.

## Criar o banco com Docker — T-02

Com o Docker Engine em execução, na raiz do repositório:

```powershell
docker compose -f src/compose.yaml run --build --rm database
```

SQLite é embarcado; o contêiner executa a migração e encerra. O banco permanece
em `src/storage/links.db`, acessível pela API local e preservado entre execuções
do contêiner. Não há porta de banco nem servidor SQLite separado.

## Aplicar e conferir a migração localmente

Alternativa para desenvolvimento e verificação sem Docker, a partir de `src/`:

```powershell
dotnet tool restore
dotnet ef database update --project LinkShortener.Api
dotnet ef migrations list --project LinkShortener.Api
dotnet ef migrations has-pending-model-changes --project LinkShortener.Api
```

A ferramenta cria a tabela `Links`, o índice único `IX_Links_Code` e o histórico
de migrações. Reaplicar a migração preserva os dados existentes. A API não aplica
migrações automaticamente. A conexão padrão aponta para `src/storage/links.db`;
uma configuração `ConnectionStrings__Links` permite substituir o caminho.

Para reverter o schema em um banco de desenvolvimento descartável, execute
`dotnet ef database update 0 --project LinkShortener.Api` em `src/`. Essa operação
remove a tabela e seus dados.

Os testes usam arquivos SQLite temporários independentes, aplicam a migração
real e verificam persistência após reabrir o banco, expiração opcional e rejeição
de códigos duplicados pelo índice. O teste unitário verifica o estado inicial
de Link: identificador, criação em UTC, contador zero e expiração ausente.

Referência: [aplicação de migrações EF Core](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying).

## Limites da T-02

As dependências EF Core, ferramenta de migrações e xUnit estão justificadas em
`specs/plan.md`. A entidade contém somente os campos do modelo e seus valores
iniciais. `ManagementKeyHash` armazena o hash recebido; geração de chaves e
autenticação ficam para a T-09. O índice único implementa a parte de schema
da D-04; checagem no serviço e resposta HTTP 409 ficam para a T-06.

Não foram implementados geração ou validação de códigos, validação de URLs,
serviços de aplicação, repositórios, endpoints, incremento de acessos, cache
ou front-end Blazor. `HasMaxLength` documenta os limites no modelo EF; SQLite
não os impõe em colunas TEXT, e a validação pertence às tarefas específicas.
