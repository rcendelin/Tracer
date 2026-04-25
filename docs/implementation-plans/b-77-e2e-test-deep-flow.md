# B-77 — E2E test Deep flow

**Status:** V realizaci
**Branch:** `claude/vibrant-dirac-eERQC`
**Start:** 2026-04-22
**Complexity:** 3
**Phase:** 3 — AI + scraping

## Cíl

End-to-end pokrytí kompletního Deep enrichment flow přes `POST /api/trace` s `Depth = Deep`.
Verifikuje, že celá pipeline (Tier 1 registry APIs + Tier 2 scraping + Tier 3 AI) proběhne
společně s perzistencí CKB, change detection a revalidací — bez reálných externích závislostí
(HTTP, Azure OpenAI, SQL, Service Bus).

Následuje vzor `BatchEndpointPublishTests` (B-44) + `WaterfallOrchestratorTests` (B-17/39/58).
Výsledek přináší regresní síť pro celé Phase 3 a slouží jako předloha pro budoucí E2E testy
(B-78 deployment + performance, B-91 final testing).

## Vstupní předpoklady

- Phase 3 providery (B-54–B-64) mergnuté do `develop` ✅
- WaterfallOrchestrator s `DepthTimeoutOverride` seamem (B-58) ✅
- CkbPersistenceService + change detection (B-41, B-42) ✅
- Revalidation scheduler kostra (B-65) ✅
- GDPR policy (B-69) ✅

## Dekompozice na subtasky

### T1 — E2E harness (`DeepFlowTestHost`) — 1.5h
Společný test fixture postavený na `WebApplicationFactory<Program>`:

- Vlastní `InMemoryTraceRequestRepository`, `InMemoryCompanyProfileRepository`,
  `InMemoryChangeEventRepository`, `InMemorySourceResultRepository`,
  `InMemoryValidationRecordRepository` + `FakeUnitOfWork` (no-op)
  — nikoli NSubstitute, ale jednoduché in-memory `Dictionary<Guid, T>` implementace,
  aby více commands v jednom testu (trace + revalidate) viděly konzistentní stav.
- `ConfigureTestServices`:
  - odstranit všechny stávající `IEnrichmentProvider` registrace → přidat `FakeEnrichmentProvider`
    instance odpovídající Tier 1/2/3 prioritám (viz T3).
  - nahradit `IWaterfallOrchestrator` Scoped scope-respecting builderem, který nastaví
    `DepthTimeoutOverride = _ => TimeSpan.FromSeconds(1)` (zrychlí tier budgety na ~3s max).
  - nahradit `ILlmDisambiguatorClient` fakem (injektovatelný `PreferredCandidateIndex`).
  - nahradit `IAiExtractorClient` fakem (vrací fixní structured JSON).
  - nahradit `IServiceBusPublisher` a `IWebhookCallbackService` no-op.
  - vyřadit `UseAzureMonitor` (nenastavovat AppInsights connection string).
  - Polly fix: `ConfigureAll<HttpStandardResilienceOptions>(o =>
    o.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(2))`.
  - auth: `["Auth:ApiKeys:0"] = "test-api-key"` + `X-Api-Key` header na client.
  - `ConnectionStrings:TracerDb` = placeholder (DbContext se nepoužije — repository
    interfaces jsou nahrazené).
  - `Revalidation:Enabled = false` — scheduler v testu nechceme, budeme volat runner
    přímo nebo přes `POST /revalidate` endpoint (pokud existuje).

### T2 — Fake providers library — 0.5h
`FakeEnrichmentProvider` v test projektu: konfigurovatelný `ProviderId`, `Priority`,
`SourceQuality`, `ProviderResult` builder + volitelný `Delay` (pro testování depth budgetu).
Builder metody: `.Returning(TracedField<string>)`, `.ReturningFields(...)`,
`.Failing(error)`, `.Timeout()`.

### T3 — Test 1: Happy-path Deep trace — 1h
`PostTrace_DeepDepth_RunsAllTiersAndPersistsProfile`:

1. Seed: prázdný CKB.
2. Request: `POST /api/trace?depth=Deep` s `{ companyName: "ACME s.r.o.", country: "CZ",
   registrationId: "00177041" }`.
