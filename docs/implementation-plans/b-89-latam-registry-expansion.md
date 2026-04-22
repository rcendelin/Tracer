# B-89 — LATAM registry expansion

> **Fáze:** 4 — Scale + polish
> **Odhad:** 4 h
> **Branch:** `claude/eloquent-babbage-iQFE8`
> **Datum zahájení:** 2026-04-22

## 1. Cíl

Přidat čtyři nové Tier 2 (Priority 200) enrichment providery pro LATAM trhy
tak, aby Tracer uměl doplnit základní firmografické údaje (LegalName,
RegistrationId, EntityStatus, LegalForm, případně RegisteredAddress) pro firmy
z Argentiny, Chile, Kolumbie a Mexika.

Bloky B-59 (Handelsregister.de), B-60 (Brazil CNPJ) a B-61 (US State SoS)
tvoří etablovaný vzor: HTML scraping/REST klient + provider + rate limit +
SSRF guard + status normalization. B-89 ten vzor replikuje pro LATAM registry
s jedním sdíleným klientem a per-country adaptéry (podobně jako StateSoS).

Klíčový požadavek **kvalita před rychlostí**: 4 nové provideři v 4 h znamenají
odhadem 1 h na provider. Abychom to dali a zároveň neobcházeli bezpečnostní
invariantů, sjednocujeme klienta a rate limit do jedné třídy s adapter
dispatchem — stejný pattern jako StateSos.

## 2. Cílový stav (akceptační kritéria)

- Čtyři nové providery registrované v `InfrastructureServiceRegistration`:
  - `latam-afip` (Argentina, CUIT)
  - `latam-sii` (Chile, RUT)
  - `latam-rues` (Colombia, NIT)
  - `latam-sat` (Mexico, RFC)
- Každý provider:
  - `Priority = 200`, `SourceQuality = 0.80` (scraping, nižší než 0.85 US SoS,
    protože LATAM registry mají horší HTML kvalitu a častější antibot
    ochranu).
  - `CanHandle` matchuje (a) country code, nebo (b) formát `RegistrationId`
    typický pro zemi.
  - `EnrichAsync` volá sdílený `LatamRegistryClient.SearchAsync` /
    `LookupAsync`, mapuje výsledek na `Dictionary<FieldName, object?>` a
    vrací `ProviderResult`.
- Sdílený klient `LatamRegistryClient`:
  - Dispatch podle `CountryCode` na `ILatamRegistryAdapter`.
  - Sliding-window rate limit (10 req/min, nejkonzervativnější z celé rodiny —
    LATAM registry mají nejpřísnější antibot opatření).
  - SSRF guard stejný jako StateSos (DNS resolve + private/reserved IP check).
  - `MaxHtmlChars` cap 2 MB.
  - `AllowAutoRedirect = false` na SocketsHttpHandler.
  - Test seamy: `Clock`, `DnsResolve`.
- Unit testy (xUnit + NSubstitute + FluentAssertions):
  - Pro každý provider: Properties, CanHandle matrix, happy path, NotFound,
    Error (HttpRequestException), Timeout, caller cancellation. (cca 10–12
    testů × 4 providers = 40–50 testů)
  - Pro sdílený klient: SSRF block, rate limit, HTML size cap, adapter
    dispatch, no-adapter fallback.
- Status normalization per jazyk/země:
  - Španělština (AFIP/SII/RUES): `activo`/`activa`→active,
    `disuelto`/`disuelta`/`cancelado`/`cancelada`→dissolved,
    `suspendido`/`suspendida`→suspended, `en liquidación`→in_liquidation.
  - Mexiko (SAT): `ACTIVO`→active, `SUSPENDIDO`→suspended,
    `CANCELADO`→dissolved.
- `dotnet build` a `dotnet test` PASS (0 warnings, 0 errors) na Release.

## 3. Dekompozice na subtasky

| # | Subtask | Odhad | Komponenty |
|---|---------|-------|------------|
| 1 | Plán + exploration | 0.5 h | Tento dokument |
| 2 | `LatamRegistryModels.cs` (DTO) + `ILatamRegistryAdapter` + `ILatamRegistryClient` | 0.5 h | Infrastructure |
| 3 | `LatamRegistryClient` — rate limit, SSRF, adapter dispatch, HTML read | 0.75 h | Infrastructure |
| 4 | 4 adaptery: ArgentinaAfip, ChileSii, ColombiaRues, MexicoSat | 1.0 h | Infrastructure |
| 5 | 4 providery: ArgentinaAfip, ChileSii, ColombiaRues, MexicoSat | 0.5 h | Infrastructure |
| 6 | DI registrace v `InfrastructureServiceRegistration.cs` | 0.1 h | Infrastructure |
| 7 | Unit testy: klient (WireMock) + 4× provider | 1.0 h | Tests |
| 8 | Review, security pass, docs (CLAUDE.md), merge | 0.65 h | — |

