# Client tests

This directory holds the tests covering [`frontend/src/`](../src/README.md), the way `backend/tests/` holds the ones
covering `backend/src/`. It carries one project, `Client.UnitTests`, covering both projects over there: it references
the application through the plain `net10.0` target the single project publishes beside its heads, and `Client.Backend`
directly, which targets that one framework and nothing else. No browser and no window is started to run it.

The suite is xUnit.net v3 on Microsoft Testing Platform, the same framework and runner the service's suites use, so one
`dotnet test` contract covers the repository:

```bash
dotnet test --solution frontend/MailFathom.Client.slnx
```

The structure rule is the repository's rather than this stack's: a test directory follows the structure of the code it
covers, so `Client.UnitTests/Presentation/` covers `src/Client/Presentation/` and `Client.UnitTests/Backend/` covers
`src/Client.Backend/`, exactly as `backend/tests/` mirrors `backend/src/`. The doubles those tests share live in
`Client.UnitTests/TestDoubles/`, because they cover nothing themselves.

What it asserts today is what a scaffold has to say for itself — that the client reports the product and version the
build declares, that the MVUX model behind the only screen yields it, and that the languages the model offers are the
ones the embedded configuration names and that the string tables under `src/Client/Strings/` agree with each other —
and the whole of what `Client.Backend` does
against a stubbed transport: the wire contract, the four ways an exchange can fail, and the sign-in end to end with the
one head-specific step stubbed at its port. `AGENTS.md` beside this file states what belongs here, how a feed is
asserted, and what is deliberately left to a head that actually runs.
