# Business Requirements Document (BRD)
## Lifestyle — Multi-Vendor Marketplace

> **Scope note (2026-08-23).** This document was drafted Malaysia-first. The project now targets
> **Bangladesh (primary), Malaysia, and one client shop in Italy**, and **v1 launches without a
> checkout** — selling happens over WhatsApp (§4.3, §6.0). Malaysia-specific rules below (SST, FPX,
> West/East zones) are correct for the Malaysian deployment and are the template for the others;
> doc 03 §13 lists the per-country revisions still outstanding.

| Field | Value |
|---|---|
| Document | 01 — Business Requirements Document |
| Version | 0.1 (Draft) |
| Date | 2026-08-23 |
| Status | Draft — pending stakeholder review |
| Related | [00 — Master Project Plan](00-PROJECT-PLAN.md) · [02 — Technical FRD](02-TECHNICAL-FRD.md) |

### Revision history

| Ver | Date | Author | Change |
|---|---|---|---|
| 0.1 | 2026-08-23 | Product | Initial draft |

### Approvals

| Role | Name | Signature | Date |
|---|---|---|---|
| Business owner | | | |
| Product lead | | | |
| Technical lead | | | |
| Finance | | | |

---

## 1. Executive summary

Lifestyle gives independent sellers **a shop of their own** — a public storefront on their own
subdomain or custom domain, which to their customers looks and behaves like the shop's own website —
backed by a shared catalogue platform, and later a shared marketplace, payments and logistics.

Primary market is **Bangladesh**, then **Malaysia**, plus **one client shop in Italy**.

**It arrives in two steps, and the first one has no checkout.** In v1 a buyer browses the storefront
and taps through to **WhatsApp or Messenger** to complete the purchase with the shop owner directly —
which is how commerce already works for these sellers and their customers. v2 adds cart, checkout,
payments and the marketplace layer on top, without taking the conversational path away.

The commercial model follows that split: **subscription per shop in v1** (commission is not
collectable when the order happens in a chat app — §4.3), moving to a **take rate** in v2. The
platform never holds inventory.

The differentiator against Shopee and Lazada is not price or logistics scale — we cannot win there.
It is **vendor brand ownership**: a real storefront the vendor can point their own domain and social
traffic at, combined with a curated lifestyle category focus rather than an everything-store.

Launch catalogue is deliberately narrow: **ladies' dresses, ladies' bags, shoes (ladies and men), and
mobile accessories**.

---

## 2. Business objectives

| # | Objective | Measure | Target (12 months post-launch) |
|---|---|---|---|
| BO-1 | Build a liquid two-sided marketplace | Active vendors with ≥1 sale/month | 300 |
| BO-2 | Achieve meaningful transaction volume | Monthly GMV | RM 1.5M |
| BO-3 | Generate sustainable revenue | Monthly net commission revenue | RM 90k (at a ~6% blended take rate) |
| BO-4 | Become the preferred channel for small fashion sellers | Vendors listing us as primary channel | 20% of active vendors |
| BO-5 | Earn buyer trust | 90-day repeat purchase rate | ≥ 25% |
| BO-6 | Deliver vendor brand value | Storefronts with a connected custom domain | 50 |
| BO-7 | Operate efficiently | Support tickets per 100 orders | < 4 |

### 2.1 Supporting KPIs

**Marketplace:** GMV, order count, AOV, take rate, refund rate, cancellation rate.
**Buyer:** visitors, conversion rate, add-to-cart rate, checkout abandonment, CAC, LTV, NPS.
**Vendor:** signups, approval rate, time-to-first-listing, time-to-first-sale, listing count,
churn, seller rating, on-time-ship rate.
**Category:** GMV mix, sell-through, return rate by category (shoes and dresses run high — sizing).
**Operations:** dispute rate, dispute resolution time, payout accuracy, listing moderation SLA.

---

## 3. Market context

| Competitor | Position | Where we differ |
|---|---|---|
| **Shopee MY** | Dominant; price-led, heavy vouchers, in-app entertainment | We cannot outspend them. We offer vendors a branded, ownable storefront and lower fee pressure |
| **Lazada MY** | Strong brand/official-store focus, LazMall | We serve small and mid sellers who cannot get LazMall status |
| **TikTok Shop MY** | Social commerce, live selling, very strong for fashion | Complementary — vendors can drive TikTok traffic to their own Lifestyle storefront |
| **Zalora** | Curated fashion, first-party + partner | Narrower and more premium; we are open to small sellers |
| **Independent Shopify/WooCommerce stores** | Full brand control, but no traffic and full ops burden | We give brand control *plus* marketplace traffic and shared logistics/payments |

**Positioning statement.** *For small and mid-sized Malaysian fashion and accessory sellers who want
a real brand presence but cannot afford to build and market their own store, Lifestyle is a
marketplace that gives them a branded storefront on their own domain, backed by shared traffic,
payments and logistics.*

This positioning has a direct product consequence: **the vendor storefront is not a secondary
feature — it is the reason vendors choose us.** It gets first-class investment.

---

## 4. Business model

### 4.1 Revenue streams

> **Read §4.3 first.** v1 sells through WhatsApp and has no checkout, so **commission is not
> collectable at launch**. The table below describes the model from v2 (checkout) onwards; v1 runs on
> RS-4 alone.

| # | Stream | MVP? | Description |
|---|---|---|---|
| RS-1 | **Commission on completed orders** | **v2** | Percentage of item subtotal, per-category rate. Charged on completion, not on order placement. Requires checkout — see §4.3 |
| RS-2 | **Payment processing fee pass-through** | Yes | Gateway cost recovered from the vendor, shown transparently |
| RS-3 | **Shipping margin** | Optional | Difference between the aggregator rate and the buyer-facing rate. Recommended: zero margin at launch, for trust |
| RS-4 | **Shop subscription / setup fee** | **v1 — the only v1 revenue** | Monthly (and/or one-off setup) fee per shop for the storefront, catalogue and ordering tools. See §4.3 |
| RS-5 | Promoted listings and ads | Post-launch | CPC placement in search and category pages |
| RS-6 | Campaign participation fees | Post-launch | Paid slots in flagship campaigns |
| RS-7 | Value-added services | Post-launch | Photography, content, fulfilment assistance |

