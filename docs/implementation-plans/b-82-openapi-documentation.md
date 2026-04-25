# B-82 — OpenAPI documentation

**Status:** V realizaci
**Branch:** `claude/vibrant-dirac-4ztVs`
**Fáze:** 4 — Scale + polish
**Odhad:** 3 h
**Datum zahájení:** 2026-04-22

## Kontext a cíl

Tracer API už registruje základní `AddOpenApi()` (Microsoft.AspNetCore.OpenApi
10 preview) a každý endpoint má `.WithTags()` + `.WithSummary()` + `.Produces`
metadata. `MapOpenApi()` je však podmíněné na Development a spec je servován
jen jako holé `/openapi/v1.json` — bez popisu, bez security scheme,
bez UI, bez XML docu. Pro integrátory (FieldForce, partneři, nové týmy)
je spec nekonzumovatelný.

B-82 zavádí produkční-ready OpenAPI dokumentaci:

1. **Generovaný XML doc soubor** pro Api projekt → `.NET 10 OpenApi`
   automaticky přečte `///` summary a parameter komentáře z endpointů
   a DTO tříd.
2. **`IOpenApiDocumentTransformer`** — vyplní `Info` (title, version,
   description, contact, license), registruje `ApiKey` security scheme
   (pro `X-Api-Key` header) a přidá server URLs.
3. **`IOpenApiOperationTransformer`** — defaultně aplikuje
   `ApiKey` security requirement na všechny endpointy kromě `/health`
   a `/openapi` (konzistentní s `ApiKeyAuthMiddleware` whitelistem).
4. **XML doc komentáře** na endpointech, DTO records a query/command
   třídách — popisuje pole, validace, chování.
5. **`Scalar.AspNetCore` UI** — moderní, rychlé Swagger-UI-alternative
   s built-in podporou pro OpenAPI v3.1 (co `AddOpenApi` generuje).
   Namontováno pod `/scalar` v Development; v Production gated přes
   `OpenApi:EnableUi` konfiguraci.
6. **Response body schemas** — explicitní `ProducesProblem` pro 401/429
   na všech chráněných endpointech, konzistentní s existujícím
   `AddProblemDetails()`.
7. **Integration test** — `WebApplicationFactory<Program>` načte
   `/openapi/v1.json`, ověří `info.title`, `components.securitySchemes`,
   počet tagů a že každá operation má alespoň jeden 200/201/202 response.

### Proč teď
- Phase 4 je "Scale + polish"; OpenAPI je vstupní bod pro externí
  integrátory. Bez proper spec FieldForce a partneři musí číst `.cs`
  soubory nebo volat endpointy "naslepo".
- B-81 Batch export přidal dva nové endpointy (`/api/profiles/export`,
  `/api/changes/export`) — ideální moment formalizovat doc generation
  před dalším rozšiřováním povrchu API.
- Self-contained blok, žádná závislost na rozpracované paralelní práci
  (B-66, B-67, B-73). Nevstupuje do konfliktu s žádným "V realizaci"
  blokem v Notion tabulce.

### Scope / Non-scope

**V scope:**
- Document transformer pro Info/Security
- Operation transformer pro default security requirement (s path whitelistem)
- XML doc generation + comments na všech public endpoints, queries, commands,
  DTO records
- Scalar UI s gating přes config
- Integration test pro OpenAPI spec

**Mimo scope** (potenciální follow-upy):
- Automated SDK generation (NSwag, OpenAPI Generator) — závisí na stabilní
  spec, B-82 teprve kvalifikuje "stabilní".
- Versioning / v2 API — aktuální verze = v1, budoucí breaking changes
  se řeší při B-87/B-90.
- Request/response schemata pro SignalR hub (OpenAPI nepokrývá WebSocket).

## Dekompozice subtasků

| # | Subtask | Odhad | Komplexita |
|---|---------|-------|-----------|
| 1 | Zapnout `GenerateDocumentationFile` pro Api projekt + suppress CS1591 | 10 min | trivial |
| 2 | `TracerOpenApiDocumentTransformer` (Info, Security, Servers, Tag descriptions) | 45 min | medium |
| 3 | `ApiKeySecurityRequirementTransformer` (operation-level, s path whitelistem) | 30 min | medium |
| 4 | Registrace transformerů + konfigurace `OpenApiOptions` | 15 min | trivial |
| 5 | XML doc komentáře na všech endpointech + DTOs (TraceEndpoints, ProfileEndpoints, ChangesEndpoints, StatsEndpoints; `TraceRequestDto`, `TraceResultDto`, `CompanyProfileDto`, `ChangeEventDto`, `DashboardStatsDto`, další dle potřeby) | 40 min | low |
| 6 | `Scalar.AspNetCore` package + UI endpoint + `OpenApi:EnableUi` gate | 25 min | medium |
| 7 | Integration test `OpenApiDocumentTests` | 45 min | medium |
| 8 | CLAUDE.md update (konvence XML docs, nové config klíče, security scheme) | 15 min | trivial |

