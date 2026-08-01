---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-07-29
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Give every first-party failure one base type and a five-digit stable error code

## Context and Problem Statement

MailFathom raised seven public sealed exception types across `Domain`, `Application`, and `Infrastructure`, each derived directly from `Exception` and unrelated to the others. `src/AGENTS.md` requires expected failures to carry stable machine-readable codes with safe human-readable messages, and requires MCP boundaries to translate failures into serialized errors that leak no inner-exception detail. Nothing in the type system carried either obligation: the code did not exist, and the message contract was stated in the XML remarks of two types and absent from the other five.

The decision question is whether first-party exceptions should share a base type, what that type must carry for the MCP error-translation layer that specifications 16 through 18 will build, and how a failure's identity is represented so it survives translation. Settling it before that layer is written is what keeps the layer from growing a `switch` over concrete types that every new exception must be added to. Recorded on issue 93; no numbered specification under `specs/` backs it.

A finding shaped the options. `.editorconfig` disables `CA1032` with the comment "disabled in favor of focused exception design", recorded in the initial scaffold. `RCS1194` from `Roslynator.Analyzers`, which restates the same rule, carried no severity and therefore ran at its default warning severity, which `TreatWarningsAsErrors` raised to an error. Every type consequently declared the parameterless, message, and message-plus-inner constructors that the repository had already decided against, and each of those constructors left the type's domain payload degenerate: nullable properties, empty collections, and `null` standing for both "absent" and "constructed through the analyzer-mandated overload".

## Decision Drivers

- An MCP boundary must report an expected failure by a stable identity and an unexpected one by a single generic code, without naming concrete exception types.
- The prohibition on credentials, hosts, remote paths, advertised mechanisms, message content, and personal data in an exception message must be enforceable rather than repeated in prose.
- A failure's published identity must survive refactoring, renaming, and reorganization of the types that raise it.
- The repository does not introduce an abstraction without a current testing, protocol, or replacement need.
- A payload must not use `null` to encode several states.

## Considered Options

- An abstract base class carrying a stable error code and the safe-message contract.
- A marker interface carrying a stable error code, implemented by each exception.
- No common parent, with the decision recorded and each exception left standalone.

## Decision Outcome

Chosen option: "An abstract base class carrying a stable error code and the safe-message contract", because the contract that matters is a constructor obligation as much as a data one, and only a base class can be the single route to `Exception.Message` while also being directly catchable.

`MailFathomException` lives in `src/Domain/Failures/`, the only project `Application`, `Infrastructure`, and `Mcp` all reference. It declares two constructors whose message parameter is named `operatorSafeMessage`, and one abstract `ErrorCode`. It carries no shared payload and no per-boundary intermediate classes, because nothing else is common to all of them.

It is not named `DomainException`. `Domain` is a project name here, and only `MailTransportSecurityPolicyViolationException` states a domain invariant; naming a translated Polly rejection or a persistence conflict a domain exception would misdescribe five of the six at every point of use. What a boundary needs is not domain membership but a stable code and a message already safe to surface.

`RCS1194` is set to `none` beside the `CA1032` entry it duplicates. Each type then declares only the constructors its callers use, and every payload becomes non-nullable. One nullable property survives deliberately: `MailboxUnavailableException.FolderAlias`, because folder discovery reaches the server for an account and no folder exists to name. That is modelled optionality, not `null` standing for several states.

`EmailContentTooLargeException` is deleted and `IMailboxSession.FetchEmailContentWithoutSettingSeenAsync` returns `RemoteEmailContentFetchResult`. Its immediate caller caught it and acted on it directly, which is a result type written as control flow. `PersistenceConcurrencyConflictException` stays an exception and is the counter-example: the fact travels through use-case code that cannot decide what a conflict means.

### The error code

`MailFathomErrorCode` is a `readonly record struct` over a five-digit integer, with a private constructor and one static member per failure, rather than an enum. It follows the closed-enumeration pattern `MailAuthenticationMechanism` already establishes: static members grouped by category, an `All` list declared last, an `IsSpecified` property reporting the unusable struct default, a `TryParse` that accepts only allocated numbers, and a `JsonConverter` carried on the type so every serializer that meets the value uses it without per-call registration. The JSON form is the number, because the number is the published identity and a member name would change with the rename the code exists to survive.

The code reads as `C S NNN`: the first digit is the category, the second the subcategory within it, and the last three number the failure. Categories are 1 configuration and transport security, 2 mail protocol, 3 persistence, 4 outbound resilience, and 5 the MCP boundary. The allocated codes are:

