# The client workspace

`frontend/` is a [pnpm](https://pnpm.io/) workspace holding the two packages the MailFathom client is split into. It
shares no build file and no configuration file with the service under `backend/`; the two meet only over the HTTP API
served beneath `/api/client`, which [the client endpoint](../docs/operations/client-endpoint.md) describes.

```bash
pnpm install --frozen-lockfile   # restore, refusing to rewrite pnpm-lock.yaml
pnpm dev                         # the development server
pnpm build                       # the static bundle, into src/Client.App/dist/
pnpm typecheck                   # both packages, under the strict set below
pnpm lint                        # every rule an error, no warning tolerated
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
