# Client tests

This directory holds the tests covering [`frontend/src/`](../src/README.md), the way `backend/tests/` holds the ones
covering `backend/src/`. It carries one project, `Client.UnitTests`, which references the application through the plain
`net10.0` target the single project publishes beside its heads — no browser and no window is started to run it.

The suite is xUnit.net v3 on Microsoft Testing Platform, the same framework and runner the service's suites use, so one
`dotnet test` contract covers the repository:

```bash
dotnet test --solution frontend/MailFathom.Client.slnx
```

The rule `backend/tests/AGENTS.md` states about structure holds here for the same reason it holds there: a test
directory follows the structure of the code it covers, so `Client.UnitTests/Presentation/` covers
`src/Client/Presentation/`.

What it asserts today is what a scaffold has to say for itself — that the client reports the product and version the
build declares, and that the MVUX model behind the only screen yields it. `frontend/AGENTS.md` § *Testing* states what
belongs here, how a feed is asserted, and what is deliberately left to a head that actually runs.
