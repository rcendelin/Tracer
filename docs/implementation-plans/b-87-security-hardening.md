# B-87 — Security hardening

**Status:** V realizaci
**Branch:** `claude/eloquent-babbage-gjscO`
**Fáze:** 4 — Scale + polish
**Odhad:** 4 h
**Datum zahájení:** 2026-04-22

## Kontext a cíl

Základní API bezpečnost už v `develop` existuje: `ApiKeyAuthMiddleware`
(B-23), CORS s povinnými origins v produkci, `UseHttpsRedirection`,
`ForwardedHeaders`, `UseExceptionHandler` + `ProblemDetails`, rate limit
pro `batch` endpoint (B-28 + B-80), Key Vault reference z App Service
(B-25, B-30). B-87 tyto základy zpevňuje na produkční úroveň:

1. **Odpovědní hlavičky** — `Strict-Transport-Security`, `Content-Security-Policy`,
   `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`,
   `Permissions-Policy`, `Cross-Origin-Opener-Policy` a
   `Cross-Origin-Resource-Policy` přes vlastní middleware.
2. **HSTS** — explicitní `app.UseHsts()` v produkci (nastavení max-age +
   `includeSubDomains`, s `preload` konfigurovatelným).
3. **API key rotation** — přechod z ploché `string[]` konfigurace na
   strukturu s metadaty (`Key`, `Label`, `ExpiresAt`). Expirované klíče
   jsou odmítnuty, příchozí validní klíč s metadaty je předán dál přes
   `HttpContext.Items` pro audit.
4. **Vulnerability scanning v CI** — `dotnet list package --vulnerable`
   (transitive) jako samostatný krok v `ci.yml`, failující na `High` /
   `Critical` CVE. `npm audit` s prahem `--audit-level=high` pro frontend.

**Mimo scope (záměrně):** Entra ID integrace pro UI (MSAL.js) —
Notion B-87 ji zmiňuje, ale jde o rozsáhlou architektonickou změnu
frontend auth toku (OIDC, MSAL, cookie vs. token, vliv na `useSignalR`
a `apiClient`). V souladu s pravidlem „kvalita před rychlostí" by se
Entra ID měla řešit jako samostatný blok (návrh B-87a nebo mimo Phase 4).
API-key rotation implementovaná zde vytvoří operační cestu pro dočasné
i dlouhodobé klíče, které Entra ID mezitím plně nenahradí.

### Proč teď

- Blok je explicitně „Nezahájeno" v Notion tabulce a jeho vstupy
  (API key auth z B-23, rate limiting z B-80, produkční Bicep a CI
  pipeline) jsou všechny v `develop`.
- Paralelně běží jen B-85 (Manual override audit) a B-86 (Performance
  testing) — oba na jiných souborech / doménách. Konflikty minimální.
- Security hlavičky a HSTS jsou nezávislé na ostatních Phase 4 blocích.
- API key rotation je nezávislá na Entra ID — dává dobu řadu měsíců,
  než Entra ID UI flow bude připravené.

## Dekompozice subtasků

| # | Subtask | Odhad | Komplexita |
|---|---------|-------|-----------|
| 1 | `SecurityHeadersMiddleware` + `SecurityHeadersOptions` + DI | 35 min | medium |
| 2 | `UseHsts()` + CSP connect-src config (prod only) | 15 min | low |
| 3 | `ApiKeyOptions` — přechod z `string[]` na strukturovaný typ s fallbackem, podpora expirace | 45 min | medium |
| 4 | `ApiKeyAuthMiddleware` — použití `ApiKeyOptions`, skip expirovaných, uložení caller metadat | 30 min | medium |
| 5 | Unit + integrační testy (xUnit + `WebApplicationFactory`) | 55 min | medium |
| 6 | CI — `dotnet list package --vulnerable` + `npm audit` | 25 min | low |
| 7 | `appsettings.json` + dokumentace Key Vault naming pro rotovatelné klíče | 15 min | low |
| 8 | CLAUDE.md update + commit historie | 20 min | trivial |

**Celkem:** ~ 3 h 40 min (odpovídá odhadu 4 h s rezervou na review/fix).

## Ovlivněné komponenty / moduly

- `src/Tracer.Api/Middleware/SecurityHeadersMiddleware.cs` — **nový**
- `src/Tracer.Api/Middleware/SecurityHeadersOptions.cs` — **nový**
- `src/Tracer.Api/Middleware/ApiKeyAuthMiddleware.cs` — **úprava** (binding
  na `ApiKeyOptions`, expirace, caller metadata, zachování SignalR
  fallbacků).