### 4.2 Commission structure (proposal — **needs your sign-off**)

| Category | Proposed rate | Rationale |
|---|---|---|
| Ladies' dresses | 8% | High margin, high return rate |
| Ladies' bags | 8% | High margin |
| Shoes (ladies/men) | 7% | High return rate, higher shipping cost |
| Mobile accessories | 5% | Low margin, high volume, price-transparent |
| *Default for new categories* | 6% | |

**Launch incentive:** 0% commission for the first 3 months per vendor, or until their first RM 5,000
GMV, whichever comes first. This is the standard lever for solving the cold-start problem (R2 in the
project plan) and should be budgeted for.

Commission is calculated on the **item subtotal after vendor-funded discounts and before shipping and
platform-funded vouchers** — precise formula in §9.4.

### 4.3 v1 revenue — commission does not work when the order happens in WhatsApp

**This needs a decision before v1 launches, and it is easy to miss.**

In v1 a buyer taps through from the storefront into WhatsApp and the sale is closed in chat
(Project Plan §6.2). The platform sees that someone *clicked*. It does not see whether a sale
happened, at what price, or for how many units — and it cannot verify any of it.

**So commission is not collectable in v1.** Not "hard to collect" — genuinely unavailable, because
there is no observed transaction to take a percentage of. Any attempt to charge commission on
self-reported sales would be unenforceable and would give vendors an obvious incentive to
under-report, which also poisons our own analytics.

**v1 must therefore be sold as a subscription**, not a take rate. The product being sold is the
storefront: a professional shop site on the vendor's own domain, a managed catalogue, product pages
that look credible, and a link they can put in their Instagram or Facebook bio. That is a real
product with a real price — it is roughly what Shopify sells — and it happens to be exactly what our
target vendors (P2, P3 in §5.2) already want.

| | **v1 — conversational** | **v2 — checkout** |
|---|---|---|
| Revenue model | Subscription and/or setup fee per shop | Commission on completed orders |
| What the vendor buys | A storefront and catalogue | Storefront + payments + buyer trust + tracked orders |
| Platform sees the order | No | Yes |
| Pricing pressure | Compared against Shopify, and against free (Instagram) | Compared against Shopee and Lazada commission rates |

**Consequences to plan for:**

1. **Set the v1 price before launch**, not after. Retrofitting a fee onto vendors who onboarded free
   is much harder than starting with one — and free-at-launch is still a valid choice, as long as it
   is a deliberate acquisition decision with an end date.
2. **v2 pricing must be a real upgrade, not a penalty.** A vendor moving to checkout should not feel
   they are being charged twice. Either commission replaces the subscription, or the subscription
   drops when commission starts.
3. **Some vendors will never move to checkout** (risk R15). Price v1 so the business works even if a
   meaningful share stays conversational forever.
4. **BO-3 in §2 assumes commission revenue.** It applies from v2. v1 needs its own target — shops
   onboarded and paying — which the business should set.

### 4.4 Cost structure to plan for

Payment gateway fees (FPX per-transaction flat fee; card and e-wallet percentage rates), courier
costs, platform-funded voucher and free-shipping subsidies, infrastructure, customer support,
marketing/CAC, chargebacks and fraud losses, and content/moderation labour.

---

## 5. Stakeholders and personas

### 5.1 Internal stakeholders

| Stakeholder | Interest |
|---|---|
| Business owner / sponsor | ROI, growth, launch date |
| Product | Scope, prioritisation, vendor and buyer satisfaction |
| Engineering | Feasibility, maintainability, technical debt |
| Operations / CS | Dispute handling, order issues, workload |
| Finance | Settlement accuracy, reconciliation, tax compliance |
| Marketing | Acquisition, campaigns, SEO, content |
| Legal / compliance | PDPA, consumer protection, vendor agreements |

### 5.2 Personas

**P1 — Aisha, the shopper (28, Klang Valley, mobile-first).**
Browses on her phone during commutes. Compares prices across Shopee and Instagram. Cares about
photo quality, real reviews, size accuracy and shipping cost. Pays by FPX or Touch 'n Go.
*Needs:* trustworthy sizing info, honest reviews, clear total cost before checkout, easy returns.
*Frustrations:* surprise shipping fees, sellers who never ship, items that do not match photos.

**P2 — Farah, the small vendor (32, home-based boutique, ~40 SKUs).**
Currently sells on Instagram and WhatsApp, manually. Wants to look professional and stop taking
orders in DMs. Not technical.
*Needs:* dead-simple listing (photos from her phone), automatic order and payment handling, a
storefront link she can put in her Instagram bio, fast payouts.
*Frustrations:* complex seller dashboards, delayed payouts, high commission, no control over branding.

**P3 — Kumar, the established vendor (41, 500+ SKUs, small team, mobile accessories).**
Already sells on Shopee and Lazada. Wants an additional channel without doubling his workload.
*Needs:* bulk CSV import, stock sync, staff sub-accounts with limited permissions, order export,
a custom domain, real analytics.
*Frustrations:* re-entering the same catalogue on every platform, stock overselling across channels.

**P4 — Nurul, platform operations (super admin).**
Approves vendors, moderates listings, handles disputes, runs campaigns.
*Needs:* a work queue rather than a search interface, bulk actions, full audit trail, the ability to
act on behalf of a vendor for support, clear escalation.
*Frustrations:* having to query the database directly for answers the admin panel should provide.

**P5 — Wei Ming, finance.**
*Needs:* daily settlement reports, payout runs that reconcile to the bank statement to the cent,
commission reports, tax-ready invoice records, refund tracking.
*Frustrations:* any number that cannot be traced back to individual transactions.

---

## 6. Scope

### 6.0 v1 versus v2 — what actually ships first

The scope table in §6.1 describes the **complete** product. It is delivered in two launches
(Project Plan §6.2):

| | **v1 — conversational** (~week 22) | **v2 — full marketplace** (~week 44) |
|---|---|---|
| Catalogue, variants, media, search | ✔ | ✔ |
| Vendor storefronts, custom domains | ✔ | ✔ |
| Vendor onboarding and admin | ✔ | ✔ |
| Super admin, moderation | ✔ | ✔ |
| **Ordering** | **WhatsApp / Messenger deep link** | Cart + checkout |
| **Payment** | Off-platform, seller-arranged | Gateway, escrow |
| **Stock** | Vendor updates manually after each sale | Automatic on order |
| **Shipping** | Vendor's policy text; arranged in chat | Rate tables (Stage 1) |
| Ledger, payouts, commission | ✗ | ✔ |
| Returns, disputes, reviews | ✗ | ✔ |
| Promotions, vouchers | ✗ | ✔ |
| **Revenue** | Subscription per shop (§4.3) | Commission |

