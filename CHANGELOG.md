# CHANGELOG.md

Sequência de versões do componente.

## [0.0.1] - 2026-10-30

- **Adicionado**: Alvo único `net10.0`.
- **Adicionado**: `AddTecHttp()` e `UseTecHttp()`: proxy reverso confiável explícito (`Http:Forwarding`), HSTS fora de Development e headers de segurança.
- **Adicionado**: Limite de requisições em memória por IP (IPv6 agrupado em /64) e por identidade, com 429 no `ApiResponse` do TEC.Core, `Retry-After` e `traceId` (`UseTecRateLimitingPerIdentity()`).
- **Adicionado**: Sem IP remoto (ex.: Unix domain socket), o limite por IP vale por conexão, em vez de juntar todos esses clientes num balde só. Sem id de conexão, fica o balde compartilhado. Um aviso (EventId 4002) é registrado uma vez.
- **Adicionado**: CORS por allowlist, fechado por padrão; `*` só em Development e sem credenciais. `UseTecHttp()` aplica o CORS antes do limite por IP: o 429 de uma origem liberada sai com os headers CORS (o navegador consegue lê-lo) e o preflight `OPTIONS` é respondido pelo CORS sem consumir o limite. Para o navegador ler o `Retry-After`, inclua-o em `Cors:ExposedHeaders`.
- **Adicionado**: Validação na subida de `AllowedHosts`, proxy, CORS e limites; configuração insegura derruba a aplicação. A validação de CORS confere `Methods`, `Headers` e `ExposedHeaders`: cada item precisa ser token HTTP; `*` só como item único e nunca com `AllowCredentials`.
- **Adicionado**: Lista configurada com valor único (ex.: `Http__Cors__Origins=https://a,https://b`) derruba a subida com a chave esperada na mensagem, em vez de ficar vazia sem aviso.
- **Adicionado**: Métricas `tec.http.ratelimit.rejected` e `tec.http.forwarded_headers.ignored`.

Veja o histórico de versões nas tags do repositório.
