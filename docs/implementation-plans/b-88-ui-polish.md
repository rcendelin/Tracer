# B-88 — UI polish

> **Fáze:** 4 — Scale + polish
> **Odhad:** 4 h
> **Branch:** `claude/eloquent-babbage-2IxvB`
> **Datum zahájení:** 2026-04-22

## 1. Cíl

Dotáhnout frontend `Tracer.Web` do produkčně použitelného stavu. Dosavadní
React pages (`Dashboard`, `Traces`, `TraceDetail`, `NewTrace`, `Profiles`,
`ProfileDetail`, `ChangeFeed`, `NotFound`) fungují funkčně, ale chybí jim
cross-cutting concerns, které uživatel od "production-ready" UI očekává:

1. **Loading states** — centralisované skeleton loadery (dnes různé *Loading…*
   textové placeholdery, jen ChangeFeed má skeletony).
2. **Error handling** — chybí top-level React error boundary, fallback pro
   nečekané rendery selže s prázdnou stránkou; error UI v jednotlivých page
   komponentách je nekonzistentní a nenabízí *Retry*.
3. **Responsive layout** — sidebar je fixní 256 px a na mobilu překrývá
   content, tabulky nemají horizontal scroll.
4. **Accessibility** — `aria-label` chybí u ikonových tlačítek, živé regiony
   (SignalR updates) nejsou oznámené screen readeru, fokus navigace
   (`NavLink`) je postavená na čistě vizuálních stavech.
5. **Empty states** — jsou textové a nekonzistentní; chybí call-to-action na
   prázdných stránkách (*Profiles*, *Changes*, *Traces* bez dat).
6. **Toast notifications** — SignalR eventy `TraceCompleted` / `ChangeDetected`
   aktuálně pouze invalidují TanStack Query cache; uživatel na jiné stránce
   neví, že se něco stalo.

Blok **B-88** tyto nedostatky pokrývá *bez* nutnosti instalovat nové
dependence (žádný Radix/Chakra/Toastify) — použijeme Tailwind v4 + vlastní
lightweight primitives. Důvod: (a) minimalizovat bundle size (4 MB limit
Static Web App), (b) zachovat vizuální konzistenci dosavadních stránek, (c)
v Phase 4 se blíží B-79 Redis cache migration a nechceme přidávat další
risk-vektor přes nový npm balíček.

## 2. Cílový stav (akceptační kritéria)

- **Error boundary** `AppErrorBoundary` wrapping `Layout` → fallback UI s
  *Retry* (reload), technickým detailem skrytým za toggle v dev módu.
- **Skeleton primitives** `SkeletonLine`, `SkeletonCard`, `SkeletonTable`
  ve `src/components/skeleton/` — všechny `Loading…` textové placeholdery
  nahrazeny.
- **Empty states** `EmptyState` komponenta (ikona, nadpis, popis, CTA) —
  použita v `TracesPage`, `ProfilesPage`, `DashboardPage` *Recent Traces*.
  `ChangeFeedPage` má prázdný stav už dnes a zůstává netknutý kvůli B-73.
- **Toast system** — provider + `useToast()` hook, max 5 toastů, auto-dismiss
  5 s, manuální close, ARIA live region `role="status"`/`role="alert"`.
- **SignalR integration** — nový singleton `useGlobalToasts` subscriber v
  `Layout`, emituje toasty pro `TraceCompleted` (success) a
  `ChangeDetected` s `Critical`/`Major` severitou (warning/error). Uživatel
  vidí notifikaci i na jiné stránce než Change Feed.
- **Responsive layout** — sidebar `fixed` na mobilu s hamburger toggle
  (šířka < `md` ~768px), hlavní content má 100 % šířky; tabulky obalené
  `overflow-x-auto`. Layout spouštíme od `md` s viditelným sidebarem.
- **Accessibility** —
  - `<main>` má `tabIndex={-1}` + `aria-label` pro keyboard skip.
  - Skip-link (`"Skip to main content"`) na začátku Layoutu (viditelný při
    focus).
  - `NavLink` má `aria-current="page"` pro aktivní stránku.
  - Ikonová tlačítka (hamburger, toast close) mají `aria-label`.
  - Toast kontejner je `role="region"` `aria-live="polite"` (+ `assertive`
    pro error toasts).
  - Pagination `<nav>` má `aria-label="Pagination"`.
  - Status/Connection pill má `role="status"` s live update pro connection
    state změny (politely).
- **Loading unifikováno** — `TracesPage`, `ProfilesPage`, `TraceDetailPage`,
  `ProfileDetailPage`, `DashboardPage` loading block nahradit skeletonem.
- **Retry tlačítka** — stránkové error fallbacky (`bg-red-50` bloky) dostanou
  *Retry* button napojený na `useQuery().refetch()`.
- **NotFound** vizuálně rozšířit (ikonka, CTA-links).
- **NewTrace** — success stav invisible pro krátkou dobu (redirect je
  okamžitý), ale přidáme *aria-live* feedback pro submit.