- `src/Tracer.Api/Middleware/ApiKeyOptions.cs` — **nový** (typovaný
  config se zpětnou kompatibilitou pro stávající `string[]` formu
  skrze custom binder).
- `src/Tracer.Api/Program.cs` — **úprava** (`AddOptions<ApiKeyOptions>()`,
  `AddOptions<SecurityHeadersOptions>()`, `UseHsts()`, `UseSecurityHeaders()`).
- `src/Tracer.Api/appsettings.json` — **úprava** (dokumentace struktury,
  `Auth:ApiKeys` přejmenováno na `Auth:ApiKeys:Keys`; zpětně kompatibilní).
- `src/Tracer.Api/appsettings.Development.json` — **úprava** (stejné).
- `tests/Tracer.Infrastructure.Tests/Integration/Security/SecurityHeadersTests.cs` — **nový**
  (WebApplicationFactory e2e — potvrzuje hlavičky na odpovědi).
- `tests/Tracer.Infrastructure.Tests/Integration/Security/ApiKeyAuthTests.cs` — **nový**
  (happy path, expirace, unknown key, SignalR access_token flow).
- `.github/workflows/ci.yml` — **úprava** (dva kroky — `dotnet list
  package --vulnerable --include-transitive` a `npm audit`).
- `CLAUDE.md` — **úprava** (konvence pro security middleware, API key
  rotation, CI vulnerability scanning).

**Nedotknuto:**

- `src/Tracer.Web/*` — frontend auth flow se nemění. Stávající
  `X-Api-Key` klient zůstává funkční.
- `deploy/bicep/modules/key-vault.bicep` — Key Vault a App Service naming
  již podporují více secrets; update je čistě dokumentační.

## Datové modely

### `SecurityHeadersOptions`

```csharp
internal sealed class SecurityHeadersOptions
{
    public const string SectionName = "Security:Headers";

    // "Enabled" controls the whole middleware; dev can disable for debugging.
    public bool Enabled { get; init; } = true;

    // HSTS max-age (seconds). RFC 6797 recommends ≥ 6 months (15_768_000 s).
    // 2 roky = 63_072_000 s, s preload list podmínkou.
    public int HstsMaxAgeSeconds { get; init; } = 63_072_000;
    public bool HstsIncludeSubDomains { get; init; } = true;
    public bool HstsPreload { get; init; } = false;

    // CSP is conservative API-only default (frame-ancestors 'none' + no script/style).
    // UI SPA has its own CSP at the Static Web App layer.
    public string ContentSecurityPolicy { get; init; } =
        "default-src 'none'; frame-ancestors 'none'";

    public string ReferrerPolicy { get; init; } = "no-referrer";
    public string PermissionsPolicy { get; init; } =
        "accelerometer=(), camera=(), geolocation=(), microphone=(), payment=()";
    public string CrossOriginOpenerPolicy { get; init; } = "same-origin";
    public string CrossOriginResourcePolicy { get; init; } = "same-origin";
}
```

### `ApiKeyOptions`

```csharp
internal sealed class ApiKeyOptions
{
    public const string SectionName = "Auth";

    // Keys a strukturou podporují rotation; starší "string[]" se čte fallbackem.
    public IReadOnlyList<ApiKeyEntry> ApiKeys { get; init; } = [];
}

internal sealed record ApiKeyEntry
{
    public required string Key { get; init; }
    public string? Label { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }

    public bool IsActive(DateTimeOffset now) =>
        ExpiresAt is null || ExpiresAt.Value > now;
}
```

**Binding strategie:**
- Primární tvar: `Auth:ApiKeys:0:Key = "abc"`, `Auth:ApiKeys:0:Label = "ci"`,
  `Auth:ApiKeys:0:ExpiresAt = "2027-01-01T00:00:00Z"`.
- Fallback pro plochý tvar `Auth:ApiKeys:0 = "abc"` (string array) —
  vlastní `Configure<IConfiguration>()` lambda v `Program.cs` detekuje
  string vs. object a namapuje.
- Validace: non-empty `Key` s minimální délkou 16 znaků (prevence slabých
  klíčů), non-past `ExpiresAt` pokud nastaveno. `ValidateOnStart`.

**Caller metadata:**
- Po úspěšné autentikaci middleware nastaví
  `HttpContext.Items["AuthLabel"] = entry.Label ?? "unlabelled"`.
- Pokračuje pattern z B-70 (`Items["ApiKeyFingerprint"]` — SHA-256 prefix).
  Fingerprint se drží dál pro audit, `Label` je druhotný human-readable údaj.

### Key Vault naming

