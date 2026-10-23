using System.Globalization;
using System.Net;
using Achadinhos.Application.Publishing;

namespace Achadinhos.Infrastructure.Telegram;

public static class TelegramMessageFormatter
{
    public const string Disclosure = "Publicidade: link de afiliado.";
    private static readonly CultureInfo PriceCulture = CultureInfo.GetCultureInfo("pt-BR");

    public static bool IsSafeHttpsUrl(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 2048
        && Uri.TryCreate(value, UriKind.Absolute, out var url)
        && url.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(url.UserInfo);

    public static string Format(TelegramPublicationRequest request, bool photo)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 10000
            || request.Currency != "BRL" || !ValidPrice(request.CurrentPrice)
            || (request.ReferencePrice is { } reference
                && (!ValidPrice(reference) || reference <= request.CurrentPrice))
            || !IsSafeHttpsUrl(request.AffiliateUrl))
            throw new TelegramPublicationException(TelegramFailureKind.InvalidRequest);

        var price = $"Por {request.CurrentPrice.ToString("C2", PriceCulture)}";
        var referenceText = request.ReferencePrice is { } previous
            ? $"De {previous.ToString("C2", PriceCulture)}\n" : "";
        var suffix = $"\n{referenceText}{price}\n{Disclosure}";
        var title = Truncate(request.Title.Trim(), (photo ? 1024 : 4096) - suffix.Length);
        return $"<b>{WebUtility.HtmlEncode(title)}</b>\n"
            + WebUtility.HtmlEncode(referenceText)
            + $"<b>{WebUtility.HtmlEncode(price)}</b>\n{Disclosure}";
    }

    private static bool ValidPrice(decimal value) =>
        value > 0 && value <= 9999999999999999.99m && decimal.Round(value, 2) == value;

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength) return value;
        // Cut whole text elements before HTML encoding; never split markup or Unicode.
        var starts = StringInfo.ParseCombiningCharacters(value);
        var end = starts.TakeWhile(x => x <= maxLength - 1).Last();
        return value[..end] + "…";
    }
}
