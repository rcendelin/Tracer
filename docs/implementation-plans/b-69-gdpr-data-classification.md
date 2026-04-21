# B-69 — GDPR: Data classification

**Status:** V realizaci
**Branch:** `claude/upbeat-planck-7Xs48`
**Fáze:** 3 — AI + scraping
**Odhad:** 3 h
**Datum zahájení:** 2026-04-21

## Kontext a cíl

Tracer buduje CKB (Company Knowledge Base), který obsahuje primárně firmografická
data (IČO, název, adresa sídla, právní forma). Některé obohacovací zdroje ale
vracejí i data osobní povahy — zejména ředitele/statutáře (Companies House PSC,
Handelsregister officers) a případně osobní telefony/emaily. Podle GDPR je
nutné tato pole identifikovat a zacházet s nimi odlišně (opt-in, retention,
přístupové audity).

Blok B-69 pokládá **klasifikační základ** pro GDPR: zavádí enum
`FieldClassification`, stateless politiku `IGdprPolicy` a audit-hook pro čtení
osobních dat. Vlastní opt-in gate, retention job a GDPR endpoints (Art. 15 / 17)
jsou v navazujícím bloku B-70.

### Proč teď
- B-65 (Re-validation scheduler) běží v paralelní session, B-66/67/68 na něm
  závisí → nejsou k práci.
- B-69 nemá závislost na B-65 (vstup: Domain model + CKB, oba hotové).
- Je to prerekvizita pro B-70 (opt-in gate + retention) i pro případné audit
  logy na profile endpointech.

## Dekompozice subtasků

| # | Subtask | Odhad | Komplexita |
|---|---------|-------|-----------|
| 1 | `FieldClassification` enum (Domain) | 10 min | trivial |
| 2 | `GdprOptions` (config binding) | 15 min | low |
| 3 | `IGdprPolicy` interface + `GdprPolicy` impl (Application) | 40 min | low |
| 4 | `IPersonalDataAccessAudit` interface + default `LoggingPersonalDataAccessAudit` | 25 min | low |
| 5 | DI registrace (Application + API config binding) | 15 min | trivial |
| 6 | Unit testy — klasifikace, RequiresConsent, retention, audit | 45 min | medium |
| 7 | XML dokumentace, CLAUDE.md update | 20 min | trivial |

**Celkem:** ~ 2 h 50 min (odpovídá 3 h odhadu v Notion).

## Ovlivněné komponenty / moduly

- `src/Tracer.Domain/Enums/FieldClassification.cs` — **nový**
- `src/Tracer.Application/Services/IGdprPolicy.cs` — **nový**
- `src/Tracer.Application/Services/GdprPolicy.cs` — **nový**
- `src/Tracer.Application/Services/GdprOptions.cs` — **nový**
- `src/Tracer.Application/Services/IPersonalDataAccessAudit.cs` — **nový**
- `src/Tracer.Application/Services/LoggingPersonalDataAccessAudit.cs` — **nový**
- `src/Tracer.Application/DependencyInjection.cs` — **úprava** (registrace služby)
- `src/Tracer.Api/Program.cs` — **úprava** (binding `GdprOptions`)
- `src/Tracer.Api/appsettings.json` — **úprava** (výchozí sekce `Gdpr`)
- `tests/Tracer.Application.Tests/Services/GdprPolicyTests.cs` — **nový**
- `tests/Tracer.Application.Tests/Services/LoggingPersonalDataAccessAuditTests.cs` — **nový**
- `CLAUDE.md` — **úprava** (sekce konvencí o GDPR klasifikaci)

## Datový model

### `FieldClassification` (Domain enum)

```csharp
namespace Tracer.Domain.Enums;

public enum FieldClassification
{
    /// <summary>Business/company data — no GDPR restrictions.</summary>
    Firmographic = 0,

    /// <summary>Data relating to an identified natural person — GDPR applies.</summary>
    PersonalData = 1,
}
```

### `GdprOptions` (Application)

```csharp
public sealed class GdprOptions
{
    public const string SectionName = "Gdpr";

    /// <summary>Retention for personal-data fields. Default 36 months (≈1095 days).</summary>
    public int PersonalDataRetentionDays { get; init; } = 1095;

    /// <summary>When true, reads of personal data fields emit an audit log entry. Default true.</summary>
    public bool AuditPersonalDataAccess { get; init; } = true;
}
```

### `IGdprPolicy` (Application)

```csharp
public interface IGdprPolicy
{
    FieldClassification Classify(FieldName field);
    bool IsPersonalData(FieldName field);
    bool RequiresConsent(FieldName field);
    TimeSpan PersonalDataRetention { get; }
}
```

**Klasifikační mapa:**

| FieldName | Classification | Rationale |
|-----------|----------------|-----------|
| `Officers` | `PersonalData` | Jména fyzických osob (GDPR Art. 4(1)) |
| vše ostatní | `Firmographic` | Firemní data, no restriction |

