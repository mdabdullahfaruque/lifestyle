# Brand assets — the single source

Everything visual that says "MyLifestyleMart" comes from this folder. Three Angular apps copy it
into their builds; none of them keeps its own copy.

## Changing the logo

```bash
cd web && npm i -D sharp                                  # once; not a workspace dependency
node assets/brand/generate.mjs /path/to/new-logo.png      # adopt the new lockup
```

Then rebuild. All three apps pick it up — there is nothing else to edit.

```
web/assets/brand/
  logo-source.png       highest resolution you have        ← THE ONE YOU REPLACE
  logo.png              900px display lockup               ← derived
  logo-mark.png         the mark alone, square             ← derived
  favicon-32.png        browser tab                        ← derived
  apple-touch-icon.png  iOS home screen                    ← derived
  og-image.png          1200x630 link preview              ← derived
```

Everything is cut from **`logo-source.png`**, never from `logo.png`. That distinction matters:
`logo.png` is already downscaled to 900px, so re-deriving from it would lose resolution on every
run and the favicon would go visibly soft after a couple of rebrands.

Regenerate whenever the lockup changes. A stale favicon still showing the old mark is the usual way
this drifts out of step.

## How it reaches the apps

`web/angular.json` gives every build target one asset entry:

```json
{ "glob": "**/*", "input": "assets/brand", "output": "brand" }
```

so the files are served at `/brand/…` in all three apps from this one folder. **Do not copy these
files into an app's `public/`** — that is exactly the duplication this arrangement exists to avoid,
and the copy will be the one that goes stale.

## How it reaches the markup

Never reference a logo path directly. Use the shared component:

```html
<lib-brand-logo [height]="40" />          <!-- full lockup -->
<lib-brand-logo variant="mark" [height]="28" />
```

Paths and the accessible name live in the `BRAND_ASSETS` token
(`web/projects/ui/src/lib/brand.ts`). A deployment that needs different branding overrides that
token in its own `app.config.ts` — no component changes, no second set of files.

## Why the storefront header is white

The lockup is dark blue and orange on white. On the cobalt header bar it read as a white rectangle
pasted over the design. The header is white so the mark carries the brand colour itself; cobalt
stays where the direction sheet puts it — CTAs, active nav, the search button.

If a future logo is a white knockout, the header can go back to cobalt and only `app.scss` changes.
