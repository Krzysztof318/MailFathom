# The end-to-end client run

<!-- describes: scripts/run-end-to-end-client.sh, .github/workflows/end-to-end-client.yml, frontend/playwright.end-to-end.config.ts, frontend/tests/end-to-end/**, backend/src/AppHost/Program.cs, backend/src/AppHost/OrchestrationContract.cs -->

One pipeline, started by hand, that stands a throwaway MailFathom up the way an operator stands one up and then drives
the real client against it in a real browser.

It exists because nothing else does that. The client's browser suite answers every request from the page's own routing,
which is what keeps it fast and hermetic and what makes it silent on whether the client and the service agree; the
service's integration suite starts a composed host and proves the pipeline behind the HTTP boundary, and stops there.
Between them sits the question of whether a person can sign in, read the mail the deployment synchronized, and work the
calendar, the task list and the address book it holds for them — and that question is what this run answers.

## It gates nothing

Nothing in this repository waits on it. It is not a pull-request check, it is not a required status, it is not on a
schedule, and neither [the nightly](agent-workflow.md) nor [a release](release-procedure.md) calls it. A change that
would break it still merges green.

That is the decision rather than an omission, and it follows from what the run costs. Two container images are pulled,
the client bundle and the service are both built, the schema artifact is generated through `aspire publish`, a mailbox
is filled one message at a time over SMTP, and a browser then drives the result — roughly fifteen to twenty minutes of
runner time. It calls no AI provider, so it spends no provider credit. What it answers is *does the whole thing still
hold together*, which is a question worth asking before a release and not worth asking on every push.

## Running it

```bash
bash scripts/run-end-to-end-client.sh                 # the whole run
bash scripts/run-end-to-end-client.sh --grep thread   # trailing arguments are forwarded to Playwright
```

`End-to-end client` in `.github/workflows/` is the same script on a runner, reachable by manual dispatch alone and
taking a `ref` to run against. It uploads the browser's traces and screenshots and the logs each step wrote.

The machine needs what the two stacks already need — the .NET SDK, Node and pnpm, and a container runtime — plus two
things a verification gate does not ask for: the Aspire CLI, at the version `backend/Directory.Packages.props` pins the
hosting packages to, because the schema artifact comes from `aspire publish`; and a Chromium for Playwright, from
`pnpm --dir frontend exec playwright install chromium`. `MAILFATHOM_CONTAINER_RUNTIME` selects a runtime other than
`docker`.

## What it stands up, in order

Each step is named, and a failure reports the step it happened in rather than only a non-zero exit — because a corpus
that never reached the mailbox and a client that cannot draw a list are two different defects.

| Step | What it produces |
| --- | --- |
| The client bundle | `pnpm install --frozen-lockfile` and `pnpm build`, the two commands the published image's client stage runs |
| The schema artifact | `scripts/build-schema-artifact.sh`, the same artifact a release attaches and an operator applies |
| The database and the mail server | The app model, on the topology `EndToEndClient=true` selects: PostgreSQL and GreenMail under the ephemeral run prefix, and no MailFathom |
| The schema | The artifact applied with `psql` inside the database container, the route [the schema page](database-schema.md) gives an operator whose database is not reachable from where they stand |
| MailFathom | `dotnet publish` of `Host`, the bundle copied to `wwwroot/` beside it as the image does, started with the client endpoint on and serving the page |
| The mailbox | `POST /api/admin/users` recording the user this run serves — the database it created holds nobody — then `POST /api/admin/mail-accounts` creating the mail account and assigning it to that user, because no configuration source declares a mailbox — the mailbox is served from that write, without a restart. The record states the account's language and carries an `env:` reference to the mailbox password rather than the password, because MailFathom refuses a secret's material into a persisted record whatever wrote it |
| The credential | `POST /api/admin/users/{id}/credentials` on [the administrative endpoint](admin-endpoint.md), so the password this run signs in with was made the way an operator makes one |
| The mail | `SyntheticMail replay` of `backend/tools/SyntheticMail/corpora/office-en.zip` into the mailbox over SMTP |
| The synchronization | Polling [the folders route](client-endpoint.md) until the account's run reports no failed folder and at least one synchronized one |
| The client | The end-to-end Playwright suite, against the origin the deployment serves — whose global setup writes the sample calendar, task list and address book over the client API before the first browser opens |

Everything created is removed when the run ends, whether it passed, failed, or was interrupted: the containers and their
volumes are named under this run's own identifier and deleted by name, so a concurrent integration suite keeps its own.

**The app model owns the two servers and this script owns nothing that runs in a container.** The pipeline declares no
image, no tag, and no container configuration of its own, for the reason [the integration suite](local-development.md)
gives about the same thing: what the run exercises has to be the PostgreSQL and the mail server every other run of this
repository exercises, at the same pins. `EndToEndClient=true` is an argument rather than a variable, so an ambient value
cannot divert an ordinary `aspire run` onto it.

**The client endpoint's per-user budget is raised for this run, and the limiter stays on.** The default allows one user
a burst of 120 requests restored at 120 a minute, queueing none beyond it — a bound written for a person reading their
mail. What drives this endpoint is a browser suite signing in once per case and drawing four spaces as fast as it can,
so the suite spends that budget inside its first minute and every request after it is refused; the client reports a
refusal as *the deployment did not answer*, which reads as a broken screen and is not one. The run therefore states a
budget no suite can exhaust rather than switching the limiter off, so a request still takes the path it takes in a
deployment.

**Every wait is on a condition rather than on a duration.** The database is waited for with `pg_isready` over TCP —
over the socket it would report ready against the temporary server the image runs while it initializes its data
directory, and that server is stopped, and its socket removed, under the command that follows — the mail
server on its own readiness route, MailFathom on its `started` probe, and the mailbox on what the account's
synchronization run reports. A sleep long enough for a slow machine is time every fast run pays, and one short enough
for a fast machine is a flake — and the last of those four is the one that matters most, because a first synchronization
of a filled mailbox takes as long as it takes.

## What the specs assert

`frontend/tests/end-to-end/` holds them and `frontend/playwright.end-to-end.config.ts` runs them. They cover the path a
person takes through each of the client's four built spaces: signing in, the mail list, opening a message, the
conversation it belongs to, a search over the mailbox, and signing out; the calendar in each of its four views and an
event written, amended and deleted; the task list under its three headings, the day beside it, and a task written,
marked done and deleted; the address book, somebody opened out of it, and a contact written and deleted. Everything is
asserted by role and by the words a person reads, as the client's unit and browser suites are. Its fourth suite is the
exception that proves nothing about this one: the desktop suite reaches its WebView over the WebDriver protocol, which
has no locator for a role and a name at all.

**Only Mail arrives by itself, so the other three are seeded.** Mail reaches the deployment because it was delivered to
a mail server and synchronized out of it; a calendar, a task list and an address book are written by the person who
owns them, so a deployment nobody has used holds none of them. `frontend/tests/end-to-end/seed.ts` is this repository's
sample set — three people, five events and five tasks, every name invented and every host a reserved one — and the
suite's global setup writes it over the client API with the credential the run provisioned, before the first browser
opens. Every day in it is an offset from the day the run happens on, stated in UTC against the zone the configuration
pins, so *Today*, *This week* and *Later* are each reachable whichever weekday a run lands on. Each of the three spaces
also writes one record of its own through its screen and removes it again, which is where the client's own write path
is held to account and what keeps the files runnable in any order.

**Discover is held to being a placeholder.** The client opens there, `routing/spaces.ts` leaves it out of
`implementedSpaces`, and nothing in the client reads the discovery routes — so there is no answer to ask this
deployment for, and configuring a chat provider would not produce one. What the spec holds is that the space is
reachable, that it says it is not built rather than drawing an empty frame, and that the question field the frame
composes for every space stands on it.

**They route nothing.** The bundle the deployment serves reaches the surface that deployment serves, over one origin,
and every answer comes from mail that arrived at a mail server and was synchronized out of it. A `page.route` here would
make this suite a slower copy of the pull-request one.

**They assert what the client says rather than what the corpus says.** The mail is generated, so no subject, sender, or
sentence in it is a value to write down; a spec restating one would be asserting against the archive rather than against
the service.

**It is a configuration of its own, and the pull-request suite is unchanged.** `frontend/playwright.config.ts` ignores
this directory so it cannot pick up a spec that needs a deployment, and that is the whole of the change to it. Folding
the two together would put a hermetic suite one mistake away from reaching a service.

## What it keeps, and why that is allowed here

The run keeps its traces and screenshots, and the workflow uploads them. That is the opposite of what the pull-request
browser suite does, and the difference is the mail rather than the storage: every message here is a corpus this
repository generated, delivered into a container that is destroyed with the run, and the credential is a password that
exists for the length of one run. A capture shows nobody's mailbox and a trace carries nobody's credential.

The other suite's rule is unchanged and is the general one: the moment a capture could show real mail it is personal
data, whatever produced it, and it stays on the machine that took it.
