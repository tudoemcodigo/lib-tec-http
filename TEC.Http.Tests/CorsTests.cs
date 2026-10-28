using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Options;

namespace TEC.Http.Tests;

public class CorsTests
{
    [Test]
    public async Task Sem_origens_configuradas_nenhuma_origem_e_liberada()
    {
        await using var app = await TestHost.StartAsync();
        using var request = TestHost.Get("/ok", "198.51.100.1", origin: "https://app.example.com");
        using var response = await app.GetTestClient().SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.Contains("Access-Control-Allow-Origin")).IsFalse();
    }

    [Test]
    public async Task Origem_da_allowlist_e_liberada()
    {
        await using var app = await TestHost.StartAsync(TestHost.Config(("Http:Cors:Origins:0", "https://app.example.com")));
        using var request = TestHost.Get("/ok", "198.51.100.1", origin: "https://app.example.com");
        using var response = await app.GetTestClient().SendAsync(request);

        await Assert.That(response.Headers.GetValues("Access-Control-Allow-Origin").Single()).IsEqualTo("https://app.example.com");
        await Assert.That(response.Headers.Contains("Access-Control-Allow-Credentials")).IsFalse();
    }

    [Test]
    public async Task Origem_fora_da_allowlist_nao_e_liberada()
    {
        await using var app = await TestHost.StartAsync(TestHost.Config(("Http:Cors:Origins:0", "https://app.example.com")));
        using var request = TestHost.Get("/ok", "198.51.100.1", origin: "https://evil.example.com");
        using var response = await app.GetTestClient().SendAsync(request);

        await Assert.That(response.Headers.Contains("Access-Control-Allow-Origin")).IsFalse();
    }

    [Test]
    public async Task Origem_com_sufixo_parecido_nao_e_liberada()
    {
        await using var app = await TestHost.StartAsync(TestHost.Config(("Http:Cors:Origins:0", "https://app.example.com")));
        using var request = TestHost.Get("/ok", "198.51.100.1", origin: "https://app.example.com.evil.io");
        using var response = await app.GetTestClient().SendAsync(request);

        await Assert.That(response.Headers.Contains("Access-Control-Allow-Origin")).IsFalse();
    }

    [Test]
    public async Task Preflight_devolve_metodos_e_max_age_configurados()
    {
        await using var app = await TestHost.StartAsync(TestHost.Config(
            ("Http:Cors:Origins:0", "https://app.example.com"),
            ("Http:Cors:Methods:0", "GET"),
            ("Http:Cors:Headers:0", "Content-Type"),
            ("Http:Cors:MaxAgeSeconds", "120")));
        using var request = new HttpRequestMessage(HttpMethod.Options, "/ok");
        request.Headers.Add(TestHost.RemoteHeader, "198.51.100.1");
        request.Headers.Add("Origin", "https://app.example.com");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "Content-Type");

        using var response = await app.GetTestClient().SendAsync(request);

        await Assert.That(response.Headers.GetValues("Access-Control-Allow-Methods").Single()).Contains("GET");
        await Assert.That(response.Headers.GetValues("Access-Control-Max-Age").Single()).IsEqualTo("120");
    }

    [Test]
    public async Task Preflight_de_metodo_nao_permitido_nao_e_liberado()
    {
        await using var app = await TestHost.StartAsync(TestHost.Config(
            ("Http:Cors:Origins:0", "https://app.example.com"), ("Http:Cors:Methods:0", "GET")));
        using var request = new HttpRequestMessage(HttpMethod.Options, "/ok");
        request.Headers.Add(TestHost.RemoteHeader, "198.51.100.1");
        request.Headers.Add("Origin", "https://app.example.com");
        request.Headers.Add("Access-Control-Request-Method", "DELETE");

        using var response = await app.GetTestClient().SendAsync(request);

        // O navegador bloqueia o preflight quando o método pedido não está em Allow-Methods
        var permitidos = response.Headers.TryGetValues("Access-Control-Allow-Methods", out var values) ? string.Join(',', values) : "";
        await Assert.That(permitidos.Contains("DELETE", StringComparison.OrdinalIgnoreCase)).IsFalse();
    }

    [Test]
    public async Task Credenciais_so_com_origem_explicita()
    {
        await using var app = await TestHost.StartAsync(TestHost.Config(
            ("Http:Cors:Origins:0", "https://app.example.com"), ("Http:Cors:AllowCredentials", "true")));
        using var request = TestHost.Get("/ok", "198.51.100.1", origin: "https://app.example.com");
        using var response = await app.GetTestClient().SendAsync(request);

        await Assert.That(response.Headers.GetValues("Access-Control-Allow-Credentials").Single()).IsEqualTo("true");
    }

    [Test]
    [Arguments("Production", "*", "false")]
    [Arguments("Development", "*", "true")]
    [Arguments("Production", "*", "true")]
    [Arguments("Production", "http://app.example.com", "false")]
    [Arguments("Production", "https://app.example.com/caminho", "false")]
    [Arguments("Production", "app.example.com", "false")]
    [Arguments("Production", "https://*.example.com", "false")]
    [Arguments("Production", "null", "false")]
    public async Task Origens_inseguras_derrubam_a_subida(string ambiente, string origem, string credenciais)
    {
        await Assert.ThrowsAsync<OptionsValidationException>(async () =>
        {
            await using var app = await TestHost.StartAsync(TestHost.Config(
                ("Http:Cors:Origins:0", origem), ("Http:Cors:AllowCredentials", credenciais)), ambiente);
        });
    }

    [Test]
    public async Task Origem_http_localhost_e_aceita_em_Development()
    {
        await using var app = await TestHost.StartAsync(
            TestHost.Config(("Http:Cors:Origins:0", "http://localhost:3000")), "Development");
        using var request = TestHost.Get("/ok", "198.51.100.1", origin: "http://localhost:3000");
        using var response = await app.GetTestClient().SendAsync(request);

        await Assert.That(response.Headers.GetValues("Access-Control-Allow-Origin").Single()).IsEqualTo("http://localhost:3000");
    }

    [Test]
    public async Task Curinga_em_Development_sem_credenciais_e_aceito()
    {
        await using var app = await TestHost.StartAsync(TestHost.Config(("Http:Cors:Origins:0", "*")), "Development");
        using var request = TestHost.Get("/ok", "198.51.100.1", origin: "https://qualquer.example.com");
        using var response = await app.GetTestClient().SendAsync(request);

        await Assert.That(response.Headers.GetValues("Access-Control-Allow-Origin").Single()).IsEqualTo("*");
    }

    [Test]
    public async Task MaxAge_invalido_derruba_a_subida()
    {
        await Assert.ThrowsAsync<OptionsValidationException>(async () =>
        {
            await using var app = await TestHost.StartAsync(TestHost.Config(
                ("Http:Cors:Origins:0", "https://app.example.com"), ("Http:Cors:MaxAgeSeconds", "-5")));
        });
    }
}
