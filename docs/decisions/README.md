# Architectural Decision Records (ADRs)

An Architectural Decision is a justified software design choice that addresses an architecturally significant functional or non-functional requirement. An Architectural Decision Record captures one decision and the rationale for it.

For background on ADRs, see <https://adr.github.io/>.

## How MailFathom uses ADRs

1. Create or modify an ADR only after explicit owner approval for that documentation change. The `Protected paths` check enforces this rather than leaving it to a reviewer: `docs/decisions/` is a protected directory, so a pull request touching this directory fails that check unless the repository owner authored it. Propose a decision in an issue and let the approving change carry the record.
2. Copy `docs/decisions/adr-template.md` to `docs/decisions/NNNN-title-with-dashes.md`, where `NNNN` is the next sequence number.
   1. Check existing branches and pull requests when possible so the sequence number does not collide.
   2. Use `docs/decisions/adr-short-template.md` only for small decisions whose trade-offs are already clear.
3. Edit the new ADR.
   1. Initial status is normally `proposed`.
   2. `deciders` lists the people who approve the decision.
   3. `consulted` lists people whose input was sought.
   4. `informed` lists people who must know about the decision but do not approve it.
4. For each option, record meaningful good, neutral, and bad consequences.
5. Update the status to `accepted` only after the decision is approved. That status closes the record: from then on the text is never corrected, extended, or brought up to date with what the code does. Every change made since was written against that text, so rewriting it replaces the reasoning retroactively and leaves nothing saying it ever changed.
6. Supersede a decision with a new ADR rather than rewriting the old one. Mark the replaced ADR `superseded` and point it at its replacement; that transition and the `describes:` marker are the only edits an accepted ADR takes, and the marker says where the code lives rather than anything about the decision. An ADR still at `proposed` is editable, which is what that status is for.

## Records

- [0001: Use application-owned repository ports for persistence access and keep EF Core behind infrastructure adapters](0001-application-owned-repositories-for-persistence-ports.md)
- [0002: Use an application-owned configuration access layer for reading, mapping, and reloadable business settings](0002-configuration-reading-mapping-and-reload-boundary.md)
- [0003: Give every first-party failure one base type and a five-digit stable error code](0003-first-party-exception-hierarchy-and-stable-error-codes.md)
- [0004: Version the four public surfaces with SemVer, stamp builds from one declared prefix, and cut a release with a Git tag](0004-versioning-and-release-policy.md)
- [0005: Seal data at rest under one deployment-wide symmetric key ring, provisioned as a secret reference the operator creates](0005-data-encryption-key-ring-and-provisioning.md)
- [0006: Identify an embedding profile by the geometry of its vector space, keep that identity immutable, and make activation state what it is about to spend](0006-embedding-profile-identity-lifecycle-and-activation-cost.md)
- [0007: Write to the remote mailbox through a session type no read path can obtain, and scope the never-marks-read guarantee to retrieval](0007-remote-mailbox-mutation-boundary-and-write-session.md)
- [0008: Store a copied message as a second local email, and leave the occurrence the only identity a stored row carries](0008-copied-message-local-identity.md)
- [0009: Keep the job store in MailFathom's own schema, claim a row with `FOR UPDATE SKIP LOCKED`, and let the enqueuer compose the one key that identifies an execution](0009-durable-job-store-and-execution-identity.md)
- [0010: Author a rule in the configuration the deployment already carries, and write its condition as one NCalc expression](0010-rule-authoring-in-configuration-and-ncalc-conditions.md)
- [0011: Reach a cloud platform over its own OpenAI-compatible surface with a bearer credential the deployment is handed, and write neither a second wire protocol nor a token-minting credential shape](0011-reaching-a-provider-outside-the-openai-wire-protocol.md)
