using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace TEC.Http;

/// <summary>
/// Leitura da seção <c>Http</c> sem reflexão (o <c>Bind</c> do configuration binder não é compatível com Native AOT).
/// Valor presente e ilegível (ex.: <c>"abc"</c> onde se espera número) derruba a subida: nunca é trocado pelo padrão em silêncio.
/// </summary>
internal static class TecHttpOptionsReader
{
    public static void Read(IConfiguration section, TecHttpOptions options)
    {
        var forwarding = section.GetSection("Forwarding");
        options.Forwarding.Enabled = Bool(forwarding, "Enabled", options.Forwarding.Enabled);
        options.Forwarding.ForwardLimit = Int(forwarding, "ForwardLimit", options.Forwarding.ForwardLimit);
        options.Forwarding.KnownProxies.AddRange(List(forwarding, "KnownProxies"));
        options.Forwarding.KnownNetworks.AddRange(List(forwarding, "KnownNetworks"));

        var hsts = section.GetSection("Hsts");
        options.Hsts.Enabled = Bool(hsts, "Enabled", options.Hsts.Enabled);
        options.Hsts.MaxAgeDays = Int(hsts, "MaxAgeDays", options.Hsts.MaxAgeDays);

        options.SecurityHeaders.Enabled = Bool(section.GetSection("SecurityHeaders"), "Enabled", options.SecurityHeaders.Enabled);

        var rateLimiting = section.GetSection("RateLimiting");
        options.RateLimiting.Enabled = Bool(rateLimiting, "Enabled", options.RateLimiting.Enabled);
        ReadRule(rateLimiting.GetSection("PerIp"), options.RateLimiting.PerIp);
        ReadRule(rateLimiting.GetSection("PerIdentity"), options.RateLimiting.PerIdentity);

        var cors = section.GetSection("Cors");
        options.Cors.Origins.AddRange(List(cors, "Origins"));
        options.Cors.Methods.AddRange(List(cors, "Methods"));
        options.Cors.Headers.AddRange(List(cors, "Headers"));
        options.Cors.ExposedHeaders.AddRange(List(cors, "ExposedHeaders"));
        options.Cors.AllowCredentials = Bool(cors, "AllowCredentials", options.Cors.AllowCredentials);
        options.Cors.MaxAgeSeconds = Int(cors, "MaxAgeSeconds", options.Cors.MaxAgeSeconds);
    }

    private static void ReadRule(IConfiguration section, TecRateLimitRule rule)
    {
        rule.PermitLimit = Int(section, "PermitLimit", rule.PermitLimit);
        rule.WindowSeconds = Int(section, "WindowSeconds", rule.WindowSeconds);
    }

    private static bool Bool(IConfiguration section, string key, bool fallback) =>
        section[key] is not { Length: > 0 } text
            ? fallback
            : bool.TryParse(text, out var value)
                ? value
                : throw Invalid(section, key, text, "true ou false");

    private static int Int(IConfiguration section, string key, int fallback) =>
        section[key] is not { Length: > 0 } text
            ? fallback
            : int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : throw Invalid(section, key, text, "um número inteiro");

    private static IEnumerable<string> List(IConfiguration section, string key) =>
        section.GetSection(key).GetChildren()
            .Select(child => child.Value?.Trim())
            .Where(value => !string.IsNullOrEmpty(value))!;

    private static InvalidOperationException Invalid(IConfiguration section, string key, string value, string expected) =>
        new($"Configuração '{(section as IConfigurationSection)?.Path}:{key}' inválida: '{value}' (esperado {expected}).");
}
