# Outbound resilience

MailMcp calls dependencies it does not control: IMAP servers, SMTP servers, PostgreSQL, and chat and embedding
providers. Each of them fails in ways that clear on their own and in ways that never will, and the difference decides
whether repeating a call is recovery or damage. This page describes the one model that decision is made in.

## Dependency classes

Resilience is configured per *dependency class*, not per call site. `OutboundDependency` in `Application` enumerates
them:

| Class | Covers |
|---|---|
| `MailboxSessionEstablishment` | Connecting, negotiating TLS with, and authenticating an IMAP session |
| `MailboxDataRetrieval` | Listing, fetching, and streaming mailbox data over an established session |
| `EmailDelivery` | Submitting an email to the SMTP server |
| `DatabaseCommandExecution` | Commands and queries against the local PostgreSQL database |
| `AiProviderInvocation` | Chat and embedding provider calls |

A class exists when its failure modes and its rules for safe repetition differ from every other class. Session
establishment is separate from retrieval because a rejected credential must never be repeated — against a mail server
that is how an account gets locked. Delivery is separate because a repeated submission is visible in the recipient's
inbox, which is why its shipped budget is the smallest of the five.

The enumeration is half the pipeline key. A value that is not declared resolves no pipeline and raises
`KeyNotFoundException`, so a typo cannot silently run an operation with no resilience at all.

## One pipeline per remote instance

The other half of the key is the *dependency instance*: the remote server the operation actually talks to.
`OutboundPipelineKey` pairs the two, and the registry keeps one built pipeline per pair.

A circuit breaker is state, and state shared between two servers reports neither of them. Both mailbox classes are
therefore keyed by account: one unreachable mail server opens the circuit for its own account, and every other account
keeps reading through a breaker that never saw those failures. The same follows for the concurrency limiter — an
account's in-flight limit is its own, so a slow server cannot shed a healthy account's work. A class that talks to one
remote instance, such as the local database, uses `OutboundPipelineKey.SharedInstance` and keeps a single process-wide
pipeline.

This costs configuration nothing. One builder is registered per dependency class and a custom `BuilderComparer` matches
a requested key to it by class alone, so the budget is still tuned once per class and the registry creates and caches
an instance the first time one is asked for. Instances are created from configured accounts, so their number is
bounded by the deployment rather than by traffic.

## What a pipeline is made of

`Infrastructure` owns every Polly type. `AddOutboundResiliencePipelines` registers one builder per class, from which
every instance of that class is composed, from outermost to innermost as:

1. **Concurrency limiter** — sheds work beyond the class's in-flight limit before it consumes any other budget.
2. **Total timeout** — bounds the whole operation, including its backoff waits. It is the only limit that can bound a
   retrying operation at all.
3. **Retry** — exponential backoff with jitter, capped by `MaxDelay` and by the attempt count.
4. **Circuit breaker** — sits inside retry, so it observes every attempt rather than every operation.
5. **Per-attempt timeout** — innermost, so a stalled attempt becomes a transient failure the retry above it acts on.

Where the total timeout expires decides what the caller sees. Expiring inside an attempt cancels it and is reported as
a rejection; expiring while the pipeline waits to retry stops the retry and surfaces the failure that ended the last
attempt. Both abandon the remaining attempts, which is the guarantee; the exception names the more useful of the two
causes.

Every limit the pipeline itself imposed — an abandoned attempt, an exhausted total timeout, an open circuit, a shed
execution — reaches the caller as `OutboundDependencyUnavailableException`, with the Polly rejection kept as its inner
exception. That translation is what stops the resilience library at the `Infrastructure.Resilience` boundary: an
adapter maps this one type onto the failure its own application port documents, and the IMAP adapter turns it into
`MailboxUnavailableException`. A caller's own cancellation is never translated; it stays an
`OperationCanceledException`, so a host shutting down and a mail server refusing work never arrive as one failure.

An exhausted attempt budget is not a rejection and is deliberately left untranslated here: retry rethrows the failure
that ended the last attempt, and which exception that is remains information a caller may need. The database paths in
particular depend on seeing the provider's own failure. An adapter that wants the two outcomes to read as one says so
itself, which is what the IMAP adapter does — a transient failure that survived every attempt becomes
`MailboxUnavailableException` alongside the rejections, while terminal failures keep passing through.

