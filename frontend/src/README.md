# Client sources

This directory holds the client application, the way `backend/src/` holds the service's projects. It is empty today.

The Uno Platform client that stood here was withdrawn, and the client is being rebuilt in React and JavaScript. What
was kept is the place rather than anything that was in it: this directory, [`frontend/tests/`](../tests/README.md)
beside it, and the `Frontend` job of `CI`, which calls
[`build-test-frontend.yml`](../../.github/workflows/build-test-frontend.yml) and asserts nothing until that workflow's
build, test, and formatting steps are written for the new stack.

Nothing about the service moved with it. The client surface it will call is the HTTP API served beneath `/api/client`,
which is a transport surface of its own with its own listener and its own credentials —
[`client-endpoint.md`](../../docs/operations/client-endpoint.md) is the page — and the container image serves whatever
bundle it carries beneath its web root, from a deployment setting rather than from anything a client build states. An
image built today carries no bundle, so a deployment that switches the client application on is refused by name.