Everything built for v1 is kept. Phase 4 adds checkout **alongside** conversational ordering, toggled
per vendor — some sellers and categories will keep converting better in chat.

### 6.1 In scope for MVP (public launch)

| Area | Included |
|---|---|
| **Buyer accounts** | Register (email/phone), login, social login, profile, multiple addresses, order history, wishlist, followed vendors |
| **Catalog** | Hierarchical categories, per-category attributes, products with variants (SKUs), images and video, stock levels, search, faceted filters |
| **Marketplace UI** | Home with promotional sections, category browse, search results, product detail, vendor card |
| **Vendor storefront** | Subdomain storefront with logo, banner, about, policies, vendor-scoped catalog and search, follow button, contact/chat |
| **Custom domain** | Verification, TLS, redirect to the subdomain (§10.4) |
| **Vendor onboarding** | Self-registration, business/KYC document upload, admin approval workflow, vendor agreement acceptance |
| **Vendor Admin** | Dashboard, product management, bulk import, inventory, orders, shipping rate configuration, promotions, storefront customisation, payouts, staff accounts, chat. *Label printing and pickup booking arrive with Shipping Stage 2* |
| **Super Admin** | Vendor approval and management, category/attribute management, product moderation, order oversight, refunds/disputes, platform promotions, banners/homepage, users and roles, reports, settings, audit log |
| **Cart & checkout** | Multi-vendor cart, per-vendor shipping selection, vouchers, address book, order review, guest checkout |
| **Payments** | FPX online banking, debit/credit cards, major e-wallets; escrow-style hold |
| **Orders** | Split by vendor, full state machine, cancellation, tracking, buyer confirmation |
| **Shipping** *(staged — see §6.6)* | **Stage 1, at launch:** vendor-configured rates per zone, vendor marks as shipped with courier + tracking number, buyer-facing tracking deep link. **Stage 2, post-launch:** aggregator integration, live rates, airway bill generation, pickup booking, automated tracking |
| **Returns & refunds** | Buyer-initiated return request, vendor response, admin arbitration, full and partial refunds |
| **Promotions** | Vendor vouchers, platform vouchers, flash sales, free-shipping thresholds, category campaigns |
| **Reviews** | Verified-purchase ratings and reviews with photos, vendor replies, vendor rating |
| **Messaging** | Buyer ↔ vendor chat tied to orders and products |
| **Notifications** | Transactional email, SMS for critical events, in-app notification centre |
| **Settlement** | Ledger, escrow, vendor wallet, scheduled payouts, statements |
| **Compliance** | PDPA consent and data-subject requests, SST handling, invoice records |
| **Localisation** | English at launch; the framework supports BM and Chinese; MYR only |

### 6.2 Explicitly out of scope for MVP

Native mobile apps · live streaming/live selling · affiliate programme · loyalty points or coins ·
platform-operated advertising · multi-currency and cross-border selling · multi-warehouse ·
subscription/recurring orders · B2B wholesale pricing · vendor-to-vendor dropshipping ·
AI recommendations (rules-based merchandising only at launch) · marketplace API for third parties ·
POS or offline integration · automatic stock sync with Shopee/Lazada (CSV import only).

Each of these is a real request that will come up. They are deferred deliberately, not overlooked.

### 6.3 Assumptions

1. Vendors are Malaysian-registered businesses or individuals with a Malaysian bank account.
2. All prices are in MYR; all buyers ship to Malaysian addresses.
3. Vendors fulfil their own orders; the platform provides no warehousing at launch.
4. The platform is the merchant of record for payment collection and holds funds in escrow before
   settling to vendors. **This must be confirmed with legal and the payment gateway** — it determines
   the gateway product needed (a marketplace/split-payment product rather than a standard one).
5. Buyers pay shipping unless a promotion covers it.
6. Support is human-staffed during business hours, with self-service outside them.
7. At launch, vendors arrange courier collection through their own existing courier accounts,
   off-platform (§6.6). Our target vendors already do this today, so this is not a new burden.

### 6.4 Constraints

1. Fixed technology stack: .NET 10, EF Core code-first, Angular, PostgreSQL.
2. Self-hosted on the client's own server (details pending).
3. Platform domain not yet acquired; pattern `{something}lifestyle.com`.
4. Must comply with Malaysian PDPA, consumer protection and e-invoicing rules.
5. Budget and team size to be confirmed; the roadmap assumes the team in Project Plan §7.

### 6.5 Dependencies

**For launch:** payment gateway merchant approval · SSM business registration and a corporate bank
account · SMS and email delivery providers · domain and DNS provider with API access · legal review
of vendor agreement, buyer T&Cs and privacy policy · brand identity assets.

**For Shipping Stage 2 (post-launch):** courier aggregator contract or direct courier accounts.
Deferring this takes a commercial negotiation off the launch critical path, and we will be
negotiating rates with real volume data rather than projections.

### 6.6 Staged delivery: Shipping & Delivery module

The Shipping & Delivery module is **deferred but committed** — scheduled as Phase 6 in the project
plan, not parked in the backlog. It is built in two stages.

| | **Stage 1 — Manual** (at launch) | **Stage 2 — Integrated** (Phase 6) |
|---|---|---|
| Rate calculation | Vendor sets flat or weight-tiered rates per zone in Vendor Admin | Live rates pulled from a courier aggregator |
| Courier booking | Vendor books with their own courier, off-platform | Pickup requested from Vendor Admin |
| Shipping label | Vendor uses the courier's own system | Airway bill generated and printed in-app |
| Tracking | Vendor enters courier + tracking number; buyer gets a deep link to the courier's site | Tracking events arrive automatically by webhook |
| Delivery confirmation | Buyer confirms receipt, or a dispatch-based timer fires | Courier-confirmed delivery event |
| Cost reconciliation | Vendor's own concern | Quoted vs actual courier charge reconciled by the platform |

