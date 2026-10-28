<div align="center">

<img src="https://www.tudoemcodigo.com/marca/PNG/tudoemcodigo-icone-512.png" alt="Tudo em Código" width="120" />

[🏠 Tudo em Código](https://github.com/tudoemcodigo) › [📦 TEC.Http](https://github.com/tudoemcodigo/lib-tec-http)

# 🛡️ TEC.Http

**Borda HTTP fechada por padrão para APIs ASP.NET Core: proxy confiável explícito, hosts permitidos, HSTS e headers de segurança, limite de requisições com resposta 429 padronizada e CORS por allowlist.**

[![CI](https://github.com/tudoemcodigo/lib-tec-http/actions/workflows/ci.yml/badge.svg)](https://github.com/tudoemcodigo/lib-tec-http/actions/workflows/ci.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-512BD4?logo=dotnet&logoColor=white)](#-compatibilidade)
[![Native AOT](https://img.shields.io/badge/Native%20AOT-compat%C3%ADvel-512BD4)](#-compatibilidade)
[![Versão](https://img.shields.io/badge/vers%C3%A3o-0.0.1-blue)](CHANGELOG.md)
[![NuGet](https://img.shields.io/badge/nuget-GitHub%20Packages-0969da?logo=github)](https://github.com/orgs/tudoemcodigo/packages)
[![Licença: Apache 2.0](https://img.shields.io/badge/licen%C3%A7a-Apache%202.0-blue)](LICENSE)

</div>

## 📑 Sumário

- [✨ Por que usar](#-por-que-usar)
- [📥 Instalação](#-instalação)
- [🚀 Início rápido](#-início-rápido)
- [⚙️ Configuração](#️-configuração)
- [🚦 Limite de requisições](#-limite-de-requisições)
- [🌐 Proxy reverso](#-proxy-reverso)
- [🧱 O que derruba a subida](#-o-que-derruba-a-subida)
- [📈 Observabilidade](#-observabilidade)
- [⚡ Compatibilidade](#-compatibilidade)
- [🛡️ Limites conhecidos](#️-limites-conhecidos)

---

## ✨ Por que usar

| Sem TEC.Http | Com TEC.Http |
|---|---|
| CORS, limite e HSTS reescritos em cada serviço, cada um de um jeito | Uma linha no `Program.cs` e uma seção `Http` no `appsettings` |
| `AllowAnyOrigin` com credenciais, `AllowedHosts: "*"` em produção | A aplicação **não sobe** com essas configurações |
| `X-Forwarded-For` aceito de qualquer cliente: o IP é forjável e o limite por IP é burlado | O header só vale se vier de um proxy listado; senão é ignorado |
| 429 com corpo e formato próprios (ou 503 padrão do limiter) | 429 no `ApiResponse` do TEC.Core, com `Retry-After` e `traceId` |

O componente usa só o ASP.NET Core (e o `TEC.Core` para o envelope de erro): nenhuma dependência de terceiros.

## 📥 Instalação

```bash
dotnet nuget add source https://nuget.pkg.github.com/tudoemcodigo/index.json -n tec-interno -u <usuario> -p <PAT>
dotnet add package TEC.Http
```

## 🚀 Início rápido

```csharp
using TEC.Http;

builder.Services.AddTecHttp(builder.Configuration);

var app = builder.Build();

app.UseTecObservability();        // correlation id primeiro
app.UseTecExceptionHandler();
app.UseTecHttp();                 // proxy, HSTS, headers, limite por IP, CORS: ANTES da autenticação
app.UseAuthentication();
app.UseTecRateLimitingPerIdentity();   // DEPOIS da autenticação
app.UseAuthorization();
```

> [!IMPORTANT]
> A ordem importa. O limite por IP precisa vir **antes** de `UseAuthentication`: é ele que contém tentativas de credencial
> (API key e token inválidos). O limite por identidade só existe depois da autenticação.

## ⚙️ Configuração

Seção `Http`. Os valores abaixo são os padrões (a seção inteira é opcional).

```json
{
  "AllowedHosts": "api.exemplo.com",
  "Http": {
    "Forwarding":      { "Enabled": false, "KnownProxies": [], "KnownNetworks": [], "ForwardLimit": 1 },
    "Hsts":            { "Enabled": true, "MaxAgeDays": 365 },
    "SecurityHeaders": { "Enabled": true },
    "RateLimiting": {
      "Enabled": true,
      "PerIp":       { "PermitLimit": 100, "WindowSeconds": 60 },
      "PerIdentity": { "PermitLimit": 600, "WindowSeconds": 60 }
    },
    "Cors": {
      "Origins": [], "Methods": [], "Headers": [], "ExposedHeaders": [],
      "AllowCredentials": false, "MaxAgeSeconds": 600
    }
  }
}
```

| Item | Comportamento |
|---|---|
| **CORS** | Sem `Origins`, nenhuma origem é liberada (é o estado de partida: servidores não usam CORS). Formato `https://host[:porta]`, sem caminho, barra final nem curinga. `*` só em Development e sem credenciais. `http` só para localhost em Development. `Methods` vazio: GET, POST, PUT, PATCH, DELETE. `Headers` vazio: Content-Type, Authorization, X-Api-Key, Idempotency-Key, If-Match. |
| **HSTS** | Só fora de Development e em HTTPS. Sem `includeSubDomains` e sem `preload` (irreversíveis na prática; ligue por código se for decisão sua). |
| **Headers** | `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`. Não sobrescrevem header já definido pela aplicação. |
| **AllowedHosts** | Fora de Development, vazio ou `*` derruba a subida. Liste também o host que o orquestrador usa nas sondas de saúde (muitos chamam pelo IP do pod, e o filtro responde 400). |

## 🚦 Limite de requisições

- Janela deslizante em memória, **por instância**: com N instâncias o limite efetivo é N vezes o configurado. Se precisar de
  limite global, use o gateway ou um limiter distribuído. Não há ponto de extensão público hoje (YAGNI): a criação do limiter
  e as chaves de partição ficam isoladas em `RateLimitMiddleware` e `ClientPartition`, onde um limiter distribuído entraria
  sem mudar o contrato de configuração.
- **Estágio por IP** (toda requisição): IPv6 é agrupado por prefixo /64 (um cliente controla o /64 inteiro; sem isso,
  rotacionar o sufixo burlaria o limite). IP desconhecido cai em um balde compartilhado: falha fechada, nunca sem limite.
- **Estágio por identidade** (só autenticadas): chave `tec_uid` (identidade normalizada do TEC.Security), `sub` ou
  `NameIdentifier`. Autenticado sem identificador usa um balde próprio. Chaves longas viram hash (memória limitada).
- **Resposta 429**: `ApiResponse` com `errors[0].code = "http.rate_limit_exceeded"`, `traceId`, `Cache-Control: no-store` e
  `Retry-After` em segundos (nunca 0).
- Desligar (`RateLimiting:Enabled: false`) é decisão explícita, para quando há gateway à frente.

## 🌐 Proxy reverso

Os headers `X-Forwarded-*` são escolhidos pelo cliente. O componente **não detecta** se há proxy: você declara.

- **Sem proxy** (padrão): `X-Forwarded-For` é ignorado; o IP é o da conexão. Forjar o header não burla o limite.
- **Atrás de proxy**: ligue `Forwarding` e liste `KnownProxies` e/ou `KnownNetworks` (CIDR). Loopback **não** é confiável
  por padrão; liste-o se o proxy roda na mesma máquina. Sem isso, todo cliente aparece com o IP do proxy e divide o mesmo balde.
- `X-Forwarded-Proto` também é aplicado, então `Request.IsHttps` e o HSTS funcionam atrás de quem termina o TLS.
- Header `X-Forwarded-For` recebido com `Forwarding` desligado: a requisição segue, o contador
  `tec.http.forwarded_headers.ignored` sobe e um aviso é registrado **uma vez**. É o sinal de proxy sem configuração.

## 🧱 O que derruba a subida

A configuração é validada na subida (`UseTecHttp` e `ValidateOnStart`); erro derruba a aplicação, não a primeira requisição.

- `AllowedHosts` ausente ou `*` fora de Development.
- `Forwarding` ligado sem proxy nem rede; IP ou CIDR inválido; prefixo `/0`; `ForwardLimit` fora de 1 a 5.
- Origem CORS fora do formato; `*` fora de Development ou com `AllowCredentials`; `http` fora de localhost.
- Limite fora de 1 a 1.000.000 ou janela fora de 1 a 86.400 s; `MaxAgeSeconds` fora de 0 a 86.400.
- Valor presente e ilegível (ex.: `"abc"` num número): nunca é trocado pelo padrão em silêncio.

## 📈 Observabilidade

Medidor `TEC.Http` (registre-o no OpenTelemetry com `AddMeter("TEC.Http")`):

| Métrica | Quando |
|---|---|
| `tec.http.ratelimit.rejected` (tag `stage`: `ip` ou `identity`) | Requisição recusada com 429 |
| `tec.http.forwarded_headers.ignored` | `X-Forwarded-For` recebido com `Forwarding` desligado |

## ⚡ Compatibilidade

.NET 8 e .NET 10, compatível com Native AOT e trimming (configuração lida sem reflexão).

## 🛡️ Limites conhecidos

- Limite **por instância** (ver acima).
- Sem CSP: a API não serve HTML; se servir (ex.: documentação), defina a política na aplicação.
- O componente não faz limite de tamanho de corpo nem de tempo de requisição: use `Kestrel` (`MaxRequestBodySize`,
  `RequestHeadersTimeout`) e o gateway.
- Atrás de proxy, o IP só é confiável se o proxy **sobrescreve** (não repassa) o `X-Forwarded-For` recebido do cliente.
