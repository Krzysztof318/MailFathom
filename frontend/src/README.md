# Frontend sources

This directory holds the frontend application's own sources, the way `backend/src/` holds the .NET
solution's projects. It is empty because no frontend project has been added yet; the directory exists
so that the first one arrives into a decided place rather than deciding one.

What lands here is the frontend and nothing else. It reaches MailFathom over the endpoints
`backend/src/Host/` exposes, so it shares no build, no package manifest, and no configuration file
with the solution beside it — a root-level MSBuild file governs `backend/` alone, and whatever
manifest a frontend build needs belongs under `frontend/`.

Tests for what is written here go to [`frontend/tests/`](../tests/README.md).