**Why we can launch on Stage 1.** Checkout needs a shipping *cost*, not a courier *API*. Our core
vendor persona (P2, §5.2) already runs their own J&T or Poslaju account and books their own pickups —
Stage 1 fits that workflow instead of replacing it. Stage 2 is a convenience and margin play, and it
lands better once we have volume to negotiate rates against.

**The business consequence you should be aware of.** Escrow release is triggered by delivery (§9.2).
With no courier feed there is no automatic delivery signal at launch, so a sub-order completes when
the **buyer confirms receipt**, or automatically **10 days after the vendor marks it shipped**
(configurable). That fallback is longer than the 5-day post-delivery window we will use in Stage 2,
because it is measured from dispatch rather than arrival.

**Net effect: vendors are paid a few days later during Stage 1.** This must be stated plainly in the
vendor agreement and in onboarding — discovering it after the first sale is how vendors lose trust.
It resolves automatically when Stage 2 ships.

**Business rules affected by the stage:** `BR-O-04`, `BR-O-05`, `BR-O-06` (see §8.5) and the return
flow's delivery-window start (`BR-R-01`).

---

## 7. Category taxonomy at launch

Four top-level categories with real sub-levels. The tree is data-driven and extensible.

```
Women's Fashion
├── Dresses
│   ├── Casual Dresses
│   ├── Evening & Party Dresses
│   ├── Maxi Dresses
│   ├── Kurung & Kebaya (Traditional)
│   └── Office & Workwear
└── Bags
    ├── Handbags
    ├── Sling & Crossbody
    ├── Backpacks
    ├── Tote Bags
    ├── Clutches & Evening Bags
    └── Wallets & Purses

Shoes
├── Women's Shoes
│   ├── Heels
│   ├── Flats
│   ├── Sandals
│   ├── Sneakers
│   └── Boots
└── Men's Shoes
    ├── Formal Shoes
    ├── Sneakers
    ├── Sandals & Slippers
    ├── Loafers
    └── Boots

Mobile & Accessories
├── Phone Cases & Covers
├── Screen Protectors
├── Chargers & Cables
├── Power Banks
├── Earphones & Headphones
├── Phone Holders & Mounts
└── Smartwatch Accessories
```

### 7.1 Attribute requirements per category

These drive the filters buyers see and the fields vendors fill in. **V** = defines a variant/SKU.

| Category | Attributes |
|---|---|
| **Dresses** | Size **(V)** — XS/S/M/L/XL/XXL/Free Size; Colour **(V)**; Material; Sleeve Length; Neckline; Dress Length; Occasion; Pattern; Care Instructions |
| **Bags** | Colour **(V)**; Material (Leather / PU / Canvas / Nylon); Size (S/M/L); Closure Type; Number of Compartments; Strap Type; Dimensions (L×W×H cm) |
| **Shoes** | Size **(V)** — EU 35–46 and UK equivalents; Colour **(V)**; Material; Sole Material; Heel Height (cm); Heel Type; Toe Shape; Closure Type; Width Fitting |
| **Mobile accessories** | **Compatible Device Model** (multi-select, critical); Colour **(V)**; Material; Charging Type; Wattage/Capacity; Cable Length; Connector Type; Wireless/Wired |

**Sizing is the highest-risk data problem in this catalogue.** Dress and shoe sizes are wildly
inconsistent between vendors, and mis-sizing drives the return rate. Requirements:

- Every dress and shoe listing **must** include a size chart (per-vendor chart, or a platform
  default the vendor can adopt).
- Size values come from a **controlled vocabulary per category**, not free text, so filters work.
- Shoe sizes store an EU canonical value with UK/US display conversions.
- The product detail page shows a "runs small / true to size / runs large" summary derived from
  buyer review feedback once there are enough data points.

**Device compatibility is the second one.** Mobile accessories are unfilterable without a controlled
device list. We will maintain a `DeviceModel` reference table (brand → series → model, with release
year), seeded with current Apple, Samsung, Xiaomi, Oppo, Vivo, Realme and Huawei models, and require
vendors to tag accessories against it. Maintaining this list is an ongoing operational task and
needs an owner.

---

## 8. Business capabilities

Each capability is expanded into functional requirements in the Technical FRD. Priority:
**M** = MVP must-have, **S** = should-have (MVP if time allows), **L** = post-launch.

### 8.1 Vendor lifecycle

| ID | Requirement | Pri |
|---|---|---|
| BR-V-01 | A prospective vendor can self-register with business name, contact person, email, phone and business type (individual / sole proprietor / Sdn Bhd) | M |
| BR-V-02 | Vendors upload verification documents: IC or SSM registration, bank statement header, and a bank account for payouts | M |
| BR-V-03 | Super admin reviews and approves, rejects with reason, or requests more information | M |
| BR-V-04 | Vendors must accept the vendor agreement before listing; the accepted version and timestamp are recorded | M |
| BR-V-05 | Approved vendors get an auto-generated storefront slug they may change once, subject to availability and reserved-word rules | M |
| BR-V-06 | Vendors can be suspended (listings hidden, no new orders, existing orders must still be fulfilled) or terminated | M |
| BR-V-07 | Vendors can invite staff with scoped roles (Manager, Order Processor, Content Editor) | S |
| BR-V-08 | Vendor performance is scored on ship-on-time, cancellation rate, response rate and rating; poor scores trigger warnings then restrictions | S |
| BR-V-09 | Vendors take a temporary "holiday mode" that pauses their storefront without deleting listings | S |
| BR-V-10 | Vendor subscription tiers gating features and listing limits | L |

**Vendor states:** `Draft → PendingApproval → Approved → Active` with branches to `Rejected`,
`InfoRequested`, `Suspended`, `Terminated`, and `OnHoliday` from `Active`.

### 8.2 Catalog and listing

