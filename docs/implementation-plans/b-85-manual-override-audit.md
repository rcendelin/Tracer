# B-85 — Manual override audit

Branch: `feature/b-85-manual-override-audit`
Fáze: 4 — Scale + polish
Odhad: 3 h

## 1. Cíl

Umožnit operátorovi manuálně přepsat hodnotu jednoho fieldu na CKB profile,
když automatický enrichment dlouhodobě selhává nebo údaj není veřejně
dostupný (typicky `Phone`, `Email`, `EntityStatus`). Každý takový override
musí být plně auditovatelný — kdo, kdy, co a z čeho na co.

Audit trail se nezapisuje do nové tabulky — využije existující `ChangeEvent`
infrastrukturu, která už dnes zaznamenává `DetectedBy`, `PreviousValueJson`,
`NewValueJson` a `DetectedAt`. Manuální override se odliší prefixem
`manual-override:apikey:<fingerprint>` v poli `DetectedBy` a `ChangeType =
ManualOverride` v `ChangeEvent`.

## 2. Dekompozice

| # | Subtask | Složitost | Výstup |
|---|---|---|---|
| 1 | `ChangeType.ManualOverride` enum value | S | Nová enum hodnota (= 3) v Domain |
| 2 | `OverrideFieldCommand` + handler + validator | M | MediatR command s `ProfileId`, `FieldName`, `NewValue`, `Reason`, `Caller` |
| 3 | `PUT /api/profiles/{id}/fields/{field}` endpoint | M | Reads caller from `HttpContext.Items[ApiKeyAuthMiddleware.CallerFingerprintItemKey]` a předá do command — nikdy ne z body |
| 4 | DTO `OverrideFieldRequest` (body) | S | `{ Value: string, Reason: string }` |
| 5 | Restrikce na overridable fields | S | Whitelist string-typed fields; `RegistrationId` / `Officers` zakázané |
| 6 | CLAUDE.md update | S | Konvence: caller fingerprint, ChangeType.ManualOverride, scope omezení |
| 7 | Plán dokument | S | Tento file |

## 3. Datové modely a API kontrakty

### 3.1 `ChangeType` enum

```csharp
public enum ChangeType
{
    Created = 0,
    Updated = 1,
    Deleted = 2,
    ManualOverride = 3, // B-85
}
```

Nová hodnota; existující 0-2 zachované. Filter / DTO / SignalR vrstvy
posílají enum jako string díky `JsonStringEnumConverter`.

### 3.2 `OverrideFieldCommand`

```csharp
public sealed record OverrideFieldCommand(
    Guid ProfileId,
    FieldName Field,
    string NewValue,
    string Reason,
    string CallerFingerprint,
    string? CallerLabel) : IRequest<OverrideFieldResult>;

public enum OverrideFieldResult
{
    Overridden,        // 200 OK — change persisted
    NoChange,          // 200 OK — value matched, no audit entry
    ProfileNotFound,   // 404
    FieldNotOverridable, // 400 — RegistrationId, Officers, Address types, Location
}
```

Handler logic:
1. Validate `CallerFingerprint` is non-empty (server-side guarantee — middleware writes it).
2. Lookup profile via `ICompanyProfileRepository.GetByIdAsync`.
3. If null → `ProfileNotFound`.
4. Whitelist check on `Field`. Allowed: string-typed `TracedField<string>?` properties
   except `RegistrationId` (not a TracedField), `EntityStatus` allowed but Critical
   notification still fires. `RegisteredAddress`, `OperatingAddress`, `Location` NOT
   in scope (complex types — separate block).
5. Build `TracedField<string>`:
   ```csharp
   var tf = new TracedField<string>
   {
       Value = command.NewValue.Trim(),
       Confidence = Confidence.Create(1.0),
       Source = $"manual-override:{command.CallerFingerprint}",
       EnrichedAt = DateTimeOffset.UtcNow,
   };
   ```
6. Call `profile.UpdateField(field, tf, source: $"manual-override:{command.CallerFingerprint}")`.
7. If returns null → `NoChange`. Else `ChangeEvent` produced (with `ChangeType.Updated`
   from existing logic). **Override the ChangeType to `ManualOverride`** before
   `SaveChangesAsync` — handler needs to mutate the event. Approach: extend
   `CompanyProfile.UpdateField` overload that accepts `ChangeType` override,
   OR call a new domain method `OverrideField` that wraps `UpdateField` and forces
   `ChangeType.ManualOverride` on the returned event.