**Celkem:** cca 5 h (odhad 4 h je těsný; složitost lehce přesahuje, dokumentace
nese část overheadu).

## 4. Komponenty a soubory

### Nové soubory

```
src/Tracer.Infrastructure/Providers/LatamRegistry/
├── ILatamRegistryAdapter.cs       # Strategy interface
├── ILatamRegistryClient.cs        # Client interface
├── LatamRegistryClient.cs         # Shared HTTP client (rate limit + SSRF)
├── LatamRegistryModels.cs         # LatamRegistrySearchResult record
├── Adapters/
│   ├── ArgentinaAfipAdapter.cs    # CUIT lookup (tax ID format: XX-XXXXXXXX-X)
│   ├── ChileSiiAdapter.cs         # RUT lookup (tax ID format: XX.XXX.XXX-X)
│   ├── ColombiaRuesAdapter.cs     # NIT lookup (tax ID format: XXXXXXXXX-X)
│   └── MexicoSatAdapter.cs        # RFC lookup (tax ID format: 12–13 alphanumeric)
└── Providers/
    ├── ArgentinaAfipProvider.cs
    ├── ChileSiiProvider.cs
    ├── ColombiaRuesProvider.cs
    └── MexicoSatProvider.cs

tests/Tracer.Infrastructure.Tests/Providers/LatamRegistry/
├── LatamRegistryClientWireMockTests.cs
├── ArgentinaAfipProviderTests.cs
├── ChileSiiProviderTests.cs
├── ColombiaRuesProviderTests.cs
└── MexicoSatProviderTests.cs
```

### Dotčené soubory

- `src/Tracer.Infrastructure/InfrastructureServiceRegistration.cs` — DI
  registrace 1× klient, 4× adapter (Singleton), 4× provider (Transient).
- `CLAUDE.md` — nová sekce o LATAM pattern, status normalization, limity
  veřejných endpointů.

## 5. Datové modely a kontrakty

### `LatamRegistrySearchResult`

```csharp
internal sealed record LatamRegistrySearchResult
{
    public required string EntityName { get; init; }
    public required string RegistrationId { get; init; }   // canonical, e.g. "AR:30-50001091-2"
    public required string CountryCode { get; init; }      // "AR" | "CL" | "CO" | "MX"
    public string? Status { get; init; }                    // raw, normalized by provider
    public string? EntityType { get; init; }                // raw
    public string? Address { get; init; }                   // raw single-line address
}
```

### `ILatamRegistryAdapter`

```csharp
internal interface ILatamRegistryAdapter
{
    string CountryCode { get; }                             // "AR" | "CL" | "CO" | "MX"
    string BaseUrl { get; }
    string LookupPath { get; }                              // templated: "{0}"
    bool IsValidIdentifier(string identifier);              // country-specific regex
    HttpRequestMessage BuildRequest(string identifier);     // GET or POST with form
    LatamRegistrySearchResult? ParseResponse(string body);  // returns null on no-match
}
```

### RegistrationId formáty (canonical pro CKB)

- `AR:<CUIT normalizovaný bez pomlček>` (11 číslic)
- `CL:<RUT včetně verifikačního znaku XX.XXX.XXX-X>`
- `CO:<NIT bez pomlček>` (9–10 číslic)
- `MX:<RFC uppercase>` (12 pro fyzickou osobu, 13 pro právnickou)

**Pozn.:** Prefixujeme country code stejně jako StateSos (`CA:C0806592`) a
NormalizedKey (`{CountryCode}:{RegistrationId}`). Tímto se vyhneme kolizi s
CNPJ/CIK.

## 6. Endpointy a limity

> **Design decision:** Veřejné HTML scraping endpointy jsou v sandbox prostředí
> netestovatelné (CAPTCHA, antibot, login). Pattern staví na principu
> "optimistic implementation" — struktura je komplet ná, parser staví na
> minimálním best-effort (název + status + ID), testy běží proti WireMock /
> FakeHttpMessageHandler. Produkční doladění selektorů je mimo scope B-89
> (evidujeme jako known follow-up).

| Země | Registry | Endpoint | Limit |
|------|----------|----------|-------|
| AR | AFIP Constancia de Inscripción | `https://seti.afip.gob.ar/padron-puc-constancia-internet/` | 10 req/min (sdílené) |
| CL | SII Situación Tributaria | `https://zeus.sii.cl/cvc_cgi/stc/getstc` | 10 req/min |
| CO | RUES Consulta | `https://www.rues.org.co/RUES_Web/Consultas/` | 10 req/min |
| MX | SAT Constancia (limitovaná veřejnost) | `https://siat.sat.gob.mx/` | 10 req/min |

Rate limit je sdílený napříč všemi adaptéry (SemaphoreSlim +
ConcurrentQueue) — pokud běží AFIP a SII paralelně, sdílí stejné okno.
Důvod: LATAM registry často sdílí AS/ISP a některá WAFka banují celý IP rozsah.

## 7. Testovací strategie

