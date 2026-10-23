using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Achadinhos.Infrastructure.Telegram;

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";
    public bool Enabled { get; set; }
    public string BotToken { get; set; } = "";
    public string ChatId { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 15;

    public override string ToString() => $"Telegram: Enabled={Enabled}; credentials redacted";
}

public sealed class TelegramOptionsValidator : IValidateOptions<TelegramOptions>
{
    public ValidateOptionsResult Validate(string? name, TelegramOptions options)
    {
        if (!options.Enabled) return ValidateOptionsResult.Success;
        var validToken = Regex.IsMatch(options.BotToken, @"\A[0-9]{5,}:[A-Za-z0-9_-]{20,}\z");
        var validChat = (long.TryParse(options.ChatId, NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var chat) && chat < 0)
            || Regex.IsMatch(options.ChatId, @"\A@[A-Za-z][A-Za-z0-9_]{4,31}\z");
        return validToken && validChat && options.TimeoutSeconds is >= 1 and <= 120
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Configure Telegram token, channel and timeout (1..120 seconds) locally. Values are redacted.");
    }
}
