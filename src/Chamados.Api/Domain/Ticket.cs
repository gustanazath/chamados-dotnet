namespace Chamados.Api.Domain;

public enum TicketStatus { Open, InProgress, Resolved, Closed }
public enum TicketPriority { Low, Normal, High }
public sealed class Ticket
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Title { get; private set; } = "";
    public string Description { get; private set; } = "";
    public TicketPriority Priority { get; private set; }
    public TicketStatus Status { get; private set; } = TicketStatus.Open;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;
    public Guid Version { get; private set; } = Guid.NewGuid();
    private Ticket() { }
    public Ticket(string title, string description, TicketPriority priority)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 120) throw new ArgumentException("Título deve ter entre 1 e 120 caracteres.");
        if (string.IsNullOrWhiteSpace(description) || description.Trim().Length > 2000) throw new ArgumentException("Descrição deve ter entre 1 e 2000 caracteres.");
        if (!Enum.IsDefined(priority)) throw new ArgumentException("Prioridade inválida.");
        Title = title.Trim(); Description = description.Trim(); Priority = priority;
    }
    public void TransitionTo(TicketStatus next)
    {
        bool allowed = (Status, next) switch
        {
            (TicketStatus.Open, TicketStatus.InProgress) => true,
            (TicketStatus.InProgress, TicketStatus.Resolved) => true,
            (TicketStatus.Resolved, TicketStatus.Closed) => true,
            (TicketStatus.Resolved, TicketStatus.InProgress) => true,
            _ => false
        };
        if (!allowed) throw new InvalidOperationException("Transição de status não permitida.");
        Status = next; Touch();
    }
    public void Touch() { UpdatedAtUtc = DateTime.UtcNow; Version = Guid.NewGuid(); }
}
public sealed class TicketEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TicketId { get; set; }
    public string Kind { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
