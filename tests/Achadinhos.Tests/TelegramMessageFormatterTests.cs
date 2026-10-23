using System.Net;
using System.Text.RegularExpressions;
using Achadinhos.Application.Publishing;
using Achadinhos.Infrastructure.Telegram;
using Xunit;

namespace Achadinhos.Tests;

public sealed class TelegramMessageFormatterTests
{
    private static TelegramPublicationRequest Request(string title = "SSD 1 TB") =>
        new(title, 329.90m, null, "https://example.com/affiliate");

    [Fact]
    public void EscapesExternalTextAndUsesBrazilianPricesWithoutInventingDiscounts()
    {
        var html = TelegramMessageFormatter.Format(Request("<SSD> & \"oferta\" 🛒"), false);
        Assert.Contains("&lt;SSD&gt; &amp; &quot;oferta&quot;", html);
        Assert.Contains("329,90", html);
        Assert.Contains(TelegramMessageFormatter.Disclosure, html);
        Assert.DoesNotContain("De R$", html);
        Assert.DoesNotContain("%", html);
        Assert.DoesNotContain("<SSD>", html);
    }

    [Fact]
    public void IncludesOnlyTheSuppliedReferencePrice()
    {
        var html = TelegramMessageFormatter.Format(Request() with { ReferencePrice = 399.90m }, false);
        Assert.Contains("399,90", html);
        Assert.Contains("329,90", html);
    }

    [Theory]
    [InlineData(false, 4096)]
    [InlineData(true, 1024)]
    public void TruncatesTextElementsBeforeEncodingAndKeepsMarkupValid(bool photo, int limit)
    {
        var title = string.Concat(Enumerable.Repeat("🛒e\u0301<&", 1300));
        var html = TelegramMessageFormatter.Format(Request(title), photo);
        var plain = WebUtility.HtmlDecode(Regex.Replace(html, "</?b>", ""));
        Assert.True(plain.Length <= limit);
        Assert.Contains("…</b>\n", html);
        Assert.Equal(2, Regex.Matches(html, "<b>").Count);
        Assert.Equal(2, Regex.Matches(html, "</b>").Count);
        Assert.DoesNotContain("\uFFFD", plain);
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:password@example.com")]
    [InlineData("file:///C:/secret")]
    [InlineData("")]
    public void RejectsUnsafeAffiliateUrls(string url)
    {
        var error = Assert.Throws<TelegramPublicationException>(() =>
            TelegramMessageFormatter.Format(Request() with { AffiliateUrl = url }, false));
        Assert.Equal(TelegramFailureKind.InvalidRequest, error.Kind);
    }

    [Theory]
    [InlineData(0, null, "BRL")]
    [InlineData(10.123, null, "BRL")]
    [InlineData(10, 5d, "BRL")]
    [InlineData(10, 10d, "BRL")]
    [InlineData(10, null, "USD")]
    public void RejectsInvalidPricesAndUnsupportedCurrency(double price, double? reference, string currency)
    {
        Assert.Throws<TelegramPublicationException>(() => TelegramMessageFormatter.Format(
            Request() with { CurrentPrice = (decimal)price, ReferencePrice = (decimal?)reference, Currency = currency }, false));
    }
}
