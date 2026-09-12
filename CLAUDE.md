# Lifestyle — multi-vendor marketplace

Multi-category marketplace where vendors sell through a shared storefront while keeping a
branded shop on their own subdomain. Target markets: **Malaysia, Bangladesh, Italy** — one
codebase, one deployment per country.

**.NET 10 · EF Core 10 · PostgreSQL 17+ · Angular 20 · Redis · S3/MinIO**

This is the deliberate second attempt at the architecture PropertyMart grew into by
accident. [docs/04 §1](docs/04-CODEBASE-STRUCTURE.md) records what was kept and what was
changed, and the discipline here is the point — don't relax it because a PropertyMart
pattern would be quicker.

## Read before adding code

- **[README.md](README.md)** — current state, what's done and what isn't.
- **[docs/04-CODEBASE-STRUCTURE.md](docs/04-CODEBASE-STRUCTURE.md)** — the binding one.
  Solution layout, module anatomy, persistence and boundary rules.
- **[docs/07-DEPLOYMENT-DEVIATIONS.md](docs/07-DEPLOYMENT-DEVIATIONS.md)** — everything
  running in production that differs from the plan, each with its removal trigger.
  Update it in the same session you add or retire a deviation; a forgotten `T` becomes
  permanent by default.

Grep headings and read windows rather than whole files — `02-TECHNICAL-FRD.md` is 99 KB,
`01-BRD.md` 51 KB, `03-INFRASTRUCTURE-MULTI-REGION.md` 40 KB, `04-CODEBASE-STRUCTURE.md`
36 KB. Only `07-DEPLOYMENT-DEVIATIONS.md` (11 KB) and `06-PHASE1-GO-LIVE.md` (18 KB) are
cheap enough to open whole.

## Project skills

**`feature-slice`** — adding or changing anything under `src/`: feature file layout,
`Result` vs throw, cross-module contracts, persistence, migrations, which tests to run.
Use it instead of inferring the conventions from a neighbouring file.

Also: **`code-review`** before a PR, and **`security-review`** for anything touching
Identity, tokens, or the deploy artifacts.

Unlike the sibling projects, this one has real test coverage (41 architecture + 89 unit +
integration). Run it rather than reasoning about whether you broke something — and never
report a **skipped** integration suite as a passing one.

## Commands

```bash
docker compose up -d                                  # postgres, redis, minio, mailpit
dotnet run --project src/Lifestyle.Api -- migrate     # schema — explicit, never on startup
dotnet run --project src/Lifestyle.Api -- seed        # needs Seed:SuperAdmin:Email/Password
dotnet run --project src/Lifestyle.Api                # https://localhost:5001, /swagger

cd web && npm install
npm start            # storefront (SSR) :4200
npm run start:seller # vendor admin     :4201
npm run start:admin  # super admin      :4202

dotnet test tests/Lifestyle.UnitTests          # domain rules, no I/O
dotnet test tests/Lifestyle.ArchitectureTests  # module boundaries — fails the build
dotnet test tests/Lifestyle.IntegrationTests   # real API against real PostgreSQL
```

Integration tests use Testcontainers, fall back to `ConnectionStrings__Default`, and skip
with an explanation when neither is available.

Whether the seeded super admin must enrol in TOTP before reaching the admin surface is the
`Identity:RequireTwoFactorOnAdmin` setting (`REQUIRE_ADMIN_2FA` in `deploy/.env`). It defaults
to **on**, which is the intended production posture. It is **off** in `bd-prod` so the seeded
account signs in with email and password alone — a deliberate choice recorded in
[docs/07 §6a](docs/07-DEPLOYMENT-DEVIATIONS.md), to be revisited before the platform holds
real vendor or buyer data.

## Architecture

A **modular monolith**: one deployable ASP.NET Core app split into modules that each own
their tables, domain logic and endpoints. Everything inside a module is `internal`; only
`Contracts/` is public. One `AppDbContext`, one PostgreSQL schema per module — so a
cross-module use case is still a single `SaveChanges`, while the seam that would allow
extraction is real.