| ID | Requirement | Pri |
|---|---|---|
| BR-C-01 | Vendors create products with title, description, brand, category, images (min 1, max 9), optional video | M |
| BR-C-02 | Products support variants generated from variant-defining attributes; each variant has its own SKU, price, stock and optional image | M |
| BR-C-03 | Category selection determines which attributes are shown; required attributes block publication | M |
| BR-C-04 | Products go through `Draft → PendingReview → Active`, with `Rejected` and `Suspended` states | M |
| BR-C-05 | New vendors have their first 5 listings manually reviewed; trusted vendors auto-publish subject to automated screening | M |
| BR-C-06 | Bulk CSV import and export of products, with validation feedback per row | M |
| BR-C-07 | Vendors set stock per variant; out-of-stock variants are unselectable, fully out-of-stock products drop in ranking | M |
| BR-C-08 | Vendors set a list price and a selling price; the strike-through discount display requires the list price to have been genuinely in effect (anti-fake-discount rule) | M |
| BR-C-09 | Prohibited and restricted item policy enforced by keyword/image screening plus manual review | M |
| BR-C-10 | Duplicate-listing detection within a vendor account | S |
| BR-C-11 | Product Q&A on the detail page, answerable by the vendor | S |

### 8.3 Storefronts

> **Two vendor surfaces, deliberately distinct.** Every vendor has a **shop profile** *inside* the
> marketplace (marketplace header and cart, vendor banner and catalogue within — the Shopee-style
> seller page) *and* a **standalone shop website** on their subdomain / custom domain, where their
> own brand fills the frame and the marketplace is reduced to a footer line. The standalone site is
> what the vendor's customers experience as "the shop's website"; the profile is how marketplace
> shoppers discover them. Requirements below apply to both unless marked.

| ID | Requirement | Pri |
|---|---|---|
| BR-S-01 | Every approved vendor gets a standalone shop website at `{slug}.{platform}.com` **and** a shop profile page inside the marketplace | M |
| BR-S-02 | Vendors customise logo, banner, accent colour, tagline, about text, and social links | M |
| BR-S-03 | The storefront shows the vendor's catalog with its own search, category filters and sorting | M |
| BR-S-04 | The storefront shows vendor rating, response rate, joined date, location and follower count | M |
| BR-S-05 | Vendors curate a featured/collection section and pin products | S |
| BR-S-06 | Vendors connect a custom domain (§10.4) | M |
| BR-S-07 | Storefronts are search-engine indexable with correct canonical URLs and structured data | M |
| BR-S-08 | Vendors publish simple static pages (About, Shipping Policy, Return Policy, FAQ) | S |
| BR-S-09 | Storefront traffic analytics for the vendor: visitors, product views, conversion, top referrers | S |
| BR-S-10 | Vendors add their own Google Analytics / Meta Pixel ID | S |
| BR-S-11 | Selectable storefront layout themes | L |

### 8.4 Buying and checkout

| ID | Requirement | Pri |
|---|---|---|
| BR-B-01 | Buyers add items from multiple vendors to a single cart; the cart is visually grouped by vendor | M |
| BR-B-02 | Each vendor group independently calculates shipping, applies vendor vouchers, and shows its own subtotal | M |
| BR-B-03 | The cart persists for logged-in users across devices, and for guests for 30 days via a cookie | M |
| BR-B-04 | Checkout shows the complete total — items, shipping, discounts, tax — before payment | M |
| BR-B-05 | Guest checkout is allowed, with an account offered after purchase | M |
| BR-B-06 | Buyers select a delivery address from an address book, with Malaysian state and postcode validation | M |
| BR-B-07 | Stock is reserved when checkout starts and released if payment is not completed within 15 minutes | M |
| BR-B-08 | Buyers apply one platform voucher plus one voucher per vendor, subject to the stacking rules in §9.5 | M |
| BR-B-09 | An order confirmation is shown and emailed immediately on successful payment | M |
| BR-B-10 | Buyers reorder from order history | S |

### 8.5 Orders and fulfilment

| ID | Requirement | Pri |
|---|---|---|
| BR-O-01 | One payment creates one buyer-facing order that splits into one sub-order per vendor, each independently fulfillable | M |
| BR-O-02 | Vendors must accept and ship within the SLA (default 2 business days) or the sub-order auto-cancels with a penalty | M |
| BR-O-03 | Buyers cancel free of charge before the vendor ships; after shipping, cancellation becomes a return | M |
| BR-O-04 | Vendors configure shipping rates per zone (flat or weight-tiered) in Vendor Admin | M *(Stage 1)* |
| BR-O-05 | Vendors mark a sub-order as shipped, recording courier name and tracking number; the buyer sees both, with a deep link to the courier's own tracking page | M *(Stage 1)* |
| BR-O-06 | Buyers confirm receipt; if they do not, the sub-order auto-completes 10 days after the vendor marked it shipped | M *(Stage 1)* |
| BR-O-07 | Order completion triggers escrow release to the vendor wallet | M |
| BR-O-08 | Vendors partially fulfil a sub-order when only some items are available, refunding the remainder | S |
| BR-O-09 | Vendors print packing slips and export orders to CSV | S |
| BR-O-10 | Vendors request a courier pickup and print an airway bill from Vendor Admin | **Stage 2** |
| BR-O-11 | Live courier rates are quoted at checkout in place of manual rate tables | **Stage 2** |
| BR-O-12 | Tracking updates arrive automatically from the courier and are visible to the buyer without vendor input | **Stage 2** |
| BR-O-13 | A courier-confirmed delivery event transitions the sub-order to `Delivered`, after which auto-completion reverts to 5 days post-delivery | **Stage 2** |
| BR-O-14 | The platform reconciles quoted shipping against the amount the courier actually charged | **Stage 2** |

*Stage 1 / Stage 2 are defined in §6.6. Stage 2 rules are committed and scheduled (Phase 6), not
backlog items.*

### 8.6 Returns, refunds, disputes

| ID | Requirement | Pri |
|---|---|---|
| BR-R-01 | Buyers request a return or refund within 7 days of delivery, with a reason and photo evidence | M |
| BR-R-02 | Return reasons: not received, damaged, wrong item, not as described, size issue, changed mind | M |
| BR-R-03 | Vendors accept or dispute within 2 business days; no response equals acceptance | M |
| BR-R-04 | Disputed cases escalate to platform arbitration with a target 3-business-day resolution | M |
| BR-R-05 | Refunds go back to the original payment method; partial refunds are supported | M |
| BR-R-06 | Return shipping cost is borne by the vendor for vendor fault, by the buyer for change-of-mind | M |
| BR-R-07 | Refunds after escrow release deduct from the vendor wallet or the next payout | M |
| BR-R-08 | Every dispute keeps a full evidence and message trail for audit | M |

