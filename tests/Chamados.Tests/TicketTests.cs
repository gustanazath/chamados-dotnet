using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Chamados.Api.Domain;
using Chamados.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
namespace Chamados.Tests;
public class TicketTests
{
    [Fact] public async Task DatabaseRejectsStaleWriteAndKeepsPersistedStatus()
    {
        var connection = $"Data Source={Path.Combine(Path.GetTempPath(), $"ticket-concurrency-{Guid.NewGuid()}.db")}";
        var options = new DbContextOptionsBuilder<TicketDbContext>().UseSqlite(connection).Options;
        var ticket = new Ticket("Título", "Descrição", TicketPriority.Normal);
        await using (var setup = new TicketDbContext(options)) { await setup.Database.EnsureCreatedAsync(); setup.Tickets.Add(ticket); await setup.SaveChangesAsync(); }
        await using var first = new TicketDbContext(options);
        await using var second = new TicketDbContext(options);
        var a = await first.Tickets.SingleAsync();
        var b = await second.Tickets.SingleAsync();
        a.TransitionTo(TicketStatus.InProgress); await first.SaveChangesAsync();
        b.TransitionTo(TicketStatus.InProgress);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var reloaded = new TicketDbContext(options);
        Assert.Equal(TicketStatus.InProgress, (await reloaded.Tickets.SingleAsync()).Status);
    }
    [Fact] public void ValidWorkflowAndReopening()
    {
        var ticket = new Ticket("Impressora", "Não imprime", TicketPriority.High);
        ticket.TransitionTo(TicketStatus.InProgress);
        ticket.TransitionTo(TicketStatus.Resolved);
        ticket.TransitionTo(TicketStatus.InProgress);
        ticket.TransitionTo(TicketStatus.Resolved);
        ticket.TransitionTo(TicketStatus.Closed);
        Assert.Equal(TicketStatus.Closed, ticket.Status);
        Assert.Throws<InvalidOperationException>(() => ticket.TransitionTo(TicketStatus.Open));
    }
    [Fact] public void CannotSkipWorkflow() => Assert.Throws<InvalidOperationException>(() => new Ticket("Teste", "Detalhes", TicketPriority.Normal).TransitionTo(TicketStatus.Closed));
    [Theory] [InlineData("")] [InlineData(" ")]
    public void RejectsEmptyTitle(string title) => Assert.Throws<ArgumentException>(() => new Ticket(title, "Detalhes", TicketPriority.Normal));
    [Fact] public async Task ApiPersistsWorkflowAndHistory()
    {
        using var factory = NewFactory();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/tickets", new { title = "Impressora", description = "Não imprime", priority = "High" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PatchAsJsonAsync($"/api/tickets/{id}/status", new { status = "Closed" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/tickets/{id}/comments", new { message = "Verificando" })).StatusCode);
        foreach (var status in new[] { "InProgress", "Resolved", "Closed" })
            Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync($"/api/tickets/{id}/status", new { status })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/tickets/{id}/comments", new { message = "Teste" })).StatusCode);
        var history = await client.GetFromJsonAsync<JsonElement>($"/api/tickets/{id}/history");
        Assert.Equal(5, history.GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/tickets", new { title = "Teste", description = "Detalhes", priority = "Urgent" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/tickets/{Guid.NewGuid()}")).StatusCode);
    }
    [Fact] public async Task ConfiguredApiKeyProtectsEndpoints()
    {
        using var factory = NewFactory("test-only-key");
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/tickets")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-only-key");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/tickets")).StatusCode);
    }
    private static WebApplicationFactory<Program> NewFactory(string key = "") => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        b.UseEnvironment("Development").UseSetting("ConnectionStrings:Default", $"Data Source={Path.Combine(Path.GetTempPath(), $"ticket-test-{Guid.NewGuid()}.db")}").UseSetting("ApiKey", key));
}