```
src/Lifestyle.Api/            host: Program, middleware, composition root
src/Lifestyle.SharedKernel/   Entity, Money, Result, IClock, HTTP glue
src/Lifestyle.Infrastructure/ AppDbContext, migrations, outbox, storage, seeding
src/Modules/                  Identity · Platform · Vendors · Catalog · Media
```

Each module: `Contracts/` · `Domain/` · `Features/` · `Internal/` · `Persistence/` ·
`{Name}Module.cs`. Features are **vertical slices** — one file per use case holding its
request, validator, handler and endpoint together.

### Rules of the road

1. A new use case is a **new file** in `Features/{Resource}/`. Don't add a method to an
   existing one.
2. `internal` by default. `public` only in `Contracts/` and request/response DTOs.
3. Reach another module through its `Contracts` or an integration event — **never its tables**.
4. Domain rules live on the aggregate; handlers orchestrate.
5. **Return `Result`, don't throw**, for anything a client can cause.
6. No `Helpers/`, `Utils/`, `Common/`. Find the owner.
7. No `DateTime.UtcNow`, `Guid.NewGuid()` or `HttpContext` outside `Api` and `Infrastructure`.
8. **Migration files are generated, never hand-written.** Use the EF Core CLI to add or remove
   them — `dotnet ef migrations add <Name>` / `dotnet ef migrations remove` — and never edit a
   file under `Persistence/Migrations/`, not even a comment. The migration, its `.Designer.cs`
   and `AppDbContextModelSnapshot.cs` are one consistent set that EF regenerates together; a
   hand edit desynchronises them, and the damage surfaces as a bogus or empty migration several
   changes later. If a migration needs explaining, put the explanation in
   [docs/07](docs/07-DEPLOYMENT-DEVIATIONS.md), not in the file.
   Wrong shape? `remove`, fix the model, `add` again. Every migration is reviewed as code and is
   reversible. **Nothing runs migrations on startup.**
9. Pin every package version — central package management, no `*`.
10. Past 300 lines, split the file before adding to it.

Rules 2, 3 and 7 are enforced by `Lifestyle.ArchitectureTests`, not by review.

`TreatWarningsAsErrors` is on solution-wide (`Directory.Build.props`) with
`latest-recommended` analysis. A warning is a bug nobody has read yet — fix it, don't
suppress it. Test projects relax only CA1707/CA2007/CS1591.

Errors on the wire are **RFC 9457 ProblemDetails**. API surfaces are separated by audience
(storefront / seller / admin) with per-surface JWT audiences; refresh tokens rotate and a
replayed token revokes its whole family.

## Frontend

Angular 20 workspace under `web/`: three apps (`storefront` with SSR, `seller`, `admin`)
over five libs (`auth`, `data-access`, `i18n`, `ui`, `util`). Prettier, 100 cols, single
quotes.

`npm run generate:client` is wired to `ng-openapi-gen`, but `data-access/models.ts` is
currently **hand-written** — if you change a contract, either regenerate or update the
hand-written model, and say which.

## Deployment

Live as `bd-prod` on the shared Contabo VPS at `mylifestylemart.com`. CI runs on pushes and
PRs to `master` and `dev`; deploy runs on a `v*` tag or manual dispatch with a country
input. Branches: `dev` → `master`, plus `prod`.

The server already runs four other APIs behind **host nginx** (PropertyMart bd-prod /
bd-staging / my-prod, SasthoSeba staging), so the documented Caddy container is replaced by
`deploy/docker-compose.host-nginx.yml`. That, and every other divergence, is catalogued in
doc 07 — read it before debugging anything about the live stack.

## Known gaps (README is authoritative; this is the short list)

- Vendor Admin / Super Admin **screens** are the biggest hole — apps, auth and API clients
  exist, the UI does not.
- Bulk CSV product import: deferred out of Phase 1.
- No `IEmailSender` implementation; Mailpit is in Compose with nothing sending to it.