The order is the one the standard HTTP resilience pipeline established, and each position follows from the one before
it. Every limit is an operator setting bound from configuration, read once at startup, so a flaky dependency is tuned
without a rebuild.

## Classifying a failure

`ITransientFailureClassifier` is an `Application` port so a use case can ask the question the pipeline asks itself
without depending on Polly. `Infrastructure` implements it per protocol family:

- **Mailbox** — a rejected credential, an unusable TLS handshake, an unavailable authentication mechanism, and a
  refused IMAP command are terminal. A dropped connection and a desynchronized protocol stream are transient, because
  a repeated read changes nothing on the server.
- **Delivery** — only an explicit 4yz reply is repeated, which is the server stating it did not take the message. A
  connection lost between the message data and the final reply is reported as an ordinary protocol, socket, or I/O
  failure, indistinguishable from one that happened before submission, so repeating it risks a second copy in the
  recipient's mailbox. Everything that is not a temporary rejection is therefore terminal and left to the outbox.
- **Database** — the provider answers through `DbException.IsTransient`, so MailMcp keeps no second SQLSTATE table.
  A `PersistenceConcurrencyConflictException` is terminal here on purpose; see the single-layer rule below.
- **Provider** — the HTTP status is the provider's own statement about whether the request may be sent again. `408`,
  `429`, and the `5xx` class are transient, an absent status means the response never arrived, and everything else is
  terminal.

A caller's own cancellation is never transient, in any family. Anything unrecognized is terminal, because an
unrecognized rejection repeated against a mail server is exactly what locks a mailbox account.

## The single-layer rule

**One logical operation is retried at exactly one layer.** Two retry layers around one call multiply their attempt
counts: three attempts wrapped by three attempts is nine calls into a server that is already struggling.

`OutboundOperationExecutor` enforces this rather than leaving it to review. It marks the dependency class as in flight
for the duration of an execution, and re-entering the same class on the same asynchronous flow throws
`InvalidOperationException` immediately. Nesting *different* classes stays legal, because each call still has one
layer.

The rule also governs three places where .NET already provides resilience, and in each of them the built-in mechanism
is the layer rather than something MailMcp re-implements:

- **HTTP.** `AddStandardResilienceHandler` in the host's service defaults already wraps every `HttpClient`. A provider
  client that reaches its model over `HttpClient` is therefore already protected, and must not also be wrapped in the
  `AiProviderInvocation` pipeline. An adapter that wants the pipeline instead has to remove the handler from its own
  client; it may not have both.
- **EF Core.** `EnableRetryOnFailure` is deliberately not configured. The obstacle is not the unit of work: with a
  retrying execution strategy each query and each `SaveChangesAsync` is already replayed as its own retriable unit. It
  is the *user-initiated* transaction. `PersistenceSessionFactory` opens one with `BeginTransactionAsync` for every
  session, and EF Core refuses that under a retrying strategy with `InvalidOperationException: The configured
  execution strategy 'NpgsqlRetryingExecutionStrategy' does not support user-initiated transactions`. Turning the
  setting on today would therefore fail every write at the moment its session starts, rather than merely leave it
  un-retried.

  The supported alternative works and stays open: hand the whole transactional unit to
  `Database.CreateExecutionStrategy().ExecuteAsync(...)`, which replays the delegate — begin, work, `SaveChanges`,
  commit — as one retriable unit. Adopting it means reshaping `IPersistenceSessionFactory` from the imperative
  `BeginSessionAsync` scope into a delegate the strategy can re-invoke, ensuring everything inside is safe to replay,
  and dropping the pipeline from those paths so the two never stack.

  Until then the boundary is: the `DatabaseCommandExecution` pipeline covers command paths that own no transaction,
  and a transient failure *inside* a transactional write is surfaced rather than retried. The commit either succeeds
  or the session rolls back and the caller decides.
