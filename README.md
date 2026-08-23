# Lifestyle

A multi-category, multi-vendor e-commerce marketplace — vendors sell through a shared marketplace
while keeping their own branded storefront on a subdomain (with optional custom domain).

Target markets: **Malaysia**, **Bangladesh**, **Italy** — one codebase, one deployment per country.
Docs 00–02 are currently written Malaysia-first; see
[doc 03 §13](docs/03-INFRASTRUCTURE-MULTI-REGION.md#13-consequences-for-the-existing-documents) for
the multi-country revisions they need.

**Stack:** .NET 10 · EF Core 10 (code-first) · PostgreSQL 17+ · Angular 20 · Redis · S3/MinIO

Status: **Phase 0 and Phase 1 backend complete and verified against a live PostgreSQL.**
See [Current state](#current-state).

## Planning documents

| Doc | Contents |
|---|---|
| [00 — Master Project Plan](docs/00-PROJECT-PLAN.md) | Scope, architecture, tech stack, roadmap, risks, open decisions |
| [01 — Business Requirements (BRD)](docs/01-BRD.md) | Business model, personas, capabilities, business rules, compliance |
| [02 — Technical FRD](docs/02-TECHNICAL-FRD.md) | Architecture, data model, flows, APIs, security, NFRs |
| [03 — Infrastructure & Multi-Region](docs/03-INFRASTRUCTURE-MULTI-REGION.md) | Server baseline, Malaysia/Bangladesh/Italy deployment strategy, capacity, gaps |
| [04 — Codebase Structure](docs/04-CODEBASE-STRUCTURE.md) | Solution layout, module anatomy, persistence and boundary rules; lessons taken from PropertyMart |
| [infrastructure-mypropertymart/](docs/infrastructure-mypropertymart/) | Existing PropertyMart infrastructure (reference baseline) |

---

## Getting started

```bash
# 1. Dependencies (PostgreSQL, Redis, MinIO, Mailpit)
docker compose up -d

# 2. Schema and seed data — explicit steps, never a start-up side effect
dotnet run --project src/Lifestyle.Api -- migrate
dotnet run --project src/Lifestyle.Api -- seed

# 3. API on https://localhost:5001, Swagger UI at /swagger
dotnet run --project src/Lifestyle.Api

# 4. Frontends
cd web && npm install
npm start            # storefront (SSR)  → :4200
npm run start:seller # vendor admin      → :4201
npm run start:admin  # super admin       → :4202
```

Set `Seed:SuperAdmin:Email` / `Password` before seeding, or no admin account is created. The seeded
admin must enrol in TOTP before it can sign in to the admin surface — that is deliberate.

### Tests

```bash
dotnet test tests/Lifestyle.UnitTests          # domain rules, no I/O
dotnet test tests/Lifestyle.ArchitectureTests  # module boundaries — fails the build on violation
dotnet test tests/Lifestyle.IntegrationTests   # real API against real PostgreSQL
```

Integration tests use Testcontainers, and fall back to `ConnectionStrings__Default` when no
container runtime is available. With neither they skip with an explanation rather than failing.

---

## Architecture in one paragraph

A **modular monolith**: one deployable ASP.NET Core app, partitioned into modules that each own
their tables, domain logic and endpoints. Everything inside a module is `internal`; only its
`Contracts` namespace is public, and a module may use nothing else from another module.
`Lifestyle.ArchitectureTests` enforces that on every build. One `AppDbContext` with one PostgreSQL
schema per module, so a cross-module use case is a single `SaveChanges` while the separation that
makes extraction possible is still real. Features are **vertical slices** — one file per use case,
holding its request, validator, handler and endpoint together.

Read [doc 04](docs/04-CODEBASE-STRUCTURE.md) before adding code.

```
src/
  Lifestyle.Api/                 Host: Program, middleware, composition root
  Lifestyle.SharedKernel/        Entity, Money, Result, IClock, HTTP glue
  Lifestyle.Infrastructure/      AppDbContext, migrations, outbox, storage, seeding
  Modules/
    Lifestyle.Modules.Identity/  Accounts, roles, permissions, JWT, TOTP
    Lifestyle.Modules.Platform/  Audit log, settings
    Lifestyle.Modules.Vendors/   Onboarding, KYC, approval, storefronts
    Lifestyle.Modules.Catalog/   Categories, attribute sets, products, variants, stock
    Lifestyle.Modules.Media/     Uploads, derivatives, orphan sweeping
tests/                           Unit · Architecture · Integration
web/                             Angular workspace: 3 apps, 5 libs
```

## Rules of the road

1. A new use case is a new file in `Features/{Resource}/`. Don't add a method to an existing one.
2. `internal` by default. `public` only in `Contracts/` (and request/response DTOs).
3. Talk to another module through its `Contracts` or an integration event. Never its tables.
4. Domain rules go on the aggregate; handlers orchestrate.
5. Return `Result`, don't throw, for anything a client can cause.
6. No `Helpers/`, `Utils/`, `Common/`. Find the owner.
7. No `DateTime.UtcNow`, `Guid.NewGuid()`, or `HttpContext` outside `Api` and `Infrastructure`.
8. Every migration is reviewed as code and is reversible. Nothing runs migrations on startup.
9. Pin every package version. No `*`.
10. If a file passes 300 lines, split it before adding to it.

Rules 2, 3 and 7 are enforced by the architecture tests, not by review alone.

---

## Current state

**Done — Phase 0 (Foundations)**

- Solution, central package management, warnings-as-errors, `.editorconfig`, Compose, CI
- `SharedKernel`: `Entity`/`AggregateRoot`, `Money`, `Result`, `IClock`/`ICurrentUser`/`ITenantContext`, paging, slugs, HTTP glue
- `Infrastructure`: one `AppDbContext`, snake_case, audit + soft-delete interceptors, concurrency tokens, transactional outbox with `SKIP LOCKED` dispatcher, local/S3 storage, seeders, `migrate`/`seed` CLI
- `Identity`: registration, login, **rotating** refresh tokens with family revocation on replay, lockout, TOTP, per-surface audiences, permission model
- `Api`: middleware pipeline, tenant resolution from `Host`, RFC 9457 ProblemDetails, validation and idempotency filters, Serilog, health checks, OpenAPI
- Angular workspace: 3 apps (storefront SSR, seller, admin) + 5 libs, auth interceptor with silent refresh, guards, typed API services
- Architecture tests (41) and unit tests (89), all green

**Done — Phase 1 (Catalog & vendor onboarding)**

- Category tree (materialised path), attribute sets, four attribute data types, variant axes
- Product/variant model with axis-signature uniqueness, price range and stock rollups
- Draft → review → published lifecycle, including "editing a live product returns it to review"
- Vendor registration, KYC document upload, super-admin approval queue, suspension cascade
- Media upload with derivative generation, EXIF stripping, orphan sweeping
- Vendor Admin and Super Admin APIs; the four launch categories seeded with real attribute sets

**Not done**

- Vendor Admin / Super Admin **UI screens**. The apps, auth and API clients exist; the screens do not.
- Bulk CSV product import (Phase 1 scope, deferred to the first Phase 2 sprint).
- Generated API client — `npm run generate:client` is wired but `data-access/models.ts` is currently hand-written.
- Email sending. `Mailpit` is in Compose; no `IEmailSender` implementation yet.

### Verified against a real database

The schema, seed and full Phase 1 flow have been run against PostgreSQL 18: 20 tables across 5
schemas, citext, jsonb, text[] collections, filtered unique indexes and the text_pattern_ops
operator class all confirmed live. `PhaseOneExitCriterionTests` walks the whole path — register,
apply, upload KYC, submit, admin TOTP, approve, sign in as seller, create a three-variant product,
submit, moderate, and read it back on the shop's own subdomain — and the suite is repeatable
against a long-lived database.

Doing this found three real defects that a clean build and 130 green tests had missed; they are
written up in [doc 04 §13](docs/04-CODEBASE-STRUCTURE.md#13-bugs-the-live-database-found).

```bash
docker compose up -d postgres        # or point at any PostgreSQL
dotnet run --project src/Lifestyle.Api -- migrate
dotnet run --project src/Lifestyle.Api -- seed
dotnet test
```
