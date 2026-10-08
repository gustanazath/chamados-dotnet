using System.Text.Json.Serialization;
using Chamados.Api.Domain;
using Chamados.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<TicketDbContext>(o => o.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=chamados.db"));
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
var app = builder.Build();
var apiKey = builder.Configuration["ApiKey"];
if (!app.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(apiKey))
    throw new InvalidOperationException("Configure ApiKey fora do ambiente Development.");
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") && !string.IsNullOrWhiteSpace(apiKey)
        && context.Request.Headers["X-Api-Key"] != apiKey)
    { await Results.Problem(statusCode: 401, title: "Chave de API inválida.").ExecuteAsync(context); return; }
    try { await next(context); }
    catch (BadHttpRequestException e) { await Results.Problem(statusCode: e.StatusCode, title: "Requisição inválida.").ExecuteAsync(context); }
    catch (ArgumentException e) { await Results.Problem(statusCode: 400, title: e.Message).ExecuteAsync(context); }
    catch (InvalidOperationException e) when (e.Message == "Transição de status não permitida.")
    { await Results.Problem(statusCode: 409, title: e.Message).ExecuteAsync(context); }
    catch (DbUpdateConcurrencyException)
    { await Results.Problem(statusCode: 409, title: "Chamado alterado por outra operação. Consulte e tente novamente.").ExecuteAsync(context); }
});
using (var scope = app.Services.CreateScope()) scope.ServiceProvider.GetRequiredService<TicketDbContext>().Database.EnsureCreated();
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "chamados" }));
app.MapGet("/", () => Results.Redirect("/health"));
var api = app.MapGroup("/api");
api.MapPost("/tickets", async (CreateTicket request, TicketDbContext db) =>
{
    var ticket = new Ticket(request.Title, request.Description, request.Priority);
    db.Tickets.Add(ticket);
    db.Events.Add(new TicketEvent { TicketId = ticket.Id, Kind = "Created", Message = "Chamado aberto." });
    await db.SaveChangesAsync();
    return Results.Created($"/api/tickets/{ticket.Id}", ticket);
});
api.MapGet("/tickets", async (TicketDbContext db, TicketStatus? status = null, int page = 1, int pageSize = 20) =>
{
    if (page < 1 || page > 100000 || pageSize is < 1 or > 100) throw new ArgumentException("Paginação inválida. page: 1..100000; pageSize: 1..100.");
    if (status.HasValue && !Enum.IsDefined(status.Value)) throw new ArgumentException("Status inválido.");
    var query = db.Tickets.AsNoTracking();
    if (status.HasValue) query = query.Where(x => x.Status == status.Value);
    return Results.Ok(new { page, pageSize, total = await query.CountAsync(), items = await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync() });
});
api.MapGet("/tickets/{id:guid}", async (Guid id, TicketDbContext db) =>
    await db.Tickets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id) is { } ticket ? Results.Ok(ticket) : Results.NotFound());
api.MapPatch("/tickets/{id:guid}/status", async (Guid id, ChangeStatus request, TicketDbContext db) =>
{
    var ticket = await db.Tickets.FindAsync(id);
    if (ticket is null) return Results.NotFound();
    var previous = ticket.Status;
    ticket.TransitionTo(request.Status);
    db.Events.Add(new TicketEvent { TicketId = id, Kind = "StatusChanged", Message = $"{previous} -> {ticket.Status}" });
    await db.SaveChangesAsync();
    return Results.Ok(ticket);
});
api.MapPost("/tickets/{id:guid}/comments", async (Guid id, AddComment request, TicketDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Trim().Length > 1000) throw new ArgumentException("Comentário deve ter entre 1 e 1000 caracteres.");
    var ticket = await db.Tickets.FindAsync(id);
    if (ticket is null) return Results.NotFound();
    if (ticket.Status == TicketStatus.Closed) return Results.Problem(statusCode: 409, title: "Chamado encerrado não aceita comentários.");
    ticket.Touch();
    var entry = new TicketEvent { TicketId = id, Kind = "Comment", Message = request.Message.Trim() };
    db.Events.Add(entry);
    await db.SaveChangesAsync();
    return Results.Created($"/api/tickets/{id}/history", entry);
});
api.MapGet("/tickets/{id:guid}/history", async (Guid id, TicketDbContext db, int page = 1, int pageSize = 20) =>
{
    if (page < 1 || page > 100000 || pageSize is < 1 or > 100) throw new ArgumentException("Paginação inválida.");
    if (!await db.Tickets.AnyAsync(x => x.Id == id)) return Results.NotFound();
    return Results.Ok(await db.Events.AsNoTracking().Where(x => x.TicketId == id).OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync());
});
app.Run();
public record CreateTicket(string Title, string Description, TicketPriority Priority);
public record ChangeStatus(TicketStatus Status);
public record AddComment(string Message);
public partial class Program { }
