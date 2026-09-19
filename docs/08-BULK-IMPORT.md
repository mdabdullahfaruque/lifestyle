# Lifestyle — Bulk Product & Image Import

| Field | Value |
|---|---|
| Document | 08 — Bulk Product & Image Import |
| Version | 1.0 |
| Date | 2026-09-19 |
| Status | **Steps 1–4 of §10 are built** (2026-09-19). Steps 5–9 are still specification — see §12. |
| Purpose | How a vendor gets a hundred products and a thousand images into the catalogue without doing it one form at a time |
| Supersedes | The single reserved line `POST /v1/vendor/products/import # CSV` in [02 §APIs](02-TECHNICAL-FRD.md) |

**Why this file exists.** Bulk import was Phase 1 scope, deferred to the first Phase 2 sprint
([README](../README.md) "Not done"). It is the largest single barrier to a vendor with an existing
catalogue moving onto the platform: the vendor admin has a perfectly good single-product editor
(`web/projects/seller/src/app/products/`), and a seller with 200 SKUs will still never use it.

This document fixes the seller-facing shape before any code is written, because the expensive
mistakes here are product mistakes, not technical ones.

---

## 1. The one thing to get right

A bulk importer fails when it asks the seller to do organising work **before** it shows them
anything. "Name your folders after your product codes, zip it, upload, and we'll tell you at the
end whether you got it right" is how every abandoned import flow works.

So the design principle is:

> **The seller's existing files are the input. The matcher does the organising. The UI only shows
> the exceptions.**

Everything below follows from that.

### 1.1 Two lanes, one pipeline

Sellers arrive with their files in one of two states, and both must work:

| Lane | Who | What they have |
|---|---|---|
| **Organised** | Has a supplier catalogue, a spreadsheet, files named by SKU | `LS-1001/main.jpg` or `LS-1001_2.jpg` |
| **Unorganised** | Photographs stock on a phone | 400 files called `IMG_20260612_142233.jpg` |

These are **not two features**. They are the same pipeline with a different amount of work done for
the seller up front:

```
files arrive ──▶ matcher assigns each image to a product ──▶ review grid ──▶ commit as drafts
                  confidence: matched │ guessed │ unmatched
```

The organised lane arrives 95% matched and the seller confirms. The unorganised lane arrives
0% matched, gets *suggested* groupings from capture-time clustering (§4.3), and the seller drags
the rest. Same screen. Same data. Same commit.

Build one screen.

---

## 2. The Image Bank

**Decision: the vendor image bank is the staging area for bulk import, and it is nearly free.**

The mechanism already exists and needs no schema change:

