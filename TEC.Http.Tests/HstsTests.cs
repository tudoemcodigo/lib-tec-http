using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Options;

namespace TEC.Http.Tests;

/// <summary>HSTS configurável: o header efetivo é conferido por HTTP (HTTPS, fora de Development).</summary>
public class HstsTests
{
    private static async Task<string?> HstsHeaderAsync(string environment, params (string Key, string Value)[] items)
    {
        var config = TestHost.Config([("AllowedHosts", "api.example.com"), .. items]);
        await using var app = await TestHost.StartAsync(config, environment);
        var client = app.GetTestClient();
        client.BaseAddress = new Uri("https://api.example.com");
        using var response = await client.GetAsync("/ok");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return response.Headers.TryGetValues("Strict-Transport-Security", out var values) ? values.Single() : null;
    }

    [Test]
    public async Task Padrao_emite_so_max_age()
    {
        var header = await HstsHeaderAsync("Production");

        await Assert.That(header).IsEqualTo("max-age=31536000");
    }

    [Test]
    public async Task IncludeSubDomains_so_sai_quando_configurado()
    {
        var header = await HstsHeaderAsync("Production", ("Http:Hsts:IncludeSubDomains", "true"));

        await Assert.That(header).IsEqualTo("max-age=31536000; includeSubDomains");
    }

    [Test]
    public async Task Preload_com_requisitos_emite_os_tres_elementos()
    {
        var header = await HstsHeaderAsync("Production",
            ("Http:Hsts:IncludeSubDomains", "true"), ("Http:Hsts:Preload", "true"), ("Http:Hsts:MaxAgeDays", "730"));

        await Assert.That(header).IsEqualTo("max-age=63072000; includeSubDomains; preload");
    }

    [Test]
    public async Task Preload_explicitamente_falso_nao_emite_preload()
    {
        var header = await HstsHeaderAsync("Production", ("Http:Hsts:IncludeSubDomains", "true"), ("Http:Hsts:Preload", "false"));

        await Assert.That(header).DoesNotContain("preload");
    }

    [Test]
    public async Task Preload_em_Development_sobe_sem_header()
    {
        var header = await HstsHeaderAsync("Development", ("Http:Hsts:IncludeSubDomains", "true"), ("Http:Hsts:Preload", "true"));

        await Assert.That(header).IsNull();
    }

    [Test]
    public async Task Preload_sem_IncludeSubDomains_derruba_a_subida_com_a_chave()
    {
        var error = await Assert.ThrowsAsync<OptionsValidationException>(async () =>
            await HstsHeaderAsync("Production", ("Http:Hsts:Preload", "true")));

        await Assert.That(error!.Message).Contains("Http:Hsts:Preload").And.Contains("Http:Hsts:IncludeSubDomains");
    }

    [Test]
    public async Task Preload_com_MaxAge_abaixo_de_um_ano_derruba_a_subida_com_a_chave()
    {
        var error = await Assert.ThrowsAsync<OptionsValidationException>(async () =>
            await HstsHeaderAsync("Production",
                ("Http:Hsts:IncludeSubDomains", "true"), ("Http:Hsts:Preload", "true"), ("Http:Hsts:MaxAgeDays", "364")));

        await Assert.That(error!.Message).Contains("Http:Hsts:MaxAgeDays");
    }

    [Test]
    public async Task Preload_invalido_tambem_derruba_a_subida_em_Development()
    {
        await Assert.ThrowsAsync<OptionsValidationException>(async () =>
            await HstsHeaderAsync("Development", ("Http:Hsts:Preload", "true")));
    }

    [Test]
    public async Task Preload_invalido_derruba_a_subida_mesmo_com_Hsts_desligado()
    {
        await Assert.ThrowsAsync<OptionsValidationException>(async () =>
            await HstsHeaderAsync("Production", ("Http:Hsts:Enabled", "false"), ("Http:Hsts:Preload", "true")));
    }

    [Test]
    [Arguments("Http:Hsts:Preload", "talvez")]
    [Arguments("Http:Hsts:Preload", "1")]
    [Arguments("Http:Hsts:IncludeSubDomains", "sim")]
    public async Task Valor_ilegivel_derruba_a_subida_com_a_chave(string chave, string valor)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await HstsHeaderAsync("Production", (chave, valor)));

        await Assert.That(error!.Message).Contains(chave);
    }
}
