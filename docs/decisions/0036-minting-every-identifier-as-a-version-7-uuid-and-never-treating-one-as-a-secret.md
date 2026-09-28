---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-09-28
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Mint every identifier MailFathom creates as a version 7 UUID in both stacks, never let an identifier stand in for a secret, and make the move forward-only

<!-- describes: .config/BannedSymbols.Production.txt, backend/src/Application/Agent/Conversations/AgentConversationId.cs, backend/src/Application/Agent/Conversations/AgentMessageId.cs, backend/src/Application/Discovery/Streaming/DiscoveryRunId.cs, backend/src/Infrastructure/Secrets/Database/DatabaseSecretReference.cs, frontend/src/Client.App/src/agent/newIdentifier.ts -->

## Context and Problem Statement

MailFathom generated UUIDs two ways, and nothing written said which a new type takes. `Guid.CreateVersion7` stood at
forty-six call sites and `Guid.NewGuid` at eleven, each of the eleven a decision taken on its own type with its own
reasoning in its own documentation, and the client added a twelfth: `newIdentifier` hand-built a version 4 UUID from
`crypto.getRandomValues` to name an Agent conversation and a message before the deployment had seen either. What
existed was a habit with exceptions, and the author of a new type had nothing to read before picking.

The split is not cosmetic. A UUID that leads a B-tree key decides whether inserts land together or scatter across the
index: version 4 is uniformly random, so every insert touches a different page, while version 7 carries a millisecond
timestamp in its leading 48 bits, so inserts of the same period share pages. `agent_conversation_entries` is keyed by
`(ConversationId, Sequence)` and allows 5000 entries per conversation, so a deployment holding hundreds of thousands of
conversations reaches the order of 10⁹ rows — and a version 4 conversation identifier makes every append write to a
cold page of a multi-gigabyte index. `discovery_run_events`, keyed behind a version 4 `DiscoveryRunId`, has the same
shape, and so does every other table one of the eleven keys.

Three reasons had been given for version 4, and any rule has to answer each of them by name:

1. **Unguessability.** `AgentConversationId` and `DiscoveryRunId` are handed to a client and presented back, and a
   guessable identifier would let one caller ask to be shown somebody else's conversation or run — which the owner
   check refuses, but which nothing should be able to attempt cheaply.
2. **Creation time is not part of the contract.** A `database:<uuid>` secret reference reaches configuration documents
   and administrative responses, and a version 7 value publishes when the secret was stored to everybody who reads one.
3. **An identifier naming a person must not say when they arrived.** A user identifier reaches administrative APIs,
   audit records, and logs, and a time-ordered one publishes when each user was provisioned and in what order — a fact
   about people rather than about rows. The organization and the mail account identifiers took version 4 for the same
   reason, and nothing reads the user or organization tables in identifier order, so the locality version 7 buys is
   worth nothing there.

A fourth consideration belongs beside them although nothing stated it: a version 7 identifier handed to a client
carries its creation instant to whoever holds it, and several of these name somebody's own material.

## Decision Drivers

- Index locality on the tables whose key an identifier leads, the conversation store above all.
- One rule an author can apply without reading the history of the types that came before it.
- No access control that rests, or could come to rest, on an identifier being hard to know.
- Data minimisation: an identifier is copied into URLs, access logs, and exported telemetry, and whatever it encodes
  travels with it.
- A stored identifier is never rewritten, because every row, foreign key, log line, and configuration document naming
  it would have to move with it.

## Considered Options

- Version 7 everywhere, in both stacks, with no exceptions.
- Version 7 by default, with version 4 kept where one of the three reasons applies.
- Leave the split as it was and decide per type.

## Decision Outcome

Chosen option: "Version 7 everywhere, in both stacks, with no exceptions", because the security reading below finds
that none of the three reasons is a control the service actually relies on, the two residuals it leaves are metadata
the owner accepts, and a rule with exceptions is exactly what left the choice to habit.

