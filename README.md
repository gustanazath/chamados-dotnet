# Chamados .NET

API REST de chamados de suporte para portfólio, em C# com ASP.NET Core 10, Entity Framework Core e SQLite.

**Status:** projeto demonstrativo, validado localmente. Não há implantação pública nem clientes reais em produção. Construído com assistência de IA; o código está disponível para estudo, revisão e evolução.

## O que resolve

Uma equipe de suporte precisa acompanhar solicitações, registrar comentários e evitar encerramentos fora do fluxo. Cada chamado tem prioridade, status e histórico de eventos persistido.

- Abertura de chamados com prioridade Low, Normal ou High.
- Fluxo `Open -> InProgress -> Resolved -> Closed`.
- Reabertura de `Resolved` para `InProgress`; chamados `Closed` não reabrem nem recebem comentários.
- Histórico de abertura, comentários e mudanças de status.
- Estado e eventos gravados juntos pelo EF Core, com controle otimista de concorrência.
- Lista paginada com filtro por status; erros HTTP padronizados.
- Testes unitários e de integração HTTP, OpenAPI, health endpoint e GitHub Actions.

## Executar

Pré-requisito: SDK .NET 10, disponível na [Microsoft](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet restore Chamados.sln
dotnet test Chamados.sln --configuration Release
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src/Chamados.Api --urls http://localhost:5082
```

O SQLite cria `chamados.db` no diretório de execução; dados sobrevivem a reinícios. Não versionar o banco. OpenAPI JSON: `http://localhost:5082/openapi/v1.json`. [requests.http](requests.http) contém exemplos para clientes HTTP.

## Exemplo completo (PowerShell)

```powershell
$base = "http://localhost:5082"
$ticket = Invoke-RestMethod "$base/api/tickets" -Method Post -ContentType "application/json" -Body '{"title":"Impressora sem conexão","description":"A impressora do atendimento não responde.","priority":"High"}'
Invoke-RestMethod "$base/api/tickets/$($ticket.id)/status" -Method Patch -ContentType "application/json" -Body '{"status":"InProgress"}'
Invoke-RestMethod "$base/api/tickets/$($ticket.id)/comments" -Method Post -ContentType "application/json" -Body '{"message":"Verificando a conexão de rede."}'
Invoke-RestMethod "$base/api/tickets/$($ticket.id)/status" -Method Patch -ContentType "application/json" -Body '{"status":"Resolved"}'
Invoke-RestMethod "$base/api/tickets/$($ticket.id)/history"
```

## Rotas

| Método | Rota | Finalidade |
|---|---|---|
| GET | `/health` | Saúde do processo |
| POST | `/api/tickets` | Abrir chamado |
| GET | `/api/tickets?status=Open&page=1&pageSize=20` | Listar chamados |
| GET | `/api/tickets/{id}` | Detalhar chamado |
| PATCH | `/api/tickets/{id}/status` | Alterar status |
| POST | `/api/tickets/{id}/comments` | Comentar |
| GET | `/api/tickets/{id}/history?page=1&pageSize=20` | Histórico |

Respostas: 201 para criação, 400 para dados inválidos, 401 para chave incorreta, 404 para recurso inexistente e 409 para transições/comentários proibidos ou concorrência. Enums no JSON usam nomes, não números. `pageSize`: 1 a 100.

## Estrutura e decisões

`Domain/Ticket.cs` concentra o fluxo e as validações. `Data/TicketDbContext.cs` configura banco, relacionamentos e versão de concorrência. `Program.cs` compõe as rotas e dependências. Os testes validam regras e HTTP com `WebApplicationFactory` e SQLite real.

O histórico descreve o que aconteceu, mas não identifica um atendente autenticado. A chave compartilhada não oferece perfis, atribuição de chamados ou auditoria por usuário. Esses são passos de evolução, junto com SLA, notificações e autenticação individual.

## Configuração e container

Em Development a chave é opcional. Fora desse ambiente, `ApiKey` é obrigatória e protege `/api`; envie `X-Api-Key`. Use variável de ambiente e nunca versione chaves reais.

```powershell
$env:ApiKey = "SUBSTITUA-POR-UMA-CHAVE-FORTE"
$env:ConnectionStrings__Default = "Data Source=chamados.db"
```

```text
docker build -t chamados-dotnet .
docker run --rm -p 8080:8080 -e ApiKey=SUBSTITUA-POR-UMA-CHAVE-FORTE -e ConnectionStrings__Default="Data Source=/data/chamados.db" -v chamados-data:/data chamados-dotnet
```

O Dockerfile é fornecido para reprodução; o container não foi executado nesta entrega. `EnsureCreated` não faz atualização de esquema: uma evolução real precisa de migrations, backups e monitoramento. TLS deve ser configurado no proxy de implantação. `/health` confirma apenas o processo. Este repositório não representa experiência comercial nem um sistema em produção.
