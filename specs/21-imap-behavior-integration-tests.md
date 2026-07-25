# IMAP Behavior Integration Tests

**Roadmap group:** E — schema consolidation and infrastructure verification
**Draft delivery stage:** deferred integration phase, draft section 6.2
**Depends on:** 20
**Estimated change size:** ~600 lines including tests and documentation

## Goal

Verify MailKit's actual wire behavior against a real IMAP server, which is the one class of claim the repository makes constantly and has never proven: that synchronization does not mark mail as read.

## Why this is needed

Every unit test in the repository proves that application code *calls* the seen-preserving fetch operation on a substituted port. None of them prove that the resulting IMAP commands leave the `\Seen` flag alone, because a substituted port cannot observe server state. Draft section 11.1 calls this a system invariant, and draft section 23 makes it the first acceptance criterion. A substitute-based test is the right unit-level control and an insufficient system-level one.

## Approach

A containerized IMAP server is added as a resource in a test-scoped app model and driven through the same `DistributedApplicationTestingBuilder` harness that specification 20 establishes, so there is one integration mechanism rather than two.

The server image is selected as part of this work rather than assumed here. Selection criteria, in order: it must support the extensions the roadmap depends on — at minimum UIDVALIDITY semantics, PEEK fetch, and ideally IDLE, NOTIFY, and CONDSTORE — it must be scriptable enough to seed known messages and to read back flag state, and its license and image terms must be verified from official sources and permit this use. The chosen image, its tag, and its license are recorded in `LICENSES.md` before the dependency is added, and the evaluation is written down so a later change of server is an informed decision. If no candidate satisfies the flag-observation requirement, that finding is documented and the specification is reduced to the scenarios that remain verifiable.

## Approved scope

The suite seeds a mailbox with known messages in known flag states and verifies:

- A full synchronization of unread messages leaves every remote `\Seen` flag unchanged, read back from the server after the run.
- Content fetch for an unread message leaves its `\Seen` flag unchanged.
- Repeated synchronization of the same account, folder, UIDVALIDITY, and UID is idempotent and stores nothing twice.
- A UIDVALIDITY change invalidates the cursor and triggers controlled reconciliation without mass local deletion.
- An expunged message is detected by the reconciliation pass from specification 10.
- The transport security policy from specification 01 rejects a clear-text mechanism on an unencrypted channel against a server that offers one.
- Where the chosen server supports them, IDLE delivers a notification that triggers exactly one synchronization pass, and CONDSTORE reconciliation reaches the same end state as the full-window path.

## Safety and privacy

The suite uses synthetic mailboxes and throwaway credentials defined in the test app model. No real account, host name, or credential appears in the repository. Nothing in this suite connects to an external network.

## Testing

The suite is the test. It runs in the same CI job as specification 20's suite, and its failure mode must be unambiguous: a `\Seen` regression fails with a message naming the message and the flag state observed, not a generic assertion failure, because this is the invariant most likely to break silently during later refactoring.

## Out of scope

SMTP delivery verification with smtp4dev, which draft section 21.3 defers to the SMTP stage. OAuth-based mailbox authentication. Provider-specific behavior of hosted mail services.

## Definition of done

- The `\Seen` invariant is proven against a real server, not only against a substitute.
- Idempotency, UIDVALIDITY change, and expunge detection are verified end to end.
- The chosen server image is license-reviewed and recorded in `LICENSES.md`, with the selection rationale documented.
- `docs/operations/` documents how to run the suite locally.
