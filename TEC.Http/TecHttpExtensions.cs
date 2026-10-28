using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TEC.Http.Internal;

namespace TEC.Http;

/// <summary>Registro e pipeline do <c>TEC.Http</c>: borda HTTP fechada por padrão (proxy, hosts, HSTS, headers, limite e CORS).</summary>
public static class TecHttpExtensions
{
    internal const string CorsPolicyName = "tec-http";

    private static readonly string[] DefaultMethods = ["GET", "POST", "PUT", "PATCH", "DELETE"];
    private static readonly string[] DefaultHeaders = ["Content-Type", "Authorization", "X-Api-Key", "Idempotency-Key", "If-Match"];

    /// <summary>
    /// Registra as opções (seção <c>Http</c>), a validação na subida, o CORS e o HSTS. Configuração insegura ou incoerente
    /// (ex.: <c>AllowedHosts</c> <c>*</c> fora de Development, CORS <c>*</c> com credenciais) derruba a aplicação.
    /// </summary>
    /// <param name="services">Container em configuração.</param>
    /// <param name="configuration">Configuração da aplicação.</param>
    /// <param name="configure">Ajuste em código, aplicado depois da configuração.</param>
    /// <returns>O mesmo container.</returns>
    public static IServiceCollection AddTecHttp(
        this IServiceCollection services, IConfiguration configuration, Action<TecHttpOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<TecHttpOptions>()
            .Configure(options =>
            {
                TecHttpOptionsReader.Read(configuration.GetSection(TecHttpOptions.SectionName), options);
                configure?.Invoke(options);
            })
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TecHttpOptions>, TecHttpOptionsValidator>());
        services.TryAddSingleton<TecHttpMetrics>();

        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<IOptions<TecHttpOptions>>((cors, http) =>
        {
            var options = http.Value.Cors;
            if (options.Origins.Count > 0)
                cors.AddPolicy(CorsPolicyName, policy => BuildCorsPolicy(policy, options));
        });

        services.AddHsts(_ => { });
        services.AddOptions<HstsOptions>().Configure<IOptions<TecHttpOptions>>((hsts, http) =>
        {
            hsts.MaxAge = TimeSpan.FromDays(http.Value.Hsts.MaxAgeDays);
            hsts.IncludeSubDomains = false;
            hsts.Preload = false;
        });

        return services;
    }

    /// <summary>
    /// Pipeline de borda, na ordem: proxy confiável, HSTS, headers de segurança, limite por IP e CORS. Chame depois do
    /// tratamento de exceções e <b>antes</b> de <c>UseAuthentication</c> (o limite por IP precisa conter tentativas de
    /// credencial). Lê as opções aqui, então configuração inválida derruba a subida neste ponto.
    /// </summary>
    /// <param name="app">Pipeline da aplicação.</param>
    /// <returns>O mesmo pipeline.</returns>
    public static IApplicationBuilder UseTecHttp(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var services = app.ApplicationServices;
        var options = services.GetRequiredService<IOptions<TecHttpOptions>>().Value;
        var environment = services.GetRequiredService<IHostEnvironment>();

        if (options.Forwarding.Enabled)
        {
            app.UseForwardedHeaders(BuildForwardedHeaders(options.Forwarding));
        }
        else
        {
            app.Use(IgnoredForwardedHeaderMiddleware(
                services.GetRequiredService<TecHttpMetrics>(),
                services.GetRequiredService<ILoggerFactory>().CreateLogger("TEC.Http")));
        }

        if (!environment.IsDevelopment() && options.Hsts.Enabled)
            app.UseHsts();

        if (options.SecurityHeaders.Enabled)
            app.Use(SecurityHeadersMiddleware);

        if (options.RateLimiting.Enabled)
            app.Use(RateLimitMiddleware(services, options.RateLimiting.PerIp, ClientPartition.Ip, "ip"));

        if (options.Cors.Origins.Count > 0)
            app.UseCors(CorsPolicyName);

        return app;
    }

    /// <summary>
    /// Limite por identidade autenticada. Chame <b>depois</b> de <c>UseAuthentication</c>. Requisições anônimas passam direto
    /// (já foram contidas pelo limite por IP do <see cref="UseTecHttp"/>).
    /// </summary>
    /// <param name="app">Pipeline da aplicação.</param>
    /// <returns>O mesmo pipeline.</returns>
    public static IApplicationBuilder UseTecRateLimitingPerIdentity(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var services = app.ApplicationServices;
        var options = services.GetRequiredService<IOptions<TecHttpOptions>>().Value.RateLimiting;
        if (options.Enabled)
            app.Use(RateLimitMiddleware(services, options.PerIdentity, ClientPartition.Identity, "identity"));

        return app;
    }

    private static void BuildCorsPolicy(CorsPolicyBuilder policy, TecCorsOptions options)
    {
        if (options.Origins.Contains("*"))
            policy.AllowAnyOrigin();
        else
            policy.WithOrigins([.. options.Origins]);

        policy.WithMethods(options.Methods.Count > 0 ? [.. options.Methods] : DefaultMethods);
        policy.WithHeaders(options.Headers.Count > 0 ? [.. options.Headers] : DefaultHeaders);
        if (options.ExposedHeaders.Count > 0)
            policy.WithExposedHeaders([.. options.ExposedHeaders]);
        if (options.AllowCredentials)
            policy.AllowCredentials();
        policy.SetPreflightMaxAge(TimeSpan.FromSeconds(options.MaxAgeSeconds));
    }

    private static ForwardedHeadersOptions BuildForwardedHeaders(TecForwardingOptions forwarding)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = forwarding.ForwardLimit
        };

        // O padrão do ASP.NET confia em loopback; aqui só vale o que foi configurado
        options.KnownProxies.Clear();
