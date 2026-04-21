# B-68 — Field TTL policy + configuration

**Status:** V realizaci
**Branch:** `claude/upbeat-planck-FrHYk`
**Fáze:** 3 — AI + scraping
**Odhad:** 2 h
**Datum zahájení:** 2026-04-21

## Kontext a cíl

Platforma má v Domain vrstvě jen hodnotový objekt `FieldTtl` s hardcoded
defaulty (`FieldTtl.For(FieldName)`). `CompanyProfile.NeedsRevalidation()`
tyto defaulty používá přímo. V `appsettings.json` už existuje sekce
`Revalidation:FieldTtl` s TimeSpan hodnotami per field, ale žádný kód ji
nečte — konfigurace je mrtvá.

B-68 zavádí konfigurovatelnou politiku přes rozhraní `IFieldTtlPolicy`
(Application, Singleton) a bindování `Revalidation:FieldTtl` do typového
objektu `FieldTtlOptions`. Služba se stane jediným přístupovým bodem pro
TTL v Application vrstvě a připraví půdu pro:

- **B-65 Re-validation scheduler** (paralelní session, branch
  `claude/upbeat-planck-7TUcr`) — scheduler používá `CompanyProfile.NeedsRevalidation()`.
  Po B-68 bude moci volat `IFieldTtlPolicy.GetExpiredFields(profile)`
  a získat konfigurovatelnou prioritu.
- **B-66 Lightweight mode** — potřebuje seznam konkrétních expired fieldů
  (ne jen bool), aby věděl které provider-queries spustit.
- **B-67 Deep mode** — trigger „≥ 3 expired fields" vyžaduje počet expired
  polí.

### Proč teď
- Blok je explicitně „Nezahájeno" v Notion tabulce.
- Jeho vstupy (`FieldTtl` VO z B-03, `CompanyProfile.NeedsRevalidation`
  z B-05, sekce `Revalidation:FieldTtl` v `appsettings.json`) jsou všechny
  v develop.
- Nezávisí na rozpracovaném B-65 (ten je na separátní branchi).
- Malý, dobře ohraničený scope (2 h) — ideální iterační blok.

## Dekompozice subtasků

| # | Subtask | Odhad | Komplexita |
|---|---------|-------|-----------|
| 1 | `FieldTtlOptions` (config binding s per-field `TimeSpan` overrides) | 15 min | low |
| 2 | `IFieldTtlPolicy` interface (Application) | 10 min | trivial |
| 3 | `FieldTtlPolicy` impl — `GetTtl`, `GetExpiredFields`, `GetNextExpirationDate`, `IsRevalidationDue` | 30 min | medium |
| 4 | DI registrace + `AddOptions<FieldTtlOptions>().ValidateOnStart()` v `Program.cs` | 15 min | trivial |
| 5 | Unit testy (defaulty, override, expired fields, next expiration, guardy) | 45 min | medium |
| 6 | XML docs, CLAUDE.md update | 15 min | trivial |

**Celkem:** ~ 2 h 10 min (odpovídá 2 h odhadu).

## Ovlivněné komponenty / moduly

- `src/Tracer.Application/Services/IFieldTtlPolicy.cs` — **nový**
- `src/Tracer.Application/Services/FieldTtlPolicy.cs` — **nový** (`internal sealed`)
- `src/Tracer.Application/Services/FieldTtlOptions.cs` — **nový**
- `src/Tracer.Application/DependencyInjection.cs` — **úprava** (registrace
  Singleton)
- `src/Tracer.Api/Program.cs` — **úprava** (`AddOptions<FieldTtlOptions>()`
  bind + validace)
- `tests/Tracer.Application.Tests/Services/FieldTtlPolicyTests.cs` — **nový**
- `CLAUDE.md` — **úprava** (konvence pro `IFieldTtlPolicy`)

**Nedotknuto:**

- `src/Tracer.Domain/ValueObjects/FieldTtl.cs` — VO s defaulty zůstává
  kanonickým zdrojem výchozích hodnot. Policy jen přidává override vrstvu.
- `src/Tracer.Domain/Entities/CompanyProfile.cs` — `NeedsRevalidation()`
  zůstává beze změny (doménová úroveň používá default TTL). Aplikační kód
  má volat `IFieldTtlPolicy.IsRevalidationDue(profile)` pro
  konfigurovatelnou variantu. XML docstring explicitně nasměruje
  čtenáře na službu.

## Datový model

### `FieldTtlOptions` (Application)

```csharp
public sealed class FieldTtlOptions
{
    public const string SectionName = "Revalidation:FieldTtl";

    /// <summary>
    /// Per-field TTL overrides. Key = FieldName name (case-insensitive),
    /// value = positive TimeSpan. Missing fields fall back to FieldTtl.For().
    /// </summary>
    public IDictionary<string, TimeSpan> Overrides { get; init; }
        = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);
}
```

