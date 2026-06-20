namespace Relaywright.Web.Data.Entities;

public sealed class QueuedMessageRecipient
{
    public int Id { get; set; }

    public Guid QueuedMessageId { get; set; }

    public string RecipientAddress { get; set; } = string.Empty;

    public QueuedMessage QueuedMessage { get; set; } = null!;
}

