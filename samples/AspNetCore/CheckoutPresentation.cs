using System.Text.Json;

namespace Lunixi.AspNetCoreSample;

/// <summary>Merchant-page presentation only; callers authorize the order and browser CSRF before use.</summary>
public static class CheckoutPresentation
{
    /// <summary>Returns an escaped fragment that mounts the existing official form. Send it with no-store.</summary>
    public static string EmbeddedHtml(string token, string? nonce = null)
    {
        if (token.Length is < 1 or > 16384 || token.Any(c => c < 0x21 || c > 0x7e)) throw new SamplePresentationException();
        if (nonce is not null && (nonce.Length is < 16 or > 128 || nonce.Any(c =>
            !(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '+' or '/' or '_' or '=' or '-'))))
            throw new SamplePresentationException();
        var attribute = nonce is null ? "" : " nonce=\"" + nonce + "\"";
        // Default JsonSerializer escaping prevents a token from terminating the inert script block.
        var json = JsonSerializer.Serialize(new { type = "payment", target = "lunixi-checkout-form", options = new { token } });
        return "<div id=\"lunixi-checkout-form\"></div><script src=\"https://cdn.lunixi.com/lunixi-checkout.min.js\"" + attribute + "></script>" +
            "<script type=\"application/json\" id=\"lunixi-checkout-config\"" + attribute + ">" + json + "</script><script" + attribute + ">" +
            "(()=>{const config=JSON.parse(document.getElementById('lunixi-checkout-config').textContent);const checkout=Lunixi.checkout.create(config);checkout.mount('#lunixi-checkout-form');})();</script>";
    }

    /// <summary>Validates only the backend URL; never derive it from or compare its token to the browser token.</summary>
    public static string HostedUrl(string? value)
    {
        const string prefix = "https://pay.lunixi.com/payment/";
        if (value is null || !value.StartsWith(prefix, StringComparison.Ordinal) || value.Length < prefix.Length + 16 ||
            value.Length > prefix.Length + 4096 || value.Skip(prefix.Length).Any(c =>
                !(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '.' or '~' or '-')))
            throw new SamplePresentationException();
        return value;
    }
}

/// <summary>A safe presentation failure; the original attempt/key must be retained for reconciliation.</summary>
public sealed class SamplePresentationException : Exception { }
