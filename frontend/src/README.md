# Client sources

This directory holds the client application, the way `backend/src/` holds the service's projects. It holds two
packages, and what separates them is the resolver rather than the folder:

- `Client.Backend/` — everything that reaches the service. It declares no React and no DOM-typed dependency.
- `Client.App/` — the application, which depends on `Client.Backend`. Nothing depends on `Client.App`.

[`frontend/README.md`](../README.md) is the page for the workspace itself: the commands, the boundary and how to
reproduce it, the strict TypeScript settings, the styling, and what the build produces. The source contract that will
govern what is written _in_ these packages is `AGENTS.md` beside this file, and it has not landed yet — until it does,
the repository-root instructions are the whole of what applies here.

Nothing about the service moved when the Uno Platform client that stood here was withdrawn. The client surface is the
HTTP API served beneath `/api/client`, which is a transport surface of its own with its own listener and its own
credentials — [`client-endpoint.md`](../../docs/operations/client-endpoint.md) is the page — and the container image
serves whatever bundle it carries beneath its web root, from a deployment setting rather than from anything a client
build states. An image built today carries no bundle, so a deployment that switches the client application on is
refused by name.
