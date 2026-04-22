# B-81 — Batch export (CSV / Excel)

**Status:** V realizaci
**Branch:** `claude/vibrant-dirac-dpAKN`
**Fáze:** 4 — Scale + polish
**Odhad:** 4 h
**Datum zahájení:** 2026-04-22

## Kontext a cíl

Fáze 4 má dodat možnost hromadně stáhnout data z CKB a Change Feedu v tabulkových
formátech (CSV / XLSX). Frontend dnes zobrazuje data jen ve stránkované tabulce —
analytici, compliance a support tým dnes nemají možnost rychle vyexportovat
rozšířený profil nebo audit change eventů.

B-81 přidává:

- `GET /api/profiles/export?format=csv|xlsx&country=CZ&minConfidence=0.5&maxRows=…`
- `GET /api/changes/export?format=csv|xlsx&severity=Critical&from=2026-01-01&maxRows=…`
- Streamovaný CSV (nepřipouštět „načíst vše do paměti"), XLSX vyrobený přes
  ClosedXML v paměti (cap 10 000 řádků → několik MB, akceptovatelné).
- `Export` tlačítka v React stránkách `ProfilesPage` a `ChangeFeedPage`.
- Rate limit policy `export` (per-IP) jako ochrana proti DoS — export je I/O těžký.

### Proč teď
- B-81 je první „Nezahájeno" blok ve Fázi 4 bez otevřených prerekvizit.
- B-79 (Redis) a B-80 (rate limiting + circuit breakers) jsou hotové/code-complete,
  infrastruktura pro export je tedy připravená.
- B-67 (deep revalidation) i B-78 (deployment fáze 3) jsou zablokované na dosud
  probíhajících Phase 3 blocích — tento blok můžeme dělat bez kolize.
- Analytická hodnota exportu je nezávislá na zbytku fáze 3 — pracuje pouze nad
  již uloženými profily a change events.

## Dekompozice subtasků

| # | Subtask | Odhad | Komplexita |
|---|---------|-------|-----------|
| 1 | Přidat `CsvHelper` a `ClosedXML` do `Directory.Packages.props` | 5 min | trivial |
| 2 | `IAsyncEnumerable<CompanyProfile> StreamAsync(...)` + `IAsyncEnumerable<ChangeEvent> StreamAsync(...)` na repo + EF Core implementaci | 25 min | medium |
| 3 | `ProfileExportRow`, `ChangeExportRow` (flat DTO pro export) + mapping extension | 20 min | low |
| 4 | `ICompanyProfileExporter` + `CompanyProfileExporter` (CSV + XLSX, streaming / cap 10 k) | 40 min | medium |
| 5 | `IChangeEventExporter` + `ChangeEventExporter` (CSV + XLSX) | 30 min | medium |
| 6 | Minimal API endpointy `/api/profiles/export` + `/api/changes/export` (Content-Disposition, Content-Type, rate limit `export`) | 30 min | medium |
| 7 | `export` rate limit policy v `Program.cs` (10 req/min per IP) | 10 min | trivial |
| 8 | Unit testy exporterů (prázdný výsledek, CSV escaping, XLSX validita, row cap, cancel) | 40 min | medium |
| 9 | Integration testy endpointů (auth, query validace, Content-Type, filter propagace) | 30 min | medium |
| 10 | React — `api/client.ts` helper + tlačítko v `ProfilesPage`, `ChangeFeedPage` | 30 min | low |
| 11 | `CLAUDE.md` update (exporty, rate limit policy, formule CSV injection obrana) | 10 min | trivial |

**Celkem:** ~ 4 h 10 min — drží odhad.

## Ovlivněné komponenty

- `Directory.Packages.props` — nové PackageVersion: `CsvHelper`, `ClosedXML`.
- `Tracer.Domain/Interfaces/ICompanyProfileRepository.cs` — nová metoda `StreamAsync`.
- `Tracer.Domain/Interfaces/IChangeEventRepository.cs` — nová metoda `StreamAsync`.
- `Tracer.Infrastructure/Persistence/Repositories/CompanyProfileRepository.cs`,
  `ChangeEventRepository.cs` — implementace `AsAsyncEnumerable()` + `Take(limit)`.
- `Tracer.Application/Services/Export/*` — nové služby + DTO + mapping.
- `Tracer.Application/DependencyInjection.cs` — registrace exporterů (Scoped,
  protože pull z EF Core DbContext přes repo).
- `Tracer.Api/Endpoints/ProfileEndpoints.cs`, `ChangesEndpoints.cs` — nové endpointy.
- `Tracer.Api/Program.cs` — rate limit policy `export` + případně package import.
- `Tracer.Web/src/api/client.ts` — nová `profilesApi.exportUrl` / `fetchExport` helper,
  plus pro `changes`. (Použijeme `fetch` + `Blob` + `anchor` download, protože auth
  je přes `X-Api-Key` header a přímý `<a href>` by hlavičku nepřenesl.)
- `Tracer.Web/src/pages/ProfilesPage.tsx`, `ChangeFeedPage.tsx` — `Export ↓` tlačítko.

## Datové modely a API kontrakty

### REST API

```
GET /api/profiles/export
    ?format=csv|xlsx   (default: csv)
    &search=...        (volitelné, max 200 znaků — sdílené s ListProfiles)
    &country=XX        (ISO-2 pattern)
    &minConfidence=0..1
    &maxConfidence=0..1
    &validatedBefore=ISO 8601
    &includeArchived=true|false (default false)
    &maxRows=1..10000  (default 1000)

Response 200:
    Content-Type: text/csv; charset=utf-8
        | application/vnd.openxmlformats-officedocument.spreadsheetml.sheet
    Content-Disposition: attachment; filename="tracer-profiles-YYYYMMDD-HHmmss.{csv|xlsx}"

Response 400 (ProblemDetails) — validation (country formát, maxRows mimo rozsah, confidence mimo [0,1])
Response 401 — chybí / špatný X-Api-Key
Response 429 — rate limit nebo
```

```
GET /api/changes/export
    ?format=csv|xlsx
    &severity=Cosmetic|Minor|Major|Critical   (volitelné)
    &profileId=GUID                            (volitelné)
    &from=ISO 8601                             (volitelné; filter DetectedAt >= from)
    &to=ISO 8601                               (volitelné; filter DetectedAt < to)
    &maxRows=1..10000                          (default 1000)
```

### Flat řádkové modely (Application DTOs)

```csharp
public sealed record ProfileExportRow
{
    public Guid Id;
    public string NormalizedKey;
    public string Country;
    public string? RegistrationId;
    public string? LegalName;
    public string? TradeName;
    public string? TaxId;
    public string? LegalForm;
    public string? RegisteredAddress;   // FormattedAddress || "Street, City PostalCode, Country"
    public string? OperatingAddress;
    public string? Phone;
    public string? Email;
    public string? Website;
    public string? Industry;
    public string? EmployeeRange;
    public string? EntityStatus;
    public string? ParentCompany;
    public double? OverallConfidence;
    public int TraceCount;
    public DateTimeOffset CreatedAt;
    public DateTimeOffset? LastEnrichedAt;
    public DateTimeOffset? LastValidatedAt;
    public bool IsArchived;
}

public sealed record ChangeExportRow
{
    public Guid Id;
    public Guid CompanyProfileId;
    public FieldName Field;
    public ChangeType ChangeType;
    public ChangeSeverity Severity;
    public string? PreviousValueJson;
    public string? NewValueJson;
    public string DetectedBy;
    public DateTimeOffset DetectedAt;
    public bool IsNotified;
}
```

### Interfaces

```csharp
public interface ICompanyProfileExporter
{
    Task WriteCsvAsync(Stream output, CompanyProfileExportFilter filter, CancellationToken ct);
    Task WriteXlsxAsync(Stream output, CompanyProfileExportFilter filter, CancellationToken ct);
}

public interface IChangeEventExporter
{
    Task WriteCsvAsync(Stream output, ChangeEventExportFilter filter, CancellationToken ct);
    Task WriteXlsxAsync(Stream output, ChangeEventExportFilter filter, CancellationToken ct);
}
```

## Architektonická rozhodnutí

1. **Žádný MediatR pro export.** MediatR handler předpokládá návratový typ —
   export píše přímo do response streamu. Exporter jako specializovaná Scoped
   service.
2. **Streamování CSV, chunkování XLSX v paměti.** CSV jde řádek po řádku přes
   `AsAsyncEnumerable` + `CsvWriter.WriteRecordsAsync` → response body, `Flush`
   po každém řádku. XLSX build přes ClosedXML do `MemoryStream` → write celého
   bufferu. Pro 10 k řádků s ~25 sloupci je to pár MB — přijatelné.
3. **Row cap: maxRows ∈ [1, 10000], default 1000.** Twin-cap: FluentValidation na
   query, druhá `Math.Clamp(..., 1, 10000)` v handleru (defense-in-depth).
4. **CSV injection ochrana.** Každá string buňka, která začíná na `= + - @ \t \r`,
   se prefixuje apostrofem (`'`). Pattern doporučený OWASP. Implementováno
   centrálně v `CsvInjectionSanitizer.Sanitize(string)` + volané z mapperu.
5. **Rate limiting.** Nová policy `export` — `FixedWindow`, `PermitLimit = 10`,
   `Window = 1 min`, `QueueLimit = 0`. Per-IP. Nasazeno na oba nové endpointy.
6. **Žádné PII navíc.** Officers dnes nejsou na `CompanyProfile` (komentář v entitě
   to potvrzuje; Officers field je zatím mimo CKB, GDPR-gated viz B-69/B-70).
   Export tedy pouze firmografie.
7. **Auth.** Existující `ApiKeyAuthMiddleware` chrání všechny `/api/*` cesty;
   nic navíc není třeba.
8. **Tracking.** `AsNoTracking()` v `StreamAsync` (konzistence s `ListAsync`).
9. **EF Core + IAsyncEnumerable.** `ToListAsync` by materializovalo vše;
   `AsAsyncEnumerable()` streamuje rows z SQL readeru. DbContext instance je
   Scoped → platí že StreamAsync musí být spotřebován v rámci téhož scope
   (což je lifetime HTTP requestu, bezpečné).

## Testovací strategie

### Unit testy (`Tracer.Application.Tests`)
- `CompanyProfileExporterTests`:
  - CSV prázdný výsledek → jen header row + newline.
  - CSV obsahuje jméno s čárkou, uvozovkami → správný escape (CsvHelper default).
  - CSV injection payload `=CMD()` → prefix `'`.
  - XLSX obsahuje očekávaný header a první data row (load pomocí ClosedXML
    v testu).
  - `maxRows = 3`, backing store má 10 profilů → export má 3 data rows.
  - `CancellationToken` již cancelled → `OperationCanceledException`.
- `ChangeEventExporterTests`: analogicky pro change eventy + severity filter.
- `CsvInjectionSanitizerTests`: `=FORMULA`, `+1`, `-1`, `@cmd`, `\tbad`, `\rbad`,
  normal string, null → null, empty → empty.

### Integration testy (`Tracer.Infrastructure.Tests`)
- `POST/GET /api/profiles/export`:
  - 200 + `Content-Type: text/csv; charset=utf-8`, body začíná headerem.
  - 200 + XLSX `application/vnd.openxmlformats-officedocument...` se správným
    magic bytes (`PK\x03\x04`).
  - 400 pro `country=XYZ`, `maxRows=20000`, `minConfidence=2`.
  - 401 bez `X-Api-Key`.
  - Filtr `country=CZ` filtruje — EF Core InMemory harness.
- `GET /api/changes/export`:
  - Severity filter propaguje.
  - `from` / `to` propaguje.

### E2E (manuální validace po deploy)
- Export 1 000 CZ profilů jako XLSX → otevře se v LibreOffice / Excelu.
- Export všech Critical changes → CSV validní v `csvkit`.

## Akceptační kritéria

- [x] `GET /api/profiles/export?format=csv` vrací validní CSV ≤ 10 000 řádků.
- [x] `GET /api/profiles/export?format=xlsx` vrací validní XLSX stažitelný v Excelu/LibreOffice.
- [x] `GET /api/changes/export` — CSV i XLSX, filtry `severity`, `profileId`, `from`, `to` fungují.
- [x] `maxRows` validace + twin-clamp (API + handler).
- [x] CSV injection ochrana.
- [x] Rate limit policy `export` (10 req/min/IP).
- [x] ProblemDetails (RFC 7807) pro všechny chybové odpovědi.
- [x] React: tlačítko „Export CSV" a „Export XLSX" na `ProfilesPage` a `ChangeFeedPage`,
  přenáší aktuální filtry, stahování přes `Blob`.
- [x] Unit + integration testy zeleně.
- [x] `CLAUDE.md` aktualizováno (rate limit policy, CSV injection obrana,
  streaming pattern).
- [x] Žádná Critical/High security nálezy (viz krok 7).

## Follow-upy / out of scope

- Export mimo 10 k → budoucí async export flow (enqueue job, pošle e-mail s odkazem).
- Export do JSON / Parquet — volat by mohl Business Intelligence tým; neřešit teď.
- Export „fulltext" přes LegalName v CZ bez computed column — stejné omezení jako
  `ListProfilesAsync` (viz CLAUDE.md konvence EF Core JSON sloupce).
- Server-side encryption of exported files (DLP) — řešení patří do B-87
  (Security hardening).
