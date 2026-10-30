using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Options;

namespace TEC.Http.Tests;

public class RateLimitingTests
{
    private static Dictionary<string, string?> Limite(int porIp, int porIdentidade = 1000) => TestHost.Config(
        ("Http:RateLimiting:PerIp:PermitLimit", porIp.ToString(CultureInfo.InvariantCulture)),
        ("Http:RateLimiting:PerIp:WindowSeconds", "60"),
        ("Http:RateLimiting:PerIdentity:PermitLimit", porIdentidade.ToString(CultureInfo.InvariantCulture)),
        ("Http:RateLimiting:PerIdentity:WindowSeconds", "60"));

    private static async Task<HttpStatusCode> Status(HttpClient client, string? remote, string? user = null, string? connection = null)
    {
        using var request = TestHost.Get("/ok", remote, user, connection: connection);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    [Test]
    public async Task Excedido_o_limite_por_ip_responde_429_no_envelope_padrao_com_RetryAfter()
    {
        await using var app = await TestHost.StartAsync(Limite(porIp: 2));
        var client = app.GetTestClient();

        await Status(client, "198.51.100.1");
        await Status(client, "198.51.100.1");
        using var request = TestHost.Get("/ok", "198.51.100.1");
        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/json");
        var retryAfter = int.Parse(response.Headers.GetValues("Retry-After").Single(), CultureInfo.InvariantCulture);
        await Assert.That(retryAfter).IsGreaterThan(0);
        await Assert.That(retryAfter).IsLessThanOrEqualTo(60);
        await Assert.That(response.Headers.GetValues("Cache-Control").Single()).IsEqualTo("no-store");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("success").GetBoolean()).IsFalse();
        await Assert.That(body.RootElement.GetProperty("statusCode").GetInt32()).IsEqualTo(429);
        await Assert.That(string.IsNullOrEmpty(body.RootElement.GetProperty("traceId").GetString())).IsFalse();
    }