#if NET10_0_OR_GREATER
        options.KnownIPNetworks.Clear();
#else
        options.KnownNetworks.Clear();
#endif

        foreach (var proxy in forwarding.KnownProxies)
            options.KnownProxies.Add(IPAddress.Parse(proxy));

        foreach (var network in forwarding.KnownNetworks)
        {
            TecHttpOptionsValidator.TryParseNetwork(network, out var address, out var prefix);
#if NET10_0_OR_GREATER
            options.KnownIPNetworks.Add(new System.Net.IPNetwork(address, prefix));
#else
            options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(address, prefix));
#endif
        }

        return options;
    }

    private static Func<HttpContext, RequestDelegate, Task> IgnoredForwardedHeaderMiddleware(TecHttpMetrics metrics, ILogger logger)
    {
        int warned = 0;
        return (context, next) =>
        {
            if (context.Request.Headers.ContainsKey("X-Forwarded-For"))
            {
                metrics.ForwardedIgnored();
                if (Interlocked.Exchange(ref warned, 1) == 0)
                    TecHttpLog.ForwardedHeaderIgnored(logger);
            }

            return next(context);
        };
    }

    private static Task SecurityHeadersMiddleware(HttpContext context, RequestDelegate next)
    {
        context.Response.OnStarting(static state =>
        {
            var headers = ((HttpContext)state).Response.Headers;
            headers.TryAdd("X-Content-Type-Options", "nosniff");
            headers.TryAdd("X-Frame-Options", "DENY");
            headers.TryAdd("Referrer-Policy", "no-referrer");
            return Task.CompletedTask;
        }, context);
        return next(context);
    }

    private static Func<HttpContext, RequestDelegate, Task> RateLimitMiddleware(
        IServiceProvider services, TecRateLimitRule rule, Func<HttpContext, string?> key, string stage)
    {
        var metrics = services.GetRequiredService<TecHttpMetrics>();
        var limiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            key(context) is { } partition
                ? RateLimitPartition.GetSlidingWindowLimiter(partition, _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = rule.PermitLimit,
                    Window = TimeSpan.FromSeconds(rule.WindowSeconds),
                    SegmentsPerWindow = rule.WindowSeconds >= 4 ? 4 : 1,
                    QueueLimit = 0,
                    AutoReplenishment = true
                })
                : RateLimitPartition.GetNoLimiter("anonymous"));
        services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopped.Register(limiter.Dispose);

        return async (context, next) =>
        {
            using var lease = limiter.AttemptAcquire(context);
            if (lease.IsAcquired)
            {
                await next(context).ConfigureAwait(false);
                return;
            }

            metrics.Rejected(stage);
            await RateLimitResponse.WriteAsync(context, lease, rule.WindowSeconds).ConfigureAwait(false);
        };
    }
}