Binding: v `Program.cs` cílíme přímo na sekci `Revalidation:FieldTtl`,
takže `appsettings.json` stávající struktura (ploché key-value mapování
`FieldName → TimeSpan`) se nemusí měnit.

### `IFieldTtlPolicy` API

```csharp
public interface IFieldTtlPolicy
{
    /// <summary>Effective TTL for the given field (override → default).</summary>
    TimeSpan GetTtl(FieldName field);

    /// <summary>Fields whose TracedField has exceeded its TTL at <paramref name="now"/>.</summary>
    IReadOnlyList<FieldName> GetExpiredFields(CompanyProfile profile, DateTimeOffset now);

    /// <summary>Earliest future expiration among enriched fields, or null if none.</summary>
    DateTimeOffset? GetNextExpirationDate(CompanyProfile profile, DateTimeOffset now);

    /// <summary>Convenience: any field expired at <paramref name="now"/>.</summary>
    bool IsRevalidationDue(CompanyProfile profile, DateTimeOffset now);
}
```

`now` je explicitně předávaný parametr (ne `DateTimeOffset.UtcNow`
interně), aby:
- Testy byly deterministické bez `IClock` abstrakce.
- Scheduler v B-65 mohl použít vlastní timestamp (konzistentní run).

### Validace

`FieldTtlOptions` validace v `Program.cs`:
- Všechny hodnoty v `Overrides` musí být strict positive (`> TimeSpan.Zero`).
- Všechny klíče se musí parsovat na `FieldName` (`Enum.TryParse<FieldName>`).
  Misconfigurovaný klíč → `ValidateOnStart()` selže při bootu.

## Decision log

**D1: Overrides slovník vs. typový objekt per field**
- **Zvoleno:** plochý `IDictionary<string, TimeSpan>` bindovaný na sekci.
- **Důvod:** `appsettings.json` už používá plochou strukturu
  `"Revalidation:FieldTtl": { "EntityStatus": "30.00:00:00", ... }`.
  Typový objekt s 16 property by bylo boilerplate a rozbije existující
  konfiguraci. Dictionary navíc umožňuje mít hodnoty jen pro podmnožinu
  polí — fallback na `FieldTtl.For()` zajistí hodnotu pro ostatní.
- **Trade-off:** Klíče validujeme run-time (`Enum.TryParse`) místo
  compile-time. `ValidateOnStart()` minimalizuje riziko — misconfigurace
  selže při bootu, ne za provozu.

**D2: `DateTimeOffset now` parametrem, ne `IClock`**
- **Zvoleno:** explicitní `now` parameter na každé volané metodě.
- **Důvod:** Konzistentní s existujícím vzorem (`FieldTtl.IsExpired(ttl)`
  přijímá TTL, `DateTimeOffset.UtcNow` dělá v repository a services
  přímo). Zavedení `IClock` by byla oddělená refaktorace napříč
  kódbázi. Per-call `now` je navíc přesnější pro scheduler, který chce
  konzistentní snapshot napříč `GetExpiredFields` → `GetNextExpirationDate`
  v jednom tiku.
- **Trade-off:** Volající si musí pamatovat předat `now`. Naopak služba
  zůstává čistá funkce.

**D3: `CompanyProfile.NeedsRevalidation()` neměnit**
- **Zvoleno:** domain metoda zůstává, s rozšířeným XML docstringem.
- **Důvod:** Rozhraní Domain vrstvy nepadá kvůli Application feature.
  `CompanyProfile` nesmí referencovat `IFieldTtlPolicy` (Application) —
  porušilo by to směr závislostí Clean Architecture.
- **Trade-off:** Existuje nepatrná duplikace chování (domain fallback vs.
  service). Žijeme s tím — obě cesty jsou dokumentované a domain varianta
  slouží jen Domain testům a interním invariantům.

**D4: Policy jako Singleton**
- **Zvoleno:** Singleton, stateless.
- **Důvod:** Konfigurace je immutable od startu (IOptions snapshot
  bychom použili, kdybychom podporovali hot reload — to není scope B-68).
  Stejný pattern jako `GdprPolicy`, `ConfidenceScorer`, `FuzzyNameMatcher`.

## API kontrakty

Žádné veřejné HTTP API endpointy se nemění. Jde o čistě interní
aplikační službu. Jediná externí viditelná změna:

- `Revalidation:FieldTtl` se stává závaznou, validovanou konfigurační
  sekcí. Misconfigurace (záporný TimeSpan, neznámý field name) selže
  při startu — jasný fail-fast.

## Testovací strategie

### Unit testy — `FieldTtlPolicyTests.cs` (Application.Tests)

**Defaulty:**
- `GetTtl(field)` bez override → odpovídá `FieldTtl.For(field).Ttl` pro
  všechny `FieldName`.