- **Build zelený:** `npm run build` + `npm run lint` bez warningu.
- **Zero nové runtime dependence** — pouze Tailwind + React utilities.

### Mimo scope (follow-upy)

- `ChangeFeedPage` je v aktivní realizaci B-73 — pouze minimální úpravy
  (nech stávající skeletony), žádné přepisování struktury.
- `ValidationDashboardPage` — code complete B-71, polish bude v B-88
  minimální; pokud tam něco je, fixneme skeletony + toasty, ale neměníme
  architekturu.
- Dark mode — mimo scope (nový design token systém).
- Localization (i18n) — mimo scope.
- Animation/transitions — mimo scope (nepřidáváme framer-motion).

## 3. Dotčené komponenty / moduly

**Nové soubory:**

| Cesta | Popis |
|-------|-------|
| `src/Tracer.Web/src/components/ErrorBoundary.tsx` | Top-level React error boundary (class component). |
| `src/Tracer.Web/src/components/skeleton/Skeleton.tsx` | Base `<Skeleton>`, `<SkeletonLine>`, `<SkeletonCard>`, `<SkeletonTable>`. |
| `src/Tracer.Web/src/components/EmptyState.tsx` | Reusable empty-state block (icon, title, description, action). |
| `src/Tracer.Web/src/components/toast/ToastProvider.tsx` | Context provider + portal host. |
| `src/Tracer.Web/src/components/toast/useToast.ts` | `useToast()` hook surface. |
| `src/Tracer.Web/src/components/ErrorMessage.tsx` | Unified error block with optional Retry. |
| `src/Tracer.Web/src/hooks/useGlobalToasts.ts` | SignalR → toast subscriber for Layout. |
| `src/Tracer.Web/src/hooks/useMediaQuery.ts` | Lightweight `useMediaQuery(query)` hook for sidebar breakpoint. |

**Modifikované soubory:**

| Cesta | Změna |
|-------|-------|
| `src/Tracer.Web/src/main.tsx` | Obal `<ToastProvider>` + `<ErrorBoundary>`. |
| `src/Tracer.Web/src/components/Layout.tsx` | Responsive sidebar + skip-link + `useGlobalToasts`. |
| `src/Tracer.Web/src/components/Pagination.tsx` | `<nav aria-label>` wrapper. |
| `src/Tracer.Web/src/pages/DashboardPage.tsx` | Skeleton, empty state, retry. |
| `src/Tracer.Web/src/pages/TracesPage.tsx` | Skeleton, empty state, retry. |
| `src/Tracer.Web/src/pages/ProfilesPage.tsx` | Skeleton, empty state, retry, tabulka `overflow-x-auto`. |
| `src/Tracer.Web/src/pages/ProfileDetailPage.tsx` | Skeleton, retry, toast on revalidate success/fail. |
| `src/Tracer.Web/src/pages/TraceDetailPage.tsx` | Skeleton, retry. |
| `src/Tracer.Web/src/pages/NewTracePage.tsx` | `aria-live` submit feedback; toast on fail (soft-fallback). |
| `src/Tracer.Web/src/pages/NotFoundPage.tsx` | Richer layout + CTA. |
| `src/Tracer.Web/src/index.css` | Helper pro `sr-only` už existuje (Tailwind). Skip-link styly. |
| `src/Tracer.Web/index.html` | `<title>` → `Tracer` + meta description. |
| `CLAUDE.md` | Konvence pro B-88 UI polish. |

## 4. Dekompozice na subtasky

| # | Subtask | Odhad | Dependencies |
|---|---------|-------|--------------|
| 1 | Implementační plán + Notion update | 10 min | — |
| 2 | Shared primitives: `Skeleton`, `EmptyState`, `ErrorMessage`, `ErrorBoundary` | 35 min | 1 |
| 3 | Toast system (`ToastProvider`, `useToast`, container UI) | 40 min | 2 |
| 4 | Responsive Layout (`useMediaQuery`, sidebar toggle, skip-link, a11y) | 35 min | 2 |
| 5 | Page refactors (Dashboard, Traces, TraceDetail, Profiles, ProfileDetail, NewTrace, NotFound) — loading/empty/error/retry/a11y | 60 min | 2, 3 |
| 6 | `useGlobalToasts` hook + integration v Layoutu | 15 min | 3 |
| 7 | Build + lint pass | 10 min | 2-6 |
| 8 | Code review + security review | 15 min | 7 |
| 9 | Commit, push | 5 min | 8 |
| 10 | Update Notion + CLAUDE.md | 10 min | 9 |

## 5. Datové modely / API kontrakty

**Žádné** backend-side změny. Všechno čistě frontend. Toast hook interface:

