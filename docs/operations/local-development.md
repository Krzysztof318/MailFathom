# Local development

<!-- describes: scripts/**, global.json, MailFathom.code-workspace, .config/dotnet-tools.json, .config/typos.toml, .config/CodeCoverage.proj, .config/testconfig.json, backend/src/AppHost/**, backend/src/Infrastructure/Persistence/MailFathomDbContextDesignTimeFactory.cs, .github/workflows/**, backend/tests/IntegrationTests/ProviderAdapters/**, backend/tests/IntegrationTests/ObjectStorage/**, backend/tools/**, frontend/package.json, frontend/pnpm-workspace.yaml, frontend/.npmrc, frontend/tsconfig.base.json, frontend/tsconfig.json, frontend/eslint.config.ts, frontend/vitest.config.ts, frontend/playwright.config.ts, frontend/src-tauri/** -->

Use the .NET SDK pinned in `global.json`. Test execution is configured for Microsoft Testing Platform through the repository-level `global.json` test runner setting.

**Linux is the only officially supported platform**, for development as much as for deployment: the orchestration starts Linux containers, the deployment shapes are a container, Kubernetes, and a systemd service, and TLS goes through the system OpenSSL. Development on Windows may work — the solution is ordinary .NET — but **expect problems and a setup of your own**, and nothing in this repository is verified against it.

A development machine also needs **OpenSSL 3.0 or later**, because every TLS connection a running MailFathom makes — to the mail server, to PostgreSQL — is handshaked by the system library rather than by .NET, and its security policy decides which servers are reachable at all. **1.1.1 is the hard floor**: .NET 10 requires it on Unix and [fails to start](https://learn.microsoft.com/en-us/dotnet/core/compatibility/cryptography/10.0/openssl-version-requirement) without it. **Anything between 1.1.1 and 3.0 may work and may not** — it is out of upstream support and nothing in this repository is verified against it, so treat a failure that reproduces only there as an environment problem rather than a defect.

Nothing has to be configured for a mail server that clears the distribution's default policy, which is nearly all of them: a checkout that sets nothing runs at that full-strength policy and negotiates the newest TLS both ends support. Relaxing it is an opt-in for one process — a development mailbox on a server the policy refuses is what [the platform TLS policy](platform-tls-policy.md) is for, and it applies to the host however the host is started.

## Opening the repository in an editor

**The repository root is not a solution directory.** The service owns its own — `backend/MailFathom.slnx` — so the
root holds no `.sln`, no `.slnx`, and no `.csproj`, and `dotnet restore` run there fails with `MSB1003` for the same
reason an editor opened there loads nothing.

Rider and Visual Studio open a solution rather than a directory, so neither notices. **VS Code opens a directory**, and
opening the root gives an editor with no project loaded. Open `MailFathom.code-workspace` at the repository root
instead:

```bash
code MailFathom.code-workspace
```

It opens `backend/` and the repository itself as two folders, recommends the extensions the service needs, and carries
a launch configuration for the Aspire app host. `.vscode/` stays gitignored, so everything the repository decides is in
that one file and everything a contributor decides stays theirs.

There is no `client` folder in it, and that is deliberate rather than an omission: the Uno Platform client was
withdrawn, the client is being rebuilt in React, and `frontend/` holds two placeholder directories with no solution in
them. It is read under the `repository` folder until the new stack arrives with whatever editor tooling it turns out to
want. `scripts/test-agent-workflow.sh` asserts the folder list, so a folder pointing at nothing cannot land quietly.

## Commands

For the normal implementation loop, run:

```bash
bash scripts/verify-fast.sh
```

Before committing, run the complete local gate:

```bash
git add <task-files>
bash scripts/verify-full.sh
```

Each runs the flow of whichever stack the change reaches — the server's, the
client's, both, or neither — from the same change filters `ci.yml` uses, so the
choice is never one to make by hand;
[Which stack a gate runs](agent-workflow.md#which-stack-a-gate-runs) carries how.

Both write a digest of what they verified under `artifacts/verify/` and answer in
under a second when handed a tree they have already passed over, so running
either one twice is cheap rather than something to avoid — and running it is
always better than reasoning about whether the last run still holds.
`VERIFY_FORCE=1` runs everything regardless.
[A gate does not prove the same tree twice](agent-workflow.md#a-gate-does-not-prove-the-same-tree-twice)
holds what a record claims, what retires one, and why a failing run records
nothing.

While reviewing the change, ask what it obliges elsewhere:

```bash
bash scripts/review-obligations.sh
```

That prints the tests covering each changed source file, named for the service
and sitting beside it for the client, the pages whose `describes:` marker covers
each changed path, and the registers whose trigger moved, each saying whether the
change touched it. It is the same index `Fathom review` runs
on a pull request, reached through an adapter that hands it a local diff, so the
answer is the one the pipeline will give rather than an approximation of it. It
reports and never gates: nothing it prints is a finding until it is confirmed in
the file it points at, and it names the untracked paths no diff contains rather
than describing less than the change while looking complete.

The fast script restores the solution, builds it in Release configuration, runs
all unit tests without rebuilding, and formats the C# files the branch changed —
each against whichever of the two solutions holds it, which
[Building and testing the client](#building-and-testing-the-client) describes for
the client half. It is the only one that rewrites source files, and every
`dotnet format` pass it runs is a repairing one.

Nothing behind it verifies, because the build in front of it has already reported
most of what there is to report. `backend/Directory.Build.props` sets
`EnforceCodeStyleInBuild` beside `TreatWarningsAsErrors` and `.editorconfig` gives
the IDE rules severity `warning`, so `IDE0005`, `IDE0055`, and `IDE0073` are build
errors naming their file and line, several steps before formatting is reached.
`IDE0060` is the one that is neither: it has no code fix for the repairing pass to
apply, and the Release build passes over it despite the same `warning` severity,
so only a verifying pass ever names it. What the repairing pass adds is what no
build sees: the ordering of using directives and a missing final newline are
`dotnet format`'s own passes rather than analyzer rules, and both have code
fixes, so rewriting them is the whole answer.

Restricting the pass to the changed files is what keeps the loop usable.
`dotnet format` reloads the MSBuild workspace on every invocation at a cost that
does not depend on scope, and the analysis after it does: the whole solution
costs several times what a handful of files costs, on any machine.

The full script additionally restores repository tools, runs the workflow
contract suite unless every path the branch touched is a C# file it added or
edited, executes the aggregate coverage gate, verifies formatting, and checks
the Git diff. It rejects remaining untracked files, so inspect the staged diff
before running it. See [Agent workflow](agent-workflow.md) for the workspace
inspection command and shared skills.

Two of its steps read the change rather than the tree, and
[Agent workflow](agent-workflow.md#entry-points) carries the argument for each.
Formatting is verified over the C# files the branch changed, and over the whole
solution only when a shared style or build input changed or was removed — an
`.editorconfig` at any depth, `backend/Directory.Build.props`,
`backend/Directory.Build.targets`, `backend/Directory.Packages.props`, `global.json`, or
`backend/MailFathom.slnx` — which is the list `ci.yml` gives its own `format:` filter. The contract suite is skipped when
every path the branch touched is a C# file it added or edited, and runs whenever
one was removed or moved. `CI` asks both questions unconditionally, so the local
narrowing withholds an earlier verdict rather than the verdict.

The full script fetches the base branch and refuses to continue when the branch
does not contain it, so it needs access to the remote and cannot run offline.
Rebase onto the fetched base when it reports the branch is behind. The fast
script queries only local Git state and remains available offline.

The base is `main` on whichever remote points at `Krzysztof318/MailFathom` —
`origin` here, and conventionally `upstream` in a fork, where `origin` is the
fork and its `main` is whatever was last synced.
[Which remote is the base](agent-workflow.md#which-remote-is-the-base) describes
how that is resolved and what the gate prints when nothing resolves.

Neither gate covers the deployment assets. Testing, building, and publishing what `deploy/` produces is one pipeline's
job rather than several local scripts' — a developer would otherwise need a Docker daemon, a Kubernetes cluster, and
Helm on the machine to learn what a runner can decide once. `Release` and `Nightly` are that pipeline: `Release`
publishes the image to both registries and the Helm chart beside it, and `Nightly` publishes the image alone.
[The container image](container-image.md) and [Kubernetes and Helm](deployment-kubernetes.md) describe what each has to
establish.

The one part with a script of its own is the chart, which needs only Helm and reaches no cluster:

```bash
scripts/render-helm-manifests.sh            # lint, render, and compare against the committed manifests
scripts/render-helm-manifests.sh --update   # take an intended change into them
```

It is what the `Helm chart` job of `repository-contracts.yml` runs — on a pull request touching `deploy/helm/`, on
every push to `main`, and on the revision a nightly or a release publishes — so running it before pushing gives the
same verdict. It stays out of both gates rather than being added to them: Helm is not a prerequisite of
building or testing this repository, and a gate that fails on a missing tool for a change that touched no chart file
would be the cost of covering something the pull request covers anyway.

The `Container image` workflow builds the image for both supported architectures and does nothing else. It is manual
dispatch only, like the integration suite, and it publishes nothing.

What does publish is `Release`, on an annotated version tag, and `Nightly`, on its schedule. Neither is something to
start as part of a task, and neither has a local equivalent: they run `Build, test, and migrations` — the same
workflow `CI` calls for a pull request, with its formatting pass turned off — then, for a release, the integration
suite, and build nothing at all until both have passed. Each also runs `Build and test the client` against the same
revision, with the same pass turned off, for the same reason and with the same standing: it lints, type-checks, tests,
and builds the client and drives the bundle in a browser, so a client that does not hold up blocks the publication
before an image is built. That covers everything a channel produces rather than the image alone: the schema script and
the `mfctl` binaries wait behind the same gate. `Repository contracts` runs on both channels as well, from the same
definition `CI` calls, and it is the one gate whose standing differs between them: a release blocks its image on it, because a tree whose deployment contract no
longer holds is not one to put a digest behind, and a nightly reports it beside a published image, because neither the
contract suite nor the chart is in what a nightly ships. [The container image](container-image.md#published-images) records
what they produce and how a published image is verified.

`Weekly diagnostics` measures `main` once a week: `Hot-path benchmarks` times chunking, ranking, and MIME extraction
with BenchmarkDotNet and leaves the table on the run and in its summary. No job waits on it and a failure there is
swallowed, because throughput on a hosted runner is not reproducible to a precision worth blocking a channel over —
what a change actually has to satisfy is the allocation budget the same paths carry in
`backend/tests/AllocationBudgets.UnitTests`, which the pull-request gate runs. It does have a local equivalent, and running it
is how a suspected regression is looked at before anything is claimed about it:

```bash
dotnet run --project backend/tests/Benchmarks/Benchmarks.csproj --configuration Release -- --filter '*' --artifacts artifacts/benchmarks
```

Both scripts stop immediately when `HEAD` resolves to `main` or `master`,
because verification on the integration branch reports on code that no change
is about to touch. Check out the branch that carries the change first. A
detached `HEAD` and any other branch name are accepted, in the primary checkout
as well as in a linked worktree.

Run the web host directly, against a PostgreSQL server you provide yourself:

```bash
dotnet run --project backend/src/Host/Host.csproj
```

The host inherits the environment it is started in, so a development mailbox on a mail server the platform's TLS
policy refuses is reached by prefixing that command with `OPENSSL_CONF=<path>` — see
[the platform TLS policy](platform-tls-policy.md#pointing-the-host-at-it). The host says at startup that it is running
under a configured file, which is also how to confirm it received one.

## Running locally with Aspire

The Aspire orchestration is the intended local start: it provisions the database, applies the schema, and starts the
host in the right order, so a working MailFathom is one command on a machine with the pinned SDK and a running Docker
daemon:

```bash
dotnet run --project backend/src/AppHost/AppHost.csproj
```

The AppHost's only launch profile is named `http` and sets `ASPIRE_ALLOW_UNSECURED_TRANSPORT`, because there is no
local TLS listener and Aspire otherwise allocates `aspire-dashboard-https` against the ASP.NET development certificate.
That certificate can be present in the user store and still untrusted by the OS; the profile must not require it.

A browser driven from a script uses `scripts/chromium-headless.sh`. Snap Chromium's `/usr/bin/chromium` wrapper
calls `snap-confine`, which cannot change AppArmor profile from an unconfined process; the script starts the chrome
binary on the snap mount instead, with `--headless` and a writable `--user-data-dir`.

Before any resource starts, Aspire asks for any of three parameters it has not already stored:
`mail-account-host`, `mail-account-username`, and the secret `mail-account-password`. They configure one local account
named `local`, over implicit TLS on port 993, with its inbox as the default folder. Aspire offers to keep the values in
the AppHost's user secrets; leaving any of them unanswered leaves the application waiting rather than starting a host
with no mailbox.

Four resources come up, three of them in dependency order. The `postgres` container starts first and has to report
healthy; the `mailfathom-migrations` resource then applies every pending migration to it; and `mailfathom-host` waits
for that run
to complete before starting, which is why the schema gate that fails a fresh deployment on purpose never fires on a
local run — the explicit schema step the deployments require is performed here by the orchestration, before the host
looks. The fourth is `mailfathom-client`, which serves the client's development server and waits for none of them,
since a development server serves the page whether or not the service behind it has started; it is
[described below](#the-client-resource).
Because the host runs in `Development`, it also publishes [the HTTP API document and the explorer](http-api-documentation.md)
at `/openapi/v1.json` and `/scalar` on every port this run actually binds. Neither exists in any other environment, and
neither is on a port of its own. The normal orchestration enables the MCP, administrative, and client surfaces, so the
document carries both HTTP APIs from the first run. The host runs in the `Development` environment on four loopback
sockets — MCP, probes, administration, and the client surface — each on a free port the run takes unless the setting
has a documented pin; the dashboard reports the numbers it took. MCP and administration authenticate nobody in this
local topology. The client surface accepts HTTP Basic and the AppHost provisions `test` / `test-password` for the sole
owner once the host reports startup readiness. A credential already present under that username is left unchanged, so
a local rotation survives the persistent database and every later orchestration restart.

There is no local TLS listener: MailFathom
never serves one out of an ASP.NET Core development certificate, so a developer who wants TLS configures
`McpEndpoint:Https` the way a deployment does, which is also the shape they will ship. None of these sockets is proxied: the
socket a client connects to is the socket Kestrel opened, which is what keeps a TLS handshake — and a client
certificate — a conversation with the host itself. The probe listener is pinned to loopback deliberately, because the
probes answer without a credential and nothing on a local network has any business asking them. The other three are
also loopback because two deliberately authenticate nobody and the remaining one sends a reusable password over plain
HTTP; none is a surface for another machine. The probes keep a socket of their own rather than sharing the MCP
endpoint's. A shared socket is one socket, so the probes would answer wherever the MCP endpoint does. The integration
topology shares one deliberately, and [what that couples](configuration-endpoints.md#which-settings-a-shared-socket-couples)
is the same list a deployment reads.

The four ports are stated by the app model the same way: a TCP endpoint that injects itself into the owning setting —
`McpEndpoint:Port`, `HealthEndpoints:Port`, `AdminEndpoint:Port`, and `ClientEndpoint:Port` — so each number is declared
once rather than declared and then configured again beside it. The scheme is what makes that possible. Aspire builds
`ASPNETCORE_URLS` from HTTP and HTTPS endpoints,
and MailFathom refuses that variable outright, because each surface states where it is served in its own section; a TCP
endpoint is published without ever reaching it. That is also why the resource carries no `WithHttpHealthCheck`, which
derives its address from an HTTP endpoint this app model declares none of.

### The client resource

`mailfathom-client` is the client under `frontend/`, served on a socket of its own that the dashboard lists like any
other endpoint. It is what makes one command bring up a MailFathom with a face on it rather than a service and a second
terminal.

It is an **executable** resource, because there is no project to make it a project one: the client is a Vite
development server, and what starts it is the workspace's own `dev` script. The app model runs
`pnpm dev --host 127.0.0.1 --port <the port this run took> --strictPort` with `frontend/` as the working directory —
the command a developer would type, with the socket stated rather than chosen. `--strictPort` is what makes the number
binding: Vite otherwise moves to the next free port, which would serve the page somewhere the dashboard never linked.

Nothing about this is a reference. The app host holds a path and a command, MSBuild is never told the two stacks are
related, and `backend/MailFathom.slnx` still names nothing under `frontend/` — so building or testing the service
restores no JavaScript, and starting this resource is what pays for the client's own toolchain.

**The client needs Node and pnpm, which the .NET SDK does not bring** — [Building and testing the
client](#building-and-testing-the-client) is the same requirement stated for the verification gates. On a machine
without them this resource fails on its own, naming the command it could not start, while PostgreSQL, the migrations,
and the host still come up: nothing waits for the client. The orchestration installs nothing either, so a fresh
checkout runs `pnpm install --frozen-lockfile` in `frontend/` once before the resource can start; a restore on every
orchestration start would be seconds paid on every run for something that changes when the lock file does.

To run the orchestration without it at all, state so in the app host's own user secrets, where the pinned ports live
and for the same reason — it is a decision about one machine rather than about a checkout:

```bash
dotnet user-secrets --project backend/src/AppHost/AppHost.csproj set "Client:Enabled" "false"
```

Its environment form leaves the client out of a single run: `Client__Enabled=false dotnet run --project
backend/src/AppHost/AppHost.csproj`. A value that is neither `true` nor `false` fails the app host at startup naming the
key, rather than being ignored and starting the resource the developer was avoiding.

The `IntegrationTesting=true` switch that selects the ephemeral topology leaves the client out of it entirely, the way
it decides every other resource there: a suite that tests the service would otherwise install a package graph on every
run that no test reads.

**What tells the client where the service is, is the process rather than the build.** The app model writes
`VITE_MAILFATHOM_SERVICE_ADDRESS` into the development server's environment, holding the client surface's own origin —
`http://127.0.0.1:<the port the client surface took>`, with no trailing separator, because the client appends its own
`/api/client` prefix to it. Vite exposes every `VITE_`-prefixed variable of its process environment on
`import.meta.env`, so the page reads the port this run took without a property, a generated file, or a rebuild;
`frontend/src/Client.App/src/environment.d.ts` is where the client declares it. It is optional there, and deliberately:
a bundle served from the deployment's own container image is fetched from the service it calls, so the origin it was
loaded from is the answer.

The dashboard publishes the client's endpoint under `127.0.0.1`, the same spelling the development server binds.
Aspire's default endpoint host is `localhost`, which resolves to the IPv6 loopback before the IPv4 one on an ordinary
machine; left at the default, the dashboard would link a socket nothing answers on while the page was alive beside it.

### The client surface

What the client calls is the **surface the service serves**, which is the service's own and was never the client's.

**It is enabled by the normal orchestration.** The app model supplies its port and loopback bind and enables the
password method, then provisions `test` / `test-password` through the ordinary administrative API — so password policy,
hashing, ownership, and audit behavior are the same as for a credential an operator created. It does not write
`ClientEndpoint:Cors:AllowedOrigins`, so the product default of every origin stands: `localhost` and `127.0.0.1` are two
origins a browser treats as distinct, and a local run that named only one of them refused the first API call from a tab
opened as the other. A deployment names the origin it actually serves.

It is on `127.0.0.1`, because the only thing that calls it is something running on this machine, and that is also why
it is a socket of its own rather than a share of the MCP endpoint's: a wildcard bind beside a specific one on a single
port is two sockets the operating system grants only one of, so sharing would have published the client surface
wherever the MCP endpoint is published. A run started with `Client:Enabled` false starts no client, and the surface and
its Basic credential stay available for a client started by hand or for a direct request.

### Pinning a port

Every port this topology publishes is taken free per run, which is what lets several checkouts of this repository run
their orchestrations at the same time: a fixed port is one port, and the second run to ask for it fails to bind and
exits. The integration-test topology has never used a fixed one, for that same reason.

A port taken per run moves between runs, and an address written once into an MCP client's configuration, a database
tool, or a browser tab should not have to. So each socket is pinned on its own, in the app host's own user secrets —
where a decision about one machine belongs, out of every checkout:

```bash
dotnet user-secrets --project backend/src/AppHost/AppHost.csproj set "Ports:McpEndpoint" "8080"
dotnet user-secrets --project backend/src/AppHost/AppHost.csproj set "Ports:HealthEndpoints" "8081"
dotnet user-secrets --project backend/src/AppHost/AppHost.csproj set "Ports:Postgres" "5432"
dotnet user-secrets --project backend/src/AppHost/AppHost.csproj set "Ports:ClientEndpoint" "8082"
dotnet user-secrets --project backend/src/AppHost/AppHost.csproj set "Ports:Client" "5173"
```

`8080` and `8081` are the ports [the container image](container-image.md) publishes and `5432` is PostgreSQL's own, so
those three values are what makes a local run answer where a deployment does; the last two answer a different want.
`Ports:ClientEndpoint` is a request written once against `/api/client`, and `Ports:Client` is a bookmarked browser
tab — `5173` is Vite's own default. Each key is read on its own, so pinning the MCP endpoint leaves the probes, the
database, the client surface, and the client on whatever the run takes. A value that is not a port
number between `1` and `65535` fails the app host at startup naming the key, rather than being ignored and leaving the
address to move anyway.

That store is keyed by the `UserSecretsId` in `backend/src/AppHost/AppHost.csproj`, which is one fixed identifier, so a port
pinned there is pinned for **every checkout on the machine** — which is the collision above, taken deliberately for the
address it buys. It is also loaded in the `Development` environment only, which is what the app host's only launch
profile runs it in. The environment form of each key is what pins a port for one run: `Ports__McpEndpoint`,
`Ports__HealthEndpoints`, `Ports__Postgres`, `Ports__ClientEndpoint`, and `Ports__Client` are read after the store and
therefore win over it.

```bash
Ports__McpEndpoint=8080 dotnet run --project backend/src/AppHost/AppHost.csproj
```

A port nothing pinned is read from the dashboard, which lists each resource's endpoints, and from the host's own
startup log, which reports the socket every surface bound.

The Aspire dashboard is served over TLS and presents the ASP.NET Core development certificate, which Kestrel uses for
an address no endpoint configuration claims. Create and trust it once per machine — without a certificate at all the app
host fails to start with nothing to present, and with an untrusted one it starts while the browser refuses the
handshake:

```bash
dotnet dev-certs https --trust
```

The MCP endpoint's own HTTPS profiles are the deployed shape and stay unconfigured locally, so a checkout needs no
certificate material of its own. [The MCP endpoint](mcp-endpoint.md) describes what a deployment configures instead.

The AppHost prints the dashboard address, including a one-time login link, as it starts. The dashboard is where a
local run is observed: per-resource console output, structured logs, traces, and metrics, all delivered over OTLP
because Aspire injects `OTEL_EXPORTER_OTLP_ENDPOINT` into the resources it starts. That injection is currently the
only place telemetry export is configured at all — a deployment exports nothing until an operator sets the variable
themselves. [Telemetry](telemetry.md) records what the host emits and where it goes.

A freshly started normal orchestration synchronizes the mailbox supplied through its required parameters. Its MCP and
administrative endpoints accept unauthenticated local requests, and the client endpoint accepts the synthetic Basic
credential above. These are AppHost development choices rather than changed product defaults: starting `Host.csproj`
directly or running a deployment still reads the shipped disabled endpoint defaults.

The normal AppHost topology starts `mailfathom-host` under the repository's supported OpenSSL security-level-1 sample,
so an older mail server is less likely to fail its TLS handshake under the misleading name of an authentication failure.
[The platform TLS policy](platform-tls-policy.md) covers exactly what the file admits and what it leaves unchanged. An
explicitly exported `OPENSSL_CONF` takes precedence when a developer needs another policy; the integration-test topology
receives neither value, because a suite whose handshakes depended on the machine that ran it would prove nothing.

An MCP client connects to `http://127.0.0.1:<the port the MCP endpoint took>/mcp` without a credential. An
administrative client uses the dashboard's `admin` address the same way, while the browser client signs in as `test`
with `test-password`. Stopping the orchestration with `Ctrl+C` — or
`aspire stop --apphost backend/src/AppHost/AppHost.csproj --non-interactive` — leaves the synchronized mail in place, because
the database volume outlives the container and the container outlives the run. The container is stopped rather than
left running, so the next start is a restart of the server that was there.

The AppHost PostgreSQL resource uses the `pgvector/pgvector:0.8.6-pg18` image so local development starts with a PostgreSQL server that can support the `vector` extension required by the RAG and embedding slices. It keeps its data in a named Docker volume, so synchronized mail survives a restart instead of costing a full IMAP synchronization every time the orchestration stops.

That volume is mounted at `/var/lib/postgresql` rather than through Aspire's `WithDataVolume`, which would choose the wrong path here. PostgreSQL 18 moved the image's data directory into a version-specific subdirectory and moved the declared volume up to the parent, and Aspire picks between the two by parsing a major version out of the image tag — taking everything before the first `-`, which on a pgvector tag shaped `0.8.6-pg18` is `0.8.6`, and reading the major component of that, which is `0`. The version test therefore never sees 18: it would mount the pre-18 path, the server would write to neither, and the database would live in the container's writable layer until the container was removed. The volume keeps the name `WithDataVolume` would have generated, so it is still one database per checkout.

That name is also the one a checkout used before PostgreSQL 18, which is what a first start after this change runs
into: the volume holding a PostgreSQL 17 data directory is mounted at the parent path, and the image refuses to start
against it rather than initializing a second cluster beside it. The container exits `1` saying *there appears to be
PostgreSQL data in: `/var/lib/postgresql`*, so the server never listens and the resource never becomes healthy. A
local database is resynchronized rather than preserved, so remove the volume and let the next start build it — the
deployment path, where the mail is worth a dump and a restore, is
[Upgrading a deployment that ran PostgreSQL 17](deployment-compose.md#upgrading-a-deployment-that-ran-postgresql-17):

```bash
docker volume ls --filter name=postgres-data      # one per checkout, named after its AppHost
docker volume rm <the volume this checkout owns>
```

The resource is also given a persistent container lifetime, which the ephemeral integration-test topology deliberately
is not. A session lifetime removes the server on every shutdown and builds it again on the next start — an image check,
an initialization pass, and a health wait, several times a day, against data that was never in question. A persistent
container is reattached instead, so the server a developer stops is the server they get back, and removing it is an
explicit `docker rm -f` rather than something stopping the app host does.

Aspire's persistent lifetime also leaves the container *running* after the app host exits, which is not what a stopped
orchestration should leave behind, and the lifetime has no third value between that and destroying the container. So
`PersistentContainerStopper` stops it during shutdown: what outlives an orchestration is the container and its data
rather than a PostgreSQL process and the port it holds. A shutdown that runs — `aspire stop`, or an ordinary
termination — stops it; a process killed outright runs nothing and leaves the container up, which is then stopped with
`docker stop` like any other.

The server is reached with conventional credentials, so a database tool needs nothing this repository has to tell it
beyond the port this run published:

| | |
| --- | --- |
| Host and port | `localhost` and the port the dashboard reports — `5432` where it is [pinned](#pinning-a-port) |
| Database | `mailfathom` |
| User and password | `postgres` / `postgres` |

Both credentials are stated by the app model rather than generated. A generated password has to be persisted to stay
stable, and PostgreSQL applies a password once — when it initializes an empty data directory — so a persisted password
and a volume that outlives it can drift apart and the server then refuses a database nothing was wrong with. A value
that never changes cannot drift from itself. It authenticates one local development database whose port Aspire
publishes on the loopback address alone, and no deployment is reached this way: a deployed MailFathom takes its
connection string from [secret provisioning](secret-provisioning.md), which this app model has no part in.

Pinning that port has one consequence worth knowing: a PostgreSQL already listening on the number pinned, whether a
system service or another orchestration, takes it first and the container then fails to start with an address-in-use
error naming it.

`backend/src/AppHost/AppHost.csproj` still declares a `UserSecretsId`, and `backend/src/AppHost/Properties/launchSettings.json` still
sets `DOTNET_ENVIRONMENT=Development`, because that is where Aspire persists what it generates for the app model
itself — the dashboard and OTLP API keys — and where [a pinned port](#pinning-a-port) is stated; user secrets are loaded
in the `Development` environment only.

To discard the local database and start from an empty one, remove the container and its volume:

```bash
aspire stop --apphost backend/src/AppHost/AppHost.csproj --non-interactive
docker volume ls --filter name=-postgres-data
docker rm -f $(docker ps -aq --filter volume=<volume>)
docker volume rm <volume>
```

Aspire names that volume after the AppHost project's path, so every clone and every worktree owns a different one and the name has to be read rather than assumed. List them first and take the one belonging to the checkout being reset; removing another one destroys a database the reset was not about.

## Development secrets

Secrets are never written into configuration as values, in development either. `appsettings.Development.json` sets the interpretation mode to `ReferenceOrInline`, which keeps `plaintext:` references convenient without weakening the shipped `ReferenceOnly` default:

```json
{
  "Secrets": { "Interpretation": "ReferenceOrInline" }
}
```

`backend/src/Host/Host.csproj` declares the `UserSecretsId` those commands write into. It is a fixed identifier rather than one
generated per clone, so every checkout reads the same store and the commands below can be named here at all. The secret
store is loaded by the framework in the `Development` environment only, which is the environment the orchestration and
both launch profiles run the host in.

When the host is started directly rather than through Aspire, configure a development account in
`appsettings.Development.json` or, better, in user secrets:

```bash
dotnet user-secrets --project backend/src/Host/Host.csproj set \
  "MailSynchronization:Accounts:0:Secrets:Password:Name" "dev-password"
dotnet user-secrets --project backend/src/Host/Host.csproj set \
  "MailSynchronization:Accounts:0:Secrets:Password:SecretReference" "plaintext:dev-password"
```

The block shape is identical to production, so moving a working development configuration to a real deployment is one string edit — `plaintext:dev-password` becomes `systemd-credential:imap-primary-password` — rather than a restructuring.

Neither file nor user secrets is a production secret store. User secrets are stored unencrypted in the developer's profile directory and exist only to keep credentials out of the repository; `appsettings.Development.json` is committed and must never hold a real credential. [Secret provisioning](secret-provisioning.md) describes the deployment paths.

**The data-encryption key is the one secret you do not provision locally.** The app model hands the host a fixed development key — `OrchestrationContract.DataEncryptionKeyMaterial`, as a `plaintext:` reference under the key identifier `development` — so a local run seals and opens stored values without anybody generating one first. It is stated rather than generated for the reason the PostgreSQL password is, and with more at stake: a key that diverged from the data volume it protects would leave every locally sealed row unopenable rather than merely reporting an authentication failure. Resetting the local database is what re-seals under a changed key. A deployment provisions its own, which [ADR 0005](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0005-data-encryption-key-ring-and-provisioning.md) records and this app model builds no part of.

## Filling a development mailbox

Working on synchronization, search, embeddings, or answering means having mail to work on. `backend/tools/SyntheticMail` is
where that mail comes from: it invents messages and delivers them over SMTP to a mailbox you name, so the mailbox
MailFathom then synchronizes fills the way a real one does — by mail arriving — rather than by anything reaching inside
it. It exists so that nobody has to point a local run at their own correspondence, which this repository classifies as
sensitive by default.

It is a development tool and is not part of the product. It ships in no artifact, is a command of `mfctl` in no sense,
and no project under `backend/src/` references it; `the_development_tooling_never_reaches_a_published_artifact` in
`scripts/test-agent-workflow.sh` is what holds all three rather than a convention. Build it from source and run it:

```bash
dotnet run --project backend/tools/SyntheticMail -- <recipient> --count 200
```

**Configure the sending account first.** The address and its password are read from
`backend/tools/SyntheticMail/synthetic-mail.local.json`, never from an argument — a password on a command line lands in the
shell history and in the process list of a shared machine. `.gitignore` covers the file as `*.local.json`, and
`synthetic-mail.example.json` beside it shows the shape:

```json
{
  "host": "smtp.example.test",
  "port": 587,
  "security": "StartTls",
  "address": "throwaway@example.test",
  "password": "the throwaway account's password"
}
```

**Use a throwaway account.** The command authenticates as whatever this file names and submits a few hundred messages
under it; nothing about that belongs to an account that reaches anything else. Startup refuses a missing or incomplete
file with a message naming the key to set.

`security` is `StartTls` or `ImplicitTls`, and there is no third value: the run authenticates with a password, so an
endpoint that cannot secure the connection is refused rather than downgraded to. `port` defaults to 587 or 465 to match,
and a written one is refused outside 0 to 65535 rather than carried as far as the connection.

A development mail server whose TLS parameters the platform refuses stops this command exactly as it stops the host,
and for the same reason: the handshake goes through the system OpenSSL rather than through .NET, so the policy on the
machine decides which servers are reachable at all. The symptom sends most people the wrong way — the handshake fails
and reads as a refused credential. [The platform TLS policy](platform-tls-policy.md) covers the file, what relaxing it
costs, and [how to confirm the handshake is what failed](platform-tls-policy.md#confirming-it-is-the-handshake); this
command inherits the environment it is started in, so pointing it at a relaxed policy is a prefix like anywhere else:

```bash
OPENSSL_CONF=/etc/mailfathom/openssl-legacy.cnf dotnet run --project backend/tools/SyntheticMail -- <recipient>
```

The relaxation is process-wide rather than per connection, which here reaches only this command's own submission
session and nothing else, because the process opens no other TLS connection.
`userName` is the address unless the server wants something else. `author` decides whose address generated mail is
`From`: `Fabricated`, the default, invents one and names the account in `Sender`; `SendingAccount` puts the account
there and moves the invented participant to `Reply-To`, which is what a hosted provider that refuses to submit mail
authored by anyone but the authenticated user needs.

**Generation is deterministic and local.** No model is called and no network is reached: word lists, name lists, and
templates carried in the repository, combined by a seeded generator, produce every subject, participant, body, thread,
date, and attachment. The one exception is the opt-in AI content mode below, where the content is the model's and the
seed keeps the rest. A seed is what makes a page boundary, a ranking, or a retrieval result something to assert
against rather than something to look at, so a run that names none chooses one and reports it, together with the exact
invocation that repeats the batch:

```text
Seed 481923: 200 messages dated 2026-05-10..2026-08-08, attachments up to 65536 bytes, 20% carrying fabricated sensitive material.
Repeat this batch with: developer@example.com --seed 481923 --count 200 --days 90 --until 2026-08-08 --attachment-bytes 65536 --sensitive-percentage 20
```

What the corpus varies is what the product actually reads: subject and body length, how many participants a message
names, threading through `In-Reply-To` and `References`, dates spread over the requested range, plain text against HTML
against `multipart/alternative`, `us-ascii` against `iso-8859-1` against `utf-8`, messages with and without an
attachment, and messages with and without something a scanner should find in them. Every invented participant is under
a domain in the reserved `.test` top-level domain, which RFC 6761 guarantees resolves to nothing, so a generated
address cannot reach a person even if it is echoed into a reply. The recipient you name is the only real address a run
touches, and it is the only one the envelope ever carries.

| Option | What it decides |
| --- | --- |
| `--count`, `-n` | How many messages, 1..2000. Defaults to 50. |
| `--seed` | What the corpus is derived from. Chosen and reported when absent. |
| `--days` | How far back from `--until` the dates reach, 1..3650. Defaults to 90. |
| `--until` | The newest day a message is dated, as `yyyy-MM-dd`. Defaults to today and is reported either way. |
| `--attachment-bytes` | The ceiling on one attachment, 0..10485760. Zero generates a corpus carrying none. Defaults to 65536. |
| `--sensitive-percentage` | How often a message carries a fabricated secret or personal identifier, 0..100. Zero generates a corpus carrying none. Defaults to 20. |
| `--interval` | Milliseconds between two submissions, 0..60000, so a real server is not hit with a burst. Defaults to 250. |
| `--config` | The credential file to read, when it is not the one beside the built command. |
| `--dry-run` | Generate and list the corpus on standard output without submitting anything. In AI content mode the provider is still called, because the content is what the listing lists. |
| `--ai` | Generate the message content with the configured OpenAI provider instead of the seeded vocabulary. The sending account is still required. |
| `--language` | The languages AI-generated messages are written in, comma-separated, as in `en` or `en,pl,de`. Defaults to `en`. Requires `--ai`. |
| `--topic` | The topics AI-generated messages are written about, comma-separated, as in `business` or `invoices,technical-support,travel`. Defaults to every supported topic. Requires `--ai`. |
| `--ai-config` | The AI provider file to read, when it is not the one beside the built command. |

### Mail with something in it to find

A fifth of a generated batch carries a fabricated credential or a fabricated personal identifier, written into a
paragraph the way somebody pastes one into a thread. That is what makes
[sensitive-content scanning](../features/sensitive-content-scanning.md) something you can watch working: fill a mailbox,
synchronize it, switch a scanner on, and compare what a search or an `ask_mail` answer returns against the same corpus
scanned with it off. `--sensitive-percentage 0` produces a corpus with nothing in it to find, and `100` one where every
message carries something; both are ordinary answers, and a run says which it produced in the line that repeats it.

The kinds are taken in turn rather than drawn, so a batch large enough to plant a dozen carries every one of them
equally often. They cover each category the two scanners look for unless a deployment names categories of its own:

| What a message carries | Reported as | Found by |
| --- | --- | --- |
| A hosting provider's access token | `Secrets` / `ProviderToken` | the secret rule corpus |
| A cloud access-key identifier | `Secrets` / `CloudAccessKey` | the secret rule corpus |
| An armoured private key | `Secrets` / `PrivateKey` | the secret rule corpus |
| A JSON Web Token | `Secrets` / `JsonWebToken` | the secret rule corpus |
| A database connection string carrying its password | `Secrets` / `ConnectionString` | the secret rule corpus |
| A download link whose query string is the credential | `Secrets` / `CredentialUrl` | the secret rule corpus |
| A payment card number | `Pii` / `PaymentCard` | the analyzer, asked in English, Spanish, Italian, or Polish |
| An IBAN | `Pii` / `BankAccount` | the analyzer, in any language |
| A medical licence number | `Pii` / `HealthIdentifier` | the analyzer, in any language |
| A PESEL | `Pii` / `NationalIdentifier` | the analyzer, asked in Polish |
| A social security number | `Pii` / `NationalIdentifier` | the analyzer, asked in English |
| A passport number | `Pii` / `IdentityDocument` | the analyzer, asked in English |

**Which of the personal identifiers a deployment finds follows the languages it asks the analyzer in**, because a
recogniser for a country's identification number is registered for one language and is not loaded for the others. A
deployment on the default `en` finds the social security and passport numbers and never the PESEL; one configured for
`pl` alone finds the PESEL and neither of the other two; one naming both finds all three, since a scan asks once per
language and merges what came back. The IBAN and the medical licence number are found either way, because the
recognisers behind them are registered for no particular language, and the payment card in the four its recogniser
names. A decoy nothing reports is therefore worth checking against
`SensitiveContent:PersonalDataAnalyzer:Languages` before it is read as a defect —
[the analyzer's languages](personal-data-analyzer-languages.md) has the whole table.

**Every value is fabricated at run time and is structurally valid.** A payment card passes Luhn, an IBAN its
remainder, a PESEL its weighted sum, a medical licence its own check digit — an identifier that failed its validator
would be discarded by the analyzer and the corpus would quietly test nothing. Structural validity is not identity: the
digits before the check digit are drawn from the seed, every host is under `.test`, and no value here belongs to
anybody or opens anything. Nothing shaped like a credential is committed to this repository either — a value exists
only in a running process and in the mailbox it was delivered to.

**Where in its sentence a value is written varies too, and it is the second half of what a decoy tests.** A rule
recognises a credential by its shape and then has to establish where that shape ends, so the character standing after
the value is part of the question it is asked. A value is therefore planted four ways — inside the sentence, closing
it, in brackets, and as a cell of a pipe-delimited table — and the placement advances once per complete cycle of kinds,
so every kind meets every placement rather than each kind meeting one. A corpus that wrote every value the same way
exercised one answer and reported the other three as though they could not fail, which is exactly what it did until a
rule that could not see past a full stop was found in a mailbox rather than here.

A dry run names the category a message carries and the placement it was written in, and never the value, exactly as a
scanner's finding does. The seed is what reproduces a value; the message is where it is.

### AI-generated content

The default mode writes every word from word lists in the repository. `--ai` is the opt-in mode in which the
*content* — subject and body, greeting and signature — is written by a model instead, and OpenAI is the only provider
the implementation reaches: the same single client construction [ADR 0011](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0011-reaching-a-provider-outside-the-openai-wire-protocol.md)
records for the service — the OpenAI wire protocol, a base address, and a key — built by the tool directly, because a
development tool composes from its own files rather than from the service's dependency injection.

**Configure the provider first.** The key, the model, and the endpoint are read from
`backend/tools/SyntheticMail/synthetic-mail-ai.local.json`, never from an argument, for the reason the sending
account's password is not. `.gitignore` covers the file as `*.local.json`, and `synthetic-mail-ai.example.json`
beside it shows the shape:

```json
{
  "apiKey": "the endpoint's API key",
  "model": "the model name the endpoint routes to",
  "endpoint": "https://api.openai.com/v1"
}
```

`endpoint` is optional and defaults to the provider's own address; written, it must be an absolute https address,
because the key travels in a header and there is no unsecured option. A run that finds the file missing or
incomplete says so with the file and the key to set, before it generates anything, for the reason the sending
account does.

**What the seed still decides, and what it no longer does.** The envelope is still the seed's: author, participants,
threading, dates, attachments, and the fabricated sensitive material are drawn exactly as in the default mode, and
the seed is reported and repeated the same way. Each message's language and topic are drawn from the seed too, so a
batch of one hundred with `--language en,pl` has a reproducible *assignment* of English and Polish. What the seed no
longer decides is the words: two dry runs of one seed in AI mode agree on everything the listing's columns carry and
differ in the subjects and bodies, so the `diff` below compares envelopes, not content. A reply keeps the subject of
the thread it answers rather than one the model invents, because the threading is what a corpus exists to exercise.
The MIME shape a message takes is still drawn from the seed; the charset is not, because a body written in the
language named is one the vocabulary's three charsets cannot be promised to hold, and `utf-8` is the one that holds
any of them.

**What leaves the machine.** In this mode a run sends the generation prompt to the endpoint and reads the message
content back. The prompt names the language, the topic, who writes the message, and — for a reply — the subject of
the thread it answers, and nothing more: no recipient, no body from another message, and nothing the default mode
would not have invented locally. The answer is message content, and neither the prompt nor the answer reaches a log,
for the reason the repository keeps both out of one.

**What a refusal says.** A provider that refuses the key, does not serve the named model, rate-limits, times out, or
fails the request stops the run with one line naming the move — check the key, check the model, wait, retry — and
delivers nothing, because a mailbox holding a prefix of the batch the seed describes is the failure this tool exists
to avoid.

The corpus listing goes to standard output and everything the run says about itself to standard error, so two dry runs
of one seed are compared with an ordinary `diff`:

```bash
diff <(dotnet run --project backend/tools/SyntheticMail -- a@example.test --dry-run --seed 42 2>/dev/null) \
     <(dotnet run --project backend/tools/SyntheticMail -- a@example.test --dry-run --seed 42 2>/dev/null)
```

A batch reports how many were delivered and names each message the server refused rather than stopping at the first
one, and it exits non-zero when any failed — a mailbox holding an unknown prefix of a corpus is worse than one that
finished and said which messages are missing from it.

The integration suite composes its own mail through the same generator, in `OrchestratedMailbox`, so there is one
implementation of *build a synthetic message* rather than two that would drift.

## Command-line tooling

The repository provisions no development environment, so install the SDK and any command-line tools on the developer machine. Repository-local tools declared in `.config/dotnet-tools.json` come from `dotnet tool restore`: `reportgenerator` merges the per-assembly Cobertura reports the coverage run produces, `dotnet-ef` generates and scripts migrations, and `dotnet-stryker` measures the mutation score `Weekly diagnostics` reports for `Domain` and `Application` through `scripts/mutation-score.sh`. Each is pinned there because each runs in continuous integration, which is also what keeps `dotnet-ef` at one version across every machine instead of at whichever one a developer installed. [The mutation score is read, never enforced](agent-workflow.md#the-mutation-score-is-read-never-enforced) states what that last report answers that coverage no longer does, and why nothing gates on it.

Two tools are installed globally when their workflows are needed:

```bash
dotnet tool install --global Aspire.Cli --version 13.4.6
dotnet tool install --global csharp-ls --version 0.26.0
```

`aspire` is only required for Aspire CLI workflows against the AppHost. `csharp-ls` is the C# language server that editors and agent tooling launch to resolve symbols before editing, instead of discovering a misspelled type at build time.

`csharp-ls` is installed globally rather than pinned in `.config/dotnet-tools.json` because a manifest-local tool is only reachable as `dotnet tool run csharp-ls`; it never lands on `PATH`, so a client that launches the bare `csharp-ls` executable still fails with `ENOENT`. A global install puts it in `~/.dotnet/tools`, which is on `PATH`, and keeps the language server out of the `dotnet tool restore` that continuous integration runs. `Aspire.Cli` is recorded in `THIRD_PARTY_LICENSES.md`, because continuous integration installs it at this version too; keep the register aligned when you move to a newer one. `csharp-ls` has no row there and nothing is missing: the register records what this repository pins, invokes, or ships, and a language server an editor launches on a contributor's machine is none of the three. The version above is still the reviewed one to install.

### EF Core design-time commands

Which mechanism a command uses is decided by one question: whether it needs a database.

**A command that reaches a database goes through the AppHost's `mailfathom-migrations` resource**, so it uses the server the orchestration provisions and the connection string it issues rather than a local environment that can differ from every real one.

Aspire 13 has no `aspire exec` command; earlier versions offered one, and it is gone. Its replacement is the `Aspire.Hosting.EntityFrameworkCore` package, which declares a migration resource in the app model. `backend/src/AppHost/Program.cs` adds it against the host project, points it at `backend/src/Infrastructure` for the migrations, and calls `RunDatabaseUpdateOnStart`, so a local run applies pending migrations before the host starts and the host waits for that to finish.

```bash
aspire resource mailfathom-migrations ef-database-status --apphost backend/src/AppHost/AppHost.csproj --non-interactive
aspire resource mailfathom-migrations ef-database-update --apphost backend/src/AppHost/AppHost.csproj --non-interactive
aspire resource mailfathom-migrations ef-database-reset  --apphost backend/src/AppHost/AppHost.csproj --non-interactive
```

The same commands are available from the dashboard. `ef-database-reset` drops the database and replays every migration into it, which is how local data is cleared; it changes no file in the repository.

**A command that reads only the checkout calls `dotnet ef` directly**, because it has no database it could see wrongly. Generating a migration, scripting one to SQL, and asking whether the model has outrun its migrations all compare the compiled model against the committed model snapshot, and they produce identical output against a database that does not exist. `scripts/add-migration.sh` and `scripts/script-migration.sh` are those commands. Both export a design-time connection string pointing at a port nothing listens on when the environment carries none, so a future version that starts requiring a connection fails there instead of silently reaching whichever database the shell happened to name.

That split is why generating a migration needs no Docker and takes seconds, while applying one needs the orchestration running. It also keeps the two failures apart: a migration that generates cleanly and fails to apply is worth seeing as two separate outcomes.

`dotnet-ef` is pinned in `.config/dotnet-tools.json` and arrives with `dotnet tool restore`. The migration resource fetches its own copy, so a global install is only needed by an editor that runs design-time commands of its own.

`Host` is the startup project, because it is the resource the orchestration issues the connection string to, and it therefore carries a design-time-only reference to `Microsoft.EntityFrameworkCore.Design`. `Infrastructure` owns the context, the design-time factory, and the migrations under `backend/src/Infrastructure/Persistence/Migrations/`.

`MailFathomDbContextDesignTimeFactory` gives EF Core a context without starting the host, which matters because the host composes its connection string during startup and design-time tooling never runs that. It reads `ConnectionStrings__mailfathom` when the orchestration supplies it, then `MAILFATHOM_DESIGN_TIME_CONNECTION_STRING` for a command run outside it, and falls back to `Host=localhost;Database=mailfathom;Username=mailfathom`. The orchestrated value wins so a stale override left in a shell cannot point a migration at a different database than the one being migrated.

It is used only when it is a connection string. Under `aspire publish` there is no database to name, so that variable carries the unresolved manifest expression `{mailfathom.connectionString}` — non-empty, and therefore not caught by an emptiness check — and the schema artifact the release attaches is generated in exactly that mode. A value the Npgsql parser rejects is skipped as though the variable were unset, because no connection could be opened from it and a command that opens none should not fail on it. The vector mapping is what made this observable: it needs a data source, which the provider builds while the options are constructed, so the string is parsed by every command rather than only by one that connects.

`MAILFATHOM_DESIGN_TIME_CONNECTION_STRING` is deliberately not covered by that tolerance. It is written by hand for a command that usually does reach a database, so a malformed one fails rather than quietly sending `ef-database-update` to `localhost`.

Every migration in the repository is permanent. A model change appends one with `scripts/add-migration.sh <MigrationName>` and never regenerates, renames, reorders, or deletes an existing one, because a migration identifier that a database has written into its `__EFMigrationsHistory` can never be reached again once it is regenerated: that database can then only be recreated, destroying whatever it held. Nothing in the repository deletes a migration, and no command offers to.

`scripts/script-migration.sh` writes the SQL for a migration range to standard output, which is what a review reads — the generated C# hides the destructive operation, the rewrite EF inferred from a rename, and the lock a column change takes. `scripts/dump-local-schema.sh` then shows the schema PostgreSQL actually holds after the migration is applied. The `add-migration` skill is the surrounding workflow, including the review, which no script performs.

`Pending model changes` in CI runs `dotnet ef migrations has-pending-model-changes` on every pull request touching `backend/src/`, so a model change merged without its migration fails there rather than at a host's startup. Configuration that produces no SQL — a constraint name, an index filter — still moves the model snapshot, so that job can fail on a change that alters no schema; the snapshot is regenerated by EF and never hand-edited.

The baseline migration also installs the `vector` extension. The `pgvector/pgvector` image ships it but does not install it, so without this the first vector column would fail on a type PostgreSQL does not know.

#### Apply policy

The host never applies migrations, in any environment. It reads the migration history at startup and fails fast when the database has not applied every migration the running build defines, so an instance either serves traffic against a known schema or does not serve traffic at all. A pending migration reports error code `32001` and an unreadable migration history `32002`.

It then checks one thing the migration identifiers cannot express. `Persistence:TextSearchConfiguration` is compiled into the search vector's stored generated column when the table is created, and the identifier of the migration that created it is the same whichever configuration produced it. A host configured for `english` against an index built with `simple` would stem its queries one way and read lexemes built the other, returning fewer results rather than an error, so the host compares its configured value against the expression PostgreSQL actually holds and fails with `32003` when they differ.

Generating a migration for a non-default configuration is therefore a deliberate act: export `Persistence__TextSearchConfiguration` before running the `add-migration` workflow, and rebuild the search documents afterwards. The design-time factory reads that variable, which is the double-underscore encoding of the setting a deployment already has.

Applying is one mechanism per environment: `mailfathom-migrations` locally, and an explicit deployment step elsewhere. A host that mutates schema while starting could race a second instance, could apply a destructive change nobody reviewed at deploy time, and would leave the operator no point at which to take a backup.

That deployment step is one idempotent SQL file, and `scripts/build-schema-artifact.sh` produces it:

```bash
scripts/build-schema-artifact.sh      # artifacts/schema/mailfathom-schema-<version>.sql, and its .sha256
```

It runs `aspire publish`, which reads the `PublishAsMigrationScript` declaration in `backend/src/AppHost/Program.cs`, so the file a release attaches and the file this produces come from one statement rather than two. Like the other commands that only read the checkout it reaches no database: the SQL is generated from the migration assembly, so it produces identical output against a server that does not exist. Unlike them it needs the Aspire CLI rather than `dotnet-ef`, because the declaration it reads lives in the app model. [Applying the database schema](database-schema.md) is what an operator then does with it.

The documentation site is the other artifact a checkout produces, and `scripts/build-docs-site.sh` produces it:

```bash
scripts/build-docs-site.sh                 # artifacts/docs-site
dotnet docfx serve artifacts/docs-site     # http://localhost:8080
```

It restores the solution — the API reference is generated by loading every project through MSBuild — and then runs
docfx once, which takes a few minutes the first time. A link docfx resolves to nothing fails the build rather than
reaching a reader as a 404. Last it writes the artifacts an AI agent reads — the map, each page's Markdown source, and
the user guide's two bundles — and refuses a map and a set of pages that disagree.
[The documentation site](documentation-site.md) records what the site carries, which versions it publishes, and what a
new page under `docs/` owes it.

The GitHub CLI (`gh`) is installed separately through the operating system package manager and is required for the issue and pull-request workflow in [Issue tracking and the roadmap board](issue-tracking.md). It needs the `project` scope on top of its default scopes so it can read and update the roadmap board.

On a machine that has never authenticated, log in and request the scope in the same step:

```bash
gh auth login -s project
```

On a machine that is already authenticated, add the scope to the stored credentials instead; `gh auth refresh` only expands existing credentials and fails when no host is authenticated:

```bash
gh auth refresh -s project
```

Confirm the result with `gh auth status`, which must list `project` among the token scopes. `THIRD_PARTY_LICENSES.md` reviews `gh` alongside the other developer tooling and pins no version for it: every copy this repository runs is the one a runner image or a distribution provided.

## Package sources and lock files

Three files decide what a restore produces, and each answers a different question. `backend/Directory.Packages.props` pins the version of every directly referenced package. The repository-root `NuGet.config` decides which sources those packages may come from. Each project's `packages.lock.json` records the transitive closure the pins resolve to, one `resolved` version and one content hash per package.

`NuGet.config` exists because NuGet merges every configuration file on the path from the drive root down to the working directory. Without a repository-owned file the source list is whatever the developer machine defines, so a privately configured feed would be searched for every package here and a restore could resolve a dependency from a source `THIRD_PARTY_LICENSES.md` never reviewed. The file clears that inherited list and declares `nuget.org` alone. Its package source mapping then requires every package identifier, transitive ones included, to match a pattern before it can be restored; the single `*` pattern costs nothing while there is one source, and it makes a second source fail closed rather than silently join the search.

Lock files close the gap central pinning leaves open. The 52 pins in `backend/Directory.Packages.props` are direct references; `backend/src/Infrastructure` alone resolves 47 further packages transitively, and nothing recorded those before. The content hash also means a package republished under a version already pinned no longer passes unnoticed, and a dependency bump shows every transitive move in the pull request diff.

Eighteen of the twenty projects carry one. `AppHost` and `IntegrationTests` do not, because `Aspire.AppHost.Sdk` adds `Aspire.Dashboard.Sdk.<rid>` and `Aspire.Hosting.Orchestration.<rid>` as references chosen from `NETCoreSdkRuntimeIdentifier`. That part of the graph describes the machine running restore rather than this repository, so a lock file written on Linux names packages a Windows, macOS, or Linux ARM64 developer never asks for, and locked mode there fails with `NU1004: A new package reference was found Aspire.Dashboard.Sdk.win-x64` before a build can start. `IntegrationTests` follows `AppHost` because it references the project and inherits those packages transitively, and a lock file cannot exclude a subtree. Both ship nowhere, and their versions stay pinned centrally like every other project's.

The lock files are committed. Both verification scripts restore in locked mode, `scripts/run-integration-tests.sh` does the same for the integration project — where the flag still enforces the lock files of every project it references — and every job of `CI` that restores does it; the `Integration tests` workflow inherits it through the script it calls. A restore that would have to rewrite a lock file fails there instead:

```text
NU1004: The package reference Roslynator.Analyzers version has changed from [4.13.1, ) to [4.13.0, ).
The packages lock file is inconsistent with the project dependencies so restore can't be run in locked mode.
```

That is the expected result of moving a pin without regenerating. Regenerate deliberately, in the same change:

```bash
dotnet restore backend/MailFathom.slnx --force-evaluate
```

Then read the resulting diff before committing it. A bump that moves one direct version and forty transitive ones is a different review from one that moves only itself, and locked mode exists so that difference is visible rather than discovered later.

## Reading every pin against its upstream

A version reaches a run from one of eight files, in seven syntaxes: the `PackageVersion` entries in `backend/Directory.Packages.props`, the exact versions the client's `frontend/package.json` and `frontend/src/*/package.json` declare, the three crate pins in `frontend/src-tauri/Cargo.toml`, the tool versions in `.config/dotnet-tools.json`, the SDK floor and any `msbuild-sdks` entries in `global.json`, the `uses:` references in `.github/workflows/`, and the container image tags and digests the deployment assets and the AppHost carry. `scripts/update-dependencies.sh` reads all of them in one pass, against the upstream that publishes each — nuget.org, registry.npmjs.org, crates.io, GitHub, the .NET release index, and the image registries — and against the terms `THIRD_PARTY_LICENSES.md` recorded when that version was reviewed.

```bash
scripts/update-dependencies.sh                      # survey every pin and report; writes nothing
scripts/update-dependencies.sh --apply              # and rewrite the pins that can be written mechanically
scripts/update-dependencies.sh --apply --verify     # and then run scripts/verify-full.sh over the result
scripts/update-dependencies.sh --only nuget         # one family at a time: nuget, npm, crates, tools, sdk,
                                                    # actions, images