### 8.7 Promotions and marketing

| ID | Requirement | Pri |
|---|---|---|
| BR-P-01 | Vendors create vouchers scoped to their store: fixed amount or percentage, minimum spend, usage caps, validity window, per-buyer limit | M |
| BR-P-02 | Platform creates vouchers valid across all vendors or a set of categories, funded by the platform | M |
| BR-P-03 | Vendors run flash sales on selected SKUs for a defined window, with a countdown on the product page | M |
| BR-P-04 | Vendors set a free-shipping threshold for their store | M |
| BR-P-05 | Super admin curates homepage banners, featured categories, featured vendors and campaign landing pages | M |
| BR-P-06 | Campaigns run across vendors, who opt in with selected SKUs and an agreed discount | S |
| BR-P-07 | Bundle deals (buy 2 get 10% off, buy A+B for a fixed price) | S |
| BR-P-08 | Vouchers can target new buyers only, or followers of a vendor | S |
| BR-P-09 | Promotion performance reports: usage, GMV uplift, discount cost | S |

### 8.8 Trust, reviews, communication

| ID | Requirement | Pri |
|---|---|---|
| BR-T-01 | Only buyers with a completed order may review, and only the purchased variant | M |
| BR-T-02 | Reviews carry a 1–5 star rating, text, and up to 5 photos | M |
| BR-T-03 | Vendors reply publicly once per review | M |
| BR-T-04 | Reviews can be reported and are removable by admin for policy violations, with the reason logged | M |
| BR-T-05 | Vendor rating is the weighted average of item ratings, weighted toward the last 90 days | M |
| BR-T-06 | Buyer ↔ vendor chat with attachments, tied to a product or order where relevant | M |
| BR-T-07 | Vendor response rate and median response time are shown publicly | S |
| BR-T-08 | Contact details shared in chat are masked to keep transactions on-platform | S |

### 8.9 Administration and reporting

| ID | Requirement | Pri |
|---|---|---|
| BR-A-01 | Role-based access with granular permissions; roles are configurable, not hard-coded | M |
| BR-A-02 | Every state change on money, stock, vendor status and content moderation is written to an immutable audit log with actor, timestamp, before/after | M |
| BR-A-03 | Admins can impersonate a vendor account for support, with impersonation clearly logged and visibly flagged | M |
| BR-A-04 | Work queues (vendor approvals, product moderation, disputes, refund approvals) with assignment and SLA timers | M |
| BR-A-05 | Platform reports: GMV, orders, commission, payouts, refunds, top vendors, top products, category performance, cohort retention | M |
| BR-A-06 | Vendor reports: sales over time, orders, AOV, conversion, top SKUs, stock alerts, payout statements | M |
| BR-A-07 | All reports export to CSV/Excel | M |
| BR-A-08 | Platform settings are managed in the admin panel, not in config files: commission rates, SLA durations, escrow window, voucher caps | M |
| BR-A-09 | Finance can reconcile a payout run line by line against the ledger and the bank statement | M |

---

## 9. Key business rules

### 9.1 Order splitting

One checkout = one **Order** (what the buyer sees and pays once) = N **Sub-orders**, one per vendor.
Each sub-order has its own shipping, status, tracking, cancellation and refund lifecycle. Money is
tracked at sub-order level, since commission and payout are per vendor.

### 9.2 Escrow and settlement

1. Buyer pays → funds are collected by the platform and held.
2. Vendor ships → status changes; funds remain held.
3. Delivery confirmed → the **return window** starts (default 7 days).
4. Buyer confirms receipt, or the window closes → sub-order completes.
5. On completion → commission and fees are deducted, the net amount credits the **vendor wallet**.
6. **Payouts run weekly** (proposed) for all wallet balances above the minimum (proposed RM 50),
   disbursed by bank transfer.

**Step 3 depends on the shipping stage (§6.6).** In Stage 1 there is no courier feed, so "delivery
confirmed" means the buyer confirmed receipt, or 10 days elapsed since the vendor marked the item
shipped. In Stage 2 it is the courier's delivered event, and the fallback shortens to 5 days after
that event. Vendors are therefore paid a few days later during Stage 1; this must be disclosed in the
vendor agreement.

Open items for you to confirm: escrow window length, payout cadence, minimum payout, the Stage 1
dispatch-based fallback duration, and whether a reserve percentage is held against high-dispute
vendors.

### 9.3 Cancellation rules

| Scenario | Outcome |
|---|---|
| Buyer cancels before vendor accepts | Free, automatic, full refund |
| Buyer cancels after acceptance, before shipping | Requires vendor approval; refused only with a valid reason |
| Buyer cancels after shipping | Not a cancellation — becomes a return |
| Vendor cancels (out of stock) | Full refund + vendor penalty point; repeated occurrences restrict the account |
| Auto-cancel: unpaid | 15 minutes after checkout for immediate methods, 24 hours for delayed methods |
| Auto-cancel: not shipped | Vendor SLA breach (default 2 business days) |

### 9.4 Money calculation order

For each sub-order:

```
item_subtotal        = Σ (variant_selling_price × qty)
vendor_discount      = vendor vouchers + vendor flash-sale discounts
platform_discount    = platform-funded vouchers (platform absorbs this)
shipping_fee         = courier rate for the parcel
shipping_discount    = free-shipping promo (vendor- or platform-funded)

buyer_pays           = item_subtotal - vendor_discount - platform_discount
                       + shipping_fee - shipping_discount

commissionable_base  = item_subtotal - vendor_discount
commission           = commissionable_base × category_commission_rate
payment_fee_share    = per the fee policy
vendor_receives      = commissionable_base - commission - payment_fee_share
                       + vendor_shipping_recovery
```

**The rule that matters:** commission is charged on the amount *after* the vendor's own discounts and
*before* the platform's discounts. A platform-funded voucher must not reduce the vendor's revenue —
the platform is buying that discount.

### 9.5 Voucher stacking rules

- Maximum one **platform** voucher per order.
- Maximum one **vendor** voucher per vendor sub-order.
- Free-shipping promotions stack with value vouchers.
- Flash-sale prices are the base price; vouchers apply on top.
- Vouchers never reduce a line below RM 0.01, and never apply to shipping unless explicitly a
  shipping voucher.
- The buyer sees the best available combination applied automatically where possible.