**Celkem:** ~ 3 h 5 min (odpovídá 3 h odhadu).

## Ovlivněné komponenty / moduly

**Nové:**
- `src/Tracer.Api/OpenApi/TracerOpenApiDocumentTransformer.cs`
- `src/Tracer.Api/OpenApi/ApiKeySecurityRequirementTransformer.cs`
- `src/Tracer.Api/OpenApi/OpenApiOptions.cs` (typed options pro sekci `OpenApi`)
- `tests/Tracer.Infrastructure.Tests/Integration/OpenApiDocumentTests.cs`

**Úpravy:**
- `src/Tracer.Api/Tracer.Api.csproj` — `<GenerateDocumentationFile>true</GenerateDocumentationFile>`, suppress CS1591
- `src/Tracer.Api/Program.cs` — registrace transformerů, Scalar UI, `OpenApi` options bind
- `Directory.Packages.props` — `Scalar.AspNetCore`
- `src/Tracer.Api/appsettings.json` — nová sekce `OpenApi`
- `src/Tracer.Api/Endpoints/*.cs` — XML doc komentáře
- `src/Tracer.Application/DTOs/*.cs` — XML doc komentáře (sample, ne všechny)
- `src/Tracer.Application/Commands/**/*.cs` — XML doc komentáře (sample)
- `src/Tracer.Application/Queries/**/*.cs` — XML doc komentáře (sample)
- `CLAUDE.md` — nová sekce "OpenAPI documentation conventions"

**Nedotknuto:**
- Doménové entity, value objekty, provider implementace — mimo public API
  surface.
- `Tracer.Contracts` — už dokumentovaný na úrovni Service Bus integrace
  (B-74), mimo REST spec.

## Datový model / API kontrakty

### Nová config sekce `OpenApi`

```json
{
  "OpenApi": {
    "Title": "Tracer API",
    "Version": "v1",
    "Description": "Company data enrichment engine ...",
    "ContactName": "FieldForce Platform Team",
    "ContactEmail": "platform@fieldforce.example",
    "LicenseName": "Proprietary",
    "TermsOfService": "https://fieldforce.example/terms",
    "EnableUi": true,
    "ServerUrls": ["https://api.tracer.example"]
  }
}
```

**Validace:** `Title`, `Version` povinné; `ContactEmail` pokud uveden musí
být valid e-mail; `ServerUrls` pokud uvedené musí být absolutní URI.

**Defaulty** platí v `appsettings.json` (development). Production nastaví
přes Key Vault / app settings — zejména `ServerUrls` a kontakt.

### `ApiKey` security scheme

- Typ: `ApiKey`
- Location: `header`
- Name: `X-Api-Key`
- Popis odkazuje na `ApiKeyAuthMiddleware` — kontrola kontraktu pro
  SignalR (bearer / query) je dokumentovaná zvlášť (není v REST spec).

Security requirement **aplikován na všechny operace** kromě:
- `GET /health` (public liveness)
- `GET /openapi/{documentName}` (spec endpoint sám sebe)

### Response additions

Všechny chráněné endpointy (tzn. vše kromě `/health`) dostanou:
- `401 Unauthorized` — `ProblemDetails` (chybějící / neplatný API klíč)
- `429 TooManyRequests` — kde je `RequireRateLimiting` (batch, export)

To existuje inline na některých endpointech; systematicky doplníme.

## Testovací strategie

### Unit testy
- **Žádné** — transformers jsou deklarativní konfigurace, integration test
  je autoritativní.

### Integration test (`OpenApiDocumentTests`)

Použije `WebApplicationFactory<Program>` (podle již etablovaného vzoru
v `tests/Tracer.Infrastructure.Tests/Integration/`) a načte
`/openapi/v1.json`. Ověří:

1. HTTP 200 + Content-Type `application/json`.
2. `info.title == "Tracer API"`, `info.version == "v1"`.
3. `components.securitySchemes["ApiKey"]` existuje s correct `type=apiKey`,
   `in=header`, `name=X-Api-Key`.
4. Každá operation (kromě path v allowlist) má security requirement
   odkazující na "ApiKey".
5. `GET /api/trace/{traceId}` má response `200` + `404`.
6. `POST /api/trace/batch` má response `202` + `400` + `401` + `429`.
7. Počet tagů ≥ 4 (Trace, Profiles, Changes, Stats).

### E2E
- Scalar UI endpoint (`/scalar/v1`) vrací 200 pokud `OpenApi:EnableUi=true`;
  404 pokud false — pokryto v testu `ScalarUiToggleTests` (v rámci stejného
  souboru integration testu).

