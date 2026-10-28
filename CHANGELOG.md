# CHANGELOG.md

Sequência de versões do componente.

## [não lançado]

- **Adicionado**: `AddTecHttp()` e `UseTecHttp()`: proxy reverso confiável explícito (`Http:Forwarding`), HSTS fora de Development e headers de segurança.
- **Adicionado**: Limite de requisições em memória por IP (IPv6 agrupado em /64) e por identidade, com 429 no `ApiResponse` do TEC.Core, `Retry-After` e `traceId` (`UseTecRateLimitingPerIdentity()`).
- **Adicionado**: CORS por allowlist, fechado por padrão; `*` só em Development e sem credenciais.
- **Adicionado**: Validação na subida de `AllowedHosts`, proxy, CORS e limites; configuração insegura derruba a aplicação.
- **Adicionado**: Métricas `tec.http.ratelimit.rejected` e `tec.http.forwarded_headers.ignored`.

Veja o histórico de versões nas tags do repositório.
