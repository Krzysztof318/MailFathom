# Client tests

This directory holds the tests covering [`frontend/src/`](../src/README.md), the way `backend/tests/` holds the ones
covering `backend/src/`. It is empty today: the Uno Platform suite that stood here was withdrawn with the client it
covered, and the suite for the React client is written with the screens it covers rather than in advance of them.

What the suite here is run by, and what it asserts, is decided then. Until it exists, `pnpm` has no test script and the
`Frontend` job of `CI` runs nothing.
