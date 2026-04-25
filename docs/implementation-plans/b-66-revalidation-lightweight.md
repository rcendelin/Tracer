# B-66 — Re-validation lightweight mode

Branch: `feature/b-66-revalidation-lightweight`
Fáze: 3 — AI + scraping
Odhad: 3 h

## 1. Cíl

Doplnit pendant k `DeepRevalidationRunner` (B-67): rychlou variantu, která
pro profil s **málo expirovanými fieldy** neboří plný waterfall, ale jen
osvěží `EnrichedAt` timestamp na expired-but-still-valid fields a tím
posune jejich TTL o další cyklus dál. Plný re-enrichment se používá až
když je počet expirovaných fieldů ≥ deep threshold.

`CompositeRevalidationRunner` dispatchuje na lightweight nebo deep
podle počtu expirovaných fieldů, takže scheduler se nemění a má jediný
`IRevalidationRunner` v DI.

## 2. Dekompozice

| # | Subtask | Složitost | Výstup |
|---|---|---|---|
| 1 | `LightweightRevalidationOptions` (config section) | S | Boolean Enabled, MaxExpiredFields threshold |
| 2 | `LightweightRevalidationRunner` | M | In-memory `RefreshEnrichedAt` na expired fields, žádný save |
| 3 | `CompositeRevalidationRunner` | M | Dispatcher, deep nebo lightweight |
| 4 | DI wiring v `ApplicationServiceRegistration` | S | Registrace composite jako `IRevalidationRunner` |
| 5 | `CompanyProfile.RefreshFieldEnrichedAt(FieldName, DateTimeOffset)` | S | Domain method (idempotent, no `ChangeEvent`) |
| 6 | Tests (`LightweightRevalidationRunnerTests`, `CompositeRevalidationRunnerTests`) | M | NSubstitute + happy path + threshold |
| 7 | Program.cs — bind options + ValidateOnStart | S | `LightweightRevalidationOptions` |
| 8 | CLAUDE.md update | S | Konvence lightweight + composite |

## 3. Datové modely a kontrakty

### 3.1 `LightweightRevalidationOptions`

```csharp
public sealed class LightweightRevalidationOptions
{
    public const string SectionName = "Revalidation:Lightweight";

    /// Když je počet expirovaných fieldů ≤ Threshold, použij lightweight.
    /// Default 2 — pro 1–2 expirované fieldy je plný waterfall přehnaný.
    public int Threshold { get; set; } = 2;

    /// Master toggle. False = composite vždy delegate na deep (B-67),
    /// což je dnešní chování před B-66.
    public bool Enabled { get; set; } = true;
}
```

### 3.2 `LightweightRevalidationRunner`

- Bere expired fields z `IFieldTtlPolicy.GetExpiredFields(profile, now)`.
- Pro každý field zavolá `profile.RefreshFieldEnrichedAt(field, now)`.
- **Nezapisuje** přes `IUnitOfWork` — kontrakt `IRevalidationRunner` říká,
  že lightweight runners nechávají persistenci na schedulerovi.
- Píše `ValidationRecord` (`ValidationType.Lightweight`, `ProviderId = "revalidation-lightweight"`)
  přes `_validationRecordRepository.AddAsync` — ale **bez save** (scheduler
  saveuje na konci profilu). Tím se vyhneme dvojí save semantice; vše,
  co se změní, je ve scoped DbContextu a pluje s následujícím save z scheduleru.
- Vrací `RevalidationOutcome.Succeeded`.

### 3.3 `CompositeRevalidationRunner`

```csharp
public async Task<RevalidationOutcome> RunAsync(CompanyProfile profile, CancellationToken ct)
{
    var expired = _ttlPolicy.GetExpiredFields(profile, DateTimeOffset.UtcNow);
    if (!_lightOptions.Enabled || expired.Count > _lightOptions.Threshold)
        return await _deep.RunAsync(profile, ct);
    return await _lightweight.RunAsync(profile, ct);
}
```

Composite je registrován jako `IRevalidationRunner` (Scoped). Lightweight
+ Deep jsou registrované jako konkrétní typy a injektovány do composite.

### 3.4 `CompanyProfile.RefreshFieldEnrichedAt`

```csharp
public void RefreshFieldEnrichedAt(FieldName field, DateTimeOffset now);
```

- Idempotentní: pokud field nemá hodnotu, no-op.
- **Nikdy** nevyvolává `FieldChangedEvent` — value se nemění, jen timestamp.
- Implementace: pro každý `TracedField<T>?` field switch-case + new record `with { EnrichedAt = now }`.
- Doménový invariant: `EnrichedAt` je monotonní (nemůže klesat).

## 4. Testovací strategie

- **`LightweightRevalidationRunnerTests`**: 5 testů (happy path / no expired
  fields → no-op / GDPR-stripped fields not touched / cancellation / argument guards).
- **`CompositeRevalidationRunnerTests`**: 4 testy (delegate to deep when over
  threshold / delegate to lightweight under threshold / Enabled=false → always
  deep / 0 expired → still go to lightweight which becomes no-op).
- **`CompanyProfileTests.RefreshFieldEnrichedAt_*`**: 3 testy (no-op on null /
  monotonic guard / unaffected fields untouched).
- Žádné integration testy — chybí DbContext harness (sdílený follow-up s B-71/B-83/B-84).

## 5. Akceptační kritéria

1. `LightweightRevalidationRunner` mutuje POUZE in-memory profile; žádné `SaveChangesAsync` volání.
2. `CompositeRevalidationRunner` nahrazuje `DeepRevalidationRunner` jako registrace pro `IRevalidationRunner`.
3. `LightweightRevalidationOptions` má `ValidateOnStart` (Threshold ≥ 0).
4. Konfigurační sekce `Revalidation:Lightweight:Enabled` / `:Threshold` se objeví v `appsettings.json` jako default placeholder.
5. `CompanyProfile.RefreshFieldEnrichedAt` zavolán na nullable field je no-op (žádný throw).
6. Žádný nový public API endpoint, žádné DTO změny — strictly internal Application change.

## 6. Konzervativní rozhodnutí

- **Lightweight nejde proti registru** — některé verze tohoto bloku v Notion popisu
  zmiňují "re-check only against primary registry". To by potřebovalo dodatečnou
  logiku per-provider routing + handle conflict s waterfall locking. Tato verze
  pouze osvěží timestamp; pokud uživatel chce skutečné single-provider re-check,
  použije manual revalidate (`POST /api/profiles/{id}/revalidate`) a přejde do
  deep cesty. Lightweight = "TTL nudge", ne "single-provider validate".
- **`ValidationRecord` jen jeden** — neukládám per-field metric. Composite výsledek je
  agregát; downstream metrika `tracer.revalidation.*` rozliší tag `mode = light | deep`
  jako follow-up.
- **No new SignalR event** — lightweight změny jsou silent; UI nemusí reagovat.

## 7. Follow-upy

- **`tracer.revalidation.mode` tag** — nyní counter neumí rozlišit lightweight / deep;
  až bude integration harness (B-71/B-83/B-84 sdílený follow-up), přidat tag.
- **Single-provider lightweight** — pokud uživatel chce skutečně re-checknout proti
  primárnímu registru, zavedeme `LightweightProviderTargetedRunner` jako třetí mode.
  Mimo scope B-66.
