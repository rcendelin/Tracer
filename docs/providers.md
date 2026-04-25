# Provider catalogue

Every data source in Tracer implements `IEnrichmentProvider`. Adding a new
source is a one-folder, one-DI-line operation. This page lists every
registered provider with the contract details that matter for operators
and integrators (`ProviderId`, priority tier, rate limits, identifier
format, status normalization).

For source code, look in `src/Tracer.Infrastructure/Providers/`.

## Tier 1 — Registry APIs (priority ≤ 100)

| Provider | `ProviderId` | Region | Priority | Source | Identifier format |
|---|---|---|---|---|---|
| ARES | `ares` | CZ / SK | 10 | REST JSON, free | 8-digit IČO |
| Companies House | `companies-house` | UK | 10 | REST JSON, key required | 8-digit CRN |
| ABN Lookup | `abn-lookup` | AU | 10 | SOAP/JSON, GUID required | 11-digit ABN |
| SEC EDGAR | `sec-edgar` | US (public) | 20 | REST JSON, no key | 10-digit CIK (zero-padded) |
| GLEIF LEI | `gleif-lei` | Global | 30 | REST JSON, free, CC0 | 20-char LEI |
| Google Maps Places | `google-maps` | Global | 50 | REST JSON, key | n/a (geo enrichment) |
| Azure Maps | `azure-maps` | Global | 50 | REST JSON, key | n/a (geo enrichment) |

Hard rules (Tier 1):

- Run in **parallel** via `Task.WhenAll` inside `WaterfallOrchestrator`.
- Per-provider timeout: **8 s**.
- **Must NOT** access `DbContext` or any repository — providers are
  Transient, the EF context is Scoped and not thread-safe.

## Tier 2 — Registry scrapers (priority 101–200)

| Provider | `ProviderId` | Region | Priority | Source | Notes |
|---|---|---|---|---|---|
| Web Scraper | `web-scraper` | Global | 150 | AngleSharp + HttpClient | SSRF guard required |
| Handelsregister | `handelsregister` | DE | 200 | HTML scraping | 60 req/h legal limiter |
| BrasilAPI CNPJ | `cnpj-brazil` | BR | 200 | REST JSON, no key | CNPJ 14 digits / `XX.XXX.XXX/XXXX-XX` |
| State SoS | `state-sos` | US (CA / DE / NY) | 200 | HTML scraping | Strategy pattern per state |
| LATAM AFIP | `latam-afip` | AR | 200 | HTML scraping | CUIT 11 digits |
| LATAM SII | `latam-sii` | CL | 200 | HTML scraping | RUT, dash-separated verifier |
| LATAM RUES | `latam-rues` | CO | 200 | HTML scraping | NIT 8–10 digits |
| LATAM SAT | `latam-sat` | MX | 200 | HTML scraping | RFC, 3–4 letter prefix |

Hard rules (Tier 2):

- Run **sequentially**, in priority order.
- Per-provider timeout: **12 s**.
- **Per-instance rate limiting** via `ConcurrentQueue<DateTimeOffset>`
  + `SemaphoreSlim`. Horizontal scaling needs distributed limiter (Redis).
- **SSRF guard**: every HTTP client that takes user-supplied URLs must
  resolve DNS and reject private/loopback/link-local/CGNAT IPs.
  `AllowAutoRedirect = false` on the primary handler.

### LATAM family

All four LATAM providers share `LatamRegistryClient` + per-country
`ILatamRegistryAdapter`. `RegistrationId` is stored as
`{CC}:{normalized-id}` (e.g. `AR:30500010912`, `CL:96790240-3`). Shared
rate limit is **10 req/min across all four** because the registries
often share ASN / WAF policy. CAPTCHA / login walls return `null` from
adapter `Parse` → treated as `NotFound`, not `Error`.

### Status normalization

| Source language | Active | Dissolved | Suspended | Other |
|---|---|---|---|---|
| English (US SoS) | `Active`, `Good Standing` | `Dissolved`, `Cancelled`, `Revoked` | `Suspended`, `Forfeited` | `Merged`, `Converted` |
| German | `aktiv` | `gelöscht`, `aufgelöst` | — | `insolvent`, `in_liquidation` |
| Portuguese (BR) | `ATIVA` | `BAIXADA` | `SUSPENSA` | `INAPTA` (inactive), `NULA` (annulled) |
| Spanish (LATAM) | `activo` | `disuelta` | `suspendida` | `in_liquidation` (CO) |

All providers normalize to canonical English: `active`, `dissolved`,
`suspended`, `in_liquidation`, `insolvent`, `inactive`, `annulled`,
`merged`, `converted`. New providers MUST follow the same vocabulary.

## Tier 3 — AI extraction (priority > 200)

| Provider | `ProviderId` | Region | Priority | Source | Notes |
|---|---|---|---|---|---|
| AI Extractor | `ai-extractor` | Global | 250 | Azure OpenAI GPT-4o-mini | Structured output, depth `Deep` only |

Hard rules (Tier 3):

- Run **sequentially**, only when `TraceContext.Depth == Deep`.
- Per-provider timeout: **20 s**.
- Strict JSON schema, UTF-8 aware truncation at 16 KB prompt cap.
- `NullAiExtractorClient` is the application default so the app boots
  without Azure OpenAI configured.

## Source quality (confidence scoring input)

| Tier | `SourceQuality` | Reasoning |
|---|---|---|
| Registry API (Tier 1) | 0.95 | Authoritative, structured, signed |
| GLEIF | 0.90 | Global, slightly less granular |
| Geo (Google / Azure Maps) | 0.85 | High-quality but commercial |
| State SoS scraper | 0.85 | HTML can break |
| Handelsregister scraper | 0.85 | Same |
| LATAM scrapers | 0.80 | HTML less stable, antibot/CAPTCHA likely |
| Web Scraper | 0.70 | Generic site scraping |
| AI Extractor | 0.60 | Calibrated by `ConfidenceScorer` |

Final `OverallConfidence` on `CompanyProfile` is the weighted aggregate
across all enriched fields. See `Tracer.Application.Services.ConfidenceScorer`.

## Adding a new provider

1. Create `src/Tracer.Infrastructure/Providers/<Name>/` with:
   - `I<Name>Client.cs` (interface, internal)
   - `<Name>Client.cs` (impl: HTTP / scraping, SSRF guard, rate limit)
   - `<Name>Provider.cs` (`IEnrichmentProvider` wrapper)
2. Register in `InfrastructureServiceRegistration.cs` as `Transient`.
3. Add `Providers:<Name>:*` config keys (key, endpoint) to
   [configuration.md](./configuration.md).
4. Mirror status / format normalization patterns from existing
   providers in the same region.
5. Pin source quality based on registry vs scraper vs LLM (see table).
6. Tests:
   - Unit tests for `I<Name>Client` parsing logic.
   - `LatamRegistryProviderBase`-style provider tests for the wrapper.
   - WireMock or `FakeHttpMessageHandler` for absolute-URL endpoints.
7. Document in this page.

## Re-validation hooks

`IFieldTtlPolicy` decides which fields are stale. `RevalidationScheduler`
calls `ICompanyProfileRepository.GetRevalidationQueueAsync` for the
auto-sweep. `DeepRevalidationRunner` synthesises a `TraceRequest` with
`Source = "revalidation"` and re-runs the entire waterfall whenever
`expiredFields.Count >= Revalidation:Deep:Threshold`.

A new provider becomes part of re-validation automatically as long as it
implements `IEnrichmentProvider` and is registered in DI — no separate
re-validation registration needed.