3. Fakes:
   - Tier 1 (priority 10, ARES-like): vrátí LegalName, RegisteredAddress, LegalForm.
   - Tier 1 (priority 30, GLEIF-like): vrátí LEI, ParentCompany.
   - Tier 2 (priority 150, scraper-like): vrátí Website, Phone.
   - Tier 3 (priority 250, AI-like): vrátí IndustryDescription (nebo podobné pole).
4. Asserts:
   - HTTP 201 Created.
   - Response body: `Status = Completed`, `CompanyProfileId != null`, `Confidence >= 0.7`.
   - Repository obsahuje 1 `CompanyProfile` s `NormalizedKey = "CZ:00177041"`.
   - Všechna 4 Tier fields jsou v profilu (`LegalName.Source = "fake-tier1-ares"` atd.).
   - `SourceResults` tabulka (mocked repo) obsahuje záznam za každý provider.

### T4 — Test 2: Change detection on re-enrichment — 1h
`PostTrace_ReEnrichment_DetectsCriticalChange`:

1. Seed: `CompanyProfile` s `EntityStatus = active` (zapsaný přes repo) + `Officers`.
2. Request: `POST /api/trace` (Deep) → ARES fake nyní vrátí `EntityStatus = dissolved`.
3. Asserts:
   - Profil updatovaný → `EntityStatus.Value == "dissolved"`.
   - `IChangeEventRepository.GetByProfile` obsahuje `ChangeEvent` se `Severity = Critical`,
     `FieldName = EntityStatus`, `PreviousValue = "active"`, `NewValue = "dissolved"`.
   - `IMediator` (zachycený přes spy) vydal `CriticalChangeDetectedEvent` notifikaci.

### T5 — Test 3: Entity resolution ambiguous → LLM disambiguation — 1h
`PostTrace_AmbiguousName_UsesLlmDisambiguation`:

1. Seed: 3 `CompanyProfile` s podobnými jmény v CZ:
   - "ACME GROUP CZ s.r.o." (exact hit: ne — jiné registrationId)
   - "ACME GROUP Czech Republic a.s." 
   - "ACME Logistics CZ s.r.o."
2. Request: `POST /api/trace { companyName: "ACME Group Czech", country: "CZ" }` bez
   `registrationId` → fuzzy ResolveAsync vrátí 3 kandidáty, všechny se skóre v pásmu
   0.70–0.85 → trigger LLM.
3. LlmDisambiguator fake nastaven na `PreferredIndex = 1` (tj. "ACME GROUP Czech Republic").
4. Asserts:
   - Response `CompanyProfileId` == seed profile #1.
   - `LlmDisambiguator` client volán právě 1× s `candidates.Length == 3`.
   - Provider fake pro "found" větev zachycen (žádná nová tvorba profilu, jen update).

### T6 — Test 4: Performance budget Deep < 30s — 0.5h
`PostTrace_DeepDepth_CompletesUnderBudget`:

1. Fakes s realistickým zpožděním: Tier 1 = 200ms, Tier 2 = 500ms, Tier 3 = 800ms.
2. `DepthTimeoutOverride` = ne-použito (necháme reálný Deep = 30s).
3. Stopwatch okolo HTTP requestu.
4. Assert: `stopwatch.Elapsed < TimeSpan.FromSeconds(5)` (konzervativní — reálné 30s
   budget, ale fakes běží rychle; cíl je verifikovat, že orchestrator nezpomalí sám o sobě).
5. Dodatek: jeden test s Tier 2 fake s delayem 15s → verifikuje, že Tier 2 vypadne
   při per-provider 12s timeoutu a celkový výsledek je stále `Completed` s partial fields.

### T7 — Dokumentace + CLAUDE.md update — 0.5h
- Short XML doc na každém test class / method.
- CLAUDE.md: nová sekce „E2E test patterns (B-77)" — seznam test seamů, jak nasadit
  `FakeEnrichmentProvider`, hlavní pastičky.

## Ovlivněné komponenty