| Code | Failure |
| --- | --- |
| 11001 | `MailTransportSecurityPolicyViolationException` |
| 21001 | `MailAuthenticationMechanismUnavailableException` |
| 22001 | `MailboxUnavailableException` |
| 23001 | `MailboxFolderRecreatedException` |
| 31001 | `PersistenceConcurrencyConflictException` |
| 41001 | `OutboundDependencyUnavailableException` |

A number is allocated once and never reused or renumbered, for the reason an enum member's value is never reordered: a code that changes meaning silently invalidates every runbook, alert, and log search written against it. The structure keeps the number as the identity, so what a reader sees in a log is what the code is, and a support conversation can name `22001` rather than a type name that a later refactoring may change.

### Consequences

- Good, because a boundary catches `MailFathomException` and reports `ErrorCode`, and answers everything else with one generic code, so a new first-party failure needs no change at the boundary to be reported safely.
- Good, because the message contract is stated once, in a place every derived type passes through, and a test proves no concrete exception in a production assembly escapes the hierarchy.
- Good, because removing the analyzer-mandated constructors makes every payload non-nullable, so a handler can no longer confuse "no violations" with "constructed through the mandated overload".
- Good, because a numeric code is stable across renames and readable in a log, an alert, and a support conversation without a lookup table for its category.
- Neutral, because the base class carries nothing else; shared logging, correlation, or retry hints are deliberately not on it and would need their own decision.
- Neutral, because `default(MailFathomErrorCode)` carries zero and names no failure. Every failure reaches its code through a declared member, so the default cannot reach a boundary, and the structure does not attempt to prevent what C# does not allow a structure to prevent.
- Bad, because the static members are not compile-time constants, so a translation layer maps them through a lookup rather than a `switch` over constants, and the compiler cannot report a missing case. An enum would have given that check but not a published number.
- Bad, because allocating a number is a decision a contributor must make rather than a name the compiler assigns, and a mis-categorized code is a mistake nothing mechanical will catch.

## Validation

Every unit-test project asserts, through the shared `ExceptionHierarchyAssertion`, that every non-abstract externally visible exception its production assembly declares derives from `MailFathomException`, so `Domain`, `Application`, `Infrastructure`, `AI`, `Mcp`, and `Host` are all covered rather than only the boundaries that declare an exception today. An exception that stays internal is exempt, because it is a control-flow signal that reaches no boundary and a published code would name something nothing publishes; `MimeStructureLimitReachedException` is the one such type. Message assertions cover each type that composes a message, and the two that wrap an inner exception are asserted not to repeat its text. `Domain.UnitTests` additionally asserts that the allocated codes are unique, are five digits, decompose into the documented category and subcategory, round-trip through JSON as a value and as a property name, and reject both an unallocated number and the unspecified default. The rule new exceptions follow is recorded in `src/AGENTS.md`, and `RCS1194` is disabled in `.editorconfig` with its reason beside `CA1032`.

## Pros and Cons of the Options

### An abstract base class carrying a stable error code and the safe-message contract

- Good, because `catch (MailFathomException)` filters directly, and the split between an expected failure and an unexpected one becomes two clauses rather than a list of types.
- Good, because the constructor is the only route to `Exception.Message`, so naming its parameter `operatorSafeMessage` puts the redaction rule where a message is written.
- Neutral, because C# allows one base class, which costs nothing here: no first-party exception needs to derive from a framework exception such as `ArgumentException`, and one that did would be a validation failure rather than a reported one.
- Bad, because it cannot cover an exception a library raises, so a boundary still needs its generic path.

### A marker interface carrying a stable error code

- Good, because it could be applied to a type that must derive from a framework exception.
- Neutral, because it expresses the code as well as a base class does.
- Bad, because `catch` cannot filter on it directly and every boundary needs `catch (Exception failure) when (failure is ... coded)`, which is easy to write as a bare `catch (Exception)` by mistake.
- Bad, because an interface can oblige nothing about how the message was constructed, leaving the redaction rule as prose again.

### No common parent

- Good, because it adds nothing, and with `RCS1194` disabled the degenerate constructors could be removed regardless of any parent.
- Neutral, because only one or two of the six can reach an MCP call today, since MCP reads local state and triggers no IMAP fetch.
- Bad, because the translation layer would recognize failures by concrete type, so every later exception must be added to it, and one that is forgotten is reported as an internal error instead of as itself.
- Bad, because the redaction rule would stay unenforceable, which is the part of the current situation that has already gone wrong once.

## More Information

Issue 93 records the review that prompted this decision, including the four types it originally listed and the three added since.

The MCP translation layer that consumes this contract is delivered by issues 50, 51, and 52. Revisit this decision if that layer finds the lookup-based translation unworkable, if a first-party failure genuinely needs to derive from a framework exception, or when the first code outside categories 1 to 4 is allocated.