- `GetTtl(FieldName.EntityStatus)` → 30 dní, `Officers` → 90, `Phone` → 180,
  `RegistrationId` → 730.

**Overrides:**
- Override na jednom poli → `GetTtl` vrátí override.
- Overrides jsou case-insensitive v klíči.
- Override s `TimeSpan.Zero` nebo negative → validace v `FieldTtlPolicy`
  konstruktoru hodí `ArgumentOutOfRangeException` (obrana proti
  obcházení `ValidateOnStart`).
- Neznámý klíč v overrides slovníku → `ArgumentException` s konkrétním
  názvem pole.

**`GetExpiredFields`:**
- Profil bez enriched fieldů → prázdný seznam.
- Profil s fresh LegalName → prázdný seznam.
- Profil s EntityStatus enriched 31 dní zpět → seznam obsahuje
  `FieldName.EntityStatus`.
- Profil s mixem fresh/expired → seznam jen expired.
- S overridem zkráceným na 1 den → LegalName enriched 2 dny zpět se stává
  expired.
- Deterministický `now` parametr — žádné flaky testy.

**`GetNextExpirationDate`:**
- Profil bez fieldů → `null`.
- Profil s EntityStatus enriched teď a Phone enriched teď → vrací
  `now + 30d` (EntityStatus vyprší první).
- Profil s jedním expired a jedním fresh fieldem → vrací datum expirace
  fresh (další budoucí expirace).

**`IsRevalidationDue`:**
- Prázdný profil → false.
- Profil s fresh fieldy → false.
- Profil s jedním expired fieldem → true.

**Guardy a DI:**
- Konstruktor s `null` options → `ArgumentNullException`.
- `FieldTtlOptions` binding: neznámý klíč (`Enum.TryParse` fail) →
  `OptionsValidationException` při startu.

### Integration
- Ne nutné — čistě in-memory služba s validovanou konfigurací.
- Program.cs startup v existujících integration testech potvrdí
  `ValidateOnStart` — přidám ošetření default `FieldTtlOptions` v test hostu
  (prázdné overrides = pass).

### E2E
- Mimo scope B-68. Scheduler (B-65) s reálnou DB bude v rámci B-66/B-67
  testován E2E.

## Akceptační kritéria

1. `FieldTtlOptions` bindovaný z `Revalidation:FieldTtl` sekce s validací.
2. `IFieldTtlPolicy` + `FieldTtlPolicy` implementace s:
   - `GetTtl(FieldName)` — override → default fallback,
   - `GetExpiredFields(profile, now)` — plný seznam expired fieldů,
   - `GetNextExpirationDate(profile, now)` — earliest expiration,
   - `IsRevalidationDue(profile, now)` — convenience predicate.
3. DI registrace jako Singleton v `AddApplication()`.
4. `Program.cs` váže options s `ValidateOnStart()` — misconfigurace
   neznámého field klíče nebo neposit. TTL selže při bootu.
5. Unit testy pokrývají happy path, override, hranice (TTL zero),
   invalid inputs. Všechny zelené.
6. `dotnet build` = 0 errors, `dotnet test` všechny suite zelené.
7. CLAUDE.md aktualizována o nový service + konfigurační konvenci.
8. Commit historie atomická (subtask = commit), conventional commits.

## Security considerations

- **Žádná PII v konfiguraci ani logování.** Policy pracuje jen s `FieldName`
  enum + `TimeSpan` hodnotami.
- **Validace vstupní konfigurace.** Negativní TimeSpan, neznámý field
  klíč — odmítnuté při startu. Žádný pokus o silent fallback, který by
  nastavil nesmyslnou TTL.
- **No reflection / dynamic code.** Switch + dictionary lookup jsou
  bezpečné pro AOT / trimming (nehraje roli teď, ale nezavádíme
  překážku).
- **`FieldTtlPolicy` je Singleton stateless** — žádný shared mutable
  state, žádné race conditions.

## Follow-upy

- **B-65 integrace:** Až se B-65 z větve `claude/upbeat-planck-7TUcr`
  merguje do `develop`, doporučené follow-up issue: refaktorovat
  `RevalidationScheduler` aby volal `IFieldTtlPolicy.IsRevalidationDue`
  místo `CompanyProfile.NeedsRevalidation`. Umožní konfigurovat TTL
  per environment bez domain změn.
- **B-66 lightweight mode:** `GetExpiredFields` je přesně to API, které
  lightweight re-validation potřebuje.
- **B-67 deep mode:** trigger „≥ 3 expired fields" = `GetExpiredFields(profile, now).Count >= 3`.
- **Hot reload konfigurace:** Pokud v budoucnu bude třeba měnit TTL za
  běhu, přepnout na `IOptionsMonitor<FieldTtlOptions>`. Nekryje scope
  B-68.