- **Nové soubory:**
  - `tests/Tracer.Infrastructure.Tests/Integration/DeepFlowE2ETests.cs` (hlavní fixture + testy)
  - `tests/Tracer.Infrastructure.Tests/Integration/Fakes/FakeEnrichmentProvider.cs`
  - `tests/Tracer.Infrastructure.Tests/Integration/Fakes/InMemoryRepositories.cs`
  - `tests/Tracer.Infrastructure.Tests/Integration/Fakes/FakeAiExtractorClient.cs`
  - `tests/Tracer.Infrastructure.Tests/Integration/Fakes/FakeLlmDisambiguatorClient.cs`
  - `tests/Tracer.Infrastructure.Tests/Integration/DeepFlowTestHost.cs` (WAF wrapper)
  - `docs/implementation-plans/b-77-e2e-test-deep-flow.md` (tento dokument)

- **Bez změn v produkčním kódu** — pouze test infrastructure. Pokud se objeví nutnost
  (např. přidat internal test seam), dokumentuji to jako follow-up commit.

## Datové modely / API kontrakty

Žádné změny — čistě test coverage.

## Testovací strategie

- **Unit testy:** neaplikováno — B-77 je E2E testovací blok.
- **Integration (E2E) testy:** 4 testy nahoře (T3–T6) + jeden variant T6b (timeout).
- **Exit kritéria:**
  - `dotnet test tests/Tracer.Infrastructure.Tests/` prochází lokálně i v CI.
  - Všech 5 nových testů zelených.
  - Žádný test nezávisí na reálné síti, DB nebo Azure zdroji.

## Akceptační kritéria

1. ✅ 4 happy-path E2E testy + 1 timeout variant — všechny zelené.
2. ✅ Deep flow běží přes reálný `SubmitTraceHandler` + `WaterfallOrchestrator`
   + `CkbPersistenceService` (ne mock).
3. ✅ Change detection v E2E kontextu (Critical severity rozpoznána).
4. ✅ Entity resolution fallback přes LLM disambiguator.
5. ✅ Performance assert demonstruje, že orchestrator sám nevnáší > 1s overhead.
6. ✅ CLAUDE.md obsahuje sekci o E2E testování + hlavní gotchy.

## Rizika a mitigace

| Riziko | Mitigace |
|--------|----------|
| `IEnrichmentProvider` DI má mnoho registrací — `RemoveAll` smaže i typed HttpClient handlery | Konfigurace přes `builder.ConfigureServices` po `AddInfrastructure` — odstranit jen `IEnrichmentProvider` descriptory. HttpClient factory zůstává. |
| `CkbPersistenceService` volá `IUnitOfWork.SaveChangesAsync` → spouští domain events přes `TracerDbContext`. In-memory repo nemá dispatch. | Nahradit `IMediator` spy, který zachytí notifikace přímo z repozitáře/entity (volá `_domainEvents`). Alternativně přepnout na EF Core InMemory provider. |
| `EntityResolver` volá `ICompanyProfileRepository.ListByCountryAsync` — in-memory repo musí to implementovat | Ano — in-memory verze jednoduše projde slovník a filtruje. |
| `WaterfallOrchestrator` registrace je Scoped — nelze snadno override přes DI zvenčí | Buď přepsat `DepthTimeoutOverride` přes `init` property v testu s vlastním DI builder scopem, NEBO pustit defaulty a akceptovat reálné 30s (ale rychlé fakes to zvládnou < 1s). Default: **druhá varianta** — žádná manipulace s seamem. |
| SignalR events nejsou součástí E2E assertů (bez `HubConnectionBuilder` vzoru v repo) | Ověřit vydání notifikace přes mock `ITraceNotificationService` místo skutečného WebSocket spojení. |

## Odhadovaná celková pracnost
~4.5h (T1–T7 dle rozpisu).

## Follow-upy mimo scope

- SignalR WebSocket E2E (přes `HubConnectionBuilder`) — doporučuju nechat na B-91.
- Testcontainers MsSql E2E proti reálnému SQL — doporučuju pro B-78 smoke test, ne unit suite.
- Provider-level integration (reálný WireMock provider test) — to je B-49/B-76 scope.
