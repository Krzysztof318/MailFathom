# The client workspace

`frontend/` is a [pnpm](https://pnpm.io/) workspace holding the two packages the MailFathom client is split into. It
shares no build file and no configuration file with the service under `backend/`; the two meet only over the HTTP API
served beneath `/api/client`, which [the client endpoint](../docs/operations/client-endpoint.md) describes.

```bash
pnpm install --frozen-lockfile   # restore, refusing to rewrite pnpm-lock.yaml
pnpm dev                         # the development server
pnpm build                       # the static bundle, into src/Client.App/dist/
pnpm typecheck                   # both packages and eslint.config.ts, under the strict set below
pnpm lint                        # every rule an error, no warning tolerated
pnpm test                        # both packages' suites, once, non-interactively
pnpm test:browser                # build the bundle and drive it in a real browser
pnpm format                      # rewrite; pnpm format:check reports instead
```

`packageManager` in `package.json` names the pnpm version this lock file was written by, and `engines` the Node
version the toolchain is run under. Corepack no longer ships with Node, so `pnpm` comes from a global install and that
field is what says which version to install. `.npmrc` declares the registry those packages come from, rather than
leaving it to whatever a machine configured. [Local development](../docs/operations/local-development.md) has the
prerequisites and how the verification gates run all of this.

## Two packages, and the resolver is what separates them

- **`src/Client.Backend/`** is everything that reaches the service: the request and response types, the session, the
  failure model, and the operations composed from them. It declares **no React and no DOM-typed dependency**.
- **`src/Client.App/`** is the application: screens, components, state, styling. It depends on `Client.Backend`, and
  nothing depends on it.

The boundary is the dependency graph rather than a convention, so crossing it fails a build instead of waiting for a
reviewer. Three mechanisms hold it, and each can be reproduced by writing the offending line and running the command
beside it:

| Write this in `Client.Backend`     | What refuses it                                                                                                              |
| ---------------------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| `import { useState } from 'react'` | `pnpm typecheck` — the package's manifest declares no React, so the module does not resolve                                  |
| `document.title`                   | `pnpm typecheck` — its `tsconfig.json` names `lib: ["ES2023"]` and `types: []`, so no browser global is declared             |
| either of the above                | `pnpm lint` — `no-restricted-imports` names the boundary rather than leaving a resolution error to read as a missing install |

`Client.Backend` therefore names no HTTP API of its own. It publishes a `MailFathomTransport` — a function from a
request to a response — and `Client.App` supplies the adapter that calls one. That is the boundary's consequence
rather than an abstraction kept in case a second transport appears.

## TypeScript only, at the strictest setting

Every source file under `frontend/src/` is `.ts` or `.tsx`. A `.js` or `.jsx` file there fails `pnpm lint` on a rule
written for exactly that, so the convention is enforced rather than remembered.

`tsconfig.base.json` is what both packages compile under. It goes past `strict`: an unchecked index access, an
inexact optional property, an unchecked `override`, an unused local or parameter, a switch fallthrough, a missing
return, and a property read off an index signature are each errors. That file carries every relaxation from the
maximum as a named entry with its reason, and there is one.

A lint violation is a build failure. `pnpm lint` runs with `--max-warnings 0`, so a rule the plugins ship as a warning
still fails — which is what `TreatWarningsAsErrors` and the analyzer set are to the service half of this repository.

## Two languages, and no library for them

`Client.App` is localized in English and Polish. English is the default and the fallback. A first run with no choice
stored reads what the browser or the operating system says the person prefers, narrowed to a language a catalogue was
written for; the control in the header overrides that, the choice survives a restart of either head, and changing it
rewrites the screen without anything restarting.

The mechanism is `src/Client.App/src/localization/` and it depends on nothing. `Intl` — which every engine both heads
render in already carries — formats dates, numbers, relative times, lists, and plural categories, so what was left to
own is a catalogue, a lookup, and a `{name}` hole to fill. That is less than the configuration an internationalization
library is adopted with, and it adds nothing to the bundle and nothing to `THIRD_PARTY_LICENSES.md`.

- `en.ts` declares the keys and `pl.ts` is annotated with the type it exports, so a key one language carries and the
  other does not fails `pnpm typecheck` rather than reaching a screen. The unit suite asserts the same parity at run
  time.
- `locale.ts` resolves which language a run opens in and stores an explicit choice; `Localization.tsx` holds the
  provider and `useLocalization.ts` the hook a screen reads through. Nothing else in `Client.App` reads either
  directly.

**A user-visible string written into a component fails `pnpm lint`**, on `no-restricted-syntax` selectors in
`eslint.config.ts`. Each can be reproduced by writing the offending line and running that command:

| Write this in a `Client.App` component | What it reports                                                                                                  |
| -------------------------------------- | ---------------------------------------------------------------------------------------------------------------- |
| `<p>Reading accounts…</p>`             | a string in markup belongs in `en.ts`, with its Polish counterpart, and reaches the screen through `translate()` |
| `<p>{'Reading accounts…'}</p>`         | the same, for a string used as a child rather than written as text                                               |
| ``<p>{`Reading ${what}…`}</p>``        | a sentence assembled in markup cannot be reordered by a translator; it is one entry with a hole                  |
| `<section aria-label="Accounts">`      | an attribute read out to somebody is a user-visible string                                                       |

`.config/typos.toml` excludes `pl.ts` from the spell check, because a dictionary of English has nothing true to say
about Polish. Every other file in this workspace stays checked, `en.ts` included.

## The two suites

`pnpm test` is the unit suite, and `vitest.config.ts` declares one Vitest project per package because the two are tested
differently: `Client.Backend` is ordinary logic run without a DOM, and `Client.App` is components rendered into jsdom
with React Testing Library. A test file sits beside the source it covers — the package boundary above is the reason.

`pnpm test:browser` is the other one. It runs `pnpm build`, serves `src/Client.App/dist/` with Vite's preview server,
and drives it with Playwright, so what it proves is the bundle a deployment publishes rather than the source: the
application loading, the version the build stamped, the screen rendering through roles and accessible names, the
browser's own back navigation, and the requests the page actually issued. It needs a browser of its own —
`pnpm exec playwright install chromium` — which is why neither verification gate runs it and the pipeline does, on every
pull request that reaches this stack. Its configuration is `playwright.config.ts` and its specs are under `tests/`.

[`tests/AGENTS.md`](tests/AGENTS.md) is where both suites' policy is decided, including which check belongs to which.

## Whitespace is decided in `.editorconfig`

The repository's one `.editorconfig` at the root holds indentation, line width, line endings, and quote style for the
client's file types. Prettier reads it: its CLI respects `.editorconfig` by default, so nothing here restates any of
those values in a second file that would drift. Prettier's own configuration here is `.prettierignore` and nothing
more.

## Styling

Tailwind is wired CSS-first through `@tailwindcss/vite`. The palette and the type scale are `@theme` tokens in
`src/Client.App/src/styles.css`, and there is no JavaScript configuration file. The colours are MailFathom's own,
sampled from the product icon.

## What the build produces

`pnpm build` writes `src/Client.App/dist/` — a directory of static files and nothing else. No Node process joins any
deployment shape: the container image serves whatever bundle it carries beneath its web root, from a deployment
setting rather than from anything a client build states.

The version the client displays comes from `<VersionPrefix>` in `Version.props`, read at build time through
`scripts/read-declared-version.sh` and substituted into the bundle. No version number is written into a manifest or
into source.
