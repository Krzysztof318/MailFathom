---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-08-19
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Let no tool call transmit mail, publish annotations that describe an irreversible act rather than a destructive one, and make a server-side confirmation the operator's choice that refuses a client which cannot meet it

<!-- describes: src/Mcp/Tools/**, src/Application/Mail/Delivery/**, src/Domain/Delivery/** -->

## Context and Problem Statement

Every tool MailFathom publishes today is safe to call by mistake. The worst outcome of a wrong `search_emails` is a wasted result, and `set_mail_flags` — the one tool that is not read-only — changes a mailbox its owner can change back. A wrong send is in somebody else's mailbox and cannot be recalled, and the caller is a language model acting on text that arrived from strangers. Issue 744 is the gate of issue 768 and asks what must have happened before mail leaves the process on a tool call, and who is responsible for making it happen.

The protocol offers four answers and they are not equivalent: rely on the client's reading of the tool annotations, require elicitation, split the send into two calls joined by a server-minted token, or queue the message behind a cancellable hold. Which of them MailFathom requires decides the shape of every tool under issue 768, including whether `send_email` is one call or two, and what `destructiveHint` means for an act that destroys nothing and cannot be undone.

Three facts constrain the answer. Each was checked rather than assumed, against the specification revision `2026-07-28` and against the `ModelContextProtocol` `2.0.0` packages this repository pins, on 2026-08-19.

**Elicitation exists, and over this deployment's transport it exists only on a protocol revision three weeks old.** MailFathom registers the Streamable HTTP transport with `Stateless = true`, deliberately and without a switch. In that mode the SDK disables every server-to-client request, because a response might arrive at a different process, and `McpServer.ClientCapabilities` is `null`; the classic `ElicitAsync` therefore has no path here. Revision `2026-07-28` removes sessions from Streamable HTTP altogether (SEP-2567), so stateless is not one option there but the only one. What replaces the server-to-client request is multi round-trip requests (SEP-2322): a tool answers `tools/call` with an `InputRequiredResult` carrying `inputRequests` and an opaque `requestState`, and the client retries the call with `inputResponses` and the echoed state. That works statelessly and the SDK exposes it as `InputRequiredException`, guarded by `McpServer.IsMrtrSupported`. It rides on `2026-07-28` and on nothing earlier.

**Every documented client asks before a write, and every one of them documents a way to stop asking.** The five clients [MCP clients](../users/mcp-clients.md) records were read again for this decision.

| Client | What it does before a non-read-only tool call | How that stops | Elicitation |
| --- | --- | --- | --- |
| The ChatGPT web application | *"ChatGPT currently requires manual confirmation in any conversation before write actions can be taken"*, and warns that write actions can occur even when a tool is incorrectly tagged read-only | `require_approval: "never"` on a server used through the API | Not documented |
| The Claude applications | Presents a tool approval request; the guidance is to *"only click 'Allow always' when using a server and tool that you trust to run unsupervised"* | `Allow always` | Not documented |
| The Claude Code command-line tool | Prompts on every MCP tool call by default | The `acceptEdits`, `auto`, and `bypassPermissions` permission modes | Form and URL, with a hook that auto-responds without showing a dialog |
| Visual Studio Code with GitHub Copilot | *"The confirmation dialog will be shown for all tools that are not marked with the `readOnlyHint` annotation"* | Auto-approval, and sandboxed servers whose calls are auto-approved | Supported, on specification revision `2025-06-18` |
| The Cursor editor | *"Cursor asks for approval before using MCP tools by default"* | Auto-review mode, in which allowlisted MCP tools run immediately | Supported |

Two readings follow, and the second is the one this record is built on. Client-side consent is real and worth advertising for: all five ask before a write by default, and two of them decide from what the descriptor says — Visual Studio Code explicitly, and the ChatGPT application by its own warning that a write can happen when a tool is wrongly tagged as read-only. And client-side consent is never a guarantee: every product above documents the setting that removes it, and Claude Code's elicitation hook removes it from elicitation too. A confirmation the client performs is a confirmation the client's operator can switch off, whichever protocol feature carries it.

**Not one of the five documents MRTR.** Visual Studio Code names revision `2025-06-18` and the others name no revision at all, and none of their documentation mentions the mechanism. So a rule requiring elicitation before a send would not be a strict deployment today; it would be a deployment where sending never works on any client anybody has been told how to connect.

The surrounding decisions are already made and are not reopened here. Sending is off until an operator turns it on per account (issue 740). It requires `mailfathom.mail.send`, a grant distinct from reading, enforced in the listing and again in the use case (issue 745, under [ADR 0012](0012-authorization-model-named-permissions-and-where-they-are-enforced.md)). Delivery is already asynchronous: [mail delivery](../features/mail-delivery.md) writes an outgoing record and a worker claims and transmits it, and `MailOutbox.EnqueueAsync` is the one way in. A hold until a named time is issue 742, and reading back and cancelling what was queued is issue 748.

## Decision Drivers

- **Only what MailFathom does itself is a guarantee.** A requirement the server states and the client performs is a requirement the client's settings can retract, so a design whose safety rests there has stated a hope rather than a control.
- **A requirement nobody can satisfy is not strictness, it is a broken capability.** The five clients this project documents are the population; a rule they all fail withholds the feature from everybody while protecting nobody.
- **Honesty in the descriptor costs nothing and is read.** Visual Studio Code decides its confirmation dialog from `readOnlyHint` alone, and Anthropic's directory policy requires `readOnlyHint`, `destructiveHint`, and `title` of a listed server. A wrong value there is a tool a client calls unattended because the descriptor said it was safe.
- **The irreversible step is the transmission, not the tool call.** MailFathom already puts a durable record between them, so the interesting question is what may be recovered in the gap rather than what may be prevented before it.
- **The capability that wants a review step already exists.** The draft tools (issue 750) publish a half that sends nothing, under a grant of their own, so a review step is available to a deployment that wants one without being imposed on every send.
- **A first deployment has to work.** Sending is already behind two operator acts; making the third one mandatory would meet an operator as an obstacle at exactly the point they have decided to accept the risk.
- **A break here would be affordable and would still be written down.** [ADR 0004](0004-versioning-and-release-policy.md) lets a `0.y.z` minor break the MCP tool contract and the configuration schema, so what a value has to be is right rather than convenient.

## Considered Options

- Rely on the client, through the tool annotations alone
- Require elicitation, so the requirement is the server's
- Split the send into two calls joined by a server-minted, short-lived, single-use token
- Queue behind a hold during which the send can be cancelled

## Decision Outcome

Chosen option: **three of the four, each in the place its strength actually holds** — the annotations because they are read and cost nothing, elicitation as an operator's choice the server enforces, and the hold as a second operator's choice no client can retract — on top of a fixed part none of the four names, which is that a tool call never transmits at all. The two-call token is refused as the general shape of a send.

The reasoning is that only two of these are guarantees MailFathom can make unaided — the asynchronous record and the hold — and both live entirely on this side of the connection. The annotations are advice given to a client, and elicitation is a requirement the client performs; both are worth having and neither is a control. Saying which is which is most of what this record is for, because a deployment that mistakes the second pair for the first has been told it is protected when it is not.

### No tool call transmits mail, and that is not configurable

A sending tool writes an outgoing record through the outbox and answers with that record's identity and state. The message leaves on the delivery worker's pass, against the bounds [mail delivery](../features/mail-delivery.md) already states, re-checked in the use case. This is fixed: no configuration, no grant, and no client capability makes an MCP call transmit synchronously.

It is the only part of this decision that holds regardless of what the caller is or how it is configured, and it is what everything else in this record composes with. It also fixes the wording a result must use — queued rather than sent — because a caller told the mail is gone will not look for the window in which it is not.

### The annotations are published, honest, and understood to be advisory

A sending tool takes `readOnlyHint=false` and `openWorldHint=true`, which together say it changes state outside the caller and reaches a server this deployment does not own. `set_mail_flags` already carries both, so neither value is new to the surface; what is new is where the second one points, since that tool reaches the owner's own mailbox and this one reaches a submission server and a recipient nobody here controls. `idempotentHint` is `false`: a repeat of a send is a second message unless the caller supplied an idempotency key, and an annotation describes the tool as it may be called rather than as a careful caller would call it. Issue 746 may set it `true` if and only if it makes such a key required, in which case the annotation becomes true of the tool rather than of one way of using it.

`destructiveHint` is `true`, and the reason is the one the protocol has no word for. The vocabulary offers two values and the SDK states them plainly: `true` where a tool *can perform destructive updates to its environment*, `false` where it *performs only additive updates*. Sending is literally additive — it creates a message where none was and overwrites nothing — so a literal reading gives `false`. The protocol has no `irreversibleHint`; `destructiveHint` is the nearest thing it has, and this record takes it, because the annotation is not a taxonomy entry but the input to a client's decision about whether a call needs a person, and `false` would place `send_email` in the same class as `create_contact`, which is one call to undo.

**This widens the rule the surface currently states, and the widening is deliberate.** [MCP tools](../features/mcp-tools.md) says of `set_mail_flags` that the annotation *"answers what the call takes away rather than how easily it can be undone"* — written to establish that a reversible act can still be destructive, which is why marking labels off a message carries `true`. Read literally against a send, which takes nothing away from anybody, that sentence gives `false`. So this record adds a second ground rather than reinterpreting the first: a call is marked destructive when it takes something away, **and** when it cannot be undone at all. The two do not conflict and point the same way — the first denies that reversibility excuses a destructive call, the second denies that additiveness excuses an irreversible one, and both exist so a client asks a person. Issue 746 is what states the second ground on that page beside the first.

What the annotations are not is a control. The specification says clients **MUST** consider tool annotations untrusted unless they come from a trusted server, and a client is free to auto-approve everything. They are published because they are read, and nothing in this deployment's safety rests on them.

### A server-side confirmation is the operator's choice, and it is off by default

An operator may require that a send be confirmed through the client before anything is queued. Where that is on, a sending tool answers the first `tools/call` with an `InputRequiredResult` carrying a form-mode `elicitation/create` and an opaque `requestState`, and only a retry carrying `action: "accept"` reaches the outbox. `decline` and `cancel` refuse; neither enqueues anything, and the distinction is preserved in what the caller is told.

It is off by default because the two acts in front of it — enabling sending on the account, and granting `mailfathom.mail.send` to the credential — are already the operator's decision that this deployment may send, and because on today's clients the setting turns sending off rather than making it safer.

What this buys is stated exactly, so no operator reads more into it than it holds: it moves the *requirement* from the client to the server, and it does not establish that a human saw anything. Claude Code documents a hook that answers elicitation without showing a dialog. The setting therefore means the server refused to act on an unconfirmed call, and never that a person consented.

### A deployment that requires confirmation and meets a client that cannot refuses the call

The refusal is a coded failure that names the missing capability, so a client developer learns why rather than reading an opaque error. The tool stays in the listing.

Withholding it from the listing was considered and refused. The specification sanctions varying the advertised tool set by the authorization presented on a request, on the ground that credentials are per-request input rather than connection state; it does not sanction varying it by what the client can do, and the two are different facts. It is also not implementable for the clients that exist: in stateless mode on the revisions those clients speak, the server does not know the capability at `tools/list` time at all, so the listing would have to be uniform anyway and the situation would arise regardless. The listing varies by the grant and by the deployment switch, and by nothing else.

### `send_email` is one call

The two-call preview-and-token shape is refused as the general shape of a send. It does not depend on any client capability, which is its real merit, and it guarantees that the message sent is the one that was shown. Against that: it establishes ordering rather than consent, since an autonomous agent calls both in sequence with nobody seeing either; it doubles every send for a protection that only pays when a human is watching, which is the case the client's own dialog already covers; and the token is a bearer handle minted per send, which the specification's own guidance on stateful tools says must then carry sufficient entropy and a bounded lifetime and must be authorized on every use — a credential to get right, for a benefit already available elsewhere.

Elsewhere is `save_draft` and `send_draft` (issue 750), which are the compose-then-review shape under a grant of their own, and are that shape done better: what sits between the two calls is a person opening a draft in their own mail client, not a token an agent is holding. A deployment that wants no unattended send grants the draft tools and withholds `mailfathom.mail.send`, and `send_draft` — which does send — carries every requirement this record places on `send_email`.

### The hold is the second operator's choice, and the one that depends on no client at all

An operator may set a window during which a queued message is not yet transmitted and `cancel_outgoing_email` still works. It composes with everything above rather than replacing any of it, and it is the one of the operator's two choices that no client setting can retract, because it lives entirely on this side of the connection. It converts a mistake from prevented to recoverable, which is a weaker promise and a keepable one.

It is off by default, for the reason the confirmation is: the mailbox owner asking an agent to send something is usually watching, and a deployment that wants the window sets it. The mechanism is issue 742's and the cancellation is issue 748's; this record decides only that the window is a legitimate answer to issue 744's question and that its absence is the default.

### What this record does not settle

- **The argument shape of any tool**, including whether the idempotency key is optional. Issue 746 decides that, within the `idempotentHint` rule stated above.
- **The names of the two settings** this record creates, their defaults beyond off, and their reload behaviour. They belong to the issues that implement them, under [ADR 0002](0002-configuration-reading-mapping-and-reload-boundary.md).
- **The default length of the hold** where an operator enables one, which is issue 742's.
- **Anything about `ask_mail`.** It cannot send, and issue 768 keeps it that way.
- **The injection boundary and per-caller ceilings**, which are issue 749 and are a different question: this record says what must happen before a send, not what a send may be talked into.
- **Whether a later protocol revision changes the answer.** If MRTR becomes ordinary in the clients this project documents, requiring confirmation by default becomes a question worth reopening — as a new record, since this one will be accepted.

### Consequences

- Good, because the guarantee MailFathom offers is one it can keep alone: no MCP call transmits, and the record between the call and the transmission is where every other protection attaches.
- Good, because a deployment that wants the requirement on the server's side can have it, and one that cannot use it loses nothing it has today.
- Good, because the descriptor tells the truth to the clients that read it and to the directory policy that requires it, without anything resting on that being honoured.
- Neutral, because `destructiveHint=true` is a deliberate reading of a word the protocol did not define for this case, and a reader comparing it to the literal definition will find the reasoning here rather than an oversight.
- Neutral, because the two settings are dormant surface until issues 742 and 746 implement them, which is ordinary below `1.0.0`.
- Bad, because a deployment that changes nothing gets no server-side confirmation, so what stands between an agent and a recipient is the two operator acts, the descriptor, and whatever the client does — and this record says so plainly rather than implying more.
- Bad, because requiring confirmation today makes sending unusable from every documented client, which is a setting an operator can turn on and be surprised by; the failure has to name the missing capability for that reason.

## Validation

- `Mcp.UnitTests` asserts the advertised `tools/list` metadata against the contract and fails the build on drift, which is what carries the four annotation values; the sending tool joins that assertion when issue 746 lands.
- The refusal paths — confirmation required and unmet, declined, cancelled — are unit-tested against the use case and proven over a real endpoint in the composed-host suite, as the existing tools' refusals are.
- That no MCP path transmits synchronously is already structural: `Boundaries.UnitTests` reads the compiled intermediate language of every assembly and fails a reference to `IMailDeliverySession` or its factory from anywhere but `Application` and `Infrastructure`, so an MCP tool cannot reach a submission channel even inside a method body.
- [MCP tools](../features/mcp-tools.md) gains the sending row in its annotation table, the second ground for `destructiveHint` beside the one it already states, and the first `openWorldHint=true` on a tool that reaches a server outside the owner's own mailbox; the sending tools' documentation states the confirmation contract and what a deployment that enables it asks of a client.

## Pros and Cons of the Options

### Rely on the client, through the tool annotations alone

Publish `readOnlyHint=false` and `openWorldHint=true` and let each client decide what to ask its user.

- Good, because it costs nothing, every documented client asks before a write by default, and two of them decide from what the descriptor says.
- Good, because it is the only option that works identically on every protocol revision and every transport mode.
- Neutral, because the specification classes annotations as untrusted input to the client, so this is advice given rather than a requirement imposed.
- Bad, because a deployment relying on it has no guarantee at all: every documented client can be configured to auto-approve, and a server cannot tell whether one has been.

### Require elicitation, so the requirement is the server's

Refuse to queue anything until the client has returned an accepted `elicitation/create`.

- Good, because the requirement sits where it can be enforced, and a client that ignores it gets nothing sent.
- Good, because the multi round-trip form works in stateless mode, which is the only mode this deployment has.
- Neutral, because it establishes that something answered, not that a human did — Claude Code documents a hook that answers without a dialog.
- Bad, because MRTR rides on the `2026-07-28` revision and not one documented client speaks it, so as a fixed requirement it turns sending off everywhere.
- Bad, because a client that cannot elicit is then a client that cannot send at all, which is a strong outcome to impose on a caller that is legitimately unattended.

### Split the send into two calls joined by a server-minted token

Compose and return a preview with a short-lived single-use token; send it on a second call.

- Good, because it depends on no client capability whatsoever and works on every revision.
- Good, because the message sent is exactly the message that was shown.
- Neutral, because it is the specification's own stateful-tool handle pattern, with that pattern's obligations: entropy, a bounded lifetime, and authorization checked on every use.
- Bad, because an autonomous agent calls both in sequence, so it establishes ordering rather than consent.
- Bad, because it doubles every send, including the ones a person is watching, for a benefit the draft tools already provide under a grant of their own and with a review step a person can actually see.

### Queue behind a hold during which the send can be cancelled

Delay transmission by a window the operator sets, and let the message be cancelled inside it.

- Good, because it is the only protection here that no client setting can remove.
- Good, because it composes with each of the other three rather than competing with any of them, and the mechanism and the cancellation already have issues.
- Neutral, because it makes a mistake recoverable rather than prevented, which is a weaker promise and one that can actually be kept.
- Bad, because it only helps where somebody notices inside the window, and a delay is a real cost to an owner who asked for a message to go now — which is why it is off by default.

## More Information

- Issue 744 is the decision this record answers, and issue 768 is the feature every child of it is written against: issue 740 (the deployment switch), issue 745 (the grant), issue 746 (`send_email` and the annotations), issue 747 (reply and forward), issue 748 (read-back and cancellation), issue 749 (the injection boundary and per-caller ceilings), issue 750 (the draft tools), and issue 742 (the hold).
- [ADR 0012](0012-authorization-model-named-permissions-and-where-they-are-enforced.md) settles the grant this rides on and the finding that a tool descriptor has nowhere to state a required permission, which is why the listing carries that decision and this record leaves the descriptor to the annotations alone.
- [ADR 0007](0007-remote-mailbox-mutation-boundary-and-write-session.md) is the same shape one boundary over: a write reaches the remote mailbox only through a session type no read path can obtain. Sending is that argument applied to a server MailFathom does not own at all.
- [Mail delivery](../features/mail-delivery.md) is the capability being exposed, including the outbox record, the bounds, and the delivery pass this record makes the sending tools answer with rather than wait for.
- Section 13.5 of `specs/2026-07-22-mail-fathom-architecture-draft.md` says that *"future sending requires `mail.send` and explicit ChatGPT confirmation semantics"*. This record supersedes it on the second half, as section 2.1 of that draft provides for: what a caller must do is MailFathom's own arrangement rather than one client's confirmation model, and a requirement written against a single product would have said nothing about the four other clients this project documents. The first half was already superseded by [ADR 0012](0012-authorization-model-named-permissions-and-where-they-are-enforced.md), which owns the permission vocabulary the name comes from.