Secrets v Key Vaultu: `Auth--ApiKeys--0--Key`, `Auth--ApiKeys--0--Label`,
`Auth--ApiKeys--0--ExpiresAt`. App Service překlad `--` → `:` zajistí,
že App Configuration projekt binding funguje. Bicep `app-service.bicep`
už umí libovolné `@Microsoft.KeyVault()` reference — rotace je operační,
ne kódová záležitost.

## Decision log

**D1: Vlastní middleware pro security headers místo `NWebsec`**
- **Zvoleno:** lehký vlastní middleware (~ 60 řádek).
- **Důvod:** závislost navíc (NWebsec, Microsoft.AspNetCore.SpaServices) je
  u minimálního API nadbytečná. Všech 8 hlaviček nastavíme přes
  `context.Response.Headers`. `IOptions<SecurityHeadersOptions>` zajistí
  per-environment konfigurovatelnost.
- **Trade-off:** ručně udržovat hlavičky. Riziko je minimální (jednorázová
  sada, updatuje se málo).

**D2: HSTS pouze v produkci**
- **Zvoleno:** `if (!app.Environment.IsDevelopment()) app.UseHsts();`.
- **Důvod:** Microsoftem doporučený pattern. V devu (localhost) HSTS
  bloky prohlížeč po prvním načtení a zneplatní fallback na HTTP.
- **Trade-off:** Test prostředí musí explicitně nastavit `ASPNETCORE_ENVIRONMENT`
  pokud chceme HSTS ověřit — integration test to řeší přes
  `WithWebHostBuilder(... UseEnvironment("Production") ...)`.

**D3: API key strukturovaná + fallback na string**
- **Zvoleno:** `Auth:ApiKeys` jako pole objektů s fallbackem na plain string.
- **Důvod:** zachovat zpětnou kompatibilitu pro již nasazené testovací
  prostředí (vývoj, smoke tests, Bicep parametrizace). Migrace je
  „opt-in rename", ne breaking change.
- **Trade-off:** kód binderu je o ~15 řádků složitější. Odměnou je nulový
  výpadek v dev/CI prostředí.

**D4: `ExpiresAt` per-key, ne globální rotation period**
- **Zvoleno:** explicitní `ExpiresAt` DateTime na každém klíči.
- **Důvod:** reálný operační model má overlapping rotation — starý klíč
  ještě pár dnů platí, zatímco nový už aktivní. Globální period by tuto
  periodu nesplnila.
- **Trade-off:** provozovatel musí updatovat config; vývojář musí rozumět
  ISO 8601. Compensating control: validační error v `ValidateOnStart`
  popisuje formát.

**D5: CSP default `default-src 'none'`**
- **Zvoleno:** nejpřísnější CSP pro API responses (žádné HTML, žádné
  skripty).
- **Důvod:** API vrací jen JSON / problem+json. Zakazuje prohlížeči
  pokusit se cokoli z odpovědi načíst / renderovat.
- **Trade-off:** Scalar OpenAPI UI (B-82) běží mimo App Service host —
  nemá na něj vliv. Pokud by někdo chtěl embedded Swagger, musel by
  rozšířit CSP pro `/openapi/*`. Documented.

**D6: Vulnerability scan failuje na High/Critical**
- **Zvoleno:** `dotnet list package --vulnerable --include-transitive`
  a failuje shell step, pokud výstup obsahuje `High` nebo `Critical`.
- **Důvod:** konzervativní baseline. Moderate + Low zatím necháme jen
  jako warning ve výstupu.
- **Trade-off:** False negatives pokud nuget API lágruje metadata (u
  čerstvého CVE). Přijatelné pro blok — continuous vulnerability
  scanning (Defender, Dependabot) je v scope B-78.

## API kontrakty

Žádné změny veřejných HTTP endpointů. Změny se dotýkají výlučně
odpovědních hlaviček a autentizace:

- Každá odpověď z API (včetně 4xx/5xx) nese HSTS + CSP + ostatní.
- `/health` a `/openapi` dostanou stejné hlavičky (anonymní endpoint
  nezbavuje odpovídajícího charakteru ochrany).
- Chybný / expirovaný API klíč → 401 ProblemDetails (stávající tvar).

## Testovací strategie

### Unit testy

- `ApiKeyOptionsBinderTests` — v `Tracer.Infrastructure.Tests` (kde už
  `WebApplicationFactory` harness existuje).
  - Ploché pole stringů → namapuje se na `ApiKeyEntry` bez expirace.
  - Strukturované pole → plné metadaty.
  - Expirovaný klíč v konfiguraci → `OptionsValidationException`
    při startu (past datum = chyba konfigurace).
  - Klíč < 16 znaků → validace selže.
  - Duplikátní `Key` → validace selže.

- `SecurityHeadersOptionsTests` — defaults, přepis přes `IConfiguration`.

### Integration testy

