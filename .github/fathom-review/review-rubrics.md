## What to look for

Six rubrics. Apply each one the change actually reaches, and say nothing about the
ones it does not: these describe where defects have been found here, not a form to
fill in.

### The repository's rules

`AGENTS.md` is a contract, so breaking it is a defect even where the code would run
correctly. Give these the same weight as a wrong result:

- **Boundaries.** `Domain` depends on no framework. `Application` depends only on
  `Domain` and owns its ports. `Infrastructure` keeps EF Core entities, MailKit types,
  Npgsql and `bytea` details, MCP SDK types, and provider-specific AI types inside the
  adapter that owns them. `Mcp` maps protocol to use cases and holds no persistence or
  mail-protocol logic. `Host` is composition, configuration, and wiring only. Raw RFC
  822 content is reached through `IEmailContentStore` and nothing else.
- **Naming.** Domain-correct, unabbreviated, and unambiguous where a reader meets it:
  `Email` and never `Message` or `MailMessage` for the mail artifact, `IMailboxSession`
  or `IPersistenceSession` rather than a bare `Session`, a method named after the
  result it produces rather than `Handle`, `Process`, `Manage`, or `Execute`.
- **Type shape.** Enum members carry explicit contiguous values that are never
  reordered or reused, `[Flags]` members are explicit powers of two with `None = 0`, a
  value that must publish an identity surviving a rename is a closed enumeration rather
  than an enum, data that represents a value is an immutable record or value object,
  implementations default to `internal sealed`, collections cross boundaries as
  read-only abstractions, and byte payloads cross them as `ReadOnlyMemory<byte>` or a
  span rather than `byte[]`.
- **Imports.** Every type reached through a `using` and written by its simple name,
  qualification only for a real collision and then on every side of it, and no `using`
  or `global using` alias anywhere.
- **Async and time.** I/O asynchronous end to end, a `CancellationToken` accepted,
  placed last, and propagated rather than replaced with `None` inside a chain, `Task`
  unless measurement chose `ValueTask`, no blanket `ConfigureAwait(false)` in
  application code, `DateTimeOffset` for timestamps, and an injected `TimeProvider`
  wherever current time affects behavior.
- **Failures.** An expected application failure is an explicit result type and an
  exceptional one is an exception; a `catch` exists only to add context, translate at a
  boundary, apply a defined retry policy, or complete cleanup, and preserves the
  original as `InnerException`. `null` never encodes more than one state.
- **Ownership.** A type that owns a resource implements the disposal contract that
  matches it and never disposes a dependency the container owns.
- **Documentation and licensing.** Public types and members documented, XML
  documentation that still matches the signature and behavior it describes, and a row
  in `THIRD_PARTY_LICENSES.md` for every dependency, service, image, or copied sample
  the change introduces, recording its exposure and the version the graph resolves.
- **Email invariants.** `(account, folder, UIDVALIDITY, UID)` is the remote occurrence
  identity, retrieval never sets `\Seen`, synchronization and outbox work is
  idempotent, an MCP read is served locally and never triggers a synchronous IMAP
  fetch, and every public query is keyset-paginated and bounded.

### Security and privacy

Email content, metadata, embeddings, retrieval snippets, tokens, certificate material,
and audit traces are sensitive by default, and an embedding inherits the retention,
access, deletion, and export constraints of the mail it was derived from.

- Untrusted input — email HTML, headers, filenames, URLs, tool arguments, model output,
  and whatever a remote server returns — is validated and encoded for the context it
  reaches, and an explicit size and count limit is applied at every public or remote
  boundary before the value is expanded rather than after.
- Nothing sensitive reaches a log, span, exception message, or exporter: no
  credentials, tokens, message bodies, attachment content, or raw MIME, and a value
  derived from an exception, a configuration entry, a certificate, or a server response
  is redacted first, on startup and shutdown paths too.
- Secrets are compared in constant time, tokens and security-sensitive identifiers come
  from a cryptographically secure generator, and database roles, OAuth scopes,
  filesystem access, and certificates carry least privilege.
- A security decision that fails open where the documentation says it fails closed, an
  authorization check reachable on only one of several paths, or a trust decision taken
  on a key alone is the most serious kind there is.
- Options are validated at startup, so unsafe or misspelled configuration fails fast
  instead of binding a default.

**When the pull request carries the `security` label.** Read the `labels` of
`pull-request.json` before the first pass. That label is this project's statement that the
change needs a security review before it merges — `docs/operations/issue-tracking.md`,
"Labels" — written on the issue and carried onto the pull request by a workflow of its own.
It is the one input here that changes how you work rather than only what you judge:

- Apply the rubric above to **every** file in front of you — every file of your group if
  you are reading one, the whole change if you are judging it — and not only to the ones
  whose diff invites it. What this label exists for is the path nobody was looking at: the second
  call site that skips the check, the failure branch that returns the value unredacted,
  the fake that stands in for the very thing being hardened.
- Where the change closes the security-labelled issue, confirm the weakness that issue
  names is closed on every path the change reaches, rather than on the one the diff
  illustrates. Merging closes the issue, so a weakness still reachable through a second
  entry point leaves a closed issue and a live defect. That is the most serious level
  there is, as a security defect and not merely as an unmet acceptance item. Where the label
  came from an issue the change is only related to — one you will not find in
  `issues.json` — there is no acceptance list to hold it to, and the rest of this section
  is the whole of what the label asks for.
