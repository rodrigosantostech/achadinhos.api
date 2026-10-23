namespace Achadinhos.Infrastructure.Telegram;

internal sealed record TelegramButton(string Text, string Url);
internal sealed record TelegramKeyboard(TelegramButton[][] InlineKeyboard);
internal sealed record TelegramTextPayload(
    string ChatId, string Text, string ParseMode, TelegramKeyboard ReplyMarkup);
internal sealed record TelegramPhotoPayload(
    string ChatId, string Photo, string Caption, string ParseMode, TelegramKeyboard ReplyMarkup);

internal sealed class TelegramApiResponse
{
    public bool? Ok { get; set; }
    public TelegramMessage? Result { get; set; }
    public int? ErrorCode { get; set; }
    public string? Description { get; set; }
    public TelegramResponseParameters? Parameters { get; set; }
}

internal sealed class TelegramMessage
{
    public int MessageId { get; set; }
    public long Date { get; set; }
    public TelegramChat? Chat { get; set; }
}

internal sealed class TelegramChat
{
    public long Id { get; set; }
}

internal sealed class TelegramResponseParameters
{
    public int? RetryAfter { get; set; }
}