### 9.6 Shipping zones

Malaysian shipping is not one zone. Rates and transit times differ substantially:

| Zone | Coverage |
|---|---|
| West Malaysia (Semenanjung) | Peninsular states |
| East Malaysia (Sabah) | Sabah + Labuan |
| East Malaysia (Sarawak) | Sarawak |

Vendors set rates per zone (and, from Stage 2, may switch to live aggregator rates). Free-shipping
thresholds may be set per
zone. **Vendors must be able to exclude East Malaysia** if they cannot serve it — this is a common
real-world requirement and its absence causes cancellations.

### 9.7 Vendor performance thresholds (proposed)

| Metric | Target | Warning | Restriction |
|---|---|---|---|
| Ship-on-time rate | ≥ 95% | < 90% | < 80% |
| Vendor cancellation rate | ≤ 2% | > 5% | > 10% |
| Chat response rate | ≥ 85% | < 70% | < 50% |
| Average rating | ≥ 4.3 | < 4.0 | < 3.5 |
| Dispute-loss rate | ≤ 1% | > 3% | > 5% |

Restriction means: removal from search boost, exclusion from campaigns, and in severe cases
suspension.

---

## 10. Storefront and domain requirements

### 10.1 Why this matters commercially

Vendor storefronts are the product differentiator (§3). A vendor with 40k Instagram followers can put
`shop.theirbrand.com` in their bio and keep their brand identity while we handle payments, logistics
and trust. That is the pitch that wins vendors away from a pure Shopee-only strategy.

### 10.2 Storefront URL structure

| Page | URL |
|---|---|
| Storefront home | `https://{slug}.{platform}.com/` |
| All products | `https://{slug}.{platform}.com/products` |
| Product detail | `https://{slug}.{platform}.com/p/{product-slug}-{id}` |
| Vendor category | `https://{slug}.{platform}.com/c/{category-slug}` |
| Static page | `https://{slug}.{platform}.com/pages/{page-slug}` |

The same product is also reachable on the marketplace at
`https://www.{platform}.com/p/{product-slug}-{id}`. **Only one of these may be the canonical URL** —
see §10.5.

### 10.3 Slug rules

3–30 characters, lowercase letters, digits and hyphens only; cannot start or end with a hyphen; not
in the reserved list (Project Plan §4.2); unique platform-wide; not confusingly similar to an
existing vendor or a known brand; changeable once, after which the old slug 301-redirects for
12 months.

### 10.4 Custom domain — business rules

| ID | Rule | Pri |
|---|---|---|
| BR-D-01 | Vendors connect one custom domain (plus its `www` variant) to their storefront | M |
| BR-D-02 | Domain ownership must be proved by a DNS TXT record before activation | M |
| BR-D-03 | The platform provisions and auto-renews TLS at no cost to the vendor | M |
| BR-D-04 | `https://xyz.com/*` returns a permanent redirect to `https://xyz.{platform}.com/*`, preserving the path | M |
| BR-D-05 | The vendor sees clear, copy-pasteable DNS instructions and a live verification status | M |
| BR-D-06 | The platform monitors connected domains and alerts the vendor if DNS or the certificate breaks | M |
| BR-D-07 | Only approved, active vendors may connect a domain | M |
| BR-D-08 | Domains on a blocklist (phishing, trademark abuse, adult) are rejected; admin can force-disconnect | M |
| BR-D-09 | Whether custom domains are free or a paid-tier feature — **open decision** | M |
| BR-D-10 | Vendors may connect several domains, all redirecting to the same storefront | L |

### 10.5 The canonical-URL decision (needs your call)

Every product is reachable at up to three URLs: marketplace, vendor subdomain, and custom domain.
Search engines must be told which one counts, or the pages compete with each other and all rank
worse.

| Option | Canonical | Consequence |
|---|---|---|
| **A — Marketplace canonical** | `www.{platform}.com/p/...` | Strongest single-domain SEO for the platform. Vendor storefronts get no independent search authority — they are effectively brand landing pages for traffic the vendor brings themselves |
| **B — Storefront canonical** (recommended) | `{slug}.{platform}.com/p/...` | Vendor pages accumulate their own authority; the marketplace still ranks on category and search pages. Matches the "vendors own their brand" positioning |
| **C — Custom domain canonical** | `xyz.com/p/...` | Maximum vendor value, but authority leaves the platform entirely and is lost if the vendor departs |

Your stated requirement (custom domain 301s to the subdomain) is consistent with **Option B**, which
is also our recommendation. The system will support all three as configuration, defaulting to B.

---

## 11. Payments — options for Malaysia

Selecting a gateway is decision #4 in the project plan and gates Phase 3. What matters is not just
fees but whether the provider supports a **marketplace/split-payment model**, since we collect on
behalf of vendors.

| Provider | Strengths | Considerations |
|---|---|---|
| **iPay88** | Long-established in MY, very wide method coverage including FPX and e-wallets | Older integration ergonomics; confirm marketplace/escrow support |
| **Razer Merchant Services** | Broad local coverage, strong e-wallet support | Confirm current fee schedule and split-payment capability |
| **Billplz** | Simple, FPX-focused, low friction to onboard | Narrower method coverage; better as a secondary |
| **senangPay** | SME-friendly, quick onboarding | Feature depth relative to needs |
| **Stripe (MY)** | Best developer experience and API; Stripe Connect is purpose-built for marketplaces and would solve escrow, split payouts and KYC in one product | Verify current FPX support and local method coverage in MY; card rates versus local providers |
| **Curlec (Razorpay MY)** | Strong local presence, direct debit / recurring | Verify marketplace features |