Pozn. `Phone`/`Email`/`Website` Tracer sbírá jako firemní kontakty (z registru a
Google Maps), nikoli osobní. Pokud se v budoucnu změní model (např. samostatné
pole `ContactPerson.Email`), bude stačit doplnit mapu.

`RequiresConsent(field) == IsPersonalData(field)` — zachovám oba jako oddělené
API, protože v budoucnu mohou lifecycle/právní rámec rozlišit (např. legitimní
zájem vs. souhlas).

### `IPersonalDataAccessAudit` (Application)

```csharp
public interface IPersonalDataAccessAudit
{
    void RecordAccess(Guid profileId, FieldName field, string accessor, string purpose);
}
```

Default implementace `LoggingPersonalDataAccessAudit` zapisuje přes
`ILogger<LoggingPersonalDataAccessAudit>` s `LoggerMessage.Define` (standardní
vzor v codebasi). Podmíněno `GdprOptions.AuditPersonalDataAccess`. Voláno bude
v budoucích blocích (B-70/B-72 manual override, profile detail reads).

## API kontrakty

Žádné nové HTTP endpointy v tomto bloku — endpointy (`GET /export`, `DELETE /personal-data`)
přijdou v B-70. Stejně tak `IncludeOfficers` flag v `TraceRequest` DTO je B-70.

## Konfigurace

```json
// src/Tracer.Api/appsettings.json
"Gdpr": {
  "PersonalDataRetentionDays": 1095,
  "AuditPersonalDataAccess": true
}
```

## Testovací strategie

### Unit testy (`Tracer.Application.Tests`)

- `GdprPolicyTests`:
  - `Classify_Officers_ReturnsPersonalData`
  - `Classify_AllOtherFields_ReturnsFirmographic` (theory enumerující všechny ostatní členy `FieldName`)
  - `IsPersonalData_Officers_ReturnsTrue`
  - `IsPersonalData_Firmographic_ReturnsFalse`
  - `RequiresConsent_Officers_ReturnsTrue`
  - `PersonalDataRetention_UsesOptionsValue`
  - `PersonalDataRetention_DefaultIs36Months` (1095 dní)
  - `PersonalDataRetention_ZeroDays_ThrowsOnStartup` (validation)
  - `PersonalDataRetention_NegativeDays_ThrowsOnStartup`

- `LoggingPersonalDataAccessAuditTests`:
  - `RecordAccess_WritesStructuredLog` (ověření, že log obsahuje všechny atributy)
  - `RecordAccess_WhenAuditDisabled_DoesNothing`
  - `RecordAccess_NullAccessor_Throws`
  - `RecordAccess_EmptyPurpose_Throws`

### Integration testy

Nepotřeba v tomto bloku — `GdprPolicy` je čistě stateless služba bez závislostí
na DB/HTTP. Integrace přijde až s B-70.

## Akceptační kritéria

1. `IGdprPolicy` je veřejné API v `Tracer.Application.Services`.
2. `FieldClassification` enum je součástí Domain vrstvy s povinnou XML
   dokumentací pro oba členy.
3. `Officers` je klasifikován jako `PersonalData`, všechny ostatní `FieldName`
   hodnoty jako `Firmographic`.
4. `GdprOptions` je registrovaná přes `Options<T>` pattern a má guardy proti
   neplatné retention hodnotě (≤ 0).
5. `IPersonalDataAccessAudit` je registrována jako Singleton
   (stateless, volatelná z libovolného scope).
6. DI: `services.AddApplication()` registruje `IGdprPolicy` a
   `IPersonalDataAccessAudit`; žádný jiný kód se nemusí měnit (zatím nikdo
   nevolá).
7. `dotnet build` zelený, `dotnet test` všechny testy zelené (unit testy pro
   politiku i audit hook).
8. CLAUDE.md doplněno o konvenci pro GDPR klasifikaci (kde sedí, jak přidávat
   nová pole, jak audit volat v B-70+).

## Rizika a poznámky

- **Rozsah** — B-69 záměrně nepřidává `IncludeOfficers` flag ani retention job.
  Vše je v B-70. Pokud by se blok rozpíjel, dělíme PR.
- **Konflikt s B-65** — B-65 upravuje `appsettings.json` (sekce `Revalidation`).
  Sekce `Gdpr` je oddělená, merge conflict nebude. Taktéž B-65 mění
  `DependencyInjection.cs` / `Program.cs` v `Application` resp. `Api` — moje
  úpravy přidávají nové řádky na konec, dohromady se vyřeší při merge.
- **Dopředná kompatibilita** — `FieldClassification` je na Domain straně,
  aby stejný enum mohl použít případný import z `Tracer.Contracts` (B-70 pro
  retention status v response).

## Harmonogram

1. Commit 1: `feat: add FieldClassification enum and GdprPolicy skeleton (B-69)`
2. Commit 2: `feat: add personal data access audit hook (B-69)`
3. Commit 3: `test: cover GdprPolicy and personal data audit (B-69)`
4. Commit 4: `docs: add B-69 implementation plan`
5. Commit 5: `docs: update CLAUDE.md with B-69 GDPR classification patterns`
6. Merge `--no-ff` do `develop` po review + security scanu.
