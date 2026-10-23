namespace Achadinhos.Application.Publishing;

public interface ITelegramPublisher
{
    Task<TelegramPublicationResult> PublishAsync(
        TelegramPublicationRequest request, CancellationToken cancellationToken = default);
}
