# Custom Exception Hierarchy Design

Recorded decision for issue 93. No numbered specification under `specs/` backs it; the driver is the rule in
`src/AGENTS.md` that expected failures carry stable machine-readable codes with safe human-readable messages, and the
MCP error-translation layer that specifications 16 through 18 will consume.

## What the review found

Seven public sealed exception types existed, not the four the issue lists; `MailboxFolderRecreatedException`,
`MailboxUnavailableException`, and `OutboundDependencyUnavailableException` arrived after it was written. All seven
repeated the same shape: a parameterless constructor, a message constructor, a message-plus-inner constructor, and then
the constructor the throwing code actually calls.

The issue attributes those three constructors to `CA1032`. That attribution is wrong, and the correction is what makes
this change possible. `.editorconfig` sets `dotnet_diagnostic.CA1032.severity = none` with the comment "disabled in
favor of focused exception design", recorded in the initial solution scaffold. The rule that forces the constructors is
`RCS1194` from `Roslynator.Analyzers`, which carries no severity in `.editorconfig` and therefore runs at its default
warning severity, which `TreatWarningsAsErrors` raises to an error. Removing the three constructors from
`EmailContentTooLargeException` and building `Application` reports `error RCS1194` and no `CA1032`.

So a deliberate repository decision was reinstated by an analyzer nobody configured, and the degenerate constructors
that decision was meant to prevent were written anyway. Every consequence the issue lists follows from them: payloads
that must be nullable or silently empty, `null` encoding both "absent" and "constructed through the mandated overload",
and tests that exist only to cover constructors no production code calls.

## Decisions

### The analyzer

`RCS1194` is set to `none` in `.editorconfig`, beside the `CA1032` entry it duplicates, with the reason written into
the same comment. The two rules state the same requirement, and the repository has already decided against it.

### A common parent

The seven types gain an abstract base class, `MailMcpException`, in `src/Domain/Failures/`.

It is a class and not an interface. A `catch` clause filters on a class directly, while an interface needs
`catch (Exception failure) when (failure is IStableErrorCodeFailure coded)` at every boundary that maps a failure. More
decisively, the safe-message contract is a constructor obligation: a base class can name its parameter
`operatorSafeMessage` and be the only route to `Exception.Message`, and an interface can oblige nothing about how the
message was built.

The base is not named `DomainException`. `Domain` is a project name in this repository, and only
`MailTransportSecurityPolicyViolationException` lives there and states a domain invariant. Calling a translated Polly
rejection or a persistence conflict a domain exception would misdescribe five of the six at every point of use. What
the MCP boundary needs is not domain membership but a stable code and a message already safe to surface, so the type is
named after the system that raised it and its contract is carried by what it declares.

It lives in `Domain` because that is the only project `Application`, `Infrastructure`, and `Mcp` all reference. The
`Failures` folder sits beside `Accounts`, `Emails`, and `Transport` and does not pretend to be another domain area.

The base carries exactly two things: the two constructors that reach `Exception.Message` through a parameter named for
its contract, and an abstract `ErrorCode`. It carries no shared payload, no logging, and no intermediate per-boundary
classes, because none of those are common to all six.

### The error code

`MailMcpErrorCode` is a `readonly record struct` in `Domain/Failures` over a five-digit integer, with a private
constructor and one static member per failure. Each concrete type overrides `ErrorCode` with its own member, so the code
sits next to the type that raises it.

The code reads as `C S NNN`: category, subcategory, then the failure's number. Categories are 1 configuration and
transport security, 2 mail protocol, 3 persistence, 4 outbound resilience, 5 the MCP boundary. A reader who sees
`22001` in a log knows it is a mail-protocol availability failure before looking anything up, and a support
conversation can name a number that survives a later rename of the type.

An enum was the first proposal and was rejected in favour of the number. An enum would have let the `Mcp` layer
`switch` over constants with a compiler-checked exhaustiveness that static readonly members cannot give; the
translation is a lookup instead. What the number buys against that is a published identity: it is stable across
renames, readable wherever it is recorded, and it decomposes into a category without a table. ADR 0003 records the
trade-off.

### The degenerate payloads

With `RCS1194` off, every type declares only the constructors its callers use, and each payload becomes non-nullable:
the four properties of `MailboxFolderRecreatedException`, `OutboundDependencyUnavailableException.Dependency`, both
properties of `MailAuthenticationMechanismUnavailableException`, and
`MailTransportSecurityPolicyViolationException.Violations`, which gains a guard rejecting an empty list because the
"an unspecified transport security rule" fallback disappears with the constructor that needed it.
`PersistenceConcurrencyConflictException` keeps only the message constructor, the only one any caller uses.

One nullable property survives deliberately. `MailboxUnavailableException.FolderAlias` stays optional because the type
has two real constructors and the absence of a folder has domain meaning: folder discovery works on the account and on
no single folder. That is modelled optionality, not `null` standing in for several states, and the remarks say so.

### Oversized content becomes a result

`EmailContentTooLargeException` is deleted. `IMailboxSession.FetchEmailContentWithoutSettingSeenAsync` returns
`RemoteEmailContentFetchResult`, built the way `EmailMimeExtractionResult` already is: an outcome enum plus a record
with static factories.

The issue places the throw and the catch inside `MailboxSynchronizer`; in fact the adapter
`MailKitImapMailboxSession` throws and `MailboxSynchronizer` catches, so it does cross a port. The objection holds
anyway. The immediate caller catches it and acts on it directly, which is a result type written as control flow, and
`EmailMimeExtractionResult` already records the reasoning for the identical case one call later: an email nobody can
use is recorded and stepped over so the batch and the folder checkpoint continue. An oversized email is recorded and
stepped over by the same method.

`PersistenceConcurrencyConflictException` is the counter-example and stays an exception. Its remarks already explain
why: the fact travels through use-case code that cannot decide what a conflict means, so restating it as a result at
every intermediate boundary would oblige layers with no stake in the decision to carry it.

### The redaction rule

The rule is stated once, in the XML documentation of `MailMcpException`, and repeated as a rule for new exceptions in
`src/AGENTS.md`. It leaves the remarks of the individual types, which is where it was previously stated by two of seven
and omitted by five.

The base constructor's parameter is named `operatorSafeMessage`, so every derived type meets the rule where the message
is written rather than in prose elsewhere.

A shared test helper in `tests/shared/` asserts that every non-abstract type deriving from `Exception` in a production
assembly derives from `MailMcpException`. `Domain.UnitTests`, `Application.UnitTests`, and `Infrastructure.UnitTests`
each call it for their own assembly. The helper reads types reflectively, which the repository otherwise avoids; the
justification is that the guarantee it enforces — that a future exception cannot leave the hierarchy and so cannot
escape the code and message contract — cannot be stated any other way, and it costs no package and no production code.

Per-type tests assert that each message names only the values its contract permits.

## Out of scope

`TransientFailureClassifier` keeps branching on concrete types. It answers whether a failure can clear on its own,
which is a different question from which code a boundary reports, and merging the two would tie retry policy to
protocol vocabulary.

No ADR is written or modified. No exception-handling or error-modelling package is added. The MCP translation layer
itself is delivered by issues 50, 51, and 52; this change settles the contract that layer consumes.