- [`MediaFile.VendorId`](../src/Modules/Lifestyle.Modules.Media/Domain/MediaFile.cs) is stamped at
  upload from `ICurrentUser.VendorId`
  ([UploadFile.cs:93](../src/Modules/Lifestyle.Modules.Media/Features/Uploads/UploadFile.cs#L93)).
- [`MediaOrphanSweeper`](../src/Lifestyle.Infrastructure/Media/MediaOrphanSweeper.cs#L62) only
  deletes rows where `OwnerType == null`.
- [`MediaFile.Attach(ownerType, ownerId, now)`](../src/Modules/Lifestyle.Modules.Media/Domain/MediaFile.cs#L74)
  already exists and is exposed through `IMediaModule.AttachAsync`.

So a banked image is one attached with a new owner type:

```csharp
await media.AttachAsync(ids, "vendor_library", vendorId, ct);
```

That is the whole feature. The image stops being an orphan, survives the hourly sweep, and is
listed with one indexed query on `VendorId`.

**Consequences to accept deliberately:**

- An image in the bank is owned by the *library*, not by a product. `ProductImage.MediaId` is a
  plain string with no FK
  ([ProductVariant.cs:100](../src/Modules/Lifestyle.Modules.Catalog/Domain/ProductVariant.cs#L100)),
  so one banked image can back many products today with no model change. That is wanted — sellers
  reuse the same size-chart and lifestyle shots across a range.
- Banked images never expire. They therefore need a **quota** (§8) or the disk fills, which
  [docs/03 §7.2](03-INFRASTRUCTURE-MULTI-REGION.md) already names as the constraint that bites
  first on this server.
- Deleting from the bank must check nothing references it, or a live product loses its image.
  `Product.RemoveImage` refuses to leave a published product with none
  ([Product.cs:181](../src/Modules/Lifestyle.Modules.Catalog/Domain/Product.cs#L181)); the bank
  delete must respect the same rule rather than route around it.

### 2.1 Endpoints

```
GET    /v1/vendor/media                  # the bank: filter by attached/unattached, paged
POST   /v1/vendor/media/bulk             # multipart, N files, returns per-file result
DELETE /v1/vendor/media/{mediaId}        # refuses if referenced by a product
```

`POST /v1/vendor/media/bulk` is the only new upload path. It reuses `UploadFile.Handler` per file
rather than reimplementing validation, and returns a per-file array so one bad file does not fail
the batch (§6.1).

---

## 3. The category template

**Decision: generate a per-category XLSX with real dropdowns. CSV is accepted but is not the
primary format.**

Everything needed is already modelled. [`AttributeSet`](../src/Modules/Lifestyle.Modules.Catalog/Domain/AttributeSet.cs)
gives, per category:

- `VariantAttributes` — which attributes multiply out into SKUs (colour, size), so which columns
  become `axis:` columns
- `ProductAttribute.IsRequired` — which columns to mark and validate
- `ProductAttribute.AllowedValues` — the legal values for a `Select` attribute, which become an
  **XLSX data-validation dropdown**

A dropdown eliminates the single largest class of import error before the file is ever uploaded.
This is why XLSX rather than CSV, and it is worth the dependency.

```
GET /v1/vendor/products/import/template?categoryId={id}&format=xlsx|csv
```

### 3.1 Column contract

One product may span several rows. Rows are joined by `product_code`; product-level columns are
read from the **first** row for that code and ignored on continuation rows. This is Shopify's
`Handle` model and sellers coming from Shopify will recognise it.

| Column | Level | Required | Notes / limit |
|---|---|---|---|
| `product_code` | product | ✅ | Join key **and** idempotency key. See §9 open decision D1 |
| `name` | product | ✅ | ≤ 300 |
| `category_slug` | product | ✅ | The category's slug, not a Guid. `Category` has no separate code |
| `brand` | product | | ≤ 150 |
| `short_description` | product | | ≤ 500 |
| `description` | product | | ≤ 20 000. Required before the product can be submitted |
| `sku` | variant | ✅ | ≤ 80, unique within the product |
| `price` | variant | ✅ | > 0 |
| `compare_at_price` | variant | | must exceed `price` or be blank |
| `stock` | variant | | ≥ 0, default 0 |
| `weight_grams` | variant | | |
| `barcode` | variant | | |
| `axis:<code>` | variant | per attribute set | One column per variant axis, e.g. `axis:colour` |
| `attr:<code>` | product | per attribute set | Non-axis attributes, e.g. `attr:material` |
| `image_urls` | product | | Pipe-separated. See §7.2 before implementing |

Limits are taken from the existing validator
([CreateProduct.cs:43](../src/Modules/Lifestyle.Modules.Catalog/Features/Products/CreateProduct.cs#L43)):
**max 200 variants and max 20 images per product**. The importer must enforce the same numbers, and
must do so by calling the same validator rather than restating them.

**There is no `currency` column.** `Product.Currency` comes from `CatalogOptions.Currency`, which is
a single value per deployment because there is one deployment per country
([CatalogOptions.cs:10](../src/Modules/Lifestyle.Modules.Catalog/Internal/CatalogOptions.cs#L10)).
Offering the seller a currency column would imply a choice that does not exist.

**There is no `status` column.** Every imported product lands as `Draft`. See §5.3.

### 3.2 Encoding — read this before touching CSV

Excel on Windows saves CSV as the system ANSI code page, which destroys Bangla text silently. The
seller sees `à¦¶à¦¾à¦¡à¦¼à¦¿` and blames the platform.

Therefore:

- The generated CSV template is written **UTF-8 with BOM**, so Excel round-trips Bangla correctly.
- On upload, detect the encoding rather than assuming UTF-8. A file with no BOM that fails strict
  UTF-8 decoding is retried as Windows-1252, and the seller is **told** which was used.
- Product names are compared with the same normalisation used elsewhere for Bangla — `ড়` and `য়`
  decompose under NFC and a raw `==` will report two identical names as different.

### 3.3 Column mapping

Sellers paste their supplier's spreadsheet, not ours. After parsing the header row, if the columns
do not match the template, show a mapping screen: their header on the left, our column on the
right, best-guess pre-selected. **Persist the mapping per vendor** so the second import skips the
step. This is WooCommerce's importer and it is the difference between "I'll reformat my sheet
later" and an import completed on the first visit.

---

## 4. The matcher

Given a set of files and a set of products, assign each file to a product, a position, and
optionally a variant. Every rule is applied **case- and separator-insensitively**: the match key is
the filename lowercased with all non-alphanumerics removed, so `LS-1001`, `ls_1001` and `LS 1001`
are one key.

### 4.1 Precedence

First rule that hits, wins.

| # | Rule | Confidence |
|---|---|---|
| 1 | ZIP **folder name** matches a `product_code` → every file inside belongs to that product | matched |
| 2 | Filename prefix, up to the first `_ - .`, matches a `product_code` | matched |
| 3 | Filename prefix matches a variant `sku` → attaches to that variant's product, flagged as a variant image | matched |
| 4 | Filename **suffix** matches a value of one of the product's variant axes (`LS-1001_red.jpg` where a variant has `colour=Red`) | matched |
| 5 | Capture-time clustering (§4.3) | **guessed** |
| 6 | Nothing | **unmatched** |

### 4.2 Ordering within a product

Position is decided by sorting on `(qualifier rank, numeric index, filename)` and then reindexing
from 0, so there is never an off-by-one argument about whether `_1` means position 0 or 1.

- Qualifier rank: `main` | `cover` | `front` = 0, everything else = 1
- Numeric index: trailing digits, `LS-1001_2.jpg` → 2
- Ties broken by filename, which is stable and matches what the seller sees in their file explorer

`Product.ReorderImages` already reindexes to a dense 0..n-1 sequence
([Product.cs:232](../src/Modules/Lifestyle.Modules.Catalog/Domain/Product.cs#L232)), so the
importer hands over an ordered list and lets the aggregate own the numbering.

### 4.3 Capture-time clustering — the unorganised lane

This is the rule that makes "just dump everything in" actually work, and no major platform does it.

Photographs of one product are taken seconds apart; moving to the next product takes longer.
So: sort by EXIF `DateTimeOriginal`, and start a new cluster where the gap exceeds a threshold
(**90 seconds**, tunable). Each cluster becomes one suggested product group.

Two constraints on this:

- EXIF must be read **from the original**, at ingest, before any stripping. Today the original is
  stored byte-for-byte with EXIF intact
  ([UploadFile.cs:88](../src/Modules/Lifestyle.Modules.Media/Features/Uploads/UploadFile.cs#L88))
  and only derivatives are stripped
  ([ImageSharpProcessor.cs:48](../src/Modules/Lifestyle.Modules.Media/Internal/ImageSharpProcessor.cs#L48)).
  If §7.3 is implemented, capture time must be persisted on `MediaFile` before the strip.
- Clusters are **suggestions**, never silently committed. They arrive in the grid pre-grouped and
  visibly marked as guesses.

Where EXIF is absent — screenshots, images already through a social app — fall back to upload order
and let the grid do the work.

---

## 5. The import job

### 5.1 State machine

```
Created ──▶ Analysing ──▶ NeedsReview ──▶ Committing ──▶ Completed
                │              │              │
                └──────────────┴──────────────┴──▶ Failed
                               │
                               └──▶ Cancelled │ Expired (after 72h)
```

- **Analysing** — parse sheet, unpack ZIP, generate derivatives, run the matcher. The slow part.
- **NeedsReview** — the grid. Nothing has been written to the catalogue yet.
- **Committing** — creates drafts and attaches images.
- **Expired** — a job left in `NeedsReview` for 72 hours is dropped and its staged images fall back
  to orphan status, where the existing sweeper collects them. Nothing new to build for cleanup.

### 5.2 Endpoints

```
POST   /v1/vendor/products/import              # sheet and/or zip → { jobId }
GET    /v1/vendor/products/import/{jobId}      # status, progress, per-row findings
PATCH  /v1/vendor/products/import/{jobId}      # the grid's corrections: reassign, reorder, drop
POST   /v1/vendor/products/import/{jobId}/commit
DELETE /v1/vendor/products/import/{jobId}      # cancel
GET    /v1/vendor/products/import/{jobId}/errors.xlsx   # errors-only, re-uploadable
```

Mapped on the existing `/v1/vendor/products` group, which already carries `RequireVendorStaff()`
([CatalogModule.cs:51](../src/Modules/Lifestyle.Modules.Catalog/CatalogModule.cs#L51)).
[02 §rate limits](02-TECHNICAL-FRD.md) already allots **5 imports per hour** per vendor.

Per rule 1 of [docs/04](04-CODEBASE-STRUCTURE.md), each of these is **its own file** under
`Features/Import/`, not methods on a shared service.

### 5.3 Partial success, and never publishing

Two rules that matter more than anything else in this section:

**An import is never all-or-nothing.** 200 rows with 3 bad ones import 197 products and hand back
an errors-only sheet containing those 3 with a reason column. The seller fixes and re-uploads that
file. Because `product_code` is the idempotency key, a re-upload **updates** rather than
duplicating. This one behaviour is what makes Amazon's flat-file import survivable at scale, and it
is the single most important thing to copy.

**An import can never publish.** Every product lands as `Draft`, and the existing lifecycle does
the rest: `SubmitForReview` already refuses a product with no variants, no images, or no
description ([Product.cs:262](../src/Modules/Lifestyle.Modules.Catalog/Domain/Product.cs#L262)),
and publishing still requires admin approval. The importer therefore needs **no completeness rules
of its own** — it must not restate them, and it must not offer a "publish on import" option, which
would route around moderation for exactly the bulk case moderation exists for.

---

## 6. Images at ingest

### 6.1 Per-file, not per-batch

A batch of 400 files will contain a corrupt one, a 40 MB one and a `.HEIC`. Each file gets its own
result, and the batch reports them. One bad file never fails the other 399.

### 6.2 Known gaps in what we accept today

| Gap | Where | Consequence |
|---|---|---|
| **HEIC not accepted** | `AllowedContentTypes` is jpeg/png/webp/pdf ([MediaOptions.cs:17](../src/Modules/Lifestyle.Modules.Media/Internal/MediaOptions.cs#L17)) | Every default-settings iPhone photo is rejected. This is not an edge case in Malaysia |
| **10 MB cap** | `MaxUploadBytes` ([MediaOptions.cs:9](../src/Modules/Lifestyle.Modules.Media/Internal/MediaOptions.cs#L9)) | A modern phone JPEG is 8–12 MB and lands right on the line |

**Recommendation: fix both in the browser, not the server.** Downscale client-side to a longest
edge of 2000 px and re-encode to JPEG before upload. That is larger than the `large` derivative
(1600 px, [MediaOptions.cs:25](../src/Modules/Lifestyle.Modules.Media/Internal/MediaOptions.cs#L25))
so nothing is lost, it takes a 400-file upload from gigabytes to a few hundred megabytes on a
Bangladeshi mobile connection, and it converts HEIC as a side effect because the browser can decode
what the server cannot. Server-side HEIC decoding would mean another native dependency in the
container for no gain.

### 6.3 Square white canvas — instead of background removal

Shopee's auto background removal was considered and is **not** in this spec. §8 has the numbers.

What actually makes a marketplace grid look professional is not cutouts, it is **consistency** —
one aspect ratio, one padding, one canvas colour. That is achievable with the ImageSharp already in
the project: pad each derivative to a square white canvas with a fixed margin instead of preserving
the source aspect. Near-zero cost, no model, no new container, and it fixes the actual visual
problem, which is a grid where every tile is a different shape.

If literal background removal is wanted later, do it **client-side in WASM** as an opt-in per-image
button. Never on this server.

### 6.4 Deduplication

Hash file content on upload and reuse the existing `MediaFile` when a vendor uploads the same bytes
twice. Sellers reuse the same size chart across a whole range, and on a shared server disk is the
first thing to run out.

---

## 7. Security

Three of these are new attack surface that does not exist anywhere in the codebase today. This
section is the reason bulk import needs a `security-review` pass before merge, not after.

### 7.1 ZIP

- **Zip slip.** An entry named `../../etc/cron.d/x` must be rejected. Resolve every entry path and
  confirm it stays under the extraction root — never trust the entry name.
- **Zip bomb.** Cap total uncompressed size, entry count, and the compression ratio of any single
  entry. Enforce while streaming, not after extraction.
- Never extract to a path derived from a seller-supplied string.

### 7.2 `image_urls` is server-side request forgery

Fetching a seller-supplied URL from the API container means the seller chooses what our server
connects to — including `169.254.169.254` and anything else on the host network, which on this box
means **four other products' containers**.

If implemented: `http`/`https` only; resolve the hostname and reject private, loopback,
link-local and multicast ranges **before** connecting; re-validate after every redirect; cap size
and time; sniff the content rather than trusting `Content-Type`.

Given the cost and that the image bank (§2) solves the same seller problem without it, `image_urls`
is the strongest candidate to drop from scope.

### 7.3 Originals carry GPS today

Worth recording because it is not specific to import but bulk import multiplies it:

EXIF is stripped from **derivatives** ([ImageSharpProcessor.cs:48](../src/Modules/Lifestyle.Modules.Media/Internal/ImageSharpProcessor.cs#L48)),
but the **original is stored raw** ([UploadFile.cs:88](../src/Modules/Lifestyle.Modules.Media/Features/Uploads/UploadFile.cs#L88))
and `ToAsset` publishes the original's URL as `MediaAsset.Url`
([UploadFile.cs:175](../src/Modules/Lifestyle.Modules.Media/Features/Uploads/UploadFile.cs#L175)).
`ResizeAsync` also returns `null` without producing a derivative when the source is already smaller
than the target ([ImageSharpProcessor.cs:37](../src/Modules/Lifestyle.Modules.Media/Internal/ImageSharpProcessor.cs#L37)),
so a small image has no stripped copy at all.

A home-based seller photographing stock in their house is publishing their home coordinates. The
[README](../README.md) currently lists "EXIF stripping" as done, which overstates it.

Fix: strip and auto-orient the original at upload, and persist `DateTimeOriginal` onto `MediaFile`
first so §4.3 clustering still has it. Small change, and it should not wait for this feature.

---

## 8. Capacity — what this costs on a shared box

The server is shared with PropertyMart `bd-prod`/`bd-staging`/`my-prod` and SasthoSeba staging
([docs/07 §1](07-DEPLOYMENT-DEVIATIONS.md)). RAM is the scarce resource, so every proposal here
carries a number.

| Component | Cost | Verdict |
|---|---|---|
| Image bank | 0 — a query and an `OwnerType` | ✅ |
| XLSX template + parse | tens of MB, transient, streamed | ✅ |
| Matcher | negligible, string work | ✅ |
| Derivative generation during import | **the real cost** — see below | ⚠️ bound it |
| Background removal, server ONNX/U²-Net | est. **500–700 MB resident**, session kept warm | ❌ |
| Background removal, external API | 0 RAM, est. $0.10–0.20/image — a 400-image import is a real bill | ❌ for v1 |
| Background removal, client WASM | 0 server RAM, ~40 MB model download per seller | later, opt-in |

**The derivative bound is the one hard requirement.** ImageSharp decodes an entire image into
uncompressed memory: a 12 MP photo is roughly 48 MB as raw pixels, and
[`GenerateDerivativesAsync`](../src/Modules/Lifestyle.Modules.Media/Features/Uploads/UploadFile.cs#L106)
opens the source once per variant across three variants. Four hundred of those processed
concurrently does not degrade the server, it OOMs it, and it takes four other products down with
it.

Therefore:

- Import processes images through a **bounded queue, concurrency 2–3**, not `Task.WhenAll`.
- **One import job at a time per instance**; a second vendor's job queues.
- Budget roughly **150–250 MB** steady for the import worker and verify it against `docker stats`
  on the real host before enabling the feature in `bd-prod`.

These are estimates from typical ImageSharp and ONNX footprints, **not measurements on our server**.
Measure before committing to them.

---

## 9. Decisions

All four were settled on 2026-09-19, before implementation began.

**D1 — Where does `product_code` live? → `Product.VendorProductCode`.** ✅ *Decided.*

There was no such concept in the domain. `Product` has `Slug`, unique per vendor, derived from the
name and used in storefront URLs. Reusing `Slug` as the import key would be free but would make the
public URL `/p/ls-1001` instead of `/p/red-silk-scarf` — worse for exactly the organic search the
storefront's SSR exists to serve, so it was rejected.

Added instead: `Product.VendorProductCode`, nullable, unique per vendor when present. Costs one
generated migration and gives the importer a stable idempotency key that is the seller's own code —
which is what they search their own catalogue by anyway.

**D2 — Variant SKUs are not unique per vendor. → Ambiguous SKU means unmatched.** ✅ *Decided.*

`AddVariant` only checks for duplicates *within one product*
([Product.cs:94](../src/Modules/Lifestyle.Modules.Catalog/Domain/Product.cs#L94)), so matcher rule 3
(filename → SKU) can be ambiguous across products. Rather than force vendor-wide SKU uniqueness with
a migration — which would reject catalogues that are legal today — a SKU matching more than one
product is treated as **unmatched** and sent to the grid. Cheaper, breaks no existing data, and
loses only a rare auto-match.

**D3 — XLSX library. → ClosedXML.** ✅ *Decided.*

MIT licensed with no revenue threshold, and writes the data-validation dropdowns that §3 argues are
most of the template's value. EPPlus was rejected on its post-v5 commercial licence; this repo
already carries one licence caveat with ImageSharp
([ImageSharpProcessor.cs:10](../src/Modules/Lifestyle.Modules.Media/Internal/ImageSharpProcessor.cs#L10))
and does not need a second.

**D4 — Does the seller ever import to an existing product? → Yes, but never silently to a live
one.** ✅ *Decided.*

`product_code` is the idempotency key, so a re-upload updates rather than duplicating (§5.3) — that
is the whole point of the errors-only round trip. But `UpdateDetails` returns a published product to
review on a material edit ([Product.cs:81](../src/Modules/Lifestyle.Modules.Catalog/Domain/Product.cs#L81)),
so a careless bulk price update would take a whole catalogue off the storefront until a moderator
cleared it.

The importer therefore splits the two cases: a matching product in `Draft`, `Rejected` or
`Unpublished` is updated in place, while a matching product that is `Published` or `PendingReview`
is reported in the review grid as **"will return to moderation"** and is only updated if the seller
confirms. Silence is never taken as consent for a live product.

---

## 10. Build order

Each step is independently useful. Do not build 3–7 before 1 and 2 are in a seller's hands.

| # | Step | Depends on | Value alone |
|---|---|---|---|
| 1 | **Image bank** — `"vendor_library"` attach, `GET /v1/vendor/media`, grid | — | High. Useful without any import |
| 2 | **Category XLSX template** | D3 | High. Useful without any import |
| 3 | **Review grid + matcher rules 1–4** | 1 | The core |
| 4 | **Sheet parse + draft creation + errors-only re-upload** | 2, D1 | The core |
| 5 | **ZIP ingest** with §7.1 guards | 3 | Unlocks the organised lane |
| 6 | **Capture-time clustering** | 3, §7.3 fix | Unlocks the unorganised lane |
| 7 | **Square white canvas** derivatives | — | Independent; helps every product |
| 8 | Client-side downscale + HEIC (§6.2) | — | Independent; do early, it is small |
| 9 | `image_urls` — only if §7.2 is fully implemented | 4 | Weakest item here |

§7.3 (strip EXIF from originals) is not in this table because it should not wait for this feature.

---

## 11. Not in scope

Recorded so they are decisions rather than omissions:

- **Server-side background removal.** §8.
- **Publishing on import.** §5.3.
- **Cross-vendor or admin-run import.** A super admin importing on a vendor's behalf is a different
  authorisation story and a different audit story.
- **Scheduled or API-key-driven import** (a supplier feed polled nightly). Wanted eventually by any
  vendor with real ERP, and a different feature.
- **Bulk *edit*** — a spreadsheet view for changing prices across an existing catalogue. Adjacent
  and frequently confused with import; Shopify treats them as separate tools and so should we.

---

## 12. Implementation status — 2026-09-19

Steps 1–4 of §10 are built, compiled and unit-tested. Steps 5–9 are untouched specification.

### Built

| Area | Where |
|---|---|
| Image library | `Modules.Media/Features/Library/` — `VendorLibrary.cs`, `BulkUploadToLibrary.cs` |
| Owner-type constants | `Modules.Media/Contracts/IMediaModule.cs` — `MediaOwnerTypes` |
| `VendorProductCode` (D1) | `Catalog/Domain/Product.cs`, migration `VendorProductCode` |
| Category template | `Catalog/Internal/ImportSchema.cs`, `ImportTemplateWriter.cs` |
| Sheet reading | `Catalog/Internal/SheetReader.cs` — XLSX, CSV, encoding detection |
| Row parsing | `Catalog/Internal/ImportRowParser.cs` |
| Image matcher | `Catalog/Internal/ImageMatcher.cs` — rules 1–4 |
| Job aggregate | `Catalog/Domain/ImportJob.cs`, migration `BulkImportJobs` |
| Endpoints | `Catalog/Features/Import/` — template, start, get, revise, cancel, commit, errors |
| Seller UI | `web/projects/seller/src/app/media/` and `.../products/import/` |
| API client | `web/projects/data-access/src/lib/import.service.ts` |

### Decisions taken during implementation

**The library is defined by `VendorId`, not by owner type.** An image stays listed after it is
attached to a product; `MediaOwnerTypes.VendorLibrary` exists only to stop an unused upload being
swept as an orphan. The "not on a product yet" filter is therefore `OwnerType == vendor_library`.

**Deleting from the library needs no call into Catalog.** The dependency graph only runs
Catalog → Media, so Media cannot ask Catalog whether an image is in use — but it does not need to,
because it owns `OwnerType`. The matching half is that `SetProductImages` now hands dropped images
**back** to the library, so an image removed from a product does not stay permanently undeletable.

**`category_code` became `category_slug`.** `Category` has `Slug` and no separate code field.

**Matcher rules 2 and 3 were merged into one longest-leading-run lookup.** Cutting a filename at
its first separator looks up `LS` for `LS-1001_main.jpg` and matches nothing — product codes
contain separators themselves. Codes and SKUs now compete in one index and the longest match wins,
so `LS1001-RED-M.jpg` resolves to the SKU rather than being shadowed by the shorter product code.

**Prices parse in every market's notation.** Italian Excel writes `1.234,56` and Malaysian Excel
writes `1,234.56`; the separator that appears last is treated as the decimal point. Getting this
wrong prices a dress at 123456.

### Not built — the rest of §10

Steps 5 (ZIP ingest), 6 (capture-time clustering), 7 (square white canvas), 8 (client-side
downscale and HEIC) and 9 (`image_urls`) are unstarted. The matcher already handles folder paths
(rule 1), so step 5 is mostly ZIP extraction plus the §7.1 guards rather than new matching logic.

### Not verified

- **No integration tests and no run against a live database.** Docker is not installed on the
  development machine, so the Testcontainers suite could not run. Everything below the unit tests
  — the EF configurations, both migrations, the jsonb value comparers, the filtered unique index
  on `vendor_product_code`, and every endpoint end to end — is **compiled but unexercised**.
  [docs/04 §13](04-CODEBASE-STRUCTURE.md) records three defects that a clean build and green unit
  tests still missed, all found by running against a real database. Assume the same risk here.
- The seller UI has been built by the Angular compiler but never opened in a browser.