**Every identifier MailFathom mints is a version 7 UUID.** The service mints it with `Guid.CreateVersion7`, from the
injected `TimeProvider` or from the domain instant the identifier belongs to where the minting code holds one, as the
existing call sites minting from an instant already do; a type that holds neither calls the parameterless overload.
The client mints it with `newIdentifier`, which writes `Date.now()` into the leading 48 bits of sixteen octets drawn
from `getRandomValues` and sets the version and the variant. It stays hand-built rather than calling `randomUUID`,
which mints only version 4 and exists only in a secure context, and a deployment reached over plain HTTP on a private
network is not one. No dependency is added for it.

**An identifier is never a secret.** A UUID of any version names a row and grants nothing. Access is decided by the
acting principal and the object's ownership, never by whether a caller could know the identifier. Anything whose
possession grants access — a session, a key, a capability, a link — is minted separately from `RandomNumberGenerator`
at 128 bits or more. A version 7 identifier carries the instant it was minted from, so it is treated as pseudonymous
metadata wherever it leaves the service: in URLs, access logs, and exported telemetry.

**The move is forward-only.** Stored identifiers stay as they are, a table holds both versions for as long as its
oldest row lives, and index locality is a property of new inserts. There is no migration and no rewriting.

**The service does not validate the version of an identifier a client supplies.** Whatever version `newIdentifier`
produces, the value is one a client chose, and the timestamp in it is the client's clock and proves nothing. The
service keeps refusing an existing identifier the caller does not own and keeps accepting any non-empty UUID — which is
also what lets a browser still running an earlier bundle during a rolling upgrade keep sending version 4 values.

### What version 7 changes, and what the code relies on instead

Version 7 turns 48 bits into a visible timestamp, lowers the random bits from 122 to 74, and makes identifiers sort by
creation. `Guid.CreateVersion7` fills the random bits from the same operating-system CSPRNG as `Guid.NewGuid` and uses
no counter, so knowing the millisecond still leaves 2⁷⁴ candidates — not an attack anybody can mount against an online
endpoint.

| Question | What the code does |
|---|---|
| Is any UUID a bearer secret? | No. A client session token is a sixteen-byte identifier and a thirty-two-byte secret, both from `RandomNumberGenerator`, the secret compared with `FixedTimeEquals`. API keys come from `UserApiKeyMinter`, and an attachment download is its own nonce-bearing capability. |
| Does an identifier reach outgoing mail? | No. `Message-ID` is 128 bits from `RandomNumberGenerator`, independent of any row identifier (`InternetMessageId.Mint`). |
| Does authorization rest on an identifier being unknown? | No. Reads are scoped to the acting user and answer `404` for an object that user does not hold, so knowing somebody else's identifier buys nothing and the `404` does not even confirm the object exists. Exports sit behind `mailfathom.admin.export`. |

### The three reasons, answered

- **Unguessability survives version 7.** The owner check refuses a foreign identifier with `404` whatever its version,
  and guessing one takes on the order of 2⁷⁴ attempts per known millisecond, so *nothing can attempt it cheaply* still
  holds. Unguessability was never the control; the owner check is.
- **Creation time not being part of the contract survives as an accepted residual (Low).** A version 7
  `database:<uuid>` reference tells anyone who reads the configuration document or the administrative response when
  the secret was stored. Those readers are operators and administrators, storage time is metadata rather than key
  material, and it exposes nothing about the sealed value. The residual is a secret's age appearing wherever the
  configuration document is copied, and this decision accepts it.
- **An identifier naming a person not saying when they arrived does not survive, and is accepted as a residual
  (Low).** A version 7 user or organization identifier tells anyone who reads an audit record, a log line, or an
  administrative response when that user or organization was provisioned and in what order relative to the others, and
  a mail account identifier says the same about when each mailbox was added. That is processing metadata about people.
  It is not credential or mail content, and every reader of those surfaces is already an operator or an administrator,
  but it is the one place this decision publishes something new. For the user and organization tables version 7 buys
  uniformity of the rule rather than index locality, since nothing reads them in identifier order. Whether provisioning
  time in audit records needs anything further under data-protection law is a question for whoever advises on it,
  not something this record settles.
