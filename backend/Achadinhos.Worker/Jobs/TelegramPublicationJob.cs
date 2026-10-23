using Achadinhos.Infrastructure.Telegram;
using Hangfire;

namespace Achadinhos.Worker.Jobs;

public sealed class TelegramPublicationJob(PublicationProcessor processor)
{
    [AutomaticRetry(Attempts = 0)]
    [Queue("telegram")]
    public Task RunAsync(Guid publicationId, CancellationToken cancellationToken) =>
        processor.ProcessAsync(publicationId, cancellationToken);
}