8. `_unitOfWork.SaveChangesAsync` — fires `FieldChangedEvent` (Major+ also goes
   through Service Bus for FieldForce).
9. Return `Overridden`.

### 3.3 Endpoint

```
PUT /api/profiles/{profileId:guid}/fields/{field}
Body: { "value": "...", "reason": "..." }
```

Reads caller from `HttpContext.Items[ApiKeyAuthMiddleware.CallerFingerprintItemKey]`
(string) and `CallerLabelItemKey` (string?). The body **never** carries caller
identity — that would be untrusted input.

Returns:
- 204 No Content on success / no-change (idempotent).
- 400 BadRequest on unsupported field (whitelist).
- 404 NotFound on missing profile.
- 401 Unauthorized if API key middleware rejects.

### 3.4 `OverrideField` domain method

Wrapper around `UpdateField` that forces `ChangeType.ManualOverride`:

```csharp
public ChangeEvent? OverrideField<T>(FieldName fieldName, TracedField<T> newValue,
                                      string callerFingerprint)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(callerFingerprint);
    var change = UpdateField(fieldName, newValue, $"manual-override:{callerFingerprint}");
    if (change is null)
        return null;
    change.MarkAsManualOverride(); // new method on ChangeEvent
    return change;
}
```

### 3.5 `ChangeEvent.MarkAsManualOverride`

```csharp
public void MarkAsManualOverride()
{
    if (ChangeType == ChangeType.ManualOverride)
        return; // idempotent
    ChangeType = ChangeType.ManualOverride;
}
```

(setter is private; method exposed.)

## 4. Testovací strategie

- **Unit:** `OverrideFieldHandlerTests` (5 testů: happy path / 404 / whitelist
  rejection / no-change idempotency / cancellation).
- **Domain:** `CompanyProfileTests.OverrideField_*` (3 testy: produces ChangeEvent
  with ManualOverride / null when value unchanged / requires non-empty fingerprint).
- **Endpoint:** integration test once DbContext harness is available (B-77/B-71
  follow-up).

## 5. Akceptační kritéria

1. `PUT /api/profiles/{id}/fields/{field}` přijímá body `{value, reason}` a vrací 204.
2. Caller fingerprint čten **pouze** ze `HttpContext.Items`, nikdy z body.
3. Audit trail viditelný v `GET /api/profiles/{id}/history` jako `ChangeEvent`
   s `ChangeType = ManualOverride` a `DetectedBy = "manual-override:apikey:XXXX"`.
4. `Reason` je v této verzi **logged-only** — log kontaminované audit message s
   reasonem, nezapisuje se do `ChangeEvent`. Důvod: `ChangeEvent` je optimalizovaný
   pro change detection; persistovat volitelný reason vyžaduje schema change a může
   být follow-up v B-92+.
5. Unsupported fields (RegisteredAddress, OperatingAddress, Location, RegistrationId,
   Officers) odmítnuté 400 BadRequest s ProblemDetails.
6. Critical-severity changes (EntityStatus) přes manual override **stále** publikují
   na Service Bus — auditní cesta je doplněk, ne náhrada notifikace.

## 6. Konzervativní rozhodnutí

- **`Reason` jen v logu, ne v `ChangeEvent`** — dnešní `ChangeEvent` schéma neobsahuje
  pole pro reason; přidání by vyžadovalo migration a změnu Service Bus contract.
  Logováno přes `LoggerMessage` s EventId 9501; v Azure Monitor dohledatelné podle
  `ChangeEventId`. Persistovaný reason je samostatný blok B-92+.
- **String-only whitelist** — Address / Location override je separátní block;
  parsování JSON bodyu na komplexní type otevírá validation surface, kterou
  mimo scope držet.
- **Confidence = 1.0** — manuální override je definitionally autoritativní;
  `ConfidenceScorer` se neúčastní (bypass single-field).
- **No new entity / table** — `ChangeEvent` je dostatečný audit nosič; přidání
  `ManualOverrideAudit` tabulky by duplikovalo data a komplikovalo `GET /api/profiles/{id}/history`.

## 7. Follow-upy

- Persistovaný `Reason` přes nový sloupec na `ChangeEvent` (samostatný blok).
- Address / Location override (komplexní typy).
- UI button "Manuálně opravit" na ProfileDetail (frontend block, B-72 enhanced
  byl v scope, ale tento subtask je už mimo).
- Bulk override import (CSV) — operační optimalizace pro hromadné fixy.