- **The fourth consideration is Low, and a minimisation concern rather than an access one.** Many call sites mint from
  a domain instant, so an identifier carries when something happened. A reader entitled to the object already sees
  that instant in the same response; what version 7 adds is the instant travelling wherever the identifier does —
  request paths in reverse-proxy access logs, and exported telemetry, which may go to an external processor. That makes
  identifiers pseudonymous metadata to be minimised there, not anonymous values. Whether a sink should drop them is a
  telemetry decision of its own.

### What would change the verdict

The verdict holds only while an identifier is never the whole of an access check. An unauthenticated link, invitation,
reset, or share URL keyed by an identifier alone, an endpoint that skips the owner check, or a generator that fills the
random bits from a counter would each turn version 7's 74 random bits and visible timestamp into a real weakness. Each
of those is refused by the rule above rather than by the choice of version: such a link carries a separate
`RandomNumberGenerator` secret, and the identifier beside it stays a name.

### Consequences

- Good, because inserts into a table keyed by a new identifier land together, which is what the conversation store
  and the Discover run events need as they grow.
- Good, because a new type's author has one rule to apply and no history to read.
- Good, because the rule that an identifier is never a secret is written down, so a future type that would lean on an
  identifier for access is refused by a record rather than by a reviewer remembering.
- Neutral, because a table holds both versions until its oldest version 4 row is gone, and locality improves only as
  new rows arrive.
- Bad, because a user, organization, or mail account identifier now says when it was provisioned, and a secret
  reference when the secret was stored, to every operator and administrator who reads one.
- Bad, because every identifier copied into a URL, a log line, or a trace carries an instant, which widens what
  telemetry and access logs disclose.

## Validation

`.config/BannedSymbols.Production.txt` bans `System.Guid.NewGuid` through `Microsoft.CodeAnalysis.BannedApiAnalyzers`
(RS0030) in every project `backend/Directory.Build.props` does not mark as a unit, integration, or evaluation test
project, so a version 4 call in production code fails the build rather than waiting for review; a test still draws a
fixture identifier however it likes. The client has no such analyzer, and `newIdentifier` is its one generator: its
unit test asserts the timestamp, the version, and the variant bits. Review holds the rest — minting from the clock or
instant the code holds, and no access decision resting on an identifier.

## Pros and Cons of the Options

### Version 7 everywhere, with no exceptions

The chosen option.

- Good, because it is one rule an analyzer can enforce.
- Good, because the reading above shows the exceptions protected nothing the owner check does not already protect.
- Bad, because it publishes provisioning and storage time to operators and administrators, which the residuals above
  accept.

### Version 7 by default, with version 4 where one of the three reasons applies

- Good, because user, organization, and mail account identifiers and secret references would keep saying nothing
  about when they were created.
- Bad, because the conversation and Discover run identifiers — the two whose tables most need locality — are exactly
  the ones reason 1 would keep on version 4, and reason 1 turns out not to be a control.
- Bad, because every exception is a judgement the next author has to make again, which is the state this record
  replaces, and an analyzer cannot tell the exceptions from a mistake.

### Leave the split as it was

- Good, because it costs nothing now.
- Bad, because the conversation store's index cost grows with every deployment that adopts the Agent.
- Bad, because nothing written says an identifier is not a secret, and the first type to rely on one would be the
  weakness nobody noticed.

## More Information

- Issue 2154 carries the reading this record summarises and the call sites it moved.
- [ADR 0014](0014-single-tenant-multi-user-ownership-on-the-mail-account.md) said a mail account identifier is version
  4. It is still `proposed`, so it carries a pointer to this record rather than being superseded by it.
- Revisit this record if a surface appears where an identifier alone grants access, if an external telemetry processor
  receives identifiers under terms that make their embedded instant a problem, or if advice on data-protection law
  finds provisioning time in audit records needs more than it gets here.