- This widens what you **read** and nothing about what you report. The callers, the other
  implementations of the port, and the configuration that reaches it are all worth
  opening; a defect in code this change does not touch is still not a finding, and where
  reading wider is what shows the change to be incomplete, the finding anchors to the line
  in the diff that leaves it so.
- Say that the pass ran and what surface it covered — in your notes if you are reading a
  group, in the summary if you are judging, and there under an approval as much as under
  findings. The label asks for a security review, and a verdict that does not say what was
  examined is not evidence that one happened.

**The sweep is what the first three passes are for.** `REVIEW POSTURE` at the top of your
prompt says which kind of pass this is, and where it reads `settling` — a fourth automatic
pass or later — the four points above narrow to one: apply the security rubric to what the
change touches, as every other rubric here is applied, and leave the wide sweep behind. It
earns its cost while the change is still being shaped and stops earning it once the author
is answering threads, because a fifth reading of a path no fix moved finds what the four
before it already read and let through. Nothing else about the label changes: the costlier
model still performs the pass, the rubric above still applies in full to what the pass
reads, and a security defect is still the most serious kind there is. Say in your notes or
your summary that the pass was a settling one and what it covered, for the same reason the
fourth point gives — a verdict that does not say what was examined is not evidence of
anything.

The absence of the label means nothing at all: the rubric above applies to every change
that reaches it, and an unlabelled change is not read more loosely for it. An entry of
`issues.json` whose `labels` are `null` is an issue nobody was given, so the judge says
that in the summary rather than deciding either way about what it asked for.

### Reliability

- Every external call is bounded by a timeout, and its failure is sorted into caller
  cancellation, shutdown, timeout, authentication failure, or transient transport
  failure rather than collapsed into one outcome.
- A retry exists only where repeating is safe, and is bounded, jittered, and never
  nested inside another retry. An exhausted budget becomes the domain outcome its
  caller acts on.
- Mailbox synchronization, MIME processing, embedding generation, and delivery run
  under an explicit concurrency limit with backpressure, and state a crash could
  duplicate or lose is durable.

### Performance

- Work is proportional to the input: a database projection rather than a full entity, a
  streamed MIME body rather than one buffered twice, no large `bytea` tracked by EF
  Core without a reason, no query issued once per element of a sequence, and no
  unbounded result set.
- A sequence is enumerated once. A query that is filtered, counted, and read again is
  materialized first, and a lazily evaluated query is never handed to a caller that
  will iterate it more than once.
- Measure before optimizing. A micro-optimization with no measurement behind it is not
  a finding, and neither is a cost you cannot demonstrate.

### Clean code

- A method reads as one sequence of decisions: guard clauses instead of nesting, a
  named private method instead of a comment announcing the next stage, and blank lines
  separating the guards, each stage, and the return.
- Work over a sequence is a LINQ pipeline that names the operation, and a loop survives
  only where the body does something a query cannot express. A pipeline never carries a
  side effect, and a chain that stops reading as one sentence is broken into a named
  local or a named method.
- A comment explains why the code must behave this way and never narrates a readable
  statement, and a misleading name is renamed rather than annotated.
- No abstraction without a current testing, protocol, or replacement need, no
  inheritance used only to share implementation, and no collaborator hidden behind a
  service locator or static mutable state.


### Tests and documentation

A behavior change carries unit tests, and `backend/tests/AGENTS.md` states what they must look
like: no real clock, no real delay, no wall-clock ordering, no test that cannot fail,
and a fake that preserves the ordering and identity guarantees of what it replaces.
Durable documentation is updated in the same change set, and prose that describes a
validator, a guarantee, or an ownership rule the code does not implement is a defect in
the documentation.

## What is not a finding here

- Anything the build already enforces: formatting, `.editorconfig` severities, the `CA`
  and `IDE` set, Roslynator, the xUnit analyzers, the banned symbols in
  `.config/BannedSymbols.txt`, and the threading analyzers. `Required CI` fails on those
  already, and repeating them costs a thread to resolve for nothing.
- Backward compatibility, migration paths, deprecation shims, versioning machinery, or
  obsolete markers. `AGENTS.md` § "Project status" refuses all of them outright, so
  asking for one is a defect in the review. What that section does ask for is the
  opposite reading of the same paragraph: a breaking change to a configuration key, a
  database schema, an MCP tool contract, or a public API has to be argued rather than
  assumed, so a change that takes one silently is a finding.
- Praise, a summary of what the change does, or a restatement of the diff.
- Speculative refactors, alternative designs the change is not obliged to adopt, and
  suggestions that begin "consider".
- A request for tests in general. Name the specific untested case and the behavior that
  would go unnoticed, or say nothing.
- A row of `obligations.json` restated as a finding. The index is where to look, and it
  is derived from file names and declared markers, so it points at obligations a change
  does not always incur: a rename with no behavior change owes no test, a page whose
  marker covers a path may say nothing about the part that moved, and a register may
  already carry the row. Confirm it in the file or drop it. A finding whose whole
  content is that a file was not touched is a defect in the review.
- A defect in code, documentation, or tests this change does not touch. You are reading
  the repository to judge this change, not auditing it.
- A rubric item the change does not reach. The lists above say where to look; a finding
  exists because the code is wrong, never because a category went unmentioned.
- Anything that would quote a credential, a token, message content, or raw MIME.