### Smoke
- `deploy/scripts/smoke-test-phase2.sh` může volitelně přidat kontrolu
  `/openapi/v1.json` status 200 — mimo scope B-82, dobrovolný follow-up.

## Akceptační kritéria

- [ ] `dotnet build` prochází s `GenerateDocumentationFile=true` bez nových
  `CS1591` (použijeme `<NoWarn>$(NoWarn);CS1591</NoWarn>` pro endpointy,
  ale veřejné DTO mají XML summary).
- [ ] `GET /openapi/v1.json` vrací dokument s vyplněným `info`
  (title, version, description, contact).
- [ ] Security scheme `ApiKey` je přítomný a povinný pro všechny endpointy
  kromě `/health` a `/openapi/*`.
- [ ] `GET /scalar/v1` (případně `/scalar`) vykreslí interaktivní dokumentaci
  v Development hostu.
- [ ] Integration test `OpenApiDocumentTests` prochází (CI).
- [ ] CLAUDE.md zdokumentuje novou sekci `OpenApi` a konvenci XML doc na
  public API surface.
- [ ] Žádný CVE High/Critical v nově přidaných balíčcích (Scalar.AspNetCore).

## Risky / známé pasti

1. **`Microsoft.AspNetCore.OpenApi` preview** — balík je 10.0.0-preview; API
   `IOpenApiDocumentTransformer` se ustálilo, ale breaking change mezi
   preview-4 a GA je možná. Mitigace: testcases jsou schéma-centrické
   (načítají JSON, ne C# API), takže budoucí breaking se projeví jako
   build failure (změna API), ne skrytá regrese.
2. **CS1591 na endpoint methods** — `.NET 10 OpenApi` umí vyčíst `///
   <summary>` ze statických endpoint method. Pokud zapneme
   `GenerateDocumentationFile` globálně (`Directory.Build.props`), ostatní
   projekty začnou hlásit missing XML docs na 100+ symbolech. **Fix:**
   zapneme pouze v `Tracer.Api.csproj` a `NoWarn` suppress CS1591 jen tam,
   kde záměrně nechceme XML doc (interní helpery).
3. **Scalar.AspNetCore trust** — Scalar je open-source (MIT), aktivně
   udržovaný (Khellang / Scalar team). Ověříme konkrétní package na NuGet
   před dependency-add. Alternativa `Swashbuckle.AspNetCore` je heavier
   a má jiný ecosystem (OpenAPI v2/v3.0 vs v3.1).
4. **Security scheme vs. dev-mode pass-through** — `ApiKeyAuthMiddleware`
   v dev módu (žádné klíče) všechny requesty propustí. OpenAPI spec bude
   i v dev módu deklarovat security requirement — to je záměrné: spec
   popisuje **kontrakt**, ne aktuální runtime chování. Konzument v dev
   módu dostane 200 bez API klíče, ale spec ho bude žádat.
5. **Path normalizace v operation transformeru** — operation paths v OpenApi
   dokumentu jsou ve formě `/api/trace/{traceId}`. Porovnávání pro allowlist
   (`/health`, `/openapi/*`) musí brát v úvahu template placeholders.
   Budeme matchovat přes prefix `/openapi` a exact `/health` (žádné
   path param).
6. **Dva OpenApi spec paths za Scalar** — Scalar očekává canonical URL;
   `MapOpenApi` default je `/openapi/{documentName}.json`. Scalar wire-up
   musí použít stejný URL (`/openapi/v1.json`).
7. **Integration test DI setup** — existující integration harness
   (`Program.cs` + auth middleware + validators) už je wire-up'nutý;
   stačí override `ConfigureTestServices` pro test API klíč a načíst spec.

## Rollback plán

Blok je aditivní — rollback = revert merge commitu.  Konfigurace defaultuje
na `EnableUi=true` v dev, `EnableUi` nekonfigurováno → Scalar UI nezobrazen
v prod bez explicitního opt-in. Žádné změny v existujících endpoint signaturách
ani schématech → žádná kompatibilní regrese pro existující klienty.

## Deliverables

- `docs/implementation-plans/b-82-openapi-documentation.md` — tento soubor
- Sada atomic commitů na větvi `claude/vibrant-dirac-4ztVs`:
  - `docs: add B-82 OpenAPI documentation implementation plan`
  - `build: enable XML doc generation for Tracer.Api`
  - `feat: add OpenAPI document + security transformers (B-82)`
  - `feat: wire Scalar UI for OpenAPI spec (B-82)`
  - `docs: add XML doc comments to public API surface (B-82)`
  - `test: cover OpenAPI document and security scheme (B-82)`
  - `docs: update CLAUDE.md with OpenAPI conventions (B-82)`