**Payment methods to support at launch:** FPX online banking (the dominant method in Malaysia —
non-negotiable), debit and credit cards, e-wallets (Touch 'n Go, GrabPay, Boost, ShopeePay), and
optionally BNPL. **Cash on delivery** is deliberately excluded from the MVP: it breaks the escrow
model, raises the failed-delivery rate, and complicates settlement. It can be revisited post-launch
if vendor demand justifies the operational cost.

> All fee schedules, method availability and product capabilities above must be verified directly
> with each provider — they change, and the commercial terms are negotiable at volume.

---

## 12. Tax, legal and compliance

> This section flags obligations and their product impact. **It is not tax or legal advice.** Engage a
> Malaysian tax advisor and a lawyer before launch; treat everything below as items to confirm.

| Area | Product impact | Action |
|---|---|---|
| **SST (Sales & Service Tax)** | Whether prices display tax-inclusive or exclusive; whether the platform's commission/service fee is taxable; how tax appears on invoices | Confirm registration threshold and applicable rates with a tax advisor before pricing display is built |
| **LHDN e-Invoice (MyInvois)** | Malaysia is phasing in mandatory electronic invoicing. The platform likely needs to submit invoices for its own service fees, and vendors may have obligations for their sales | Confirm the applicable phase and deadline for our revenue band; design invoice records to be exportable to the required format from the start |
| **Low-value goods import tax** | Only relevant if we allow cross-border sellers — out of scope for MVP | Revisit if cross-border is added |
| **PDPA 2010 (as amended)** | Consent capture, privacy notice, data-subject access and deletion requests, breach notification, possibly a Data Protection Officer, restrictions on cross-border data transfer | Privacy notice drafted by legal; consent and DSR workflows built in Phase 5; keep personal data on servers within the agreed jurisdiction |
| **Consumer Protection Act / e-commerce rules** | Sellers must display identity and contact details; the platform must record seller particulars; clear pricing and return terms required | Vendor identity display on storefront and product pages; KYC records retained |
| **Vendor agreement** | Commission, settlement terms, liability, prohibited items, termination, IP warranties | Lawyer-drafted; versioned in the system with per-vendor acceptance records |
| **Buyer T&Cs, Privacy Policy, Return Policy** | Must be accepted at registration and versioned | Same versioning mechanism |
| **Prohibited items** | Counterfeits, weapons, regulated cosmetics and health products, alcohol/tobacco restrictions | Policy list plus enforcement in the moderation flow |
| **IP / counterfeit takedowns** | A notice-and-takedown process brand owners can use | Build a takedown request intake in Phase 5 |

---

## 13. Non-functional business expectations

| Area | Expectation |
|---|---|
| Availability | 99.5% monthly; no planned maintenance during 11:00–14:00 or 19:00–23:00 MYT (peak shopping) |
| Performance | Product pages usable within 3 s on a mid-range Android phone over 4G |
| Mobile | Mobile-first; ≥ 70% of traffic is expected to be mobile web |
| Scale (year 1) | 30k SKUs, 300 active vendors, 50k registered buyers, 1,000 orders/day peak, 10× that during campaigns |
| Support hours | 9am–6pm MYT, Mon–Sat, with self-service help centre outside those hours |
| Support SLA | First response within 4 business hours; disputes resolved within 3 business days |
| Data retention | Order and financial records 7 years (tax); personal data per PDPA and the privacy notice |
| Languages | English at launch; BM ready for a fast follow |
| Accessibility | WCAG 2.1 AA for buyer-facing pages |

---

## 14. Release plan

| Release | Contents | Target |
|---|---|---|
| **Internal alpha** | Catalogue + vendor onboarding, seeded data | End of Phase 1 |
| **Storefront preview** | Live storefronts, browse and search, no ordering | End of Phase 2 |
| **▶ v1 — conversational launch** | Catalogue, storefronts, custom domains, **WhatsApp/Messenger ordering**, manual stock. Pilot cohort, then open | **End of Phase 3, ~week 22** |
| **Closed beta — checkout** | Full purchase flow, sandbox payments, 5 friendly vendors | End of Phase 4 |
| **Soft launch — checkout** | Live payments, 10–20 vendors migrated, real money | End of Phase 5 |
| **▶ v2 — full marketplace** | Checkout, payments, ledger, returns, promotions, reviews. Shipping Stage 1 | **End of Phase 6, ~week 44** |
| **v2.1 — Shipping & Delivery** | **Shipping Stage 2**: courier integration, live rates, labels, pickup, automated tracking (§6.6) | End of Phase 7 |
| **v2.2** | Vendor tiers, bundles, campaign tooling, further localisation | v2 + 3 months |
| **v3.0** | Mobile app, live selling, ads, affiliate programme | v2 + 9 months |

---

## 15. Acceptance criteria for launch

The platform is launch-ready when:

1. A vendor can register, be approved, list a product with variants, and receive a real order without
   any manual intervention from the platform team.
2. A buyer can buy from two different vendors in one checkout, pay by FPX, and receive two separately
   tracked parcels — with each vendor's shipping cost calculated correctly for the destination zone,
   and each tracking number visible to the buyer.
3. A completed order settles to the vendor wallet with commission calculated correctly, and a payout
   run reconciles to the cent against the bank statement.
4. A return can be requested, approved and refunded end to end, including a case that goes to
   arbitration.
5. A vendor storefront is live on its subdomain, indexed by Google, and reachable via a connected
   custom domain over HTTPS.
6. Super admin can answer, from the panel alone and without database access: how much GMV we did
   yesterday, which vendors are pending approval, which orders are late, and what we owe each vendor.
7. Load testing sustains 10× expected launch-day traffic at target latencies.
8. A security review and penetration test are complete with no unresolved critical or high findings.
9. Legal documents are in place and accepted by all pilot vendors.
10. A production restore has been rehearsed from backup successfully.

---

## 16. Glossary

| Term | Meaning |
|---|---|
| **AOV** | Average order value |
| **Airway bill (AWB)** | Courier shipping label with a tracking number |
| **BM** | Bahasa Melayu |
| **Escrow** | Platform holding payment until the buyer receives the goods |
| **FPX** | Financial Process Exchange — Malaysian online banking payment rail |
| **GMV** | Gross merchandise value — total value of goods sold |
| **LHDN** | Lembaga Hasil Dalam Negeri, the Malaysian tax authority |
| **PDPA** | Personal Data Protection Act 2010 (Malaysia) |
| **SKU** | Stock keeping unit — an individual sellable variant |
| **SSM** | Suruhanjaya Syarikat Malaysia, the companies commission |
| **SST** | Sales and Service Tax |
| **Sub-order** | The per-vendor portion of a buyer's order |
| **Take rate** | Platform revenue as a percentage of GMV |
| **Variant** | A specific combination of product options, e.g. Red / Size M |
| **West / East Malaysia** | Peninsular Malaysia vs Sabah and Sarawak — different shipping zones |
