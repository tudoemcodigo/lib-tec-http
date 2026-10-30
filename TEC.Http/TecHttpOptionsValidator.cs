using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace TEC.Http;

/// <summary>Valida as opções na subida: configuração insegura ou incoerente derruba a aplicação, nunca a primeira requisição.</summary>
internal sealed class TecHttpOptionsValidator(IConfiguration configuration, IHostEnvironment environment) : IValidateOptions<TecHttpOptions>
{
    private const int MaxLimit = 1_000_000;
    private const int MaxSeconds = 86_400;
    private const int MaxTokenLength = 128;

    public ValidateOptionsResult Validate(string? name, TecHttpOptions options)
    {
        var failures = new List<string>();
        bool development = environment.IsDevelopment();

        ValidateAllowedHosts(development, failures);
        ValidateForwarding(options.Forwarding, failures);
        ValidateHsts(options.Hsts, failures);
        ValidateRateLimiting(options.RateLimiting, failures);
        ValidateCors(options.Cors, development, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private void ValidateAllowedHosts(bool development, List<string> failures)
    {
        if (development)
            return;

        var hosts = configuration["AllowedHosts"];
        if (string.IsNullOrWhiteSpace(hosts)
            || hosts.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(h => h == "*"))
        {
            failures.Add("AllowedHosts deve listar os hosts da API (ex.: 'api.exemplo.com') fora de Development; vazio ou '*' aceita qualquer Host.");
        }
    }

    private static void ValidateForwarding(TecForwardingOptions forwarding, List<string> failures)
    {
        if (!forwarding.Enabled)
            return;

        if (forwarding.KnownProxies.Count == 0 && forwarding.KnownNetworks.Count == 0)
            failures.Add("Http:Forwarding:Enabled exige ao menos um item em KnownProxies ou KnownNetworks (sem eles qualquer cliente forjaria o IP).");

        foreach (var proxy in forwarding.KnownProxies)
        {
            if (!IPAddress.TryParse(proxy, out _))
                failures.Add($"Http:Forwarding:KnownProxies: '{proxy}' não é um endereço IP.");
        }

        foreach (var network in forwarding.KnownNetworks)
        {
            if (!TryParseNetwork(network, out _, out _))
                failures.Add($"Http:Forwarding:KnownNetworks: '{network}' não é uma rede CIDR válida com prefixo maior que 0 (ex.: 10.1.0.0/16).");
        }

        if (forwarding.ForwardLimit is < 1 or > 5)
            failures.Add("Http:Forwarding:ForwardLimit deve ficar entre 1 e 5.");
    }

    private static void ValidateHsts(TecHstsOptions hsts, List<string> failures)
    {
        if (hsts.MaxAgeDays is < 1 or > 730)
            failures.Add("Http:Hsts:MaxAgeDays deve ficar entre 1 e 730.");
    }

    private static void ValidateRateLimiting(TecRateLimitingOptions rateLimiting, List<string> failures)
    {
        if (!rateLimiting.Enabled)
            return;

        ValidateRule(rateLimiting.PerIp, "Http:RateLimiting:PerIp", failures);
        ValidateRule(rateLimiting.PerIdentity, "Http:RateLimiting:PerIdentity", failures);
    }

    private static void ValidateRule(TecRateLimitRule rule, string path, List<string> failures)
    {
        if (rule.PermitLimit is < 1 or > MaxLimit)
            failures.Add($"{path}:PermitLimit deve ficar entre 1 e {MaxLimit.ToString(CultureInfo.InvariantCulture)}.");
        if (rule.WindowSeconds is < 1 or > MaxSeconds)
            failures.Add($"{path}:WindowSeconds deve ficar entre 1 e {MaxSeconds.ToString(CultureInfo.InvariantCulture)}.");
    }

    private static void ValidateCors(TecCorsOptions cors, bool development, List<string> failures)
    {
        if (cors.MaxAgeSeconds is < 0 or > MaxSeconds)
            failures.Add($"Http:Cors:MaxAgeSeconds deve ficar entre 0 e {MaxSeconds.ToString(CultureInfo.InvariantCulture)}.");

        foreach (var origin in cors.Origins)
        {
            if (origin == "*")
            {
                if (!development)
                    failures.Add("Http:Cors:Origins: '*' só é aceito em Development; liste as origens.");
                else if (cors.AllowCredentials)
                    failures.Add("Http:Cors:Origins: '*' não pode ser combinado com AllowCredentials.");
                continue;
            }

            if (!IsValidOrigin(origin, development))
            {
                failures.Add($"Http:Cors:Origins: '{origin}' inválida; use 'https://host[:porta]' sem caminho, barra final ou curinga ('http' só para localhost em Development).");
            }
        }

        ValidateTokens(cors.Methods, "Http:Cors:Methods", "método HTTP", cors.AllowCredentials, failures);
        ValidateTokens(cors.Headers, "Http:Cors:Headers", "nome de header", cors.AllowCredentials, failures);
        ValidateTokens(cors.ExposedHeaders, "Http:Cors:ExposedHeaders", "nome de header", cors.AllowCredentials, failures);
    }

    /// <summary>
    /// Itens de <c>Methods</c>, <c>Headers</c> e <c>ExposedHeaders</c>: cada um é um token HTTP (RFC 9110), sem espaço,
    /// vírgula ou caractere de controle. <c>*</c> só sozinho (misturado, o ASP.NET o trataria como texto literal) e nunca
    /// com <c>AllowCredentials</c>: com credenciais o ASP.NET devolve ao navegador os headers pedidos no preflight, o que
    /// liberaria qualquer header com cookies e Authorization.
    /// </summary>
    private static void ValidateTokens(List<string> values, string path, string kind, bool allowCredentials, List<string> failures)
    {
        foreach (var value in values)
        {
            if (value == "*")
            {
                if (allowCredentials)
                    failures.Add($"{path}: '*' não pode ser combinado com AllowCredentials; liste os itens.");
                else if (values.Count > 1)
                    failures.Add($"{path}: '*' precisa ser o único item da lista.");
                continue;
            }

            if (value.Length > MaxTokenLength)
                failures.Add($"{path}: item com mais de {MaxTokenLength.ToString(CultureInfo.InvariantCulture)} caracteres não é um {kind} válido.");
            else if (!IsToken(value))
                failures.Add($"{path}: '{value}' não é um {kind} válido (token HTTP, sem espaço nem vírgula; liste um item por índice).");
        }
    }

    /// <summary>tchar da RFC 9110 (seção 5.6.2): letras, dígitos e <c>!#$%&amp;'*+-.^_`|~</c>.</summary>
    private static bool IsToken(string value)
    {
        if (value.Length == 0)
            return false;

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && !"!#$%&'*+-.^_`|~".Contains(c, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static bool IsValidOrigin(string origin, bool development)
    {
        if (origin.Contains('*', StringComparison.Ordinal) || !Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            return false;
        if (!string.Equals(uri.GetLeftPart(UriPartial.Authority), origin, StringComparison.Ordinal) || uri.UserInfo.Length > 0)
            return false;

        if (uri.Scheme == Uri.UriSchemeHttps)
            return true;

        return development && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
    }

    /// <summary>Lê <c>endereço/prefixo</c>; recusa prefixo 0 (confiaria em toda a internet) e fora da faixa do protocolo.</summary>
    internal static bool TryParseNetwork(string value, out IPAddress address, out int prefix)
    {
        address = IPAddress.None;
        prefix = 0;

        var parts = value.Split('/');
        if (parts.Length != 2
            || !IPAddress.TryParse(parts[0], out var parsed)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out prefix))
        {
            return false;
        }

        int max = parsed.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        address = parsed;
        return prefix >= 1 && prefix <= max;
    }
}