```ts
type ToastKind = 'info' | 'success' | 'warning' | 'error';

interface Toast {
  id: string;              // uuid
  kind: ToastKind;
  title: string;
  description?: string;
  durationMs?: number;     // default 5000, 0 = sticky
  action?: { label: string; onClick: () => void };
}

interface UseToastReturn {
  push: (toast: Omit<Toast, 'id'>) => string;
  dismiss: (id: string) => void;
  dismissAll: () => void;
}
```

## 6. Testovací strategie

Frontend doposud nemá unit testy (`vitest` / `testing-library` není
v `package.json`). Záměrně ho v B-88 **nezavádíme** — zavedení test
infrastruktury je samostatná architektonická změna (blok mimo plán).
Ověření v B-88:

- **`npm run build`** — TypeScript strict + Vite production build must pass.
- **`npm run lint`** — zero errors, zero nové warningy.
- **Manual smoke test checklist** (dokumentovaný v merge commitu):
  - Dashboard s/bez dat — loading skeleton, empty state, retry button
    funguje po `/api/*` 500.
  - Traces: loading skeleton → data → empty state po aplikaci filtru bez
    shody.
  - ProfileDetail: revalidate button vyvolá success toast.
  - Mobil (DevTools ≤ 375 px šířky): hamburger otevírá sidebar, overlay
    zavírá sidebar, skip-link viditelný při Tab.
  - Screen reader (NVDA/VoiceOver `Ctrl+Option+A`): toast je oznámený
    politely, Critical Change toast je assertive.
  - Keyboard: Tab projde skip-link → nav items → search → tabulka; `Enter`
    na řádku traces ji otevře.
  - Error boundary: trigger `throw` v komponentě (dočasně) → fallback UI
    s Retry.

## 7. Architektonická rozhodnutí (ADR-light)

### 7.1 Proč vlastní toast systém a ne `react-hot-toast` / `sonner`

Phase 4 exit criteria zmiňují stabilitu a monitoring. Každá nová runtime
dependence je vektor CVE, přidá KB do bundle a sváže CLI behaviorem
s balíčkem třetí strany. 60 řádků kódu (provider + portal + reducer) je
dostatek pro 4 typy toastů, fronta max 5, auto-dismiss. Pokud v budoucnu
potřebujeme queue grouping, swipe-to-dismiss, stacking — přeskočíme na
`sonner`, ale to není dnešní problém.

### 7.2 Error boundary na úrovni `main.tsx`, ne v `Layout`

Pokud error v `Layout` samotném (teoreticky `useGlobalToasts` → `useSignalR`
crash), boundary v Layoutu by ho nezachytila. Boundary proto wrappuje
`<BrowserRouter>` v `main.tsx` → zachytí i vadný layout render.

### 7.3 Skip-link místo plného ARIA landmark tree

Plný landmark tree (`<header>`, `<nav>`, `<main>`, `<aside>`) by šel, ale
náš layout je sidebar-first. Skip-link + `<main>` s `tabIndex={-1}` dává
90 % user experience zisku s ~20 řádky změn.

### 7.4 Responsive breakpoint `md` (768 px)

Tailwind default. Sidebar `fixed` pod `md`, `static` od `md`. Dochází
k transition přes `translate-x-0` / `-translate-x-full`, žádná
animation library.

### 7.5 `useMediaQuery` vs. čistě CSS

CSS-only by stačilo, ale potřebujeme JS stav pro hamburger (zavřít overlay
při resize z mobilu na desktop). Vlastní hook 20 řádků je cleaner než
hack s `window.addEventListener('resize')`.

## 8. Security review checklist

- **XSS v toast description:** toast popisy jdou z (1) statických řetězců
  v kódu, (2) SignalR eventů. Nikdy nerenderujeme přes `innerHTML`/
  `dangerouslySetInnerHTML`. Pokud description přijde z SignalR (např.
  `company name`), React JSX auto-escapuje.
- **Error boundary data exposure:** technický detail (`error.stack`)
  zobrazíme pouze v `import.meta.env.DEV`. V produkci jen generic message.
  Zero PII disclosure.
- **No new runtime dependencies:** žádný nový vektor CVE (`npm audit`
  pre/post stejný).
- **ARIA live region nebude použit k exfiltraci:** toasty obsahují jen
  strings co si vyrábí sám frontend; SignalR server je trusted surface
  (stejný origin, API-key auth).
- **Skip-link bezpečnost:** jen CSS trik s `.sr-only` → focus viditelný.
  Žádný nový risk.
- **npm audit:** spustit před commitem. High/Critical → buď patch, nebo
  follow-up issue + plán zmírnění.

## 9. Follow-upy

Pokud po dokončení B-88 zjistíme další polish items mimo scope, založíme
jako samostatný blok (případně B-88a). Typicky:

- Dark mode (design token system + Tailwind `dark:` variants).
- Framer-Motion animace (focus transitions, toast slide).
- Vitest + Testing Library setup (first frontend unit tests).
- i18n (cs-CZ currently hard-coded via `toLocaleString`).
- ChangeFeedPage polish — after B-73 merge.
- ValidationDashboardPage polish — after B-71 merge.
