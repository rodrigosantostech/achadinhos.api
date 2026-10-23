namespace Achadinhos.Domain.Discovery;

public sealed class PublicationSimulation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PublicationId { get; set; }
    public DateTimeOffset SimulatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Operator { get; set; } = "";
    public string PreviewText { get; set; } = "";
    public string ProductUrl { get; set; } = "";
    public string Snapshot { get; set; } = "{}";
}