- **Optimistic concurrency.** `OptimisticConcurrencyRetryPolicy` in `Application` already retries a commit that lost a
  race. That is why the classifier reports a concurrency conflict as terminal: the pipeline must not become a second
  layer around the same rows.

## Telemetry and privacy

Polly's metrics stay on and carry the dependency class as the pipeline name, the remote instance as the pipeline
instance, the event, the attempt number, the outcome, and the duration. Emitting them is not exporting them: the host
subscribes OpenTelemetry to Polly's meter in `ServiceDefaultsExtensions`, without which the instruments would exist and
nothing would collect them. Its *logging* is replaced: Polly renders the outcome exception in full, and a mail server
puts the rejected recipient into its error text. `OutboundResilienceEvents` therefore records a retry, a circuit
opening, and a circuit closing with the dependency class, the instance, the operation, the failure's type name, the
attempt number, and the delay — never a message, an address, an identifier, or a payload.

The instance is the configured account identifier and the operation is the folder alias, or the fixed name
`folder-discovery` for a connection that pins no folder, both carried into the callbacks by the pipeline key and by
`ResilienceContext.OperationKey`. Neither is mailbox content, and neither is the server's own folder path: they are
the same deployment vocabulary synchronization already logs, and they are what makes a degrading dependency
attributable to one account and one folder rather than to "IMAP".

## Configuration

Each class binds from `Resilience:<DependencyClass>`, and every setting has a class-specific default, so a deployment
names only the limits it disagrees with:

```json
{
  "Resilience": {
    "MailboxDataRetrieval": {
      "MaxAttempts": 4,
      "BaseDelay": "00:00:01",
      "MaxDelay": "00:00:15",
      "AttemptTimeout": "00:01:00",
      "TotalTimeout": "00:03:00",
      "CircuitBreakerFailureRatio": 0.5,
      "CircuitBreakerMinimumThroughput": 10,
      "CircuitBreakerSamplingDuration": "00:00:30",
      "CircuitBreakerBreakDuration": "00:00:15",
      "ConcurrencyLimit": 8
    }
  }
}
```

Binding is strict in both directions: an unknown key inside a section fails startup, and so does a section that names
no dependency class. The second check is separate because strict binding only inspects the keys of a section it was
pointed at — `Resilience:EmailDelivry` is not an unknown key to it, it is a section nobody reads, which would leave an
operator convinced they tuned a limit that never moved. Validation runs on start and rejects contradictions as well as out-of-range values — an attempt allowed
to outlive its operation, or a backoff ceiling longer than the total timeout, describes a limit that can never be
reached. `MaxAttempts` counts the first call, so `1` disables retry and leaves the other strategies in place.

The settings are classified **restart-required** under
[ADR 0002](../decisions/0002-configuration-reading-mapping-and-reload-boundary.md), which is that ADR's default for a
group without a validated-snapshot layer. The registration binds a frozen copy of the section rather than the live
configuration, so the classification holds by construction: a reloaded budget is not adopted, and a malformed one
cannot disturb a pipeline that is already serving.

That indirection is not ceremony. Bound against the live configuration, `OptionsMonitor` drops its cache when a change
token fires and rebuilds the named instance *inside* that notification, so one malformed edit raises
`OptionsValidationException` on the thread that reported the change — a file-watcher callback in a deployed host. ADR
0002 forbids validation on that thread precisely because of this. Making these budgets reloadable therefore needs the
validated-snapshot layer the mail and persistence settings already have, not a call to Polly's `EnableReloads`.

## Deliberate exclusions

- **`Microsoft.Extensions.Resilience` enrichment** is not added. It is already present transitively behind the HTTP
  handler, and its enrichers describe HTTP request metadata that the non-HTTP classes do not have. Polly's own metrics
  already carry the pipeline name and the exception type, which is what an operator reads here.
- **Chaos injection** is not wired up. Polly's chaos strategies ship inside `Polly.Core`, so adopting them later costs
  no new dependency, but injecting faults is only meaningful against the real adapters and belongs with the
  integration-test foundation rather than with the pipelines themselves.
- **Distributed rate limiting and cross-process circuit state** are out of scope. Every limit here is per process, and
  within a process per dependency instance.
