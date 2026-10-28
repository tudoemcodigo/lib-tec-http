using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Options;

namespace TEC.Http.Tests;

public class HardeningTests
{
    [Test]
    public async Task AllowedHosts_curinga_fora_de_Development_derruba_a_subida()
    {
        await Assert.ThrowsAsync<OptionsValidationException>(async () =>
        {
            await using var app = await TestHost.StartAsync(TestHost.Config(("AllowedHosts", "*")), "Production", allowedHosts: false);
        });
    }

    [Test]
    public async Task AllowedHosts_ausente_fora_de_Development_derruba_a_subida()
    {
        await Assert.ThrowsAsync<OptionsValidationException>(async () =>
        {
            await using var app = await TestHost.StartAsync(environment: "Production", allowedHosts: false);
        });
    }

    [Test]
    public async Task AllowedHosts_curinga_em_Development_sobe()
    {
        await using var app = await TestHost.StartAsync(TestHost.Config(("AllowedHosts", "*")), "Development", allowedHosts: false);
        using var response = await app.GetTestClient().GetAsync("/ok");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task AllowedHosts_com_subdominio_curinga_e_aceito()
    {
        await using var app = await TestHost.StartAsync(TestHost.Config(("AllowedHosts", "*.example.com;localhost")), allowedHosts: false);
        using var response = await app.GetTestClient().GetAsync("/ok");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Headers_de_seguranca_estao_presentes()
    {
        await using var app = await TestHost.StartAsync();
        using var response = await app.GetTestClient().GetAsync("/ok");

        await Assert.That(response.Headers.GetValues("X-Content-Type-Options").Single()).IsEqualTo("nosniff");
        await Assert.That(response.Headers.GetValues("X-Frame-Options").Single()).IsEqualTo("DENY");
        await Assert.That(response.Headers.GetValues("Referrer-Policy").Single()).IsEqualTo("no-referrer");
    }

    [Test]
    public async Task Headers_de_seguranca_podem_ser_desligados()
    {
        await using var app = await TestHost.StartAsync(TestHost.Config(("Http:SecurityHeaders:Enabled", "false")));
        using var response = await app.GetTestClient().GetAsync("/ok");
        await Assert.That(response.Headers.Contains("X-Content-Type-Options")).IsFalse();
    }

    [Test]
    public async Task Hsts_sai_em_https_fora_de_Development()
    {
        await using var app = await TestHost.StartAsync(TestHost.Config(("AllowedHosts", "api.example.com")));
        var client = app.GetTestClient();
        client.BaseAddress = new Uri("https://api.example.com");
        using var response = await client.GetAsync("/ok");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.GetValues("Strict-Transport-Security").Single()).Contains("max-age=31536000");
    }

    [Test]
    public async Task Hsts_nao_sai_em_Development()
    {
        await using var app = await TestHost.StartAsync(TestHost.Config(("AllowedHosts", "api.example.com")), "Development");
        var client = app.GetTestClient();
        client.BaseAddress = new Uri("https://api.example.com");
        using var response = await client.GetAsync("/ok");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.Contains("Strict-Transport-Security")).IsFalse();
    }

    [Test]
    public async Task XForwardedFor_e_ignorado_sem_proxy_configurado()
    {
        await using var app = await TestHost.StartAsync();
        using var request = TestHost.Get("/ip", remote: "198.51.100.7");
        request.Headers.Add("X-Forwarded-For", "203.0.113.9");

        using var response = await app.GetTestClient().SendAsync(request);

        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("198.51.100.7");
    }

    [Test]
    public async Task XForwardedFor_e_aceito_de_proxy_conhecido()
    {
        await using var app = await TestHost.StartAsync(TestHost.Config(
            ("Http:Forwarding:Enabled", "true"), ("Http:Forwarding:KnownProxies:0", "10.0.0.1")));
        using var request = TestHost.Get("/ip", remote: "10.0.0.1");
        request.Headers.Add("X-Forwarded-For", "203.0.113.9");

        using var response = await app.GetTestClient().SendAsync(request);

        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("203.0.113.9");
    }

    [Test]
    public async Task XForwardedFor_e_aceito_de_rede_conhecida()
    {
        await using var app = await TestHost.StartAsync(TestHost.Config(
            ("Http:Forwarding:Enabled", "true"), ("Http:Forwarding:KnownNetworks:0", "10.1.0.0/16")));
        using var request = TestHost.Get("/ip", remote: "10.1.5.5");
        request.Headers.Add("X-Forwarded-For", "203.0.113.9");

        using var response = await app.GetTestClient().SendAsync(request);

        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("203.0.113.9");
    }

    [Test]
    public async Task XForwardedFor_de_origem_nao_confiavel_e_ignorado_mesmo_com_forwarding_ligado()
    {
        await using var app = await TestHost.StartAsync(TestHost.Config(
            ("Http:Forwarding:Enabled", "true"), ("Http:Forwarding:KnownProxies:0", "10.0.0.1")));
        using var request = TestHost.Get("/ip", remote: "198.51.100.7");
        request.Headers.Add("X-Forwarded-For", "203.0.113.9");

        using var response = await app.GetTestClient().SendAsync(request);

        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("198.51.100.7");
    }

    [Test]
    public async Task Forwarding_ligado_sem_proxy_nem_rede_derruba_a_subida()
    {
        await Assert.ThrowsAsync<OptionsValidationException>(async () =>
        {
            await using var app = await TestHost.StartAsync(TestHost.Config(("Http:Forwarding:Enabled", "true")));
        });
    }

    [Test]
    public async Task Forwarding_com_proxy_invalido_derruba_a_subida()
    {
        await Assert.ThrowsAsync<OptionsValidationException>(async () =>
        {
            await using var app = await TestHost.StartAsync(TestHost.Config(
                ("Http:Forwarding:Enabled", "true"), ("Http:Forwarding:KnownProxies:0", "nao-e-ip")));
        });
    }

    [Test]
    public async Task Forwarding_com_rede_invalida_derruba_a_subida()
    {
        await Assert.ThrowsAsync<OptionsValidationException>(async () =>
        {
            await using var app = await TestHost.StartAsync(TestHost.Config(
                ("Http:Forwarding:Enabled", "true"), ("Http:Forwarding:KnownNetworks:0", "10.0.0.0/99")));
        });
    }
}
