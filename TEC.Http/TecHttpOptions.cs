namespace TEC.Http;

/// <summary>
/// Opções do <c>TEC.Http</c> (seção <c>Http</c> da configuração). Tudo é fechado por padrão: sem configuração, nenhuma
/// origem CORS é liberada, nenhum header <c>X-Forwarded-*</c> é aceito e o limite de requisições fica ligado.
/// </summary>
public sealed class TecHttpOptions
{
    /// <summary>Seção de configuração.</summary>
    public const string SectionName = "Http";

    /// <summary>Proxy reverso confiável (origem aceita para <c>X-Forwarded-For</c> e <c>X-Forwarded-Proto</c>).</summary>
    public TecForwardingOptions Forwarding { get; set; } = new();

    /// <summary>HSTS (só fora de Development).</summary>
    public TecHstsOptions Hsts { get; set; } = new();

    /// <summary>Headers de segurança nas respostas.</summary>
    public TecSecurityHeadersOptions SecurityHeaders { get; set; } = new();

    /// <summary>Limite de requisições.</summary>
    public TecRateLimitingOptions RateLimiting { get; set; } = new();

    /// <summary>CORS.</summary>
    public TecCorsOptions Cors { get; set; } = new();
}

/// <summary>
/// Confiança em proxy reverso. Os headers <c>X-Forwarded-*</c> são de livre escolha do cliente: só são aceitos quando a
/// conexão vem de um proxy listado aqui. Desligado (padrão), são ignorados e o IP é o da conexão.
/// </summary>
public sealed class TecForwardingOptions
{
    /// <summary>Liga o tratamento de <c>X-Forwarded-For</c> e <c>X-Forwarded-Proto</c>. Exige ao menos um proxy ou uma rede.</summary>
    public bool Enabled { get; set; }

    /// <summary>Endereços IP dos proxies confiáveis (ex.: <c>10.0.0.1</c>). Loopback não é confiável por padrão: liste-o se precisar.</summary>
    public List<string> KnownProxies { get; set; } = [];

    /// <summary>Redes dos proxies confiáveis em CIDR (ex.: <c>10.1.0.0/16</c>). O prefixo <c>/0</c> é recusado.</summary>
    public List<string> KnownNetworks { get; set; } = [];

    /// <summary>Quantos proxies encadeados são aceitos no <c>X-Forwarded-For</c>. Padrão: 1 (1 a 5).</summary>
    public int ForwardLimit { get; set; } = 1;
}

/// <summary>HSTS: o navegador passa a exigir HTTPS. Não é emitido em Development.</summary>
public sealed class TecHstsOptions
{
    /// <summary>Liga o header <c>Strict-Transport-Security</c> fora de Development. Padrão: <c>true</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Validade em dias (1 a 730). Padrão: 365. Sem <c>includeSubDomains</c> nem <c>preload</c> (irreversíveis na prática).</summary>
    public int MaxAgeDays { get; set; } = 365;
}

/// <summary>Headers de segurança: <c>X-Content-Type-Options</c>, <c>X-Frame-Options</c> e <c>Referrer-Policy</c>.</summary>
public sealed class TecSecurityHeadersOptions
{
    /// <summary>Liga os headers. Padrão: <c>true</c>.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Limite de requisições em memória, <b>por instância</b>: com N instâncias o limite efetivo é N vezes o configurado.
/// Dois estágios: por IP (antes da autenticação, contém tentativas de credencial) e por identidade (depois dela).
/// </summary>
public sealed class TecRateLimitingOptions
{
    /// <summary>Liga o limite. Padrão: <c>true</c> (desligar é decisão explícita, por exemplo com gateway à frente).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Limite por IP (IPv6 agrupado por prefixo /64). Vale para toda requisição, autenticada ou não.</summary>
    public TecRateLimitRule PerIp { get; set; } = new() { PermitLimit = 100, WindowSeconds = 60 };

    /// <summary>Limite por identidade autenticada. Requisições anônimas não passam por este estágio.</summary>
    public TecRateLimitRule PerIdentity { get; set; } = new() { PermitLimit = 600, WindowSeconds = 60 };
}

/// <summary>Regra de janela deslizante.</summary>
public sealed class TecRateLimitRule
{
    /// <summary>Requisições permitidas por janela (1 a 1.000.000).</summary>
    public int PermitLimit { get; set; }

    /// <summary>Tamanho da janela em segundos (1 a 86.400).</summary>
    public int WindowSeconds { get; set; }
}

/// <summary>
/// CORS fechado por padrão: sem <see cref="Origins"/>, nenhuma origem é liberada. Servidores que consomem a API não usam
/// CORS (é uma regra de navegador).
/// </summary>
public sealed class TecCorsOptions
{
    /// <summary>
    /// Origens liberadas, no formato <c>https://app.exemplo.com</c> (sem caminho, sem barra final, sem curinga). <c>*</c> só
    /// em Development e sem <see cref="AllowCredentials"/>. <c>http</c> só para localhost em Development.
    /// </summary>
    public List<string> Origins { get; set; } = [];

    /// <summary>Métodos liberados. Vazio: GET, POST, PUT, PATCH e DELETE.</summary>
    public List<string> Methods { get; set; } = [];

    /// <summary>Headers de requisição liberados. Vazio: Content-Type, Authorization, X-Api-Key, Idempotency-Key e If-Match.</summary>
    public List<string> Headers { get; set; } = [];

    /// <summary>Headers de resposta expostos ao navegador (ex.: <c>ETag</c>, <c>Retry-After</c>).</summary>
    public List<string> ExposedHeaders { get; set; } = [];

    /// <summary>Permite credenciais (cookies, Authorization). Exige origens explícitas.</summary>
    public bool AllowCredentials { get; set; }

    /// <summary>Cache do preflight em segundos (0 a 86.400). Padrão: 600.</summary>
    public int MaxAgeSeconds { get; set; } = 600;
}
