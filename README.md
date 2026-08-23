# Lifestyle

A multi-category, multi-vendor e-commerce marketplace — vendors sell through a shared marketplace
while keeping their own branded storefront on a subdomain (with optional custom domain).

Target markets: **Malaysia**, **Bangladesh**, **Italy** — one codebase, one deployment per country.
Docs 00–02 are currently written Malaysia-first; see
[doc 03 §13](docs/03-INFRASTRUCTURE-MULTI-REGION.md#13-consequences-for-the-existing-documents) for
the multi-country revisions they need.

**Stack:** .NET 10 · EF Core 10 (code-first) · PostgreSQL 17+ · Angular 20+

## Planning documents

| Doc | Contents |
|---|---|
| [00 — Master Project Plan](docs/00-PROJECT-PLAN.md) | Scope, architecture, tech stack, roadmap, risks, open decisions |
| [01 — Business Requirements (BRD)](docs/01-BRD.md) | Business model, personas, capabilities, business rules, compliance |
| [02 — Technical FRD](docs/02-TECHNICAL-FRD.md) | Architecture, data model, flows, APIs, security, NFRs |
| [03 — Infrastructure & Multi-Region](docs/03-INFRASTRUCTURE-MULTI-REGION.md) | Server baseline, Malaysia/Bangladesh/Italy deployment strategy, capacity, gaps |
| [infrastructure-mypropertymart/](docs/infrastructure-mypropertymart/) | Existing PropertyMart infrastructure (reference baseline) |

Status: **planning**. No code yet.