### Unit testy (xUnit + NSubstitute + FluentAssertions)

**Per-provider (4 × cca 10 testů):**
- `Properties_AreCorrect` — `ProviderId`, `Priority = 200`, `SourceQuality = 0.80`.
- `CanHandle_MatchingCountry_ReturnsTrue`
- `CanHandle_MatchingIdentifierFormat_ReturnsTrue` (country jiná, ale ID matchuje)
- `CanHandle_Quick_ReturnsFalse`
- `CanHandle_OtherCountryNoId_ReturnsFalse`
- `CanHandle_AlreadyEnrichedRegistrationId_ReturnsFalse` (defer to higher-priority)
- `EnrichAsync_Found_MapsFields` (s NSubstitute stubem klienta)
- `EnrichAsync_NotFound_ReturnsNotFound`
- `EnrichAsync_HttpError_ReturnsError` (zkontroluje sanitized message)
- `EnrichAsync_Timeout_ReturnsTimeout`
- `EnrichAsync_CallerCancel_Propagates`
- `NormalizeStatus_XToCanonical` (theory data)

**Client (WireMock):**
- `LookupAsync_SsrfBlocked_ReturnsNull` (private IP resolve)
- `LookupAsync_RateLimitEnforced` (11th request v okně waits)
- `LookupAsync_HtmlOver2MB_Truncated`
- `LookupAsync_AdapterDispatch_CorrectCountryRouted`
- `LookupAsync_NoAdapterForCountry_ReturnsNull`

### Integration testy

Mimo scope B-89 — Phase 3 integration testy (B-76) budou rozšířeny o LATAM
případ jen jako follow-up.

## 8. Pasti a architektonická rozhodnutí

1. **Sdílený klient místo 4× samostatných HTTP klientů.** Rate limit má být
   "per-provider-family", ne "per-country". Handelsregister / BrasilAPI jsou
   per-country izolované; StateSos už sdílel klient — LATAM pokračuje.
2. **`SourceQuality = 0.80`**, ne 0.85 (StateSos). Odůvodnění: LATAM registry
   HTML je méně konzistentní, více false positives, jazyková bariéra → snížený
   confidence.
3. **Prefix `AR:` / `CL:` / `CO:` / `MX:`** na RegistrationId — stejně jako
   StateSos. Jinak kolize s CNPJ (BR) a CIK (US).
4. **Žádný API klíč** pro žádný LATAM registry → validace v DI registraci je
   pro tuto skupinu N/A. Pokud by v budoucnu přibyl placený endpoint, vstoupí
   do konvence `ArgumentException.ThrowIfNullOrWhiteSpace` v DI tak, jak to
   dělá Companies House.
5. **Status normalization** je per-adapter (ne centrální) — terminologie se
   liší (AR/CL/CO používají "activo/a", MX má "ACTIVO"/"SUSPENDIDO"
   konvenci uppercase).
6. **Mexico SAT je "limited availability"** per zadání — adaptér rozliší
   CAPTCHA / login wall a vrátí `null` (což provider mapuje na `NotFound`).
   Nepoužíváme `Error` — business-wise "kaptcha" není error, je to
   "endpoint nepřístupný pro anonymní klient".
7. **Scraping rizika (HTML drift).** Kdyby registry změnila layout, parser
   vrátí `null` (ne crash). Budoucí follow-up: integrace s `AiExtractor`
   jako Tier 3 fallback pro parsing.
8. **InternalsVisibleTo.** `Tracer.Infrastructure.csproj` už má
   `Tracer.Infrastructure.Tests` i `DynamicProxyGenAssembly2` — není nutná
   další úprava.

## 9. Security checklist

- [ ] Žádné hardcoded credentials (LATAM endpointy jsou anonymní)
- [ ] SSRF guard: `DnsResolve` + private IP check stejně jako StateSos
- [ ] Auto-redirect OFF na `SocketsHttpHandler`
- [ ] HTML size cap 2 MB
- [ ] Error messages sanitized (exception type only, CWE-209)
- [ ] Rate limit enforced (10 req/min) — shared across countries
- [ ] Query parameters escaped pomocí `Uri.EscapeDataString`
- [ ] Timeouty přes Polly (`HttpClient.Timeout = Infinite`)
- [ ] Response parse throws no PII do logs (jen count + duration + provider id)

## 10. Known follow-ups (po mergi)

- HTML selectors kalibrace proti živému provozu (mimo sandbox)
- Integrace do B-76 integration test suity
- Eventual Mexico SAT upgrade, až bude stabilní veřejný endpoint nebo dohoda
  s XTuning pro API access
- Performance profiling po nasazení (B-78 / B-86)

## 11. Merge + Notion

- Branch: `claude/eloquent-babbage-iQFE8`
- Merge strategy: `--no-ff` do `develop`
- Notion status: `V realizaci` → `Dokončeno` po úspěšném mergi + CI green
- Follow-up tasky: mimo tento blok (HTML kalibrace, Mexico SAT)
