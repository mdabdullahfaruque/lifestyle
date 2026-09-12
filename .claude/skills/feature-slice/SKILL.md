---
name: feature-slice
description: Add or change a use case in the Lifestyle modular monolith without breaking module boundaries — feature file layout, Result vs throw, cross-module contracts, persistence, migrations, tests. Use for any code change under src/.
---

# Adding a use case

Lifestyle is a **modular monolith**. The boundaries are the design, and three of them are
enforced by `Lifestyle.ArchitectureTests` on every build — a violation fails CI, not review.

## Orient

```bash
ls src/Modules/                                    # Identity Platform Vendors Catalog Media
ls src/Modules/Lifestyle.Modules.<Module>/Features/
```

Module anatomy: `Contracts/` · `Domain/` · `Features/` · `Internal/` · `Persistence/` ·
`{Name}Module.cs`.

Read an existing feature file in the owning module and match it. `docs/04-CODEBASE-STRUCTURE.md`
§3.3 shows the canonical shape; §3.1 the layout. Grep the heading rather than reading the
36 KB file:

```bash
rg -n "^#{2,3} " docs/04-CODEBASE-STRUCTURE.md
sed -n '255,320p' docs/04-CODEBASE-STRUCTURE.md    # §3.3 A feature file
```

## The rules

1. **A new use case is a new file** in `Features/{Resource}/`, holding its request,
   validator, handler and endpoint together. Never add a method to an existing one.
2. `internal` by default. `public` only in `Contracts/` and request/response DTOs.
   *(enforced)*
3. Reach another module through its `Contracts` or an integration event — **never its
   tables**. *(enforced)*
4. Domain rules go on the aggregate; handlers orchestrate.
5. **Return `Result`, don't throw**, for anything a client can cause.
6. No `Helpers/`, `Utils/`, `Common/`. Find the owner.
7. No `DateTime.UtcNow`, `Guid.NewGuid()` or `HttpContext` outside `Api` and
   `Infrastructure` — inject `IClock`, `ICurrentUser`, `ITenantContext`. *(enforced)*
8. Every migration is reviewed as code and is reversible. **Nothing runs migrations on
   startup** — `dotnet run --project src/Lifestyle.Api -- migrate` is explicit and stays
   explicit.
9. Pin every package version in `Directory.Packages.props`. No `*`.
10. Past 300 lines, split the file before adding to it.

`SharedKernel` has an admission rule (§3.2) — read it before putting anything there. The
default answer is no.

## Persistence

One `AppDbContext`, one PostgreSQL schema per module, module-scoped context interfaces
(§4.2). A cross-module use case is still a single `SaveChanges`. Cross-module references
are by id, not navigation property (§4.3). Snake_case conventions, audit and soft-delete
interceptors, concurrency tokens, and a transactional outbox with `SKIP LOCKED` are already
in `Infrastructure` — use them, don't re-implement.

## Build and test

`TreatWarningsAsErrors` is on solution-wide with `latest-recommended` analysis. **A warning
is a bug nobody has read yet — fix it, don't suppress it.** Test projects relax only
CA1707/CA2007/CS1591.

```bash
dotnet test tests/Lifestyle.ArchitectureTests   # boundaries — run this first, it is fastest
dotnet test tests/Lifestyle.UnitTests           # domain rules, no I/O
dotnet test tests/Lifestyle.IntegrationTests    # real API against real PostgreSQL
```

Integration tests use Testcontainers, fall back to `ConnectionStrings__Default`, and skip
with an explanation when neither is available — **a skipped suite is not a passing suite**.
Say which actually ran.

Unlike the other projects here, this one has real coverage. Use it: a green
`ArchitectureTests` run is the cheapest proof you did not break a boundary.

`docs/04-CODEBASE-STRUCTURE.md` §13 records three defects that a clean build and 130 green
tests still missed, all found by running against a live database. Prefer exercising the
real path for anything touching aggregates, tokens, or unique constraints.

## Frontend

`web/` — Angular 20 workspace, three apps (`storefront` SSR, `seller`, `admin`) over five
libs (`auth`, `data-access`, `i18n`, `ui`, `util`).

`npm run generate:client` is wired to `ng-openapi-gen`, but `data-access/models.ts` is
currently **hand-written**. If you change a contract, either regenerate or update the
hand-written model — and say which you did.

## Before you finish

If anything you did diverges from `docs/05-DEPLOYMENT.md` or `06-PHASE1-GO-LIVE.md`, record
it in `docs/07-DEPLOYMENT-DEVIATIONS.md` with its removal trigger and a **P** (permanent) /
**T** (temporary) / **G** (gap) marker. An unrecorded deviation becomes permanent by being
forgotten — that file exists because that already happened.