    [Test]
    public async Task Ips_diferentes_tem_baldes_separados()
    {
        await using var app = await TestHost.StartAsync(Limite(porIp: 1));
        var client = app.GetTestClient();

        await Assert.That(await Status(client, "198.51.100.1")).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await Status(client, "198.51.100.1")).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(await Status(client, "198.51.100.2")).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Ipv6_do_mesmo_prefixo_64_compartilha_o_balde()
    {
        await using var app = await TestHost.StartAsync(Limite(porIp: 1));
        var client = app.GetTestClient();

        await Assert.That(await Status(client, "2001:db8:1:2::1")).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await Status(client, "2001:db8:1:2:ffff:ffff:ffff:ffff")).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(await Status(client, "2001:db8:1:3::1")).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Sem_ip_o_limite_vale_por_conexao_e_nao_junta_clientes_num_balde_so()
    {
        await using var app = await TestHost.StartAsync(Limite(porIp: 1));
        var client = app.GetTestClient();

        await Assert.That(await Status(client, remote: null, connection: "conexao-a")).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await Status(client, remote: null, connection: "conexao-a")).IsEqualTo(HttpStatusCode.TooManyRequests);
        // Outro cliente (outra conexão) não é punido pelo primeiro
        await Assert.That(await Status(client, remote: null, connection: "conexao-b")).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Sem_ip_com_id_de_conexao_longo_continua_limitado_sem_confundir_conexoes()
    {
        await using var app = await TestHost.StartAsync(Limite(porIp: 1));
        var client = app.GetTestClient();
        var a = new string('c', 500);
        var b = new string('c', 499) + "d";

        await Assert.That(await Status(client, remote: null, connection: a)).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await Status(client, remote: null, connection: a)).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(await Status(client, remote: null, connection: b)).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Sem_ip_nem_id_de_conexao_cai_em_balde_compartilhado_e_continua_limitado()
    {
        await using var app = await TestHost.StartAsync(Limite(porIp: 1));
        var client = app.GetTestClient();

        await Assert.That(await Status(client, remote: null)).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await Status(client, remote: null)).IsEqualTo(HttpStatusCode.TooManyRequests);
    }

    [Test]
    public async Task Id_de_conexao_nao_muda_o_balde_quando_ha_ip()
    {
        await using var app = await TestHost.StartAsync(Limite(porIp: 1));
        var client = app.GetTestClient();

        // Com IP conhecido, abrir outra conexão não dá limite novo
        await Assert.That(await Status(client, "198.51.100.1", connection: "conexao-a")).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await Status(client, "198.51.100.1", connection: "conexao-b")).IsEqualTo(HttpStatusCode.TooManyRequests);
    }

    [Test]
    public async Task XForwardedFor_forjado_nao_burla_o_limite_sem_proxy_configurado()
    {
        await using var app = await TestHost.StartAsync(Limite(porIp: 2));
        var client = app.GetTestClient();
        var statuses = new List<HttpStatusCode>();

        for (var i = 0; i < 4; i++)
        {
            using var request = TestHost.Get("/ok", "198.51.100.1");
            request.Headers.Add("X-Forwarded-For", $"203.0.113.{i + 1}");
            using var response = await client.SendAsync(request);
            statuses.Add(response.StatusCode);
        }

        await Assert.That(statuses.Count(s => s == HttpStatusCode.OK)).IsEqualTo(2);
        await Assert.That(statuses.Count(s => s == HttpStatusCode.TooManyRequests)).IsEqualTo(2);
    }

    [Test]
    public async Task Atras_de_proxy_confiavel_o_limite_e_por_cliente_real()
    {
        var config = Limite(porIp: 1);
        config["Http:Forwarding:Enabled"] = "true";
        config["Http:Forwarding:KnownProxies:0"] = "10.0.0.1";
        await using var app = await TestHost.StartAsync(config);
        var client = app.GetTestClient();

        async Task<HttpStatusCode> ViaProxy(string cliente)
        {
            using var request = TestHost.Get("/ok", "10.0.0.1");
            request.Headers.Add("X-Forwarded-For", cliente);
            using var response = await client.SendAsync(request);
            return response.StatusCode;
        }

        await Assert.That(await ViaProxy("203.0.113.1")).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await ViaProxy("203.0.113.1")).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(await ViaProxy("203.0.113.2")).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Rajada_concorrente_respeita_exatamente_o_limite()
    {
        await using var app = await TestHost.StartAsync(Limite(porIp: 10));
        var client = app.GetTestClient();

        var statuses = await Task.WhenAll(Enumerable.Range(0, 60).Select(_ => Status(client, "198.51.100.1")));

        await Assert.That(statuses.Count(s => s == HttpStatusCode.OK)).IsEqualTo(10);
        await Assert.That(statuses.Count(s => s == HttpStatusCode.TooManyRequests)).IsEqualTo(50);
    }

    [Test]
    public async Task Limite_por_identidade_separa_usuarios_e_ignora_anonimo()
    {
        await using var app = await TestHost.StartAsync(Limite(porIp: 1000, porIdentidade: 1));
        var client = app.GetTestClient();

        await Assert.That(await Status(client, "198.51.100.1", "alice")).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await Status(client, "198.51.100.1", "alice")).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(await Status(client, "198.51.100.1", "bob")).IsEqualTo(HttpStatusCode.OK);
        // anônimo não passa pelo estágio de identidade (só pelo de IP)
        await Assert.That(await Status(client, "198.51.100.1")).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await Status(client, "198.51.100.1")).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Autenticado_sem_identificador_cai_em_balde_proprio_e_continua_limitado()
    {
        await using var app = await TestHost.StartAsync(Limite(porIp: 1000, porIdentidade: 1));
        var client = app.GetTestClient();

        await Assert.That(await Status(client, "198.51.100.1", "(sem-id)")).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await Status(client, "198.51.100.2", "(sem-id)")).IsEqualTo(HttpStatusCode.TooManyRequests);
    }

    [Test]
    public async Task Identificador_muito_longo_e_limitado_sem_confundir_identidades()
    {
        await using var app = await TestHost.StartAsync(Limite(porIp: 1000, porIdentidade: 1));
        var client = app.GetTestClient();
        var a = new string('a', 5000);
        var b = new string('a', 4999) + "b";

        await Assert.That(await Status(client, "198.51.100.1", a)).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await Status(client, "198.51.100.1", a)).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(await Status(client, "198.51.100.1", b)).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Identidade_e_limitada_em_qualquer_ip()
    {
        await using var app = await TestHost.StartAsync(Limite(porIp: 1000, porIdentidade: 2));
        var client = app.GetTestClient();

        await Status(client, "198.51.100.1", "alice");
        await Status(client, "198.51.100.2", "alice");

        await Assert.That(await Status(client, "198.51.100.3", "alice")).IsEqualTo(HttpStatusCode.TooManyRequests);
    }

    [Test]
    public async Task Desligado_nao_limita()
    {
        var config = Limite(porIp: 1);
        config["Http:RateLimiting:Enabled"] = "false";
        await using var app = await TestHost.StartAsync(config);
        var client = app.GetTestClient();

        for (var i = 0; i < 5; i++)
            await Assert.That(await Status(client, "198.51.100.1")).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Padrao_esta_ligado()
    {
        await using var app = await TestHost.StartAsync();
        var client = app.GetTestClient();

        var statuses = await Task.WhenAll(Enumerable.Range(0, 300).Select(_ => Status(client, "198.51.100.1")));

        await Assert.That(statuses.Any(s => s == HttpStatusCode.TooManyRequests)).IsTrue();
    }

    [Test]
    public async Task Resposta_429_sai_com_headers_cors_para_origem_liberada()
    {
        var config = Limite(porIp: 1);
        config["Http:Cors:Origins:0"] = "https://app.example.com";
        config["Http:Cors:ExposedHeaders:0"] = "Retry-After";
        await using var app = await TestHost.StartAsync(config);
        var client = app.GetTestClient();

        using (var primeira = TestHost.Get("/ok", "198.51.100.1", origin: "https://app.example.com"))
            (await client.SendAsync(primeira)).Dispose();

        using var request = TestHost.Get("/ok", "198.51.100.1", origin: "https://app.example.com");
        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(response.Headers.GetValues("Access-Control-Allow-Origin").Single()).IsEqualTo("https://app.example.com");
        await Assert.That(response.Headers.GetValues("Access-Control-Expose-Headers").Single()).Contains("Retry-After");
    }

    [Test]
    public async Task Resposta_429_nao_libera_origem_fora_da_allowlist()
    {
        var config = Limite(porIp: 1);
        config["Http:Cors:Origins:0"] = "https://app.example.com";
        await using var app = await TestHost.StartAsync(config);
        var client = app.GetTestClient();

        await Status(client, "198.51.100.1");
        using var request = TestHost.Get("/ok", "198.51.100.1", origin: "https://evil.example.com");
        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
        await Assert.That(response.Headers.Contains("Access-Control-Allow-Origin")).IsFalse();
    }

    [Test]
    public async Task Preflight_cors_nao_consome_o_limite()
    {
        var config = Limite(porIp: 1);
        config["Http:Cors:Origins:0"] = "https://app.example.com";
        await using var app = await TestHost.StartAsync(config);
        var client = app.GetTestClient();

        for (var i = 0; i < 5; i++)
        {
            using var preflight = TestHost.Preflight("/ok", "198.51.100.1", "https://app.example.com");
            using var resposta = await client.SendAsync(preflight);
            await Assert.That(resposta.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        }

        // Preflight de origem não liberada também é respondido pelo CORS (sem headers de liberação) e não conta
        using (var estranho = TestHost.Preflight("/ok", "198.51.100.1", "https://evil.example.com"))
        using (var resposta = await client.SendAsync(estranho))
            await Assert.That(resposta.Headers.Contains("Access-Control-Allow-Origin")).IsFalse();

        await Assert.That(await Status(client, "198.51.100.1")).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await Status(client, "198.51.100.1")).IsEqualTo(HttpStatusCode.TooManyRequests);
    }

    [Test]
    public async Task Sem_cors_configurado_options_continua_contando_no_limite()
    {
        // Sem CORS não há preflight a responder: OPTIONS é uma requisição comum e passa pelo limite (falha fechada)
        await using var app = await TestHost.StartAsync(Limite(porIp: 1));
        var client = app.GetTestClient();

        using (var preflight = TestHost.Preflight("/ok", "198.51.100.1", "https://app.example.com"))
            (await client.SendAsync(preflight)).Dispose();

        await Assert.That(await Status(client, "198.51.100.1")).IsEqualTo(HttpStatusCode.TooManyRequests);
    }

    [Test]
    [Arguments("Http:RateLimiting:PerIp:PermitLimit", "0")]
    [Arguments("Http:RateLimiting:PerIp:PermitLimit", "-1")]
    [Arguments("Http:RateLimiting:PerIp:WindowSeconds", "0")]
    [Arguments("Http:RateLimiting:PerIdentity:PermitLimit", "0")]
    [Arguments("Http:RateLimiting:PerIdentity:WindowSeconds", "86401")]
    public async Task Limites_invalidos_derrubam_a_subida(string chave, string valor)
    {
        await Assert.ThrowsAsync<OptionsValidationException>(async () =>
        {
            await using var app = await TestHost.StartAsync(TestHost.Config((chave, valor)));
        });
    }
}
