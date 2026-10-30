# Instruções para agentes — TEC.Http

Este arquivo complementa as instruções herdadas de `AGENT_BASE.md`. O TEC.Http endurece a borda HTTP das APIs ASP.NET Core: proxy reverso confiável explícito, hosts permitidos, HSTS e headers de segurança, limite de requisições com 429 padronizado e CORS por allowlist, tudo fechado por padrão.

## Escopo e segurança de alterações

- O escopo padrão é este repositório. Não altere outros repositórios `TEC.*`, não publique pacotes/tags/releases e não rode sincronizações que escrevam fora daqui sem pedido explícito.
- Antes de editar, confira `git status` e `git diff`. Preserve alterações não commitadas; não use reset, checkout destrutivo ou limpeza para descartar trabalho existente.
- Mudanças na ordem do pipeline de `UseTecHttp`, nas chaves de partição do limite, na confiança em `X-Forwarded-*`, nas regras de CORS ou nos alvos .NET exigem revisão de compatibilidade, documentação, changelog e consumidores.
- Testes devem atravessar contratos públicos (`AddTecHttp`, `UseTecHttp`, `UseTecRateLimitingPerIdentity`, configuração `Http`) e verificar resultados observáveis por HTTP. Para mudanças de comportamento, cubra entradas inválidas, limites, falhas e concorrência quando aplicável.
- Trate todo dado externo como não confiável: `X-Forwarded-For`, `Origin`, `Host` e claims são escolhidos pelo cliente até prova em contrário. Não exponha segredos, PII ou conteúdo controlado pelo usuário em exceções, logs, URLs ou sumários.
- Fechado por padrão: sem configuração, nenhuma origem CORS é liberada, nenhum `X-Forwarded-*` é aceito e o limite fica ligado. Configuração insegura, incoerente ou ilegível derruba a subida; nunca é trocada pelo padrão em silêncio.

## Mapa da implementação

- `TEC.Http/`: biblioteca `net10.0`, compatível com Native AOT (configuração lida sem reflexão).
  - `TecHttpExtensions.cs`: `AddTecHttp` (opções, validação, CORS, HSTS), `UseTecHttp` (ordem: proxy confiável, HSTS, headers de segurança, CORS, limite por IP) e `UseTecRateLimitingPerIdentity`.
  - `TecHttpOptions.cs`: seção `Http` (`Forwarding`, `Hsts`, `SecurityHeaders`, `RateLimiting`, `Cors`).
  - `TecHttpOptionsReader.cs`: leitura sem `Bind`; valor ilegível ou lista com valor único derrubam a subida.
  - `TecHttpOptionsValidator.cs`: `AllowedHosts`, proxies e redes CIDR, limites, origens CORS e tokens de `Methods`/`Headers`/`ExposedHeaders`.
  - `Internal/ClientPartition.cs`: chaves de partição (IP com IPv6 em /64, conexão quando não há IP, identidade com hash acima de 128 caracteres).
  - `Internal/RateLimitResponse.cs`: 429 no `ApiResponse` do TEC.Core, com `Retry-After` e `traceId` (JSON gerado em compilação).
  - `Internal/TecHttpLog.cs` e `Internal/TecHttpMetrics.cs`: avisos (EventId 4001 e 4002) e medidor `TEC.Http`.
- `TEC.Http.Tests/`: testes TUnit de integração com `TestServer` (`TestHost.cs` simula IP remoto, id de conexão e usuário por headers de teste).
- `build/`: props/targets compartilhados (canônicos do `tec-workflows`; não edite aqui).

## Contratos relevantes

- `Directory.Build.props` é a fonte única de versão do pacote. Não introduza versões nos `.csproj`.
- Dependências TEC.* são declaradas com `<TecReference Include="..." />`, não com `PackageReference` direto. O TEC.Http depende do `TEC.Core` (envelope `ApiResponse`); a versão publicada fica no `Directory.Packages.props`.
- O `packages.lock.json` versionado é o do modo pacote (TEC.Core vindo do feed `tec-interno`). Regenere com `dotnet restore TEC.Http.slnx --force-evaluate`; sem acesso ao pacote publicado o restore falha e pode reescrever o lock sem o TEC.Core: confira o diff antes de aceitar.
- O limite é em memória e por instância. O código de erro `http.rate_limit_exceeded`, o medidor `TEC.Http` e as métricas `tec.http.ratelimit.rejected` e `tec.http.forwarded_headers.ignored` são contrato com os consumidores.
- Workflows dos consumidores chamam `tudoemcodigo/tec-workflows` (`@v1`); confira as tags realmente publicadas antes de alterar referências.
- A licença é Apache-2.0. Mantenha `LICENSE`, `NOTICE`, badge/README e `<PackageLicenseExpression>` coerentes.

## Documentação

- `README.md` e `CHANGELOG.md` têm conteúdo próprio do TEC.Http: siga o padrão do template, mantenha a marca Tudo em Código e não substitua o conteúdo específico por placeholders genéricos.
- Atualize `README.md` (configuração, "O que derruba a subida", limites conhecidos) e `CHANGELOG.md` (`[Não lançado]`) quando uma mudança alterar comportamento ou contrato.

## Verificação local

```powershell
dotnet restore TEC.Http.slnx
dotnet build TEC.Http.slnx -c Release
dotnet test --solution TEC.Http.slnx -c Release
git diff --check
```

O CI também valida convenções, pacote e workflows reutilizáveis. Não afirme que o CI está funcional apenas com build/testes locais: confirme que as tags chamadas existem e que os `permissions` do chamador incluem o mínimo exigido pelo workflow reutilizável.
