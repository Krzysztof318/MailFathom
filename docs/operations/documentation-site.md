# The documentation site

<!-- describes: docfx/**, docs/toc.yml, docs/*/toc.yml, docs/index.md, docs/api/index.md, scripts/build-docs-site.sh, scripts/compose-docs-site.sh, scripts/list-documented-versions.sh, .github/workflows/publish-documentation.yml -->

The pages under `docs/` are published as a browsable site at
<https://krzysztof318.github.io/MailFathom/>. The site is generated from this repository by
[docfx](https://dotnet.github.io/docfx/), deployed by GitHub Pages, and rebuilt in full by two pushes: a merge to
`main`, which moves `latest`, and a release tag, which adds that release's version to the selector. It is the tag
rather than the release, because the version list below is read from the tags in the checkout rather than from GitHub's
releases — and because a `release: published` trigger cannot start a run at all, the release being created by `Release`
with `GITHUB_TOKEN` and GitHub starting no workflow from an event that token generated.

Nothing is authored on the site. A page is written here, reviewed in the same pull request as the behavior it
describes, and published by the merge — which is the whole reason the site is generated from `docs/` rather than kept
in a wiki. A wiki is a second repository with no pull request and no review, so nothing would make a page change when
the code it describes does, and the drift would be invisible until a reader hit it.

## What the site carries

Everything under `docs/` except the architectural decision records, plus two things from outside it:

| On the site | Where it comes from |
| --- | --- |
| The user guide, the operations pages, the feature pages, and the architecture pages | `docs/`, unchanged |
| The API reference | Generated from the XML documentation comments in `src/`, every project but `AppHost` |
| The changelog | The repository-root `CHANGELOG.md`, published as it stands |
| The landing page | `docs/index.md`, which exists for the site alone |

Four kinds of file under `docs/` are deliberately not published:

- **The architectural decision records.** An ADR records why a decision was taken and is closed once accepted; it is
  not documentation of how MailFathom behaves, and a reader looking for the current shape of something should reach
  the page that describes it rather than the record of the argument behind it. They stay readable in the repository,
  and a page that cites one links to it as an absolute GitHub URL.
- **`README.md`**, which is the index GitHub shows when somebody browses `docs/` in the repository. On the site the
  navigation is the index, so publishing it would be a second one to keep in step.
- **Every `AGENTS.md` and `CLAUDE.md`**, which are instructions to whoever is changing the repository rather than
  documentation of the product.
- **The `toc.yml` files**, which are the navigation itself.

The specifications under `specs/` are not published either, and are not under `docs/` to begin with: a specification
states intent, and every page on this site states fact.

## Which versions are published, and which one opens

The site holds one directory per version and a landing page that sends a reader into one of them:

- **`latest`** is built from `main`. It documents work that no release carries yet, so it is never what the site opens
  on — a page there can describe a setting the version you are running does not accept.
- **One directory per released minor line**, at that line's newest patch, built from the release tag itself. `0.3.1`
  replaces `0.3.0` in the selector when it ships, because a patch exists to correct the line it belongs to.

**The site opens on the newest release.** `latest` is one click away in the selector every page carries, and every
page outside the default version says which version it is and links to the current one.

A release tag is published only if it carries the site definition itself, so `0.1.0` and `0.2.0` are not on the site:
they predate it. Every version on the site is built from its own commit, with the navigation, the pages, and the API
surface that release actually shipped, which is what makes a page true of the release it is filed under — rendering an
older tag's pages through today's configuration would produce navigation naming pages that release never had.

`scripts/list-documented-versions.sh` is where that policy lives, and it answers from the tags in the checkout it runs
in. The whole site is a function of the repository rather than of the deployments that came before it: each publish
rebuilds every version and replaces the site, so nothing accumulates and a version that stops being documented
disappears on the next run.

## Navigation

The site's navigation is written rather than derived. `docs/toc.yml` is the header, and a `toc.yml` in each section
directory is that section's sidebar, ordered by the question a reader arrives with rather than alphabetically.

**A new page under `docs/` joins the `toc.yml` of its section in the same change.** A page in no table of contents is
published and unreachable, which is worse than not publishing it, so `scripts/test-agent-workflow.sh` fails a page
that no table of contents lists and an entry that names no page.

The href in a `toc.yml` is resolved against the file's own directory in `docs/`, not against the site — which is why
these files live beside the pages they order rather than in `docfx/`.

## Links

Two forms, and which one to use is decided by whether the target is on the site:

- **A link to another published page stays relative**, as `../operations/mcp-endpoint.md`. docfx rewrites it to the
  published page, and GitHub resolves it to the file, so one link works in both renderings.
- **A link to anything the site does not carry is written as an absolute `https://github.com/Krzysztof318/MailFathom`
  URL** — an ADR, a specification, a deployment asset, a source file. A relative link to one of those resolves on
  GitHub and reaches a 404 on the site.
- **A link into the API reference is a `xref:`**, naming the type or namespace rather than the generated file, as
  `[Domain](xref:MailFathom.Domain)`. It is the one kind of link a refactor breaks without touching the page, so it
  resolves through docfx's cross-reference map and fails the build when the name stops being generated. On GitHub such
  a link renders as text rather than as a link, which is why only `docs/api/index.md` uses it: that page exists for
  the site.

`scripts/build-docs-site.sh` fails when docfx resolves no target for a link, so neither mistake reaches a reader: the
site build runs on every pull request that changes `docs/` or the site definition, and a broken link is a red check
rather than something found later. `CHANGELOG.md` is the one file exempt from that check, because it is written by the
release pull request and by nothing else — a link in it is corrected by the next release rather than by whoever
notices it, and failing every documentation build in the meantime would stop the site over a file the build is not
allowed to touch. The same rule reaches the XML documentation comments, where a `<see href>` to an
ADR is an absolute URL for a second reason — a relative path from a source file resolves to nothing in an editor
either.

The repository-root `README.md` is a separate case with a stricter rule, recorded in `AGENTS.md`: every link in it is
absolute, because it is rendered outside the repository as well, and a link to a published page goes to the site
rather than to the Markdown file behind it.

### The addresses that outlive a release

A page has two addresses. `…/MailFathom/v<version>/operations/mcp-endpoint.html` is where it is served, and
`…/MailFathom/operations/mcp-endpoint.html` — the same path without the version — is a redirect at the site root that
lands on whichever version the site opens on, carrying the heading anchor with it.

The second is what anything outside the site links to, the root `README.md` above all. A link carrying a version would
be wrong the day the next one ships, and one carrying `latest` would quietly opt a reader out of the release the site
opens on, which is the whole point of opening on it.

`scripts/compose-docs-site.sh` writes one such redirect per page of the default version, so the set of stable
addresses moves with the pages rather than being maintained beside them: a page that a release removes loses its
address on the next publish. The API reference is left out — it is a thousand generated pages whose names are type
names, nothing links into it by hand, and mirroring it would treble the file count of the site for nothing.

## Building it locally

```bash
scripts/build-docs-site.sh                 # artifacts/docs-site
dotnet docfx serve artifacts/docs-site     # http://localhost:8080
```

The build restores the solution, because generating the API reference loads every project through MSBuild, and then
runs docfx once. Expect a few minutes the first time. While a page is being written,
`dotnet docfx docfx/docfx.json --serve` rebuilds and serves in one step instead.

docfx is pinned in `.config/dotnet-tools.json` and restored by `dotnet tool restore` like the other command-line
tools, so no global install is involved. A locally built site carries no version selector: the selector reads a
manifest that `scripts/compose-docs-site.sh` writes above the version directories, and a single build has nothing
above it.

## What the template adds

`docfx/template/` is a thin layer over docfx's own `modern` template — the appearance is docfx's, deliberately, and
what is added is behavior the pages need. The one appearance rule is the header logo, which `modern` does not size at
all: its own logo is an SVG whose intrinsic size already fits a header, so a raster file arrives at whatever size it
was saved at. Nothing in the build can see that — docfx renders a page without laying it out, so a logo that fits and
one that covers the page produce the same output.

The selector itself sits at the right-hand end of the header, in front of the icon links. That is inside the element
`modern` renders and re-renders — it writes the section links and the icon links there after the template's own module
has run, and writes them again whenever the theme picker among them is used — so the selector is placed and then kept
placed, from an observer that puts it back rather than from an insertion that happens once.

That is the general shape of what this template can get wrong. Everything it adds happens in the browser, after the
build has finished and against files the build never reads, so a page that renders the selector and one that silently
does not are the same output as far as every gate here is concerned. **The site's appearance and its run-time
behaviour are the parts of it verified by looking at the deployed site**, and every defect found that way so far — a
logo at its natural size, a selector missing from the two pages served from a version's own directory, and the same
selector missing again when that directory was addressed without its trailing slash — was invisible to a green build.

What the template adds beyond that:

- **The version selector** in the header, and the banner a page outside the default version carries. Both read
  `versions.json` from the site root at run time, so a version built months ago joins the selector correctly without
  being rebuilt.
- **A viewer for diagrams and images.** Clicking a Mermaid diagram or an image opens it over the page, where the wheel
  and the buttons zoom about the pointer, a drag pans, a double click resets, and <kbd>Esc</kbd> closes. Pinch-zoom
  works the same way on a touch screen. A diagram is drawn at the width of the article, which has nothing to do with
  how much detail it holds, and this is what makes the detail reachable.

Both are written against the DOM the `modern` template produces, which re-renders the navigation bar and every Mermaid
diagram after the page loads. The selector is therefore placed in the one header element that is never rewritten, and
the viewer opens from a single delegated listener rather than from handlers bound to elements about to be replaced.

## Publishing

`.github/workflows/publish-documentation.yml` runs on every push to `main`, on a pushed release tag, and on demand. It
resolves the version list, builds each version in parallel, composes them into one tree, and deploys that tree with
the repository's own Pages deployment — `actions/deploy-pages`, not a bot pushing to a branch. It needs no secret: the
`pages: write` and `id-token: write` scopes on the deploying job are all it holds, and nothing in it writes to the
repository.

A pull request that changes `docs/` or the site definition builds `latest` and deploys nothing. A change under `src/`
does not trigger it: an XML comment docfx dislikes is a warning rather than a failure, so the build would add minutes
to nearly every pull request the repository sees and catch nothing.

Pages itself is enabled once, in the repository settings, with **Build and deployment → Source** set to **GitHub
Actions**. The workflow cannot enable it: the action that would needs a token with administration rights, which is
exactly the kind of credential this workflow is built not to hold.