```

Each pin comes back as `current`, `behind`, `ahead`, `moved`, or `unknown`, beside the licence identifier its upstream declares *now* and the identifiers the register records for it. A disagreement between those two is printed as a line to read rather than as a failure, which is the same contract [`scripts/review-obligations.sh`](agent-workflow.md#entry-points) carries: the licence is read from the upstream's own metadata and the register side is read out of prose by pattern, so a flag is a place to look and never a conclusion. A `behind` row whose leading version segment moved also carries a `MAJOR` line, because that is the bump SemVer stops promising compatibility across — and under `0.y.z`, where it makes that promise about the minor instead, a moving minor is marked the same way. The line says where a break is *permitted* rather than that one happened, which is why it ends by pointing at the upstream's release notes: nothing here fetches them, and reading them is [the first of the four questions](agent-workflow.md#dependency-update-pull-requests) a bump answers. A reference outside version ordering carries no such line rather than a guessed one — a digest, and the timestamp naming a MinIO-derived release, are both that case. `moved` belongs to a digest pin alone: two digests are either equal or they are not, and which of them is newer is not a question a digest can answer, so a difference is reported as a difference rather than ranked. The whole run exits zero whatever it found, and a family whose host does not answer is reported as `unresolved` rather than ending the survey.

`--apply` rewrites five of the seven: `backend/Directory.Packages.props`, the client's `package.json` manifests, `frontend/src-tauri/Cargo.toml`, `.config/dotnet-tools.json`, and the workflow references, along with `global.json`'s `msbuild-sdks` whenever it declares any — it declares none today, the Uno SDK having left with the client it belonged to. It then regenerates whichever lock files the pins it actually wrote belong to, and only those: the `--force-evaluate` restore above for a NuGet or MSBuild SDK pin, `pnpm install --lockfile-only` for a client package pin, and `cargo update --package <crate>` for each crate pin it moved — named crate by crate rather than as one bare `cargo update`, which would re-resolve the whole five-hundred-crate graph instead of the pin that changed. A tool manifest and an action reference reach no project graph, so neither obliges a lock file at all. Each ecosystem's tool is needed only when that ecosystem moved, which is why a survey runs on a machine that has never built either stack.

Two families are surveyed and never rewritten:

- **`global.json`'s `sdk.version` is a floor rather than a version.** `rollForward: latestFeature` means the toolchain a run executes is chosen on the machine, so moving the floor changes what this repository *requires* rather than what it uses, and that decides who can build it.
- **A container image pin is written in up to four assets in four syntaxes** — a Compose default, a Helm value split across `registry`, `repository`, and either `tag` or `digest`, a Quadlet unit source, and an AppHost call — and two of them are digests. Moving one also obliges the golden manifests under `deploy/helm/mailfathom/ci/golden/`, which only `scripts/render-helm-manifests.sh --update` may write. The survey names every file carrying each reference so the blast radius is visible; the edit stays a person's.

It never edits `THIRD_PARTY_LICENSES.md` either. A row there is a completed review written as prose — what the component is used for, what its terms oblige, which of them a distribution has to discharge — and a machine cannot restate one. What `--apply` prints instead, for every pin it actually rewrote, is the register lines still naming the version that pin moved from, by line number, so that edit is guided rather than searched for. A survey prints none of it: a pin nothing moved leaves the register saying something still true, and sending a reader to a correct row is worse than saying nothing.

A moved client pin costs the register one thing more, and the run says so rather than leaving it to be found. The register records each of the client's two closures as a census — how many packages resolve under which terms, and every one of them carrying a condition — and a census is a count nothing here recomputes from a manifest. So re-running the two enumeration commands in that file's § *The client's two dependency closures* is part of the same change as the pin, exactly as regenerating the lock file is.

The survey needs the network: nuget.org, registry.npmjs.org, crates.io, the .NET release index, GitHub through `gh`, and the three registries the images live in. It is not part of either verification script and nothing gates on it, for the reason [the actions section below](#keeping-the-pinned-actions-current) gives about proposals: what a dependency is worth updating to is a judgement each time, and this is the reading that makes the judgement cheap.

Taking that judgement is the `update-dependencies` skill, which runs the survey before `start-task` — it writes nothing and needs no branch, so that order is what lets the issue describe pins whose state is known — then decides, then applies on the branch, then rewrites the register rows by hand from the line numbers the run printed. [Dependency update pull requests](agent-workflow.md#dependency-update-pull-requests) holds the four questions a bump answers and why no updater opens one here.

## Building and testing the client

**The client needs Node and pnpm, which the .NET SDK does not bring.** `frontend/` is a pnpm workspace of React and
TypeScript, so a machine that only has the SDK builds the service and fails the client's half of either verification
gate by name. Install the Node version `frontend/package.json` names in `engines`, then pnpm globally — corepack no
longer ships with Node, which is why `packageManager` in that manifest states the pnpm version the lock file was
written by rather than leaving it to a shim. The registry those packages come from is declared in `frontend/.npmrc` rather than left to whatever a machine configured, for the reason `NuGet.config` clears its inherited source list.

Every command runs from `frontend/`, and `frontend/README.md` is the page for the workspace itself — its two packages,
what separates them, and how that separation is reproduced.

```bash
cd frontend
pnpm install --frozen-lockfile   # restore, refusing to rewrite pnpm-lock.yaml
pnpm build                       # the static bundle, into src/Client.App/dist/
pnpm dev                         # the development server
pnpm typecheck                   # both packages, plus the workspace's own configuration
pnpm lint                        # every rule an error, no warning tolerated
pnpm test                        # both packages' suites, once, non-interactively
pnpm test:browser                # build the bundle and drive it in a real browser
pnpm format                      # rewrite; pnpm format:check reports instead
```

`--frozen-lockfile` is to pnpm what `--locked-mode` is to `dotnet restore`: a manifest whose pins moved without the
lock file being regenerated fails here rather than resolving to something nobody reviewed. Regenerate it by running
`pnpm install` without the flag, as part of the change that moved the pin.

Four things around those commands are worth knowing before they are discovered:

- **Both verification gates run this flow**, through `scripts/resolve-changed-stacks.sh`, for any change that reaches
  the client stack. The fast loop restores, lints, type-checks, runs the suite, and formats — repairing, the way
  `dotnet format` does there — and the full gate runs the same steps with the formatting pass verifying instead, and
  the build after them.
  [Which stack a gate runs](agent-workflow.md#which-stack-a-gate-runs) carries how that is decided and what keeps it in
  step with `ci.yml`.
- **`CI`'s `Frontend` job runs the full gate's client flow and the browser suite.** It is gated on the `frontend` path
  filter and calls `.github/workflows/build-test-frontend.yml`, whose one job installs Node and pnpm, restores the
  workspace, and then runs the same commands in the same order the full gate does — the linter, the type check, both
  packages' unit suites, `pnpm format:check`, and the build — before installing Chromium and driving the bundle. The
  build is not a step of its own there because `pnpm test:browser` is `pnpm build` and then Playwright, so nothing in
  the job builds the bundle twice.
- **`pnpm test:browser` needs a browser, and neither verification gate runs it.** It builds the bundle, serves it with
  Vite's preview server, and drives it with Playwright, so it wants one install of its own —
  `pnpm exec playwright install chromium`, roughly 300 MB, from `frontend/` — which is why it is not one of the steps a
  gate runs on every client change. The pipeline runs it on every pull request that reaches the client instead, and the
  reasoning for gating it there rather than nightly or locally is in `.github/workflows/build-test-frontend.yml`. The
  port its preview server binds is derived from the workspace's own path rather than fixed, so two worktrees on one
  machine do not contend for it. What that suite covers, and what belongs in `pnpm test` instead, is
  `frontend/tests/AGENTS.md`.
- **`pnpm test` is the whole of the client suite.** It is Vitest, one project per package — `Client.Backend` without a
  DOM and `Client.App` in jsdom with React Testing Library — and a test file sits beside the source it covers rather
  than under `frontend/tests/`, which holds the suite's contract and no test. `frontend/tests/AGENTS.md` is that
  contract. The same command collects coverage and enforces no threshold on it;
  [the client's suite](#the-clients-suite) below is where that is decided.
- **Nothing the service does for a client changed**, because none of it was the client's: the surface under
  `/api/client` is an endpoint of its own — [the client endpoint](client-endpoint.md) is the page — and `Host` serves
  whatever files an image carries beneath its web root. No current image carries any, so a deployment that switches
  the client application on is refused at startup by name.

### Building the desktop head

The commands above need Node and pnpm and nothing else. The desktop head needs **a Rust toolchain and the platform's
WebView development packages as well**, because ADR 0021 chose Tauri, which links the shell in Rust and renders in the
WebView the operating system supplies rather than in one MailFathom ships. A contributor who only touches a screen
never installs either: `pnpm build` produces the web bundle without them, and only the two commands below reach the
crate graph.

```bash
cd frontend
pnpm desktop:dev     # the shell around the development server, on a port reserved for that run
pnpm desktop:build   # the release application and the installers named in tauri.conf.json
```

**Rust comes from [`rustup`](https://rustup.rs/)**, on the stable channel, and the shell builds on the 2024 edition —
so a toolchain older than Rust 1.85 refuses `frontend/src-tauri/Cargo.toml` before it reaches a dependency. Nothing
pins the Rust version the way `global.json` pins the SDK: `Cargo.lock` fixes the crate closure, and the compiler is
whatever stable a machine has.

**The WebView packages are the part that fails obscurely.** Tauri's build reads them through `pkg-config`, so a
machine without them stops in a `-sys` crate rather than in anything named after a browser — the error names
`glib-sys`, `gobject-sys`, `javascriptcore-rs-sys`, or `webkit2gtk-sys`, and says a package configuration file could
not be found. That is a missing development package every time, never a Rust or a Tauri defect.

| Platform | What to install |
| --- | --- |
| Debian and Ubuntu | `libwebkit2gtk-4.1-dev build-essential curl wget file libxdo-dev libssl-dev libayatana-appindicator3-dev librsvg2-dev` |
| Fedora | `webkit2gtk4.1-devel openssl-devel curl wget file libappindicator-gtk3-devel librsvg2-devel libxdo-devel gcc gcc-c++ make` |
| Arch | `webkit2gtk-4.1 base-devel curl wget file openssl appmenu-gtk-module libappindicator-gtk3 librsvg xdotool` |
| Windows | The **Microsoft C++ Build Tools** with the *Desktop development with C++* workload, and **WebView2**, which Windows 10 1803 and later already carry — install the Evergreen Bootstrapper on anything older |

One more thing a Windows machine needs, and it is not Tauri's: **Git Bash on the path**, because both builds read the
declared version by running `scripts/read-declared-version.sh`. The toolchain half needs nothing done — `rustup`
already defaults to the MSVC ABI there, which is the one the C++ Build Tools above serve; a machine that was switched
to the GNU host triple at some point is the case to notice, because that is the deviation rather than the default.

`frontend/README.md` is where the shell itself is described — what it owns, where each of its decisions is written
down, and why the version reaches it as a configuration patch rather than as a number in a manifest.

## Code coverage

Both stacks are measured and one of them is enforced. The service's figure is the repository's only coverage threshold,
and everything down to [the client's suite](#the-clients-suite) below is about it; the client's is collected on every
run of its suite and gates nothing.

The full verification script collects and enforces the service's coverage. To run only the
underlying coverage target after a Release build:

```bash
dotnet tool restore
dotnet msbuild .config/CodeCoverage.proj -t:Collect
```

The command runs the whole solution in one test invocation, which produces one uniquely named Cobertura report per unit-test assembly, merges the reports, and requires at least 85% aggregate line coverage. The result always represents the whole configured scope, not only changed lines.

The scope is every project under `backend/src/` except `Host` and `AppHost`, which are excluded as thin executable composition roots. `.config/CodeCoverage.proj` names those two and derives everything else from them: it asks each remaining project for its `AssemblyName` and admits exactly those assemblies to the merged report, so a project enters the measurement by existing rather than by matching a naming pattern. A pattern would not do, because an assembly is not always named after its boundary — `Cli` publishes as `mfctl`, since an operator types it, and a name with no dot in it matches nothing shaped like `MailFathom.*`.

The collector is the half that has to be told each such name. The Coverlet `Include` filter in `.config/testconfig.json` matches by assembly name, and `[mfctl]*` is what instruments the command at all; a project whose `AssemblyName` does not begin with `MailFathom.` needs the same entry. Forgetting it removes a boundary from the measurement without lowering any number, which no percentage can reveal, so the gate checks the denominator before it reads one. Every measured project that compiles at least one file of its own must appear in the merged report, and no assembly outside that set may. Either mismatch fails the target the way a test project missing from `backend/MailFathom.slnx` already does, and the failure names the file that decides it. A project compiling nothing is expected to be absent — `AI` is a scaffold today and appears in no report — and stops being expected to the moment it holds a file, which keeps that tolerance from becoming a silent exclusion of its own.

Two attributes take code out of that denominator, and `.config/testconfig.json` configures the collector to honor both. `[ExcludeFromCodeCoverage]` marks code that should never participate in coverage. `[RequiresIntegrationCoverage]`, declared in `backend/src/shared/RequiresIntegrationCoverageAttribute.cs`, marks code whose verification needs a real database, a real mail server, or a composed host: the EF Core context and its entities, the persistence stores, the file-system and environment secret readers, and the infrastructure registration extensions carry it today. The MailKit adapter deliberately does not, even though the integration suite now exercises it against a real IMAP server, because MailKit publishes `IImapClient` and `IMailFolder` and the adapter is reachable from a unit test through them; it stays in the enforced denominator and the integration suite proves the wire behavior a substitute cannot. Marked code is measured by the integration suite instead, in a separate report that enforces nothing — see [Integration tests](#integration-tests) below. The marker stays once the class is covered there: it records where the verification lives, not whether it has been written, and a class a unit test cannot reach stays unreachable afterwards. Remove it only when unit-testable logic enters the class, which puts every line back into this denominator and is how to check that the exclusion is still earned.

A third exclusion is applied by path rather than by attribute: `.config/CodeCoverage.proj` filters `**/Persistence/Migrations/*.cs` out of the merged report. EF Core generates those files, so they carry no attribute the generator would preserve, and no unit test may execute them — a migration is proven by applying it to a real PostgreSQL server and reviewing the resulting schema. Leaving them in put roughly a thousand uncoverable lines in the denominator and moved the aggregate by more than twenty points, which would have masked a real regression anywhere else.

A fourth exclusion is about which projects the target *runs* rather than which assemblies it measures. `backend/tests/Benchmarks` sits under `backend/tests/` and is not a suite — it asserts nothing and its numbers gate nothing — so `.config/CodeCoverage.proj` names it as the one exception to the glob that finds test projects, and `.config/testconfig.json` excludes its assembly beside `SyntheticMail`'s. It stays in the check that every project under `backend/tests/` appears in `backend/MailFathom.slnx`, because a project missing from the solution is unbuilt, unanalyzed, and unformatted whether or not anything runs it.

Raw Cobertura reports and TRX files are written under `artifacts/coverage/raw/`. The merged Cobertura and HTML reports are written under `artifacts/coverage/report/`. The client's report sits beside them under `artifacts/coverage/client/`. The verification records the two gates write sit beside all of it under `artifacts/verify/`; the whole directory is ignored, so nothing there is ever staged, and deleting any of it costs one repeated run.

### The client's suite

`pnpm test` collects its own coverage every time it runs, which is every time either verification gate or `CI` reaches
the client stack. There is no second command and no flag: `frontend/vitest.config.ts` enables Vitest's v8 provider,
which reads counters the runtime already keeps rather than instrumenting the module graph, so the figure costs a report
rather than a slower suite. A text summary is printed where the run is read, and the HTML report is written to
`artifacts/coverage/client/`.

The measured scope is both packages' `src/` whether or not a test imported the file, so a module nobody covers sits at
zero rather than going missing from the report. Declaration files and `Client.App/src/main.tsx` are excluded — the
latter mounts React into the document and decides nothing, which is the argument that excludes `Host` and `AppHost`
above — and Vitest drops the suite's own test files.

**Nothing enforces it**, in either verification script or any workflow, and the 85% above stays the repository's only
coverage threshold. `frontend/tests/AGENTS.md` § *Coverage* holds why a second one was refused rather than defaulted
to.

## Integration tests

`backend/tests/IntegrationTests` verifies what a unit test structurally cannot: EF Core mappings, the baseline migration, database constraints, transaction and concurrency behavior, the SQL PostgreSQL actually runs and the plans it chooses, the two readers that reach the file system and the process environment, and what MailKit puts on the wire against a real IMAP server. It starts the repository's own app model through `Aspire.Hosting.Testing`, so the orchestration under test is the one `aspire run` starts rather than a second container topology maintained beside it. [The stored email schema](../architecture/stored-email-schema.md#what-the-integration-suite-proves) lists what the persistence half of the suite establishes.

Run it on request:

```bash
bash scripts/run-integration-tests.sh
```

Arguments are forwarded to Microsoft Testing Platform, so `bash scripts/run-integration-tests.sh --filter-class '*RemoteSeenFlag*'` narrows the run to the flag-preservation tests. xUnit v3 names the option `--filter-class`, with `--filter-method`, `--filter-namespace`, and their `--filter-not-*` counterparts beside it; a plain `--filter` is not one of them and makes the run print its help and exit non-zero.

The whole suite takes a little over a minute after the images are pulled, and a filtered run is not much faster: the orchestration, the migration, and both containers start once for the assembly whatever the filter selects.

The suite needs a container runtime. The script uses `docker`; set `MAILFATHOM_CONTAINER_RUNTIME` to use another one.

It is deliberately not part of any other command. `scripts/verify-fast.sh` and `scripts/verify-full.sh` never start it, the 85% coverage gate never measures it, and no pull-request workflow runs it. The mechanism is one MSBuild property: `IsTestingPlatformApplication` is `false` for the project, which is what a solution-wide `dotnet test` uses to discover test projects, so neither the fast loop nor the coverage collection finds it. The project stays in `backend/MailFathom.slnx` regardless, so it is built, analyzed, and formatted by exactly the same gates as everything else — a compile or style error in an integration test still fails an ordinary pull request.

### Ephemeral resources

The app host is started with the argument `IntegrationTesting=true`, which selects a second topology in `backend/src/AppHost/Program.cs`:

- every container and volume is named `mailfathom-integrationtests-<run>-…`, where `<run>` is eight hex characters
  generated for that run, rather than taking Aspire's random postfix and the path-derived volume name a developer's
  orchestration uses. The shared leading part is what a filter finds them all by; the run identifier is what keeps two
  suites started on one machine from racing for one name, and what lets a run remove exactly what it created;
- the PostgreSQL container is therefore `mailfathom-integrationtests-<run>-postgres` and its data volume
  `mailfathom-integrationtests-<run>-postgres-data`. The volume is new on every run by construction, which is what the
  baseline migration has to apply to for a run to prove it applies cleanly at all;
- a `mailserver` container named `mailfathom-integrationtests-<run>-mailserver` is added, which a developer's orchestration never gets — it exists so the suite has a real IMAP server to synchronize against, and starting one beside a developer's own accounts would advertise a mailbox nothing points at;
- a `presidio-analyzer` container named `mailfathom-integrationtests-<run>-presidio-analyzer` is added, on the same terms and for the same reason: the personal-data scanner's whole claim is about what a real analyzer answers, and a developer's orchestration never gets one because personal-data scanning is off by default and the image is the largest thing this repository pulls;
- a `spamassassin` container named `mailfathom-integrationtests-<run>-spamassassin` is added, on the same terms again: the spam scanner's whole claim is about what a real rule corpus concludes, and a developer's orchestration never gets one because spam scanning is off by default;
- an `object-storage` container named `mailfathom-integrationtests-<run>-object-storage` is added, on the same terms once more: the object content backend's whole claim is about what a real S3 server accepts, and a developer's orchestration never gets one because the object backend is off by default and a deployment that selects it points at whatever endpoint its operator runs. It is the one container here with no volume — nothing it holds is meant to survive a run, and an endpoint that kept objects between runs would let a test read one an earlier run wrote;
- the `mailfathom-host` project resource is added to the model but never started, because the suite exercises classes against real infrastructure and a running MailFathom would synchronize mail underneath the data a test is asserting on. What a collection eventually starts is a host serving both of its network surfaces under the posture worth proving end to end: the MCP endpoint behind an API key and a narrowed origin list, and the administrative endpoint enabled on a listener of its own behind an API key that is none of the MCP ones — which is what lets the suite establish from outside the process that neither surface's credential authenticates the other's routes. The probes are served on the MCP endpoint's own socket rather than on one of theirs, which is the arrangement a single-node deployment publishes and the only place anything proves it works: a shared socket serves the union of what its surfaces answer, so a probe is answered there without a credential while the MCP route on it still requires one. Both sections therefore state the same bind address, because they describe one socket and an address written in one place and defaulted in the other would be two sockets the host refuses to open. The administrative endpoint keeps a socket of its own, so the suite carries both arrangements at once and can establish that a path belonging to a surface a listener does not serve is refused on it rather than served by whichever route matches. Every port on that resource is allocated rather than defaulted, because two MailFathom processes run at once under this topology, and every endpoint is published as a TCP one for the same reason: an HTTP one joins `ASPNETCORE_URLS`, which the host refuses outright. It is configured with the one account identifier the suite stores its mail under, and — for the tools that send — with what an account has to declare to be allowed to send at all: a submission endpoint in the reserved testing domain, the address to send from, and the switch that turns sending on, which is off for every account until an operator sets it. Configuration is what defines the accounts a deployment serves and it is read whether or not synchronization runs, so a host naming none would answer every mailbox read with an empty window over a database that is not empty, and one declaring no delivery would answer every sending tool with the coded refusal that says so rather than exercising the contract the suite is about. Beside those it carries what the delivery block's own validation requires and nothing then spends: the login the account is reached under, and a named password secret — a secret block is identified by its name rather than by where it sits, so one stating only a reference fails validation and stops the host before it binds a socket, which reaches the suite as every request to that host timing out rather than as configuration being wrong. The delivery pass itself runs on that host like it does on any deployment — the outbox worker is registered unconditionally and answers the signal a queued send raises — and the submission host it offers a message to resolves nowhere, so every attempt ends as a transport failure that defers the send rather than delivering or ending it. The first retry is therefore stretched past the length of a run, which is what leaves a send recorded and withdrawable after its one attempt instead of being claimed again while a test is reading it. Nothing else about the account is configured: with synchronization off, a reading server or a credential for it would be configuration nothing acts on;
- a second project resource, `mailfathom-mtls-host`, is added on the same terms and started by a collection of its own, `MutualTlsHostCollectionDefinition`, which the assembly's orderer places after the collection that starts the host above — starting a second project process must not be what a rate limit is measured against. It serves the endpoint over an HTTPS profile behind a `Required` client-certificate profile, which is what lets the suite prove the mTLS rules against a real handshake; a certificate requirement is one answer for a whole process, so it cannot be a posture applied to the host above. Its server certificate, private key, and trust anchor are issued in memory per run by the test suite and injected into the environment variables the app model's `env:` secret references name, so nothing of the kind is committed and a developer's orchestration never gets this resource at all.

The prefix comes from `OrchestrationContract` in `backend/src/AppHost`, and nothing else in the repository uses it. The run
identifier is generated by `scripts/run-integration-tests.sh` and passed to the app model in
`MAILFATHOM_INTEGRATIONTESTS_RUN_ID`; the script needs it before the suite starts, because it is what scopes the
removal afterwards. An unset variable makes the app model generate one, which is what a suite started by hand gets —
and what nothing then removes, since only the script cleans up.

The script removes this run's containers and volumes when the run ends, whether it passed, failed, or was interrupted,
because nothing the suite creates is meant to outlive it. It removes nothing beforehand: a name no earlier run could
have used is already an empty database, and a sweep of the shared prefix would destroy a concurrent run's containers to
establish what this run's own name already establishes.

A run killed with `SIGKILL` skips that removal. What it leaves cannot be cleaned up automatically by a later run, which
has no way to tell a dead run's containers from a live one's, so the script reports them at the end instead and prints
the command that removes them once nothing else is running. To look by hand:

```bash
docker ps --all --filter name=mailfathom-integrationtests
docker volume ls --filter name=mailfathom-integrationtests
```

A developer's own orchestration is untouched by any of this: its container name and its volume name are derived from the AppHost project path and never carry the test prefix.

### The mail server

The `mailserver` resource is `greenmail/standalone:2.1.11`, configured through `GREENMAIL_OPTS` to start only SMTP on 3025, IMAP on 3143, and the API server on 8080. The API server is what the resource's health check polls — `/api/service/readiness` — so the suite waits for a server that is accepting rather than for a container that has started; without it the first test would race the listener and fail as a connection refusal that says nothing about the behavior under test.

It serves one throwaway mailbox, `mailfathom` / `mailfathom@mailfathom.test`, whose credentials are constants in `OrchestrationContract`. They exist only in the ephemeral topology, unlock nothing outside the container, and are declared once so the app model that configures the server and the suite that logs into it cannot drift apart. GreenMail's own verbose logging stays off, because it transcribes the whole IMAP conversation — password included — into the orchestration log.

Two behaviors of this server are worth knowing when reading a failure:

- It advertises `AUTH=XOAUTH2` and nothing else, so both the adapter under test and the suite's own observations empty MailKit's advertised mechanism set and authenticate with the IMAP `LOGIN` command. That is a legal server shape, and `MailKitTransportSecurityMapping` permits the fallback exactly when the account's policy already allows a clear-text mechanism.
- Each folder carries a real UIDVALIDITY, `System.currentTimeMillis() / 1000` at creation, so replacing a folder hands out a new one. That is how the suite produces a UIDVALIDITY change without simulating anything. Two consequences are built into `OrchestratedMailbox.RecreateFolderAsync`: it waits past the next whole second, because a folder replaced inside the same second is handed back the value it just had, and it retires the old folder by renaming it rather than deleting it. GreenMail 2.1.11 crashes on `DELETE` of a folder an earlier session had selected — it notifies the folder's listeners and dereferences a response object a disconnected session no longer holds — and every folder this suite replaces has been selected by a synchronization run.

smtp4dev was evaluated first and rejected. It advertises no SASL mechanism at all, which is workable, but its INBOX reports a hard-coded UIDVALIDITY that can never change, so the suite's UIDVALIDITY scenario would have been unverifiable. Separately, a `UID SEARCH UID 1:*` against it exhausts the container's memory and kills the process; MailFathom never sends that shape, because it computes a concrete upper bound from `UIDNEXT`, but it is worth recording for anyone who reaches for that image again.

### The personal-data analyzer

The `presidio-analyzer` resource is `ghcr.io/data-privacy-stack/presidio-analyzer:2.2.364`, the same pin all three
deployment shapes carry, and its health check polls the analyzer's own `/health`. Healthy rather than running matters more
here than for the mail server: the container loads a spaCy language model before it serves anything, so a suite that waited
only for the container would ask an analyzer that is not ready and read the refusal as an analyzer that recognises nothing.

It is configured with nothing. The analyzer's default configuration is the one the deployment assets ship it with, which
is the point — what the suite establishes is that the entity names MailFathom's categories are made of are ones *that*
image registers, and that the offsets it answers with land on the region of the original text.

Two costs are worth knowing before a first run. The image is roughly two gigabytes, so the first `scripts/run-integration-tests.sh`
on a machine spends several minutes pulling it, and the container wants about a gigabyte of memory while it holds the model.
Neither reaches a developer's ordinary orchestration, which starts no analyzer at all.

### The spam scanner

The `spamassassin` resource is `docker.io/axllent/spamassassin`, pinned to the digest all three deployment shapes carry,
and it is an ordinary app-model resource rather than a fixture of the suite's own — there is one topology here and a
dependency a test needs joins it like any other. It runs with `DNS_CHECKS=0`, which is the posture every deployment
asset ships, so a run reaches no blocklist and a machine with no route out scores exactly what a machine with one does.

The daemon compiles its rule corpus before it listens, so the fixture waits for a daemon that answers the protocol's own
`PING` rather than for a container that is running — the same distinction the analyzer's health check draws, reached
through the protocol because there is no HTTP endpoint to poll.

What the suite establishes against it is what only a real corpus can settle: that the GTUBE test string is scored spam,
that ordinary synthetic correspondence is not, and that the rule names and the corpus revision reach the stored
classification record. Every message it is sent is written by the test, and none of it is real mail.

### The object-storage endpoint

The `object-storage` resource is `docker.io/pgsty/silo:RELEASE.2026-08-06T00-00-00Z`. Silo is a maintained fork of the
open-source MinIO server, keeping one release line alive after upstream ended community distribution, and it is here for
the reason the mail server and the analyzer are: what a substituted S3 client proves is that MailFathom composed the
request it meant to, and what a real server proves is that the request is one an S3 implementation accepts. A request a
mock accepts and a server refuses would otherwise first fail in somebody's deployment.

It is started with the server's own `server /data` command, its root credential comes from the same two constants in
`OrchestrationContract` the suite signs its requests with, and its health check polls `/minio/health/live` — the one
route that answers before any bucket exists, because every other route on that port is a request about a bucket. The
bucket itself is created by `MailFathomOrchestrationFixture` at start-up, since the server ships none and creating one is
a request the S3 API already answers.

`backend/tests/IntegrationTests/ObjectStorage/S3Surface.cs` is the list of the S3 operations and behaviours the adapter
depends on, each naming the test that exercises it, and it is the answer to "which S3 implementations can MailFathom
run against" — a list to check another server against rather than a reading of the adapter's source. Two tests beside it
hold the list to that claim: every entry names a test the runner runs, and every test in the class is named by an entry.

Two costs are worth knowing, and neither is large next to the analyzer's. The image is about 50 megabytes to pull and
around 200 on disk, so the first run on a machine spends a few seconds fetching it; and the server formats its
one-drive pool on every start, because no volume outlives the run. Neither reaches a developer's ordinary
orchestration, which starts no endpoint at all.

Silo is AGPL-3.0-or-later, which the acceptance policy in
[`THIRD_PARTY_LICENSES.md`](https://github.com/Krzysztof318/MailFathom/blob/main/THIRD_PARTY_LICENSES.md) places behind
the owner's explicit approval. That approval is recorded, and so is the reading it was granted under: the server is a
separate process reached over the network, pulled from its own registry, with nothing vendored, linked, or redistributed
here. The same image is what the three deployment shapes offer an operator as an optional store beside MailFathom, off
in every default rendering, which is why the pin is one decision — a server the suite verified the adapter against is
the server an operator gets.

### The provider-contract tests

`backend/tests/IntegrationTests/ProviderAdapters` holds the tests that call a real AI provider — the embedding adapter's and the chat adapter's — and they are the only part of this suite that costs money. Everything else runs against the containers the run starts, so it costs runner time and nothing else.

They are skipped unless somebody asks. One switch covers both adapters, `MAILFATHOM_AI_CONTRACT_TESTS`, and nothing sets it: a developer's run and every ordinary pipeline run spend no provider credit. Which half of the AI boundary a test bills against is not a distinction the operator turning them on makes, which is why there is one switch rather than one per provider.

A run that was asked for and finds a variable missing fails rather than skipping, because a run somebody requested and which then quietly proved nothing is worse than one that never started. What each adapter needs beside the switch:

| Adapter | Variables |
|---|---|
| Embedding | `MAILFATHOM_EMBEDDING_API_KEY`, `MAILFATHOM_EMBEDDING_MODEL`, `MAILFATHOM_EMBEDDING_DIMENSION`, and optionally `MAILFATHOM_EMBEDDING_ADDRESS` and `MAILFATHOM_EMBEDDING_ROUTED_MODEL` |
| Chat | `MAILFATHOM_CHAT_API_KEY`, `MAILFATHOM_CHAT_MODEL`, and optionally `MAILFATHOM_CHAT_ADDRESS` and `MAILFATHOM_CHAT_REASONING_EFFORT` |

An absent address means the provider library's own default, which is what a first-party OpenAI endpoint needs; a cloud deployment sets the resource's OpenAI-compatible address. Turning the switch on with only one adapter's variables configured therefore fails the other adapter's tests, which is the same asymmetry rather than a separate rule.

The chat adapter's two tests each run **twice**, once over each of the provider's two request APIs, because each is a distinct wire protocol against the same endpoint and a surface nobody called is one whose first failure reaches an operator instead of this suite. Which API a call goes to is therefore not a variable a run sets; the declared model has to serve both, which the first-party and cloud endpoints do for a request carrying no tools, and a contract request carries none. The four chat calls a requested run makes are two answers and two refusals, and a refusal is answered before a model runs, so it costs a round trip and no tokens.

`MAILFATHOM_CHAT_REASONING_EFFORT` is the chat adapter's one optional choice, carrying whatever level the model documents, exactly as [`Chat:ReasoningEffort`](configuration-ai.md#chat) does. Unset by default, which sends what the tests always sent. A value the provider does not recognize fails the run as a refused request rather than passing quietly, which is the point of pointing a run at a reasoning model in the first place.

[ADR 0006](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md) holds the reasoning, and `backend/tests/AGENTS.md` states how such a test is written.

### Coverage

The suite collects its own coverage report, and nothing enforces it. The 85% gate above stays the repository's only coverage threshold, and this report never merges into it.

Its scope is the classes marked `[RequiresIntegrationCoverage]` and nothing else, which is the debt the suite exists to pay off, so the number reads as progress through that inventory. The two runs therefore need opposite collector configurations of the same attribute: `.config/testconfig.json` excludes marked code because a unit test cannot reach it, and `backend/tests/IntegrationTests/testconfig.json` does not exclude it. `scripts/run-integration-tests.sh` then narrows the report to exactly the files carrying the marker, deriving that filter by searching for the marker rather than keeping a second list, so a newly marked class enters the report on its own.

That number is currently 93.9% of the lines in the 22 marked classes, up from the 26.9% the harness started at. What remains uncovered is the failure paths of a database that is behaving: an unreadable migration history, a catalogue the configured user may not read, a generated column that is absent. Reaching them means breaking the orchestrated database rather than exercising it, so they stay uncovered deliberately, and the percentage is read as progress rather than as a target to close.

The script prints the summary at the end of a run and writes the full output under `artifacts/integration-tests/`: TRX and raw Cobertura under `raw/`, and the merged Cobertura, HTML, and text summary under `report/`. The directory is removed at the start of each run, so a report never merges numbers an earlier run produced. A failing run still produces the report, because that is when it is worth reading.

A covered class keeps its marker. The marker records where a class's verification lives rather than whether it has been written, and a class no unit test can execute stays that way once its integration test exists; dropping the marker would remove the class from this report and add it to the enforced denominator at nearly zero, so writing an integration test would lower the aggregate and hide the coverage it just produced. Progress is the percentage here rising, not the inventory shrinking.

### Continuous integration

The `Integration tests` workflow runs the same script. It is not a required status check and never runs on a pull request. Start it from the Actions tab when a change is one this suite can speak to; it uploads the TRX results and the coverage report as artifacts, and enforces no threshold on either.

A dispatch takes an optional `ref` to run against and `run_ai_provider_contract_tests`, which turns on [the provider-contract tests](#the-provider-contract-tests) above and defaults to off. The workflow supplies the credentials with it, from the `EMBEDDING_PROVIDER_*` and `CHAT_PROVIDER_*` repository secrets and variables. `Release` reaches this suite through `workflow_call` instead, and that trigger declares no such input at all, which is what keeps a release from ever spending provider credit.

## Pull request checks

Five workflows run for every pull request targeting `main`. Three of them always run; `Typo check` and `CodeQL` always run except on a draft. `CI` also runs on every push to `main`, which [`CI` after a merge to `main`](#ci-after-a-merge-to-main) below describes. It carries eight jobs.

Each check reads as `<stack> / <question>`, and the stack in front is what makes the list legible: the server's three jobs arrive under `Backend` and the client's one under `Frontend`, so a job that skipped is read as work the change could not reach rather than as a stack nobody verified. The two are not symmetrical in shape and are not meant to be — the server asks its three questions in three jobs and the client asks all of its in one, for reasons the bullets below give — which is exactly why each name has to carry both halves.

- `Detect changes` reads the change with `dorny/paths-filter` and publishes five decisions: whether it can affect the build, whether it can affect formatting, whether it can affect the EF Core model, whether it can affect the client, and whether it can affect what the Helm chart renders. It takes seconds, checks nothing out, and holds `contents: read` and `pull-requests: read` because the one event it reads is a pull request, which the GitHub REST API answers without a working copy. Skipping work a change cannot affect is a pull request's economy and only a pull request's: on a push to `main` and on a manual dispatch the filter step is skipped and all five decisions fall back to `true`, so a merge runs the whole pipeline and an explicitly started run always does the work. [`CI` after a merge to `main`](#ci-after-a-merge-to-main) gives the argument for the first of those. Two of the five path lists — `build` and `frontend` — are also what `scripts/verify-fast.sh` and `scripts/verify-full.sh` decide from, through `scripts/resolve-changed-stacks.sh`, so a gate on a developer's machine builds the stacks this job would; `scripts/test-agent-workflow.sh` fails a filter edited here and not there.
- `Backend / Build and unit test the service` runs when the change touches production code, tests, the solution or SDK selection, shared build and package configuration, coverage tooling, or the workflow file. It restores `backend/MailFathom.slnx` in locked mode and repository-local tools, builds the solution in Release configuration, runs all unit-test projects through Microsoft Testing Platform with unique coverage prefixes, merges their Cobertura reports, and fails below 85% aggregate line coverage for the complete configured production scope. It uploads raw and merged coverage artifacts and TRX results even when the threshold fails.
- `Backend / dotnet format` runs when the change touches `backend/src/**`, `backend/tests/**`, `.editorconfig`, the workflow file, the shared build files, `backend/Directory.Packages.props`, `backend/MailFathom.slnx`, or `global.json`. It restores `backend/MailFathom.slnx` in locked mode and verifies repository formatting without applying changes. The command runs its analyzer pass as well as its whitespace and style passes, so a centrally pinned analyzer version, a property set in a shared build file, a project added to the solution, or a different SDK can move its verdict without a single C# file changing; the trigger covers all four. `.config/**` and `NuGet.config` stay out, because they decide what the build rejects, restores, runs, and measures rather than how code is written. It is a job of its own rather than a step of the build above, which is where the two stacks deliberately differ: here the setup a separate job pays is about 25 seconds against a pass of about 155, so a job keeps the longer of the two off the path a pull request waits on, while the client's proportions are inverted and its pass is a step. Runner minutes are free on a public repository, so the trade is wall-clock against wall-clock, decided the same way in both places from opposite measurements. The second half of the reason is this list: a job reports its own conclusion here and a step reports none.
- `Backend / Pending model changes` runs when the change touches `backend/src/**`, `.config/dotnet-tools.json`, the workflow file, or `backend/Directory.Packages.props`. It restores in locked mode, restores local tools, builds `backend/src/Host` in Release configuration, and runs `dotnet ef migrations has-pending-model-changes`, which fails when the EF Core model has moved without a migration recording it. The command opens no connection — it compares the compiled model against the committed model snapshot — so no database is provisioned for this job. Production code is the only thing that can move the model, which is why tests and documentation are not triggers; `backend/Directory.Packages.props` is one because raising the EF Core version can change what the generator emits for an unchanged model. `Persistence__TextSearchConfiguration` is deliberately left unset, so a migration generated under a non-default configuration fails here by design rather than by accident.
- `Frontend / Check, build, and test the client` is the client stack's half of the same required check, and it calls `build-test-frontend.yml` the way `Backend` calls the server's workflow. **What it asserts is everything the full gate asks the client, plus the browser suite.** The job checks the repository out, installs the Node version `frontend/package.json` names in `engines` and the pnpm version its `packageManager` field names, restores the workspace with a frozen lock file, and then runs `pnpm lint`, `pnpm typecheck`, `pnpm test`, and `pnpm format:check` — the same four commands in the same order `scripts/verify-full.sh` runs them, at the same severity, so a lint violation, a type error, or a formatting difference fails here exactly as it fails locally. Only then does it install Chromium and run `pnpm test:browser`, which builds the bundle and drives it; that step is the build as well, so the bundle is not built twice. The four cheap checks sit ahead of the browser steps deliberately — a lint violation is answered without the 300 MB download behind it. Two caches make the rest affordable: the pnpm store and `~/.cache/ms-playwright`, both keyed on the client lock file, which is what pins the resolved closure and the Playwright version that decides which browser build is downloaded. Nothing is uploaded from a failure, deliberately: a trace or a screenshot of a client is personal data the moment the suite drives a real deployment rather than the stubbed transport, so a failure is read from the log and reproduced locally. It runs when the change touches `frontend/**`, that workflow file, `ci.yml`, or one of the three repository-wide files a client run actually reads — `Version.props`, `scripts/read-declared-version.sh`, which is how `vite.config.ts` reads it, and `.editorconfig`, whose client section Prettier reads rather than restating — and it keeps the draft exemption the server's build carries, which it earns rather than anticipates: a browser download and a bundle build are the expensive-job case that exemption exists for. `run-format` is the one input `CI` passes this workflow, and it carries one exemption: it is `false` on a push to `main`, for the reason [`CI` after a merge to `main`](#ci-after-a-merge-to-main) gives about the server's job, and `Release` and `Nightly` pass it as `false` to both workflows on the same argument. This job's filter names no path under `backend/` and the three the server's jobs use name none under `frontend/`, which is what keeps a change to one stack from costing anything in the other; `scripts/test-agent-workflow.sh` asserts that disjointness rather than leaving it to a review.
- `Workflow contracts` runs `scripts/test-agent-workflow.sh`, and it is one of the two jobs that read the part of the repository the build, formatting, and model jobs cannot. Both live in `repository-contracts.yml`, which `CI` calls as `Repository contracts` and which `Nightly` and `Release` call as well against the revision they publish, so a channel asserts them from one definition rather than from a copy. What this one covers is the workflows, the verification scripts, the fathom-review helpers, the skills, the licensing header outside the solution, and the two page contracts under `docs/` — the `describes:` marker every page carries, and the fixed notice a page whose steps happen in somebody else's product opens with. It has no trigger condition of any kind — no path filter, no draft exemption, no event condition, no switch its caller can turn off — so it runs beside the expensive jobs rather than after them, on a push to `main` as well as on a pull request, and on both publication channels. It checks the repository out shallowly with `persist-credentials: false` and runs the suite; nothing else is provisioned, because the suite fakes `dotnet` with a symlink to itself, builds the Git repositories it tests under a temporary directory, and reaches no network. The whole job takes about twenty seconds on a GitHub-hosted runner, of which the suite itself is fifteen. It takes longer than that under `scripts/verify-full.sh` on a developer machine, which is a different measurement rather than a contradiction: there it competes with whatever else that machine is doing, and the gate is one run of many rather than the one that decides a merge. That is also why the local gate narrows — it skips the suite for a branch that only added or edited C# files — while this job asks nothing and runs always: a skipped run costs a verdict that arrives minutes later, and there is no later here. [Entry points](agent-workflow.md#entry-points) describes the suite itself and the local gate that runs it as well.
- `Helm chart` is the second job in `repository-contracts.yml`, and it runs `scripts/render-helm-manifests.sh`. It is the one of the two a change can fail to reach, so it is the one its caller switches: `CI` turns it on when the change touches `deploy/helm/**`, that script, or `ci.yml`, and a publication leaves it at its default because a publication skips nothing. It lints the chart with `helm lint --strict` against every `deploy/helm/mailfathom/ci/*-values.yaml`, renders each of them with `helm template`, and compares the rendering against the manifests committed under `ci/golden/`. The deployment contract is one of the four public surfaces [ADR 0004](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0004-versioning-and-release-policy.md) names, and before this job it was verified for the first time inside `Publish Helm chart` — after the release had already pushed the image, and with re-tagging a release meaning a registry deletion first. Committing what each values document renders is what puts the second half in the diff: a template edit that still renders but produces a different object is otherwise invisible in a review of the templates alone. Helm is the runner image's preinstalled copy, which is what the release run uses too, and the rendering is normalized before it is compared so that the Helm version deciding the whitespace between documents cannot decide the verdict. The job installs nothing, restores nothing, reaches no cluster and no network, and costs seconds rather than the minutes an SDK, a restore, and a build cost. `Publish Helm chart` keeps its own lint and render, which asks a different question rather than repeating this one: that one renders the packaged chart against the digest the release just published, and this one holds the chart in the released tree against the manifests committed beside it. [Deploying to Kubernetes](deployment-kubernetes.md#verification) describes running the same script by hand and regenerating the manifests.
- `Required CI` is this workflow's one required status check, and the only conclusion the ruleset reads from it. It depends on the four things `ci.yml` declares — `Detect changes`, and the three called workflows behind `Backend`, `Frontend`, and `Repository contracts` — runs under `if: always()` so a cancelled or skipped dependency cannot skip it in turn, and reads their results: `Detect changes` and `Repository contracts` must have succeeded, and `Backend` and `Frontend` must have either succeeded or been skipped. `failure` and `cancelled` fail it. `Repository contracts` is held to the stricter rule because the contract suite inside it has no way to skip on the events this job runs for: a path filter and a draft exemption are what produce a legitimate `skipped`, and it has neither, so the conclusion could only come from a job that failed to start. Its chart job does skip on a change reaching no chart file, and a called workflow whose remaining job succeeded reports `success`, so that complete answer arrives here as one. This job itself does not run on a push to `main`, for the reason [`CI` after a merge to `main`](#ci-after-a-merge-to-main) gives.

The second workflow, `Protected paths`, carries one job of the same name and answers a different question: not whether the change builds, but whether its author may make it at all. It reads the pull request's changed files through the GitHub REST API, checks nothing out, and fails when the pull request adds, modifies, deletes, or renames a protected path and its author is not the repository owner. A rename is read from both ends, so moving a file out of a protected directory counts as changing it. A pull request larger than the 3000 files that endpoint reports fails rather than passing on a list that may be missing the change it was asked about. Everything else it sees passes in seconds, including drafts, which run it for the same reason: the fact it reports is worth having in the first minute rather than at the moment a draft is marked ready.

The protected set is matched in three shapes, and the shape follows from what the entry is rather than from how it is spelled.

Five **directory prefixes** cover a directory and everything beneath it, because each decides how every other change is judged rather than being judged by it. `.github/` names who approves a change and which checks the ruleset waits for. `.config/` decides which API calls `BannedSymbols.txt` rejects, what `CodeCoverage.proj` demands, which local tools `dotnet-tools.json` restores, how the test runner is configured, and which spellings `typos.toml` accepts. `.agents/` holds the skills that define the task, review, verification, and completion contract, and the tracked `.claude/skills` symlink points into it, so repointing that one link redirects all of them. `docs/decisions/` holds the architectural decision records, the two templates that shape the next one, and the process that admits it: an ADR is what a later change to architecture, boundaries, persistence, configuration, or security-sensitive behavior is written to be consistent with, so rewriting one moves what the next change is judged against. The owner-approval rule `docs/AGENTS.md` and `docs/decisions/README.md` both state is what this prefix makes mechanical.

Six **file names** are matched at the repository root and after any `/`, so a copy at any depth is covered. `.editorconfig` decides which analyzer and style diagnostics `TreatWarningsAsErrors` turns into build failures, which header IDE0073 requires, and — through the contract suite, which parses the same template — the header every workflow, script, Helm template, and skill outside the solution has to carry; `.gitattributes` decides how the diff a reviewer reads is produced, down to whether a path has reviewable content at all; `.worktreeinclude` decides which gitignored files, local secrets among them, are copied into every worktree an agent works in. `AGENTS.md` carries the architecture, conventions, verification gates, and workflow contract every agent-authored change is written and judged against, which is the same kind of instruction the skills under `.agents/` carry, and `CLAUDE.md` is the tracked entry point to it exactly as `.claude/` is to `.agents/` — so protecting the directories and not these files would protect neither. Depth is what makes them a name match rather than a root-file match: a nested copy overrides the root one for its own subtree, so `backend/src/Infrastructure/Persistence/Migrations/.editorconfig` can relax for one directory what the root file enforces everywhere, and `backend/tests/AGENTS.md` states the test rules that the root file does not. `Directory.Build.props` carries the analyzer and warning policy every project beneath it inherits and the SPDX and copyright metadata that ships inside the assemblies, and it is matched by name rather than at one path for the reason the two stacks exist: `backend/Directory.Build.props` owns the .NET build contract, a second stack's copy decides the same thing for its own subtree, and a copy placed above either would decide it for both. The anchoring is to a whole path segment, so `docs/my.editorconfig` is not caught and neither is `docs/CONTRIBUTING-AGENTS.md`.

Seven **repository-root files** are matched whole, so a file of the same name elsewhere is not caught and a longer name beginning the same way is not either. `Version.props` carries the declared version every build is stamped with and every artifact is named after, and it sits at the root because every stack imports it, so an edit there moves the number the whole product reports. `CLA.md` states the terms every contribution arrives under, so narrowing it would change what this repository may do with everything merged after it. `LICENSE` is the grant itself, detected by matching the file against the known Apache-2.0 text, so any edit turns a detected `Apache-2.0` into `NOASSERTION`; `NOTICE` is the attribution Apache-2.0 section 4(d) preserves. `NuGet.config` decides which feeds a package may come from, and it clears the inherited source list, so adding a source is a supply-chain and licensing decision. `global.json` pins the SDK every build and every gate resolves against. `CHANGELOG.md` is there for a different reason: it is written by the release pull request alone, so an edit arriving through ordinary work is out of band by construction.

The set is written out rather than expressed as a pattern: an entry joins it because a change to it moves what the repository enforces, what the project is published under, or what a release claims it shipped — not because of how it is spelled. A dotted directory is not protected merely for being dotted, and `docs/decisions/` is the entry showing the converse: prose joins the set when a later change is judged against it, while the rest of `docs/` stays outside it, because documentation that describes implemented behavior is corrected by the change that made it wrong. `backend/Directory.Packages.props` is deliberately not in it either, because a version bump there is ordinary contribution-shaped work and the review it needs is `THIRD_PARTY_LICENSES.md`'s rather than this gate's.

Whichever way it decides, the job prints the protected paths the pull request touched, to the step log and to the job summary, and a refusal additionally annotates each one in the Files changed view. The pass needs that list as much as the refusal does: the owner is allowed to change these paths, not assumed to have meant to, and a `.editorconfig` that arrived with a rebase is only visible if something says so.

What it reads is the pull request's author, not the author of each commit, so a commit pushed by someone else onto a pull request the owner opened passes it. That case is the code-owner review's to catch, and `Require approval of the most recent reviewable push` is the ruleset setting that would tighten it; [Code owners](#code-owners) below records why it stays off until a second code owner exists.

The third workflow, `Typo check`, carries one job of the same name and spell-checks the words a pull request changes. It checks out the merge commit the pull request would produce, reads the changed files through the GitHub REST API, and hands that list to `crate-ci/typos`, pinned to a commit. Every finding becomes an annotation in the Files changed view, and the job fails when there is one. It reports no required status check and is not in the `main` ruleset: a misspelling is worth surfacing on the pull request, not worth blocking a merge over.

It is the one pull-request workflow with a draft exemption, and the only one. A draft is a change still being written, where a half-finished sentence is expected rather than reportable; marking the pull request ready starts the job through `ready_for_review`, and later commits keep starting it through `synchronize`. There is no path filter, because there is no such thing as a change this check does not concern — prose is what it reads, and prose is in a C# doc comment, a workflow's own comments, and a Helm value's description alike.

Three situations leave the job unable to pass a list it can trust, and all three widen its scope rather than narrowing it. A pull request larger than the 3000 files the changed-files endpoint reports would arrive incomplete. A changed path containing whitespace or a glob character would arrive as different paths altogether, because the action receives its file list as one unquoted string: whitespace splits one path into two, and `docs/a[1].md` becomes `docs/a1.md` where that file exists. And an exclusion in the configuration that is not a path anchored to the repository root — a glob, a negation, or a name that matches at any depth, which under gitignore's rule is any pattern carrying no `/` before its last character — cannot be applied to a list of files exactly, so the job hands over the whole checkout, where `typos` applies the pattern itself. Every one of the three checks the whole checkout instead, which is more than the pull request changed and never less; a pull request that only deletes files, or that changes nothing but images and excluded paths, leaves nothing to read and skips the check entirely. Scanning everything is only a workable fallback because the tree is kept clean, which is the job the configuration below does.

`.config/typos.toml` is that configuration, and it separates three kinds of entry that a single list would blur. Accepted vocabulary is spellings MailFathom uses on purpose — `unparseable`, `requeueing`, `HashiCorp` — where correcting the dictionary's objection would be a repository-wide rename in service of no reader. Fixtures are the opposite: `Directroy`, `Enabeld`, `Authentcation`, `Passwrod`, and `MaxAttemps` are misspelled because that is their job. Every security-sensitive configuration section binds strictly, so a key nobody defined fails startup instead of binding silently, and the tests that prove it and the documentation that explains it have to name the misspelling the rule catches; correcting one deletes the example and, in a test, its assertion. Non-prose literals are the third kind and are not English at all: the argument of a `[GeneratedRegex]` attribute, which is a credential format rather than a sentence and whose alternations put two-letter runs of base64 and vendor prefixes in front of a dictionary; a rule name such as `airtable-personnal-access-token`, reproduced exactly as the corpus it came from spells it because that name is what an operator types to suppress the rule; and the base64 of the Latin alphabet the synthetic secret fixtures are built out of. Rewriting one of those changes what an expression matches or what a suppression has to say, so the entry records that the string is data rather than that its spelling was reviewed and kept. The file also turns off the tool's default of skipping hidden files, because most of the prose that decides how this repository works sits behind a leading dot — `.github/workflows/`, the skills under `.agents/`, `.editorconfig`. Version-control metadata under `.git/` stays excluded regardless. Two paths are excluded whole rather than word by word, because a word admitted in the vocabulary is admitted everywhere: `frontend/pnpm-lock.yaml`, which is package names and integrity digests rather than prose, and `frontend/src/Client.App/src/localization/pl.ts`, the client's Polish catalogue and the one part of this repository deliberately not written in English. Both exclusions govern the walk `typos` does over a directory, and neither survives the path being named on the command line, which is what the workflow does with the files a pull request changed. So the workflow applies them itself: the step that builds the list reads the excluded paths out of that same file and drops them before handing the rest over, which leaves the exclusion one decision in one place rather than one that depends on how the run was started. Both steps name the configuration through a single `TYPOS_CONFIGURATION` variable for that reason — a list filtered against a file the checker never read would be the same defect in a new shape.

The workflow names that path rather than relying on it being found, because `typos` looks for a configuration file only under a fixed set of names and only alongside or above the file it is checking; the checking step states the rule it is working around. Two consequences follow for a reader rather than for the workflow. A `typos` run started by hand needs `--config .config/typos.toml`, or it applies none of the above. And the path puts the vocabulary under the `.config/` prefix `Protected paths` covers, so adding a word is a change only the owner can merge — `CONTRIBUTING.md` says so where a contributor meets it.

The fourth workflow, `CodeQL`, carries one job, `Analyze C#`, and is the only check here that reads what the code *does* with a value rather than how it is written. It restores in locked mode, initializes CodeQL in `manual` build mode, builds `backend/MailFathom.slnx` in Release configuration inside the traced window, and runs GitHub's C# security query pack over the resulting database. It runs for a pull request, for a push to `main`, weekly on a schedule, and on manual dispatch, and it carries the same draft exemption `Typo check` does — for a stronger reason, since it is the one check that occupies a runner for minutes.

Three of its decisions are the ones a reader would otherwise have to reconstruct, and the workflow file argues each at length. It is an advanced setup rather than GitHub's default setup, so the check that reads this repository's source is a file a pull request can change and a reviewer can read, and so it can see the SDK pin and the locked restore. Its build mode is `manual` rather than `none`, so the analysis sees the graph the committed lock files fix instead of one CodeQL resolved for itself. And its last step compares the extracted source archive against what `backend/src/` contains, because a bundle that cannot extract this SDK's output produces an empty database and a green check — an answer that looks like "no findings" and means "no analysis". The weekly run exists for a fourth reason that has nothing to do with this repository: a query pack updates upstream, so a commit that was clean when it merged can become a finding with nothing here having changed.

On a pull request from a fork the run gets the token GitHub grants that event, which is not the token a branch in this repository gets, and whether the alert upload succeeds there follows from GitHub's rules rather than from anything in this file. The check is required by nothing either way, so no merge waits on how it resolves, and the push to `main` after the merge analyses the same code under a token that certainly can upload.

The fifth workflow, `Apply pull request rules`, derives every fact a pull request earns and carries
two jobs, split by the event that can answer them. On a `pull_request` — opened, reopened, marked
ready, or edited — it checks out the merge commit and applies the labels
`.github/pull-request/select-labels.sh` says the change earns, today `security` when any issue the
body refers to carries it, whether the change closes that issue or merely names it. On a push to
`main` it instead sweeps every open pull request, asks GitHub whether each still merges, and writes
the roadmap board where `.github/pull-request/select-board-status.sh` says a state earns a status —
today, moving what a conflicting pull request closes from `Ready to merge` to `Conflicts`. Each job
runs on one of those events and skips the other, which is what keeps the labelling as short as it
was: `Fathom review` waits for this workflow's run before it reads the labels. It reports no status
check and blocks nothing; a draft runs it, because a label is worth having while the change is still
being written. It only ever adds a label, so one a hand applied stays. [Rules on the pull
request](agent-workflow.md#rules-on-the-pull-request) carries the reasoning, including why the
labelling takes `pull_request` rather than the trigger `Fathom review` holds and what that costs on a
fork.

### `CI` after a merge to `main`

`CI` runs on every push to `main` as well, and runs very nearly what a pull request gets — with one difference in the other direction: nothing here is narrowed by what the merge touched. Six of its eight jobs execute, and all six execute unconditionally: `Detect changes`, then `Backend / Build and unit test the service`, `Backend / Pending model changes`, the job behind `Frontend`, and both jobs behind `Repository contracts`. `Backend / dotnet format` and `Required CI` are the two that never run here.

Each absence has its own reason, and neither of them is cost.

- `dotnet format` is a property of the files a change wrote, which is exactly what the pull request that wrote them answered. It is also the one verdict here that two changes merging without seeing each other cannot break. The client's own formatting pass is exempt on the same argument and by the same route — `CI` passes `run-format` to both workflows, so one rule about one kind of verdict is written once and applied to both stacks — and today that input reaches a job which formats nothing. `Release` and `Nightly` are exempt on it too, and pass the same input as `false`, which is what makes this a rule about the verdict rather than about the event — a tag and a nightly are downstream of the push this paragraph exempts, and no commit reaches either without having been a pull request. The model check is not: one pull request can move the EF Core model while another adds a migration, and neither run sees both, which is why that job stays.
- `Required CI` exists to be the `main` ruleset's one required check, and a ruleset evaluates a pull request. On a push nothing waits for a conclusion and every job it aggregates reports one on its own, so a further name in front of the same results would add a check rather than an answer. The run's own conclusion is what a push reports through. On every event the ruleset does read, the job still runs under exactly that name.

`Repository contracts` runs here for the reason the model check does, applied to the half of the repository the build cannot see. What the suite asserts is a property of the whole tree — a licensing header in every `.yml`, `.sh`, Helm template, Quadlet unit source, documentation-site asset, and `SKILL.md`, a `describes:` marker covering every page under `docs/`, a table-of-contents entry behind every published page and a page behind every entry — and each of those invariants spans files that different changes own. Two pull requests can therefore each be sound over the tree they were verified against and break one over the tree their merges produce: one deletes a page while the other adds the entry naming it, or one renames a directory while the other writes a marker pattern that no longer resolves. That tree is the one no pull-request run was ever shown, and this is the only run that sees it.

The `push` trigger carries no path filter either, and nothing narrows it inside the run. The `pull_request` reason does not reach here: that one protects a required check, because a run GitHub never instantiated reports no conclusion and a pull request would wait on it forever, and nothing waits on a push run. What decides this trigger is what the run is asked. A push is asked whether `main` is green, and a run that verified the part of the tree this merge happened to touch has not answered that: two changes that never saw each other are exactly what a merge produces, and the paths one of them moved say nothing about which half of the tree the pair broke. The contract suite has been read this way since it existed — it takes the whole tree at once, and `docs/`, `deploy/`, `scripts/`, `.agents/`, `.claude/`, and the repository-root Markdown files are its subject matter rather than paths it can be told to ignore — and the argument holds no less for a solution that two merges can leave uncompilable while each pull request compiled. So a merge costs a full build, a coverage run, and both contract jobs, whatever it moved.

Why run any of it, when the `main` ruleset requires a branch to be current with `main` before it merges and the run therefore normally repeats a verdict. Three things, and each of them is also why the run is not narrowed. The repository admin role bypasses the ruleset when merging, for the reason [Code owners](#code-owners) gives. `Nightly` and `Release` do gate the commit they publish, so a broken `main` is caught before anything reaches a registry — but without this run the earliest it is *reported* is that night's publication failing its own gate, which names a scheduled run at 02:00 rather than the merge that broke it. And *is `main` green right now* becomes a fact with a run behind it rather than one inferred from whichever pull request closed last.

Concurrency differs by event too. Cancelling a superseded run is a pull-request behavior, because there the run being cancelled is answering about a commit that is no longer the head. A push to `main` is the opposite case: every commit there is a state that was merged and that a nightly can be built from, and a run the next merge cancelled would leave that commit carrying a conclusion which reads as a failure everywhere while having verified nothing. Merges in quick succession queue behind each other instead, and a manual dispatch on `main` — which shares their concurrency group — queues rather than displacing whichever of the two is running.

The `CI` badge in the repository `README` reads this event and nothing else. Its `branch=main&event=push` query is what makes the badge a statement about `main`; without it the badge would report whichever run finished most recently, which is usually a pull request's and says nothing about the branch a reader would be installing from.

Nothing else moves. `Typo check` and `Protected paths` stay pull-request-only, because both answer questions about a change under review rather than about a branch, and `CodeQL` already watched `main` this way for its own reasons.

### Why the typo check is a third workflow

The reasoning is the protected-paths one applied to a different verdict. `Required CI` says the change is sound and `Protected paths` says its author may make it; this says a word is misspelled, which is a third unlike answer and deserves its own status line rather than a share of one. Folding it into `CI` would also tie a job that needs no SDK, no restore, no cache, and no build to the concurrency group, the change detection, and the aggregate of the jobs that need all four.

It is also the workflow whose difference from the other two is worth keeping visible. `Protected paths` deliberately has no draft exemption, because the fact it reports is most useful in the first minute. This one has the exemption for the opposite reason: what it reports about a draft is mostly noise about sentences the author has not finished writing.

The remaining workflow, `Integration tests`, is manual dispatch only and never runs for a pull request. See [Integration tests](#integration-tests) above.

### Why one workflow with a conditional interior

GitHub reports a workflow that an `on.pull_request.paths` filter excluded as neither successful nor failed. A required check then never arrives, and a documentation-only pull request waits indefinitely on a run that was never created. The filtering therefore moved inside the workflow: the trigger has no path filter, `Detect changes` decides what the change can affect, and the expensive jobs skip themselves through `if` conditions. A skipped job reports `skipped`, which is an answer the aggregate can act on, unlike a workflow that was never instantiated.

`Required CI` is one job in one workflow for the same reason a required check is identified by its job name. Two workflows each publishing a job by that name would leave the ruleset ambiguous about which run it is waiting for, so the two former workflows behind what are now `Backend / Build and unit test the service` and `Backend / dotnet format` became jobs of this one. Its name must stay stable and independent of the event, the changed files, the source branch, and any matrix dimension, because that name is the entire contract with the branch ruleset.

### Why the protected-paths guard is a second workflow

The rule above is about a required check that aggregates jobs which are allowed to skip, and it is the reason the server's build and its formatting pass are jobs of `CI` rather than workflows of their own. `Protected paths` is not that shape. It has no path filter, no `if` condition, and no draft exemption, so it always runs and always reports a conclusion; there is nothing for an aggregate to make an answer out of, and adding one would only put a second name in front of the same verdict.

Keeping it out of `CI` keeps two unlike verdicts apart. `Required CI` says the change is sound. `Protected paths` says the author is allowed to make it. Folding the second into the first would leave one red check meaning either thing, and would tie a governance answer to the build pipeline's concurrency group, draft conditions, and change detection, each of which exists to let work be skipped.

The check is deliberately not a security boundary on its own. A `pull_request` run uses the workflow file as the pull request would leave it, so a pull request that rewrites this workflow is judged by the rewritten one. What closes the gap is the pair rather than either half: weakening the check means editing `.github/`, which `CODEOWNERS` sends to the repository owner for approval, and deleting or renaming the job leaves the required check permanently unreported, which the ruleset refuses to merge past. The two exits are covered by different mechanisms, which is why both are needed.

An outside contributor whose change genuinely needs one of these directories — a new local tool, a coverage setting, a workflow step — splits it out and asks the owner for that part. The guard is deliberately blunt about this: a change to what the repository enforces is worth a separate conversation, not a line inside a feature's diff.

### Draft pull requests

Draft pull requests skip the build, formatting, model-check, typo-check, and code-scanning jobs without allocating a runner; only the seconds-long `Detect changes`, `Required CI`, and `Protected paths` jobs and the two behind `Repository contracts` — the twenty-second `Workflow contracts`, and, when the change reaches the chart, the equally short `Helm chart` — do any work, and `Required CI` clears the ones that skipped because a skipped job is a valid outcome.

Skipping is not disappearing: each skipped job still reports a `skipped` conclusion and is listed among the pull request's checks, `Typo check` and `CodeQL` included. Its workflow puts no draft condition on the trigger, for the same reason `CI` puts no path filter on one — the run is instantiated and the decision is taken inside, where a job that declines to work still says so. A draft cannot be merged regardless. Marking a draft ready for review starts the applicable jobs immediately through the `ready_for_review` activity, and later commits continue to start them through `synchronize`. Converting a ready pull request back to draft cancels the superseded active run through the concurrency group and skips the replacement jobs. `CI` and `CodeQL` remain available through manual dispatch regardless of pull request state; `Typo check` and `Protected paths` carry no `workflow_dispatch` and run only for a pull request.

The two jobs behind `Repository contracts` are what allocates a runner for a draft, and their reason is the opposite of the typo check's. What follows is the contract job's argument, and the chart job's is the same one about a different half of the repository: it installs nothing, costs about as much, and verifies files a draft is as likely to be in the middle of editing as any other. A draft-exempt contract check would stay silent for as long as a pull request stays a draft and report for the first time at the end of that interval, which is the whole interval in which a broken contract is cheap to fix. What it verifies is also what a draft is as likely to have broken as anything else, since a workflow, a script, or a skill is edited while the pull request is still a draft like any other file. The cost that justifies the exemption elsewhere is not there either: about twenty seconds of runner time, against the SDK, restore, build, and analysis each exempt job pays for. A draft can therefore show a red `Required CI` while every other job in the workflow skipped, which is the signal working rather than a fault to chase; a draft is unmergeable either way.

### Branch protection

The `main` branch ruleset requires a pull request with one approving review from a code owner, dismisses stale approvals when a new commit is pushed, requires review conversations to be resolved, permits squash as the only merge method, and requires the branch to be current with `main` and the `Required CI` and `Protected paths` status checks to pass. Creation, deletion, and force-pushes of `main` are refused. The repository admin role bypasses the rules when merging a pull request, for the reason [Code owners](#code-owners) below gives. The GitHub repository coverage rule must remain disabled because GitHub Code Quality coverage uploads are unavailable for this user-owned repository; the required repository-owned check enforces the same 85% minimum against the complete configured code scope.

The required checks are exactly `Required CI` and `Protected paths`, and both are added to the ruleset by hand under **Require status checks to pass**. Requiring any other job reintroduces exactly the problem this arrangement removes: a job that legitimately skipped never reports a conclusion the ruleset accepts. `Typo check` is the live example — it skips on every draft — and it would not be required even if it never skipped, because a misspelling is a thing to fix rather than a thing to block a merge on. Those two never skip, which is what qualifies them and what their workflows are written to preserve — neither name may become conditional on the event, the changed files, the source branch, or a matrix dimension, because the name is the entire contract with the ruleset.

`CodeQL` skips on a draft for the same mechanical reason, but its case against being required is a different one and worth stating separately, because the severity argument runs the other way: a taint path is worth more than a misspelling, and if severity decided this it would be required. What decides it is that the verdict moves without a commit. The query pack is maintained upstream and updates on its own schedule, so the same tree can be clean one week and a finding the next; requiring the check would let somebody else's release stop MailFathom's, and it would do so at the moment a release is being cut rather than while the code is being written. GitHub's separate **code scanning merge protection**, which blocks on alerts above a chosen severity, is off for the same reason. What answers a security question here is the code-owner review the ruleset already requires, reading an alert that is on the pull request either way. Revisit both once the pack's false-positive rate against this code has been observed rather than guessed at.

`Protected paths` is required for a reason `Required CI` does not share. Its value when it passes is small; its value is that it cannot be removed. A pull request that deletes or renames the job stops the check from ever reporting, and a required check that never reports blocks the merge, so the only way to disable the guard is a change the guard's other half already sends to the repository owner. Leaving it unrequired would turn that into a red check anyone could ignore.

The ruleset lives in repository settings rather than in this repository, so a maintainer changes it there and this section is the record of what it has to say.

### Code owners

`.github/CODEOWNERS` names `@Krzysztof318` as the owner of every path, and it is the half of the review requirement that lives in the repository. Requiring code-owner review without that file requires nobody: the ruleset asks for the approval of whoever owns the changed paths, and a repository with no `CODEOWNERS` has no owner for any path, so the condition is satisfied vacuously. The two settings are only a gate together.

Naming an owner is deliberately not the same as requiring one approval from anybody. An arbitrary approving review satisfies the count and says nothing about who gave it; the code-owner requirement is what makes the approval have to come from the maintainer. Both stay on, because the count alone would be a weaker rule wearing the same name.

The repository is on a personal account rather than in an organization, so the owner is a user. A GitHub Team is not available here and is not a substitute to reach for.

The file's ordering carries a rule of its own. GitHub applies the last matching pattern, so the repository-wide entry is first and a path-specific entry added below it replaces ownership for that path instead of adding to it. A directory that must still require the maintainer names them among its owners rather than relying on the global line.

The paths `Protected paths` guards are deliberately not restated here. The repository-wide rule already makes the owner their code owner, so an entry naming them would change nothing, and it would not survive a path rule added below it either: the last matching pattern wins over a restatement exactly as it wins over the global line. A path earns an entry when a rule gives it other owners as well, and that entry is where the owner has to be named alongside them.

That leaves the two halves of the protection doing different work rather than the same work twice. This file decides whose approval merges a change, and it is read from the base branch, so a pull request cannot alter the owners its own merge requires. The `Protected paths` check decides whether the change belongs in that pull request at all, and it answers within seconds of a push, before a reviewer is involved.

GitHub does not let the author of a pull request approve it. Every pull request the maintainer opens is therefore unapprovable by the only code owner, which is why the ruleset lists the repository admin role as a bypass actor in `pull_request` mode: the maintainer merges their own pull request through the bypass, and a pull request from anyone else has no bypass available and waits for the code-owner review. Removing that bypass without adding a second code owner would make the repository unmergeable rather than more careful.

`Require approval of the most recent reviewable push` stays off for the same reason. It requires that the approval come from someone other than whoever pushed last, so on a single-maintainer repository it removes the one path a self-authored pull request has to a satisfied rule while adding nothing to a pull request that already needs an outside owner's review. Turn it on when a second code owner exists.

### Shared workflow behavior

Both expensive jobs restore from a cached `~/.nuget/packages` keyed on `backend/Directory.Packages.props`, `global.json`, `NuGet.config`, `.config/dotnet-tools.json`, and every `packages.lock.json`. Those files decide the versions, the permitted sources, and the resolved transitive closure, which together are the whole of what restore downloads, so a changed pin or a changed source policy misses the cache rather than resolving against a stale package set.

The client's job keys two entries of its own rather than sharing the one above — two graphs that barely overlap under one key would evict the whole cache on every change to either stack, which is the cost the stacks were separated to avoid. Both are keyed on `frontend/pnpm-lock.yaml` and nothing else: the pnpm store, which the lock file fixes the resolved closure of, and `~/.cache/ms-playwright`, because the same lock file pins the Playwright version that decides which browser build is downloaded.

The workflow uses the SDK pinned in `global.json`, cancels superseded runs for the same pull request, requests read-only repository permissions, and avoids credentials or service-specific secrets.

## GitHub Actions policy

Half of what governs Actions here is committed and half is a repository setting, and neither half is
worth anything alone: restricting the settings while the YAML references a mutable tag leaves the
reference mutable, and hardening the YAML while the settings admit any action lets the next workflow
introduce one nobody reviewed. This section records both, so the half that no diff shows is written
down somewhere a change to the other half will read.

**What the contract suite asserts**, on every pull request and every publication through the `Workflow contracts`
job of `repository-contracts.yml` and again locally in `scripts/verify-full.sh`, from
`scripts/test-agent-workflow.sh`:

| Contract | What it refuses |
|---|---|
| `every_external_action_names_an_approved_owner` | An action from an owner outside the reviewed set: `actions`, `github`, `Krzysztof318`, `dorny`, `anthropics`, `docker`, `crate-ci`, `aquasecurity`, `oras-project`, `peter-evans` |
| `every_workflow_job_declares_its_permissions` | A job that inherits the repository default instead of declaring a `permissions:` block, at the workflow level or its own |
| `every_write_scope_is_one_the_policy_records` | A write scope appearing anywhere the list in that contract does not already name |
| `every_checkout_refuses_to_persist_credentials` | An `actions/checkout` step that leaves the workflow token in `.git/config` for the steps after it |
| `only_the_recorded_workflows_use_pull_request_target` | A third `pull_request_target` trigger beside the two `fathom-review.yml` and `contributor-licence.yml` hold, and a licence workflow that checks anything out, reaches an action beyond the token mint, or runs a command fetching the contribution directly |
| `the_reviewer_resolves_one_claude_credential_everywhere` | A step in `fathom-review.yml` reaching a Claude credential without the `CLAUDE_CODE_PROFILE` selector, or a fifth step holding one, either of which leaves a leak check comparing a review against a token no run spent |

Every write scope in the repository but two belongs to publishing something: `packages: write` with
`id-token: write` and `attestations: write` in `nightly.yml`, `publish-container-image.yml`, and
`publish-helm-chart.yml`, plus `packages: write` on the nightly prune job and `contents: write` on
the job that writes the release announcement. `publish-documentation.yml` holds `pages: write` with
`id-token: write` on its deploying job alone, which is what a Pages deployment takes and no more —
the documentation site is published by the repository's own deployment rather than by pushing a
branch, so nothing there writes to the repository at all.

`release.yml` states them for each workflow it calls, because a caller hands down the permissions the
called workflow runs under. It calls two that need any — the image and the chart — so it carries
`packages: write`, `id-token: write`, and `attestations: write` twice each, plus the `contents: write`
its own announcing job holds. The jobs that build the schema artifact and the command binaries need
nothing beyond a read, because neither publishes anything: they upload an artifact the announcing job
attaches.

The first exception is `security-events: write` in `codeql.yml`. It is what the analysis is for: the
scope writes code-scanning alerts and nothing else — not repository contents, not a package, not a
release — and an analysis that cannot record an alert produces a log line instead of a check.

The second is `contents: write` in `fathom-review.yml`, on the one job that turns a review somebody
asked for in a comment into the `repository_dispatch` that performs it. That is the narrowest
permission `POST /repos/{owner}/{repo}/dispatches` reaches, and it is the widest of the two
exceptions, so what contains it is where it is held rather than how it is scoped: the job checks
nothing out, runs no model, holds no Claude credential, and its whole body is one API call built from
values the gate already decided. `main`'s ruleset is what stands between that token and the default
branch. [When a review is cancelled](agent-workflow.md#when-a-review-is-cancelled) is why the comment
path is two runs at all.

Both are held by jobs reached from a pull request rather than from a publication — one from the
change itself and one from a comment on it — and the contract above is what keeps the list honest, so
adding a third such scope is an edit somebody argues rather than a line nobody notices.

**What lives in the repository settings**, which no check here can read:

| Setting | Value | Why |
|---|---|---|
| Default workflow token | `read` | A job that needs more says so in its own `permissions:` block, which is reviewable; a permissive default is not |
| Actions may create or approve pull requests | disabled | A workflow approving its own change would satisfy the ruleset's review requirement without a person |
| Allowed actions | `all` today; #160 owns narrowing it | The allowlist is the settings-side twin of the owner contract above. The contract suite already refuses an owner outside the reviewed set on every pull request, so what the setting adds is coverage of a workflow that reaches the repository some other way |
| Require actions pinned to a full-length commit SHA | off, and deliberately | Turning it on would refuse every reference this repository makes. Actions are referenced by major tag so that an upstream fix arrives without a commit, which is the whole update mechanism now that nothing proposes one; a commit SHA freezes that and makes each patch a pull request somebody has to open. Two references are exact versions for reasons their own workflows state, and neither is a supply-chain one. `THIRD_PARTY_LICENSES.md` records each version and the argument for allowing it, which is where the trust in a reference actually comes from, and [Keeping the pinned actions current](#keeping-the-pinned-actions-current) carries the rest |
| Artifact and log retention | 30 days | The REST API exposes no retention field, so the settings page is both where it is set and the only evidence it was |
| Cache retention and size | 7 days, 10 GB | Unchanged unless measured eviction pressure argues otherwise |
| Fork pull request approval | `Require approval for first-time contributors` | The workflows a fork's push can start hold a read-only token and no repository secret, so a wider setting protects nothing this one does not, and a narrower one turns every first contribution into a maintainer's click. The REST API exposes no field for it, so the settings page is the only place it can be read |
| Pages build and deployment | Source: `GitHub Actions` | The documentation site is deployed by `publish-documentation.yml` rather than built by Pages from a branch. The workflow cannot set this itself: `actions/configure-pages` can enable Pages only with a token carrying administration rights, and holding one would defeat the point of a publishing job that reaches nothing else. [The documentation site](documentation-site.md) records the rest |
| GHCR package access | inherited from the repository | A package's visibility is its own setting rather than one it takes from the repository, so it is configured to follow the repository's access instead of being set again beside it. A private package would break the anonymous `docker pull` every installation path documents |

The retention rows, the fork approval, and the package access are the ones to re-read after any
settings change, because no API exposes them and nothing else will notice them moving.

**A fork's pull request** runs `CI`, `Protected paths`, `Typo check`, `CodeQL`, and `Apply pull
request labels` on the `pull_request` event with a read-only token and no repository secret, which is
what makes running a contribution's code safe at all. The last of those is the one whose work that
token refuses: it resolves the labels and reports that it could not apply them, so a fork's pull
request is labelled by a maintainer's hand exactly as its review is started by one. `Fathom review` is the exception and stays one: it holds an App
private key, so a fork's own pushes never start it and only a maintainer's `fathom-review` label or
comment does.
[Why `pull_request_target` is a granted exception](agent-workflow.md#why-pull_request_target-is-a-granted-exception)
records the reasoning, and the contract above is the automated half of it.

### Keeping the pinned actions current

Every action a workflow runs is referenced by its major tag — `actions/checkout@v7`,
`docker/login-action@v4`, `github/codeql-action@v4`. The tag *is* the update mechanism: a run already
executes whatever the newest release under that major is, so an upstream fix reaches this repository
without a commit on either side, and what a version bump costs here is a major.

Two references are exact versions instead, each for a reason of its own. `crate-ci/typos@v1.48.0` is
pinned because the action's entrypoint hard-codes the `typos` binary it downloads, which makes the
reference decide the dictionary a pull request is judged against; a moving tag there turns a green
pull request red with no commit anywhere. `aquasecurity/trivy-action@v0.36.0` is pinned because the
project publishes no moving major tag at all — its `v0` line is exact versions only — so there is
nothing else to follow.

Nothing in this repository proposes an update automatically, and that is the deliberate half. A bump
is a licensing review before it is a diff: `THIRD_PARTY_LICENSES.md` records every action's version,
terms, and the argument for allowing it, so the change that moves a reference is the change that
updates the register, and neither half is worth having without the other. What a proposal would add
is a version number somebody still has to research; what it costs is a pull request a maintainer
reads every week.

So a major is caught by looking, and the looking is worth scheduling rather than assuming. The two
questions are whether a newer major exists and whether this repository would have chosen it, and only
the first of them is mechanical. [`scripts/update-dependencies.sh`](#reading-every-pin-against-its-upstream)
answers it for every action at once, beside the licence each upstream declares now — it reads a moving
reference against the newest major rather than against the newest release, so it never proposes an
exact tag lower than what a run already executes, which is what an update written the other way round
does. The second question is read from the upstream release notes and nowhere else:

```bash
gh api repos/<owner>/<action>/releases/tags/<tag> --jq '.body'
```

[Dependabot alerts](#repository-security-features) are the other half and cover a different failure.
They report a published advisory against something this repository pins, which is the case worth an
interruption; they say nothing at all about a version merely being behind, so they are not what keeps
a major current and reading them as if they were is the way a pin rots quietly.

`Protected paths` and `Fathom review` both still recognise `dependabot[bot]`, because
`Dependabot security updates` is a repository setting rather than a fact about this repository, and
an advisory the owner decides to act on that way arrives as a pull request from that author. Neither
recognition grants anything: the `main` ruleset asks the same code-owner review of such a pull
request as of any other, `Required CI` still has to pass, nothing auto-merges, and that author holds
no write-capable token.

## Repository security features

Every setting below is a repository setting rather than a file, so no check here can read one and no
diff will show it moving. This section is the record; the commands beside it are how the state is
read back, which is the only thing that confirms a setting rather than confirming that a call
succeeded.

```bash
gh api repos/Krzysztof318/MailFathom --jq '.security_and_analysis'
gh api repos/Krzysztof318/MailFathom/private-vulnerability-reporting
gh api repos/Krzysztof318/MailFathom/vulnerability-alerts -i | head -1   # 204 enabled, 404 disabled
gh api repos/Krzysztof318/MailFathom/automated-security-fixes
```

| Feature | State | Why |
|---|---|---|
| Private vulnerability reporting | enabled | `SECURITY.md` names the *Report a vulnerability* button as one of its two channels, and a policy naming a button that is not there sends the first researcher who reads it to the fallback. A report arriving this way carries a draft advisory, a private fork for the fix, and a CVE request path, instead of an email thread to convert by hand |
| Secret scanning alerts | enabled | An alert says a credential is already public and must be rotated. It is the half that covers what push protection did not stop, including anything a bypass let through |
| Push protection | enabled | The other half, and the more valuable one: it refuses the object before it reaches GitHub, which is the only point in a credential's life where the remedy is free. `CONTRIBUTING.md` states what a contributor sees when it fires and what to do about it |
| Secret scanning validity checks | unavailable | Part of paid GitHub Secret Protection. Recorded so a later reader reads the configuration as complete for what a free public repository gets rather than as half-finished |
| Non-provider (generic) patterns | unavailable | The same entitlement. Provider patterns — the credential formats GitHub's partners publish detectors for — are what a free public repository does get, and they are what the two rows above run on |
| Push-protection bypass | nobody but the repository owner | Delegated bypass, which routes a bypass request to a reviewer, is a paid feature. Without it the question reduces to who holds write access, and that is the owner alone. A contributor who believes a block is a false positive says so in the pull request; there is deliberately nothing for them to click |
| Dependabot alerts | enabled | The advisory database's opinion about the pinned closure, which is worth having whatever can be done about it automatically. It is the only Dependabot half that runs here, and it reports rather than proposes |
| Dependabot malware alerts | enabled | The same shape of thing for a different failure: a package that is not vulnerable but hostile. It reports and opens nothing, so the lock-file argument below does not reach it |
| Dependabot version updates | inert | The switch reads `.github/dependabot.yml`, which this repository does not have, so nothing is proposed whichever way it is set. The mechanism is the file, and leaving the switch alone keeps it one decision rather than two that can disagree. [Keeping the pinned actions current](#keeping-the-pinned-actions-current) carries why an updater is not what maintains a pin here |
| Dependabot security updates | off, and deliberately | This is the half that opens a pull request, and for NuGet it would open one that cannot go green: the fix edits a central pin without regenerating the lock files, and every gated restore runs in locked mode, which fails with `NU1004` rather than resolving. Regenerating them is exactly what the NuGet updater does not do — [dependabot/dependabot-core#13950](https://github.com/dependabot/dependabot-core/issues/13950) records that lock files of projects reached transitively through a `<ProjectReference>` are left untouched under central package management, and it is open. Turn this on for NuGet the day that issue closes and a bump is shown to restore in locked mode, not on the strength of the updater existing; for actions it would work today, and what it would add over an alert is a diff the register still has to be written against by hand. The alert is what the owner acts on; the bump is made by hand, with `dotnet restore backend/MailFathom.slnx --force-evaluate` |
| Code scanning | advanced setup, `.github/workflows/codeql.yml` | Described under [Pull request checks](#pull-request-checks) above |
| Code scanning merge protection | off | The reasoning is [Branch protection](#branch-protection)'s: a query pack updates upstream, so a required verdict here can change with no commit on either side |
| Copilot Autofix | off, and deliberately | It drafts a patch for a CodeQL alert by sending the code around it to a hosted model. Every AI service this repository uses carries a row in `THIRD_PARTY_LICENSES.md` naming exactly what a run submits and under whose terms, and a suggested patch on a repository where one maintainer reads every finding anyway does not earn that row. Turning it on means writing the row in the same change |
| Code scanning check-run failure threshold | `High or higher` for security alerts, `Only errors` for standard ones | This decides when the `CodeQL` check reports failure, not when a merge is refused — the second would take a branch ruleset, which the row above declines. A high-severity finding is worth a red check somebody looks at |
| Automatic dependency submission | off | It submits dependencies observed during a build, for ecosystems that resolve at build time. The committed lock files already give the dependency graph the exact closure, so there is nothing here for it to discover |
