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
    [Arguments("Methods", "*", "true")]
    [Arguments("Headers", "*", "true")]
    [Arguments("ExposedHeaders", "*", "true")]
    [Arguments("Methods", "GET POST", "false")]
    [Arguments("Methods", "GET,POST", "false")]
    [Arguments("Headers", "X-Header\r\nInjetado", "false")]
    [Arguments("Headers", "Content Type", "false")]
    [Arguments("ExposedHeaders", "ETag;", "false")]
    [Arguments("ExposedHeaders", "Retry-After:", "false")]
    public async Task Metodos_e_headers_invalidos_derrubam_a_subida(string lista, string valor, string credenciais)
    {
        await Assert.ThrowsAsync<OptionsValidationException>(async () =>
        {
            await using var app = await TestHost.StartAsync(TestHost.Config(
                ("Http:Cors:Origins:0", "https://app.example.com"),
                ($"Http:Cors:{lista}:0", valor),
                ("Http:Cors:AllowCredentials", credenciais)));
        });
    }

    [Test]
    [Arguments("Methods")]
    [Arguments("Headers")]
    [Arguments("ExposedHeaders")]
    public async Task Curinga_misturado_com_outros_itens_derruba_a_subida(string lista)
    {
        await Assert.ThrowsAsync<OptionsValidationException>(async () =>
        {
            await using var app = await TestHost.StartAsync(TestHost.Config(
                ("Http:Cors:Origins:0", "https://app.example.com"),
                ($"Http:Cors:{lista}:0", "*"),
                ($"Http:Cors:{lista}:1", "X-Outro")));
        });
    }

    [Test]
    public async Task Item_muito_longo_derruba_a_subida()
    {
        await Assert.ThrowsAsync<OptionsValidationException>(async () =>
        {
            await using var app = await TestHost.StartAsync(TestHost.Config(
                ("Http:Cors:Origins:0", "https://app.example.com"),
                ("Http:Cors:Headers:0", "X-" + new string('a', 200))));
        });
    }

    [Test]
    public async Task Curinga_sozinho_sem_credenciais_e_aceito_em_metodos_e_headers()
    {
        await using var app = await TestHost.StartAsync(TestHost.Config(
            ("Http:Cors:Origins:0", "https://app.example.com"),
            ("Http:Cors:Methods:0", "*"),
            ("Http:Cors:Headers:0", "*")));
        using var request = TestHost.Preflight("/ok", "198.51.100.1", "https://app.example.com", "DELETE");
        request.Headers.Add("Access-Control-Request-Headers", "X-Qualquer");

        using var response = await app.GetTestClient().SendAsync(request);

        await Assert.That(response.Headers.GetValues("Access-Control-Allow-Methods").Single()).Contains("DELETE");
    }

    [Test]
    public async Task Metodos_e_headers_validos_sobem_com_credenciais()
    {
        await using var app = await TestHost.StartAsync(TestHost.Config(
            ("Http:Cors:Origins:0", "https://app.example.com"),
            ("Http:Cors:AllowCredentials", "true"),
            ("Http:Cors:Methods:0", "GET"),
            ("Http:Cors:Methods:1", "POST"),
            ("Http:Cors:Headers:0", "Content-Type"),
            ("Http:Cors:ExposedHeaders:0", "ETag")));
        using var request = TestHost.Get("/ok", "198.51.100.1", origin: "https://app.example.com");
        using var response = await app.GetTestClient().SendAsync(request);

        await Assert.That(response.Headers.GetValues("Access-Control-Expose-Headers").Single()).IsEqualTo("ETag");
    }

    [Test]
    [Arguments("Http:Cors:Origins", "https://a.example.com,https://b.example.com")]
    [Arguments("Http:Cors:Methods", "GET,POST")]
    [Arguments("Http:Cors:Headers", "Content-Type")]
    [Arguments("Http:Cors:ExposedHeaders", "ETag")]
    [Arguments("Http:Forwarding:KnownProxies", "10.0.0.1")]
    [Arguments("Http:Forwarding:KnownNetworks", "10.1.0.0/16")]
    public async Task Lista_com_valor_unico_derruba_a_subida_com_mensagem_clara(string chave, string valor)
    {
        // Ex.: variável Http__Cors__Origins=https://a,https://b. Ignorar deixaria a lista vazia sem aviso.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var app = await TestHost.StartAsync(TestHost.Config((chave, valor)));
        });

        await Assert.That(error!.Message).Contains(chave + ":0");
    }

    [Test]
    public async Task Lista_vazia_explicita_continua_valendo_como_vazia()
    {
        // JSON "Origins": "" (ou variável vazia) não é lista com item: segue fechado, sem derrubar a subida
        await using var app = await TestHost.StartAsync(TestHost.Config(("Http:Cors:Origins", "")));
        using var request = TestHost.Get("/ok", "198.51.100.1", origin: "https://app.example.com");
        using var response = await app.GetTestClient().SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.Contains("Access-Control-Allow-Origin")).IsFalse();
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