Využijí stávající vzor `WebApplicationFactory<Program>` bez subclassu
(CLAUDE.md konvence):

- `SecurityHeadersTests`:
  - Development: žádný HSTS header (middleware ho vynechává).
  - Production: `Strict-Transport-Security: max-age=63072000; includeSubDomains`.
  - CSP / X-Content-Type-Options / X-Frame-Options / Referrer-Policy
    přítomné na 200 OK a 401 response.
  - Zakázaný blok (`Enabled = false`) hlavičky nenastaví.

- `ApiKeyAuthTests` (rozšíří stávající, pokud jsou):
  - Expirovaný klíč → 401.
  - Aktivní klíč → 200, caller fingerprint + label v response headeru
    diagnostickém (pouze dev).
  - SignalR `access_token` query string → 200 na negotiate endpoint
    (pokud funkční).
  - Prázdný `Auth:ApiKeys` v ne-dev → startup throw.

### Smoke script

- `deploy/scripts/smoke-test-phase4.sh` (pokud existuje) rozšíříme
  o `curl -I` + grep na HSTS hlavičku.

### Mimo scope

- Fuzz / load testy bezpečnostních hlaviček.
- Browser-side CSP reporting endpoint (nastavíme později, pokud bude
  potřeba).

## Akceptační kritéria

1. `SecurityHeadersMiddleware` emituje minimálně
   `Strict-Transport-Security` (prod), `Content-Security-Policy`,
   `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`,
   `Referrer-Policy`, `Permissions-Policy`, `Cross-Origin-Opener-Policy`,
   `Cross-Origin-Resource-Policy`.
2. HSTS se aplikuje pouze v produkci, v devu se nezapíše (ověřeno testy).
3. `Auth:ApiKeys` podporuje jak ploché pole stringů, tak pole objektů
   `{ Key, Label?, ExpiresAt? }`. Fallback je zpětně kompatibilní.
4. Expirovaný klíč → 401. Aktivní → 200 + `HttpContext.Items["AuthLabel"]`
   nastavený, takže audit (B-70, B-85) může použít.
5. Slabý klíč (< 16 znaků), past `ExpiresAt`, duplikáty → startup selže
   s konkrétní chybovou hláškou.
6. CI `backend` job má krok `dotnet list package --vulnerable
   --include-transitive`, který failuje na High/Critical.
7. CI `frontend` job má krok `npm audit --audit-level=high`.
8. Unit + integration testy pokrývají happy path i edge cases. `dotnet
   build` i `dotnet test` = 0 errors.
9. CLAUDE.md aktualizována o konvence security middleware + API key
   rotation + CI gate.
10. Commity atomické, conventional commits. Žádný direct push na
    `develop`.

## Security considerations

- **Principle of least privilege.** CSP `default-src 'none'` vypíná
  všechny zdroje pro prohlížeč. API odpovědi nikdy neobsahují HTML.
- **Defense in depth.** HSTS + `UseHttpsRedirection` + Key Vault
  transport — tři vrstvy zajištění HTTPS.
- **Fail-secure defaults.** `ValidateOnStart` preferuje chybu při bootu
  před silent fallbackem (pattern z B-68/B-69).
- **Audit logability.** Caller identity se dál derivuje serverově
  (`HttpContext.Items["ApiKeyFingerprint"]` SHA-256, label jen jako
  popisek). Žádné klíčové metadaty se nezapisují do logů.
- **Rotace.** Overlapping windows umožní rotaci bez výpadku — doporučený
  postup: přidej nový klíč + ExpiresAt na starý → deploy → vyřaď starý
  po expiraci → deploy.
- **Časová závislost.** `IsActive(DateTimeOffset now)` používá explicitní
  `now` pro testovatelnost (TimeProvider pattern). V middleware voláme
  `_timeProvider.GetUtcNow()`.

## Follow-upy

- **B-87a — Entra ID SPA integration.** Rozsáhlý blok (odhad 6 h) pro
  MSAL.js + OIDC flow. Navržen separátně v Notion. Tento blok prioritně
  sníží provozní tření (rotace klíčů) a dá času na návrh Entra ID.
- **Dependabot / Defender for Cloud.** Continuous monitoring je scope
  B-78 (Deployment Phase 3 + performance).
- **CSP reporting endpoint.** Pokud začne naráz bot traffic, přidáme
  `Content-Security-Policy-Report-Only` na staging a report-URI sběr.
- **API key hash-at-rest.** Dnes klíče ležejí v Key Vault cleartext;
  zvážit SHA-256 store + konstantně-časové porovnání pokud bude klient
  schopný přednést vlastní hash. Ne-urgentní — Key Vault je sám hranice
  důvěry.
