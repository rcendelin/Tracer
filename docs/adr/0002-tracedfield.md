# ADR 0002 — `TracedField<T>` as the unit of enrichment data

**Status.** Accepted (B-03).

## Context

A `CompanyProfile` aggregates ~15 fields (LegalName, RegistrationId,
Phone, RegisteredAddress, …). Each field can come from a different
source, with a different confidence, at a different time. We need
sourcing + freshness metadata at the **field** level, not at the
profile level — partial trace results must merge sensibly with existing
CKB data.

## Decision

Every enriched field on `CompanyProfile` is a `TracedField<T>`:

```csharp
public sealed record TracedField<T>
{
    public required T Value { get; init; }
    public required Confidence Confidence { get; init; }   // [0.0, 1.0]
    public required string Source { get; init; }            // "ares", "companies-house", ...
    public required DateTimeOffset EnrichedAt { get; init; }
    public bool IsExpired(TimeSpan ttl) => DateTimeOffset.UtcNow - EnrichedAt > ttl;
}
```

`Confidence` is a separate value object (`[0.0, 1.0]`). `Source` is a
free-form provider id string (`ProviderId` from `IEnrichmentProvider`).

Each Domain field on `CompanyProfile` is `TracedField<T>?` — null when
never enriched, present once any provider has supplied it.

## Consequences

**Positive.**
- `GoldenRecordMerger` can run a per-field tournament (best confidence
  + most recent wins) without keeping a separate "audit table".
- `IFieldTtlPolicy` can ask each field "are you stale?" without joining
  to anything else.
- `ChangeDetector` can compare new enrichment results against current
  field state and produce per-field `ChangeEvent`s.

**Negative.**
- EF Core mapping is non-trivial — `TracedField<T>` is owned-type-mapped
  via `ToJson()`; querying *into* it via LINQ is brittle (see CLAUDE.md
  "EF Core JSON column LINQ queries"). LIKE-search must hit regular
  columns (`RegistrationId`, `NormalizedKey`).
- Memory overhead per profile is higher than a flat record. For 100k+
  CKB rows this is acceptable but would need re-evaluation at 10M+.

**Neutral.**
- Aggregate-level fields (`OverallConfidence`, `TraceCount`,
  `LastValidatedAt`) are NOT `TracedField<T>` — they're scalar columns.
  `OverallConfidence` is a shadow property with `ValueConverter<Confidence?, double?>`.
- `RegistrationId` is intentionally a plain `string` column (no
  `TracedField<>`) so it can be used as a uniqueness / lookup key
  without unwrapping. It's excluded from TTL sweeps for the same reason.

## Related

- ADR 0001 — Clean Architecture (TracedField lives in Domain).
- B-68 Field TTL policy.
- B-91 Shadow-property aggregate pattern.
