# Lifestyle — Codebase Structure Plan

| Field | Value |
|---|---|
| Document | 04 — Codebase Structure |
| Version | 0.1 (Draft) |
| Date | 2026-08-23 |
| Status | Draft — proposes revisions to FRD §1.3, §2, §2.1 |
| Related | [00 — Project Plan §5.1](00-PROJECT-PLAN.md#51-repository-layout) · [02 — Technical FRD §1–3](02-TECHNICAL-FRD.md#1-architecture) · PropertyMart API (reference codebase) |

---

## 0. Summary

This document defines how the Lifestyle backend and repository are laid out, what goes where, and
the rules that keep it that way. It was written after reviewing the PropertyMart API codebase (our
previous .NET project) and the structure proposed in FRD §1–2.

The decision in one paragraph: **a modular monolith with one project per business module**, each
module containing its own domain, features, persistence mapping and endpoints as *folders*, not as
separate projects. Boundaries are enforced by the compiler (`internal` by default, `public` only in
a `Contracts` namespace) and by one architecture test. There is **one `AppDbContext`**, one
migration history, and one PostgreSQL schema per module. Features are organised as **vertical
slices** (one file per use case) rather than as controller/service/DTO layers.

This is deliberately between the two references:

| | PropertyMart (what we have) | FRD §1–2 (what was proposed) | **This plan** |
|---|---|---|---|
| Projects | 5 | ~70 (14 modules × 5) | **~8 at Phase 0, ~18 at v2** |
| Code organised by | Technical layer | Module → layer | **Module → feature** |
| Module boundaries | None (one `IAppDbContext` with 50 DbSets) | Separate `Contracts` project per module | **`internal` + `Contracts` namespace + arch test** |
| DbContext | One | One per module, per-module migration history | **One, schema per module** |
| Cross-module transaction | Trivial (same context) | Shared connection, ambient transaction | **Trivial (same context)** |
| Use cases | Fat services (1000–2000 lines) | Handlers | **One file per use case** |
| Errors | Exceptions → middleware | `Result<T>` → ProblemDetails | **`Result<T>` → ProblemDetails** |
| Endpoints | Controllers, `ApiResponse<T>` envelope | Minimal APIs, no envelope | **Minimal APIs, no envelope** |

---

## 1. What we learned from PropertyMart

### 1.1 Keep

| Practice | Why it worked |
|---|---|
| Thin `Program.cs`; wiring in `Add*()` / `Use*()` extension methods | `Program.cs` stays readable as the app grows |
| One EF `IEntityTypeConfiguration<T>` per entity, `ApplyConfigurationsFromAssembly` | Mapping is discoverable and never in the entity |
| `BaseEntity` / `ISoftDeletable` + global query filters | Audit and soft-delete come for free |
| A single exception-to-HTTP translation point | No status-code logic in handlers |
| FluentValidation for request validation | Validators are plain classes, easy to unit test |
| Serilog + correlation ID + Sentry, error-only | Good signal, cheap to run |
| `BackgroundService` for scheduled work | No job framework until one is needed |
| Services use the DbContext directly — no repository-per-entity layer | Repositories over EF add a layer that mostly forwards calls |
| Typed `Options` classes bound from configuration | Settings are discoverable and testable |

### 1.2 Change

| Problem in PropertyMart | Consequence | What we do instead |
|---|---|---|
| Folders by technical type: `Controllers/`, `Services/`, `DTOs/`, `Interfaces/`, `Validators/` | Adding one feature touches 5–6 folders; nothing is cohesive; `ListingService.cs` is 1036 lines, `ChatbotService.cs` 1989, `SuperAdminController.cs` 1017 | Vertical slices: one folder per resource, one file per use case (§3.3) |
| `IAppDbContext` exposes all 50+ `DbSet`s to every service | Any service can query any table; no boundaries; nothing can be extracted later | Each module declares its own narrow `I{Module}DbContext` with only its tables (§4.2) |
| `PropertyMart.Shared` as a junk drawer (`LandUnitConverter` exists there **and** in `Application/Helpers`) | Duplication, unclear ownership | `SharedKernel` has a strict admission rule (§3.2); no `Helpers/` folders |
| Magic strings for roles and types (`"User"`, `"Agent"`, `"Company"`) | Typos compile; refactors miss call sites | Enums and strongly-typed IDs |
| `PackageReference Version="10.*"` floating versions | Builds are not reproducible | Central Package Management with pinned versions (§2.3) |
| `Logs/*.log` and `wwwroot/uploads/**` committed to git | Repo bloat, PII in source control | `.gitignore` from day one; uploads go to object storage (§5) |
| `ProductionMY` environment name to run a second country | `IsProduction()` breaks; config sprawl | Country is configuration (`Platform:Country`); environment names stay standard (§6) |
| Seeding on application startup; no migrations in the repo | Startup side-effects; no schema history | Migrations are code in the repo and run as an explicit deploy step (§4.4) |
| DI registration grouped by "Phase 2A / 2B" comments in the API project | The API project knows every module's internals | Each module registers itself: `services.AddCatalogModule()` (§3.1) |
| Mapster referenced, manual mapping used | Dead dependency | Manual mapping only; no mapper library |
| Super-admin as one giant controller | Admin logic for every domain in one file | Admin endpoints live in the module that owns the data, under `/v1/admin/*` (§3.4) |

---

## 2. Repository layout

```
/
├── .editorconfig
├── .gitignore
├── Directory.Build.props          # TFM, nullable, implicit usings, analyzers, warnings-as-errors
├── Directory.Packages.props       # central package versions
├── Lifestyle.sln
├── global.json                    # pinned SDK
├── docker-compose.yml             # postgres, redis, minio, mailpit
├── README.md
├── docs/
├── src/
│   ├── Lifestyle.Api/             # host: Program.cs, middleware, composition root
│   ├── Lifestyle.SharedKernel/    # tiny: base types used by every module
│   ├── Lifestyle.Infrastructure/  # AppDbContext, migrations, outbox, storage, email, cache
│   └── Modules/
│       ├── Lifestyle.Modules.Identity/
│       ├── Lifestyle.Modules.Vendors/
│       ├── Lifestyle.Modules.Catalog/
│       └── ...                    # one project per module, added when its phase starts
├── tests/
│   ├── Lifestyle.UnitTests/
│   ├── Lifestyle.IntegrationTests/
│   └── Lifestyle.ArchitectureTests/
├── web/                           # Angular workspace (FRD §20.1 — unchanged)
│   ├── apps/{storefront,seller,admin}/
│   └── libs/{ui,data-access,auth,i18n,util}/
└── deploy/                        # Dockerfiles, compose for staging/prod, reverse-proxy config
```

Differences from Plan §5.1: `tools/` is dropped until something needs it (seeders live in
`Infrastructure/Seeding` and run via a CLI switch); `Lifestyle.Migrator` is replaced by
`Lifestyle.Api migrate` (§4.4); `tests/` holds three projects, not one per module.

### 2.1 Project dependency graph

```
Lifestyle.Api ──────────────► Lifestyle.Infrastructure ──► every Lifestyle.Modules.*
      │                                                           │
      └──────────────────────► every Lifestyle.Modules.* ─────────┤
                                                                  ▼
                                                      Lifestyle.SharedKernel
```

Rules, checked by `Lifestyle.ArchitectureTests`:

1. A module references `SharedKernel` and other modules, **but may only use types in another
   module's `Contracts` namespace**. Everything else in a module is `internal`.
2. `SharedKernel` references nothing in the solution and no framework beyond `System.*`.
3. `Infrastructure` and `Api` may see module internals via `[InternalsVisibleTo]` (they host the
   modules — they need entities for the DbContext and the endpoint registrations).
4. No module references `Infrastructure` or `Api`.
5. No `DateTime.Now`/`UtcNow`, no `Guid.NewGuid()` (use `Guid.CreateVersion7()`), no
   `HttpContext` in `Domain/` or `Features/` — `IClock`, `ICurrentUser`, `ITenantContext` instead.

### 2.2 Project count over time

| Phase | Modules present | Total projects (excl. tests) |
|---|---|---|
| 0 — Foundations | Identity, Platform | 5 |
| 1 — Catalog & onboarding | + Vendors, Catalog, Media | 8 |
| 3 — Conversational ordering | + Messaging, Notifications | 10 |
| 4 — Checkout & payments | + Ordering, Payments, Ledger | 13 |
| 5–7 | + Reviews, Promotions, Shipping | 16 |

Modules are created when their phase starts, not scaffolded empty on day one. Inventory starts as
a feature area inside Catalog (variants and stock are edited together); it becomes its own module
in Phase 4 when reservations arrive. Search likewise lives inside Catalog behind an
`ISearchProvider` until an external engine is adopted.

### 2.3 Build configuration

- `Directory.Build.props`: `net10.0`, `Nullable=enable`, `ImplicitUsings=enable`,
  `TreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true`, `AnalysisLevel=latest-recommended`.
- `Directory.Packages.props`: `ManagePackageVersionsCentrally=true`; every version pinned. Renovate
  or Dependabot opens bump PRs.
- `global.json` pins the SDK so CI and developers build identically.
- `.editorconfig` carries the C# style rules; `dotnet format --verify-no-changes` is a merge gate.

---

## 3. Inside a module

### 3.1 Layout

```
Lifestyle.Modules.Catalog/
├── CatalogModule.cs               # AddCatalogModule(IServiceCollection), MapCatalogEndpoints(IEndpointRouteBuilder)
├── Contracts/                     # PUBLIC — the only namespace other modules may use
│   ├── ICatalogModule.cs          # sync queries other modules need (e.g. GetVariantSnapshots)
│   ├── ProductPublishedEvent.cs   # integration events (records)
│   └── VariantSnapshot.cs         # DTOs returned through the contract
├── Domain/                        # internal
│   ├── Product.cs                 # aggregate: invariants, state transitions, raises domain events
│   ├── ProductVariant.cs
│   ├── Category.cs
│   ├── ProductStatus.cs           # enums next to the entities that use them
│   └── Events/ProductPublished.cs # domain events (in-module)
├── Features/                      # internal — vertical slices
│   ├── Products/
│   │   ├── CreateProduct.cs
│   │   ├── UpdateProduct.cs
│   │   ├── PublishProduct.cs
│   │   ├── GetProduct.cs
│   │   ├── ListVendorProducts.cs
│   │   └── ProductResponses.cs    # shared response shapes + mapping for this resource
│   ├── Categories/
│   └── Admin/                     # /v1/admin/catalog/* — admin use cases for this module's data
├── Persistence/                   # internal
│   ├── ICatalogDbContext.cs       # DbSet<Product>, DbSet<Category>, ... + SaveChangesAsync
│   └── Configurations/
│       ├── ProductConfiguration.cs
│       └── ...
└── Internal/                      # internal — module-private services (slug generator, price resolver)
```

`CatalogModule.cs` is the single entry point the host calls:

```csharp
public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services)
    {
        services.AddScoped<ICatalogModule, CatalogFacade>();
        services.AddValidatorsFromAssembly(typeof(CatalogModule).Assembly, includeInternalTypes: true);
        services.AddHandlersFromAssembly(typeof(CatalogModule).Assembly); // SharedKernel helper: scans for IHandler<,>
        return services;
    }

    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var pub = app.MapGroup("/v1/catalog").WithTags("Catalog");
        GetProduct.Map(pub);
        ListProducts.Map(pub);

        var vendor = app.MapGroup("/v1/vendor/products").RequireVendorStaff().WithTags("Vendor · Products");
        CreateProduct.Map(vendor);
        UpdateProduct.Map(vendor);
        PublishProduct.Map(vendor);

        var admin = app.MapGroup("/v1/admin/catalog").RequirePermission("catalog.admin").WithTags("Admin · Catalog");
        Admin.ForceUnpublish.Map(admin);
        return app;
    }
}
```

### 3.2 SharedKernel admission rule

`Lifestyle.SharedKernel` contains only:

| Type | Purpose |
|---|---|
| `Entity`, `AggregateRoot`, `IAuditable`, `ISoftDeletable` | Base classes; `AggregateRoot` collects domain events |
| `Money` | Value object; decimal + ISO code; arithmetic guarded for currency mismatch |
| `Result`, `Result<T>`, `Error`, `ErrorType` | Expected-failure channel (§3.5) |
| `IClock`, `ICurrentUser`, `ITenantContext` | Injected ambient context |
| `IDomainEvent`, `IIntegrationEvent`, `IIntegrationEventHandler<T>` | Event contracts |
| `IHandler<TRequest, TResponse>`, `Unit` | Use-case handler contract |
| `PagedResult<T>`, `CursorPage<T>`, `PageRequest`, `CursorRequest` | Pagination shapes |
| `Slug` | Slug generation and the reserved-hostname list, shared by vendor, product and category slugs |
| `Http/` — `ResultExtensions`, `ValidationFilter<T>`, `EndpointExtensions`, `AddHandlersFromAssembly` | The HTTP glue every module needs to map its own endpoints |

Admission rule: a type goes into `SharedKernel` only if it is used by **two or more modules** and
has **no dependency on any module**. If only one module uses it, it lives in that module's
`Internal/`. There is no `Helpers/`, `Utils/` or `Common/` folder anywhere in the solution.

**SharedKernel takes the ASP.NET Core framework reference and FluentValidation.** Every module maps
its own endpoints, so `Result → IResult`, the validation endpoint filter and
`RequirePermission()`/`RequireVendorStaff()` have to live somewhere every module can reach — and
`Api` is downstream of all of them. The rule that matters is unchanged and is what the architecture
test asserts: *SharedKernel references no other project in the solution*.

Strongly-typed IDs (`ProductId`, `VendorId` as `readonly record struct`) are **not** in Phase 0.
They interact badly with EF value conversions, minimal-API route binding and OpenAPI schema
generation, and the mix-ups they prevent are largely caught by the compiler already now that every
cross-module reference goes through a typed contract. Revisit if a real Guid mix-up ever ships.

### 3.3 A feature file

One file per use case. Request, validator, handler and endpoint are nested so the whole slice is
readable top to bottom and nothing leaks outside the module.

```csharp
namespace Lifestyle.Modules.Catalog.Features.Products;

internal static class CreateProduct
{
    public sealed record Request(string Name, CategoryId CategoryId, Money BasePrice, IReadOnlyList<VariantInput> Variants);
    public sealed record Response(ProductId Id, string Slug);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
            RuleFor(r => r.BasePrice.Amount).GreaterThan(0);
            RuleFor(r => r.Variants).NotEmpty();
        }
    }

    internal sealed class Handler(ICatalogDbContext db, ITenantContext tenant, IClock clock)
        : IHandler<Request, Result<Response>>
    {
        public async Task<Result<Response>> Handle(Request req, CancellationToken ct)
        {
            var category = await db.Categories.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == req.CategoryId, ct);
            if (category is null) return Error.NotFound("catalog.category_not_found");

            var product = Product.Create(tenant.VendorId, req.Name, category, req.BasePrice, clock.UtcNow);
            foreach (var v in req.Variants) product.AddVariant(v.Sku, v.Attributes, v.Price);

            db.Products.Add(product);
            await db.SaveChangesAsync(ct);
            return new Response(product.Id, product.Slug);
        }
    }

    public static void Map(IEndpointRouteBuilder group) =>
        group.MapPost("/", async (Request req, Handler h, CancellationToken ct) =>
                (await h.Handle(req, ct)).ToCreated(r => $"/v1/vendor/products/{r.Id}"))
            .WithName("CreateProduct")
            .Validate<Request>()                 // endpoint filter running the FluentValidation validator
            .RequireIdempotency();               // only on endpoints that need it
}
```

Guidelines:

- A feature file over ~300 lines is a sign the use case should be split, or that domain logic
  belongs on the aggregate. Domain rules live on the entity (`product.Publish()` returns an `Error`
  if invariants fail), not in the handler.
- Reads that don't need the aggregate are plain EF projections in the handler — no separate "query
  service", no repositories.
- Response DTOs shared across several features of one resource go in `{Resource}Responses.cs` with
  static `From(entity)` mapping. No AutoMapper/Mapster.
- Handlers are resolved by concrete type (`Handler h` in the route lambda). There is no MediatR or
  pipeline-behaviour layer: logging is middleware, validation is an endpoint filter, transactions are
  `SaveChanges`. If cross-cutting handler behaviour is ever needed, a decorator on `IHandler<,>` is
  one class — we don't pre-pay for it.

### 3.4 Endpoint grouping

Endpoints are grouped by surface first (FRD §19.2) **inside the module that owns the data**:

| Group | Auth | Lives in |
|---|---|---|
| `/v1/catalog/*`, `/v1/storefronts/*` | Anonymous, tenant-scoped | Catalog, Vendors |
| `/v1/me/*` | Buyer | Identity (profile), Ordering (orders), Reviews |
| `/v1/vendor/*` | Vendor staff, scoped to caller's vendor | The owning module's per-resource folders |
| `/v1/admin/*` | Platform admin, permission-gated | The owning module's `Features/Admin/` |
| `/v1/webhooks/*` | Signature-verified | Payments, Shipping |

There is **no Admin module**. The "Admin & Reporting" box in Plan §5 is a UI surface, not a backend
module; reporting queries live in the module whose tables they read. This is the direct fix for
PropertyMart's 1000-line `SuperAdminController`.

### 3.5 Errors and results

- Handlers return `Result<T>`. `Error` is `(string Code, string Message, ErrorType Type)` where
  `ErrorType` ∈ `Validation | NotFound | Conflict | Forbidden | Unauthorized | Failure`.
- Error codes are stable, namespaced strings: `catalog.category_not_found`,
  `ordering.stock_unavailable`. The frontend switches on the code, never the message.
- `ToHttpResult()` / `ToCreated()` extensions in `Lifestyle.Api` map `ErrorType` to status and
  emit RFC 9457 `ProblemDetails` with `type = "https://{platform}/errors/{code}"`.
- Validation failures from the endpoint filter produce `ValidationProblemDetails` with field errors.
- Exceptions are for bugs and infrastructure failures only. The global exception handler returns a
  generic 500 with `traceId`, logs, and reports to Sentry. `DbUpdateException` unique-violation
  parsing (PropertyMart did this well) is kept as a last resort — handlers should check first.

### 3.6 Cross-module communication

Two mechanisms, no more:

1. **Synchronous contract call** — `ICatalogModule.GetVariantSnapshots(ids)`. Used when the caller
   needs an answer now (checkout needs prices). Returns DTOs, never entities. Implemented by a
   `{Module}Facade` class inside the module.
2. **Integration event via outbox** — `ProductPublished`, `OrderPaid`. Used when the caller does not
   need to wait (Notifications, search indexing, ledger posting). Raised from the aggregate as a
   domain event; the `SaveChanges` interceptor converts it to an outbox row in the same transaction;
   the dispatcher (§5) invokes every `IIntegrationEventHandler<T>` registered in any module.

No module injects another module's `DbContext` interface, handler, or entity. The architecture test
asserts that the only types used across module boundaries are in a `Contracts` namespace.

---

## 4. Persistence

### 4.1 One `AppDbContext`, one schema per module

`Lifestyle.Infrastructure/Persistence/AppDbContext.cs` is the single EF context. Each entity
configuration sets its schema: `builder.ToTable("products", "catalog")`. The context applies
configurations from every module assembly:

```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IIdentityDbContext, IVendorsDbContext, ICatalogDbContext /* , ... */
{
    public DbSet<Product> Products => Set<Product>();
    // one line per DbSet, grouped by module

    protected override void OnModelCreating(ModelBuilder b)
    {
        foreach (var asm in ModuleAssemblies.All) b.ApplyConfigurationsFromAssembly(asm);
        b.ApplySoftDeleteFilters();
    }
}
```

Why one context instead of FRD §2.1's per-module contexts:

- Checkout (Ordering + Inventory + Promotions + Payments) is one `SaveChanges` — no shared-connection
  transaction plumbing, nothing to get wrong.
- One migration history, one `dotnet ef migrations add`. Per-module migrations would be 14 folders
  and ordering problems whenever two modules change in one PR.
- It costs nothing we'll want later: when a module is extracted, its `I{Module}DbContext` interface
  becomes its own context with a one-line change, and its schema is already separate.

### 4.2 Module-scoped context interfaces

Each module declares what it can see:

```csharp
internal interface ICatalogDbContext
{
    DbSet<Product> Products { get; }
    DbSet<ProductVariant> Variants { get; }
    DbSet<Category> Categories { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

`AppDbContext` implements all of them; each is registered as
`services.AddScoped<ICatalogDbContext>(sp => sp.GetRequiredService<AppDbContext>())` by
`Infrastructure`. A Catalog handler therefore cannot query `orders` even by accident — the compiler
stops it. This replaces PropertyMart's one-interface-with-everything.

### 4.3 Cross-module references

- Another module's aggregate is referenced **by ID only** (`VendorId` on `Product`). No navigation
  properties across modules, ever — that is what keeps EF from coupling the graphs.
- A database FK constraint across schemas is **allowed** where it protects money or integrity
  (`ordering.order_items.variant_id → catalog.product_variants.id`) and avoided otherwise. FRD §2.1
  forbids them outright to keep extraction easy; we relax this because we are in one database and a
  dropped FK is a one-line migration when a module is extracted.
- Cross-schema **joins in queries are not allowed**; use the contract call. An architecture test
  can't catch this, so it is a code-review rule.

### 4.4 Conventions and migrations

FRD §3.1 stands: `snake_case` via EFCore.NamingConventions; `Guid.CreateVersion7()` IDs assigned
in the constructor; `Money` as owned type; `timestamptz`; `xmin` concurrency token on editable
aggregates; soft delete via `deleted_at` + global filter; audit columns via a
`SaveChangesInterceptor`.

Migrations live in `Lifestyle.Infrastructure/Persistence/Migrations/` and run with
`dotnet run --project src/Lifestyle.Api -- migrate` (the host checks `args[0]`, runs
`Database.Migrate()`, and exits). CI's migration-drift gate runs
`dotnet ef migrations has-pending-model-changes`. No migration runs on normal startup. Seed data for
local/dev runs with `-- seed`.

---

## 5. `Lifestyle.Infrastructure`

Everything that talks to something outside the process, plus the things that need to see every module:

```
Lifestyle.Infrastructure/
├── InfrastructureModule.cs        # AddInfrastructure(services, config)
├── Persistence/
│   ├── AppDbContext.cs
│   ├── Migrations/
│   ├── Interceptors/              # AuditInterceptor, DomainEventsToOutboxInterceptor
│   └── ModuleAssemblies.cs
├── Outbox/                        # OutboxMessage, OutboxDispatcher (BackgroundService, SKIP LOCKED), dead-letter
├── Storage/                       # IFileStorage → S3/MinIO; presigned upload URLs
├── Email/                         # IEmailSender → SMTP (Mailpit locally)
├── Sms/ · WhatsApp/               # added in Phase 3
├── Caching/                       # Redis: ICache, distributed rate-limit store
├── Identity/                      # JWT issuing/validation, ASP.NET Identity stores (used by the Identity module)
├── Seeding/                       # dev seeders, run via CLI switch
└── Time/                          # SystemClock : IClock
```

Scheduled jobs (reservation expiry, unpaid-order cancellation, payout runs) start as
`BackgroundService`s in the module that owns them. Hangfire is introduced in Phase 4 when the
payment-timer jobs arrive and a dashboard and retry semantics earn their keep — not before.

---

## 6. `Lifestyle.Api`

```
Lifestyle.Api/
├── Program.cs                     # ~40 lines: builder, Add*(), Use*(), Map*(), Run — or migrate/seed
├── appsettings.json · appsettings.Development.json
├── Composition/
│   ├── ServiceRegistration.cs     # AddLifestyle(): infrastructure + every module's Add*Module()
│   ├── EndpointRegistration.cs    # MapLifestyle(): every module's Map*Endpoints()
│   ├── AuthSetup.cs               # JWT bearer, audiences, permission policies
│   ├── OpenApiSetup.cs
│   └── ObservabilitySetup.cs      # Serilog, OpenTelemetry, Sentry, health checks
├── Middleware/
│   ├── TenantResolutionMiddleware.cs   # host → ITenantContext (FRD §5.2)
│   ├── CorrelationIdMiddleware.cs
│   └── GlobalExceptionHandler.cs       # IExceptionHandler → ProblemDetails
├── Filters/
│   ├── ValidationFilter.cs        # .Validate<T>()
│   └── IdempotencyFilter.cs       # .RequireIdempotency()
└── Http/
    ├── ResultExtensions.cs        # Result<T> → IResult / ProblemDetails
    └── EndpointExtensions.cs      # RequireVendorStaff(), RequirePermission()
```

Configuration: standard environment names only (`Development`, `Staging`, `Production`). Country
and currency are configuration — `Platform:Country = MY|BD|IT`, `Platform:Currency`,
`Platform:RootDomain` — bound to a validated `PlatformOptions` with `ValidateOnStart()`. One
container image, one env file per deployment.

---

## 7. Tests

| Project | Scope | Notes |
|---|---|---|
| `Lifestyle.UnitTests` | Domain invariants, `Money`, discount maths, state machines, validators, handlers against a mocked `I{Module}DbContext` | Folder per module mirrors `src/Modules` |
| `Lifestyle.IntegrationTests` | Endpoints via `WebApplicationFactory` against Testcontainers Postgres + Redis | Folder per module; one shared fixture; the FRD §26 "must exist" scenarios live here |
| `Lifestyle.ArchitectureTests` | The rules in §2.1 via NetArchTest | Fails the build on any violation |

Three projects, not three per module — test projects don't need boundaries, and one
`WebApplicationFactory` fixture shared across modules is faster.

---

## 8. Frontend

FRD §20.1 stands as written: one Angular workspace, `apps/storefront` (SSR), `apps/seller`,
`apps/admin`, and `libs/{ui,data-access,auth,i18n,util}`. One addition: `libs/data-access` is
**generated** from the API's OpenAPI document in CI (`ng-openapi-gen`), committed, and diffed — a
backend change that breaks the client fails the build, which is the contract test FRD §26 asks for.

---

## 9. Phase 0 scaffold checklist

In order; each item is a PR:

1. Solution, `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `.editorconfig`,
   `.gitignore` (logs, uploads, `bin/obj`, `.vs`, `*.user`), `docker-compose.yml`.
2. `SharedKernel` with the §3.2 types and unit tests for `Money` and `Result`.
3. `Infrastructure`: `AppDbContext`, naming convention, interceptors, outbox table, `migrate`/`seed`
   CLI switches, first migration.
4. `Api`: `Program.cs`, middleware order (FRD §1.4), `ProblemDetails`, validation + idempotency
   filters, Serilog, health checks, OpenAPI.
5. `Modules.Identity`: ASP.NET Identity, register/login/refresh rotation, audiences, permissions,
   `ICurrentUser`. First real feature files — they set the pattern everyone copies.
6. `Modules.Platform`: settings, audit log, feature flags (small; may fold into Infrastructure if
   it stays small).
7. `ArchitectureTests` green; CI with build, test, format, migration-drift, arch gates.
8. Angular workspace with three shells and the generated client.

Exit criterion is unchanged from Plan §6 Phase 0.

---

## 10. Rules of the road (pin to the README)

1. A new use case is a new file in `Features/{Resource}/`. Don't add a method to an existing one.
2. `internal` by default. `public` only in `Contracts/`.
3. Talk to another module through its `Contracts` or an integration event. Never its tables.
4. Domain rules go on the aggregate; handlers orchestrate.
5. Return `Result`, don't throw, for anything a client can cause.
6. No `Helpers/`, `Utils/`, `Common/`. Find the owner.
7. No `DateTime.UtcNow`, `Guid.NewGuid()`, or `HttpContext` outside `Api` and `Infrastructure`.
8. Every migration is reviewed as code and is reversible. Nothing runs migrations on startup.
9. Pin every package version. No `*`.
10. If a file passes 300 lines, split it before adding to it.

---

## 11. Revisions this document proposes to existing docs

| Doc | Section | Change |
|---|---|---|
| 00 | §5.1 | Replace the repository tree with §2 above; drop `tools/` and `Lifestyle.Migrator` |
| 02 | §1.3 | A module is one project with folders, not five projects |
| 02 | §2 | `Lifestyle.Migrator` → `Api migrate` switch; tests are three projects |
| 02 | §2.1 | One `AppDbContext` with schema per module; per-module context *interfaces*; cross-schema FKs allowed where they protect integrity |
| 02 | §16 | Search is a feature area inside Catalog behind `ISearchProvider` until an external engine is adopted |
| 02 | §21.2 | Hangfire deferred to Phase 4 |
| 02 | §4.2 | Access tokens carry one `perm` claim per permission rather than a bitmask reference. Compaction only pays off once the set is large, and it makes tokens far harder to debug — revisit when a token gets uncomfortable |
| 02 | §4.2 | JWTs are signed HS256 with a symmetric key, not RS256. Keeps local development and tests free of key material; switching to RS256 is a change to `AuthSetup` and `TokenService` only |
| 02 | §1.2 | Catalog depends on Media's contracts, not the reverse. Media owns files and knows nothing about products; Catalog stores media ids and asks for URLs. The FRD diagram has this arrow backwards |

---

## 12. What implementation changed, and why

Recorded during the Phase 0/1 build so the reasons survive.

| Decision | Reason |
|---|---|
| Vendor onboarding (`/v1/vendor-applications/*`) authenticates with an ordinary buyer token, not the seller surface | The seller role is granted **on approval**. Requiring it to upload KYC documents or submit an application made approval unreachable — a deadlock the end-to-end test caught. `/v1/vendor/*` stays seller-only for everything post-approval |
| No `Lifestyle.Modules.Inventory` yet | Stock lives on `ProductVariant` and is edited with the product. It becomes a module in Phase 4 when reservations arrive and stock stops being a simple integer |
| `Money` is not used on `Product`/`ProductVariant` | One deployment per country means one currency, held on the product as a `char(3)` alongside `numeric(18,4)` prices. `Money` earns its place in Ordering and Ledger, where several amounts combine |
| Modules never reference the Npgsql provider | Keeps `EF.Functions.ILike` and `HasOperators("text_pattern_ops")` out of module code. Postgres-specific *type names* in `HasColumnType` are fine — they are strings, not a compile dependency. The one operator-class index is applied as raw SQL in the migration |
| `text_pattern_ops` on `catalog.categories.path` | Without it PostgreSQL cannot use the index for `LIKE 'prefix%'` under a non-C collation, and the category-subtree scan on every browse request degrades to a sequential scan |
| Analyzer suppressions live in `.editorconfig` with a stated reason | `CA1716`/`CA1711`/`CA1000` fight names that are correct here; `CA1848`/`CA1873` push `IsEnabled` guards around cheap log arguments. Each is disabled with the reason written next to it, not silently |
| `TreatWarningsAsErrors` plus `NU1903` | The vulnerability gate caught two real transitive CVEs during Phase 0 — `Testcontainers → SSH.NET` and `Microsoft.AspNetCore.OpenApi → Microsoft.OpenApi`. Both are pinned to patched versions in `Directory.Packages.props` |
| All Guid primary keys are `ValueGenerated.Never` (`AppDbContext.ApplyApplicationGeneratedKeys`) | See §13 — without it, every "load aggregate, add child, save" path issues an UPDATE against a row that was never inserted |
| JWT validation is configured from `IdentityModuleOptions`, not from `IConfiguration` at registration time | See §13 — reading configuration eagerly let the signing key used to *validate* diverge from the one used to *sign* |

---

## 13. Bugs the live database found

Three defects survived a clean build, 130 green unit and architecture tests, and a reviewed
migration. All three were caught the moment the API ran against a real PostgreSQL. Recorded because
each is a class of mistake worth recognising again.

### 13.1 Children added to a loaded aggregate were UPDATEd, never INSERTed

**Symptom.** Every login returned 500 with `DbUpdateConcurrencyException: expected to affect 1
row(s), but actually affected 0`.

**Cause.** `Entity` assigns `Guid.CreateVersion7()` in a field initialiser, so a new child reaches
the change tracker with its key already set. EF's default for a Guid key is `ValueGenerated.OnAdd`,
and its graph attacher reads *store-generated key + value already present* as *this row already
exists* — tracking the child as `Modified` and emitting an UPDATE against a row that had never been
inserted.

**Blast radius.** Every load-then-add path: issuing a refresh token on login, attaching a KYC
document, adding a variant, image, or staff member. Creating a whole new aggregate worked, which is
why nothing looked wrong until a second request touched an existing one.

**Fix.** A model-wide convention declaring Guid primary keys `ValueGenerated.Never`, which is what
they are — the application generates them (§4.4).

### 13.2 Tokens were signed with one key and validated with another

**Symptom.** A freshly issued, valid token was rejected with a bare 401.

**Cause.** `AddAuthentication` read `Identity:JwtSigningKey` from `IConfiguration` at *registration*
time, while `TokenService` bound it lazily through `IdentityModuleOptions`. Any configuration source
added after the builder was constructed reached the lazy binding but not the eager read.

**Fix.** JWT validation is now configured from `IOptions<IdentityModuleOptions>`, so signing and
validation cannot diverge. The general rule: **bind options, do not read `IConfiguration` during
service registration.**

### 13.3 "Unique" test data collided across runs

**Symptom.** The suite passed once, then failed on every re-run against the same database.

**Cause.** Two of them. Test data used `Guid.CreateVersion7().ToString("N")[..8]` as a unique
suffix — but a v7's leading bits are the *millisecond timestamp*, whose top 32 bits change roughly
once a minute, so consecutive runs produced identical suffixes. Separately, the tests enrolled the
*seeded* admin in TOTP, which is a one-way change, so the "an admin without TOTP is blocked"
assertion could only ever pass once.

**Fix.** `LifestyleApiFactory.UniqueSuffix()` uses random (v4) bits, and the fixture mints its own
admin pair per run — one enrolled with a known secret, one deliberately not. The suite now passes
repeatedly against a long-lived database, which is what CI and a developer's local server both are.
