# Estrutura inicial — T-01

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
    Data/          # Persistência e repositórios
```

As pastas estão reservadas para a arquitetura definida no plano. Os arquivos
`.gitkeep` permitem versionar essas pastas enquanto estiverem vazias.

## Compilar e executar

Requer o SDK do .NET 10. Na raiz do repositório:

```powershell
dotnet build src/LinkShortener.slnx
dotnet run --project src/LinkShortener.Api
```

A T-01 está concluída quando a solução compila. A aplicação inicia sem endpoints
mapeados, portanto requisições HTTP retornam 404.

## Limites desta tarefa

Nenhuma dependência externa foi adicionada. EF Core, SQLite e xUnit serão
configurados nas tarefas que os utilizarem, com justificativa no plano antes
de adicionar os pacotes, conforme a constituição. Não há regras de negócio
ou endpoints nesta etapa que exijam testes unitários ou de integração.

O front-end Blazor pertence à T-11. Entidade Link, migrações, serviços,
repositórios, validações e funcionalidades HTTP pertencem às demais tarefas.
A configuração de Docker para persistência também fica para a etapa de dados.
