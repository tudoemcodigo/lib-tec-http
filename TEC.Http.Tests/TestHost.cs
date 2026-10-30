using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TEC.Http.Tests;

/// <summary>Host de teste com o pipeline na ordem recomendada. O IP de origem vem do header <c>X-Test-Remote</c>.</summary>
internal static class TestHost
{
    public const string RemoteHeader = "X-Test-Remote";
    public const string UserHeader = "X-Test-User";
    public const string ConnectionHeader = "X-Test-Connection";

    public static async Task<WebApplication> StartAsync(
        IDictionary<string, string?>? config = null,
        string environment = "Production",
        bool allowedHosts = true)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        if (allowedHosts)
            builder.Configuration["AllowedHosts"] = "localhost";
        if (config is not null)
            builder.Configuration.AddInMemoryCollection(config);

        builder.Services.AddTecHttp(builder.Configuration);

        var app = builder.Build();
        app.Use((ctx, next) =>
        {
            ctx.Connection.RemoteIpAddress = ctx.Request.Headers.TryGetValue(RemoteHeader, out var ip) && IPAddress.TryParse(ip, out var parsed)
                ? parsed
                : null;
            // Id de conexão controlado pelo teste (sem o header: vazio, como servidor que não informa a conexão)
            ctx.Connection.Id = ctx.Request.Headers.TryGetValue(ConnectionHeader, out var connection) ? connection.ToString() : "";
            return next();
        });
        app.UseTecHttp();
        app.Use((ctx, next) =>
        {
            if (ctx.Request.Headers.TryGetValue(UserHeader, out var user))
            {
                // "(sem-id)": autenticado, mas sem claim de identificador
                Claim[] claims = user.ToString() == "(sem-id)" ? [] : [new Claim("sub", user.ToString())];
                ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "teste"));
            }
            return next();
        });
        app.UseTecRateLimitingPerIdentity();
        app.MapGet("/ok", () => "ok");
        app.MapGet("/ip", (HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "null");

        await app.StartAsync();
        return app;
    }

    public static HttpRequestMessage Get(
        string path, string? remote = null, string? user = null, string? origin = null, string? connection = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (remote is not null) request.Headers.Add(RemoteHeader, remote);
        if (user is not null) request.Headers.Add(UserHeader, user);
        if (origin is not null) request.Headers.Add("Origin", origin);
        if (connection is not null) request.Headers.Add(ConnectionHeader, connection);
        return request;
    }

    /// <summary>Preflight CORS (OPTIONS com <c>Origin</c> e <c>Access-Control-Request-Method</c>).</summary>
    public static HttpRequestMessage Preflight(string path, string remote, string origin, string method = "GET")
    {
        var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add(RemoteHeader, remote);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        return request;
    }

    public static Dictionary<string, string?> Config(params (string Key, string Value)[] items) =>
        items.ToDictionary(i => i.Key, i => (string?)i.Value);
}
