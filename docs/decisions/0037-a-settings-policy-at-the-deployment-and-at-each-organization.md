---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-10-10
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Hold a settings policy at the deployment and at each organization, read a user's record and a mail account through it in one fixed order, keep a forced value off the record it overrides, and store both policies as rows rather than as configuration

<!-- describes: backend/src/Host/Configuration/UserSettings/**, backend/src/Host/Configuration/Mail/MailSynchronizationOptions.cs, backend/src/Application/Preferences/ClientPreferences.cs, backend/src/Infrastructure/Persistence/Entities/OrganizationEntity.cs -->

## Context and Problem Statement

A deployment serves many users, more mail accounts than users, and organizations that group both, and every setting is still stated on exactly one record: a person's `Language`, `TimeZone`, `ClientTelemetryLevel`, `Portrait`, and `EndpointAccess` on their user record, and everything about a mailbox on its mail account. Nothing states a value once for many records, nothing lets an organization hold a value its members cannot move, and the only thing deciding whether a person may change a setting is the grant over the whole record.

That was deliberate. [ADR 0014](0014-single-tenant-multi-user-ownership-on-the-mail-account.md) said *an organization is not a configuration layer*, the remarks on `UserAccountOptions` say there is no user configuration layer, and the administrative endpoint's page says an organization declares no setting its members or accounts inherit. The owner decided on 2026-10-10, in issue 2347, to reverse all three: the deployment and each organization hold a **settings policy**, and a user's record and a mail account are read through it — the arrangement large hosted suites use for their tenants. Issue 2348 is this record. It writes down what issue 2347 settled, with the reasoning, and answers the seven questions that issue left open, so that the children implementing it build one design rather than four readings of it.

The answers were taken after reading the code the policy has to sit beside rather than from the shape of the problem, and seven findings from that reading decided them. Each is stated where it bears below.

**A record this build will not bind is held back, not fatal.** `ServedUserRecordComposition` composes a user's record and each of their accounts separately; a document that fails the strict binder or a validator is held back, the user or the account is served from the last version that bound where the replica holds one, the refusal is logged at `Error` naming the record, and every other record is unaffected — *one operator's broken row must never be every other user's outage*. A start reads nobody's record, so nothing a stored document says can stop one.

**What is read without a record is a column, written beside the document.** `EndpointAccess` is stated in the user's document and copied onto `McpEndpointEnabled` and `ClientEndpointEnabled` on the user's row by the commit that writes it, and credential resolution and the session lookup read those two columns, so authenticating a request never reads a record. A mail account has the same arrangement for every question asked of all accounts at once: how an account is synchronized, whether its mail is classified, what it is scanned and screened for, and which of its folders are mirrored, shown to tools, embedded, and classified are columns of `settings_mail_accounts` and `mail_account_folder_settings`, read out of the account's document and written in the transaction that writes it. Both record what the record itself states.

**Replicas converge on a version, and the announcement is only the fast path.** `ConfigurationConvergenceWorker` compares versions every thirty seconds whether or not anything was announced — *the interval is the guarantee and the announcement is the optimization* — and recomposes a held user whose row moved on. The persisted configuration document and the user records converge on that one worker.

**Configuration is composed per process.** The persisted layer is one source among several, and environment variables and command-line arguments outrank it by design, so that a bad persisted value can be repaired without reaching the database that holds it. Two replicas of one deployment can therefore read two different values of one key for as long as their files or their environments differ, which is every rolling change to either.

**Whether a secret reference is admissible is judged against one user.** A person may name only a reference their record already carries or one whose material was provisioned for them; an administrator below the deployment may name only what was provisioned for a user the account is assigned to; only a grant at the deployment is unbounded. Nothing judges a reference for a set of records.

**`ClientTelemetryLevel` is documented as an operator's act, and nothing refuses a person writing it.** The only refusals `POST /api/client/record` makes because it is the person who is writing are a moved `EndpointAccess` and a secret reference that is not theirs to name; the level binds from their own save as `Language` does.

**`telemetryEnabled` is applied by the client alone.** `ClientPreferences` stores the switch and the client reads it; nothing on the service but the preferences routes reads it, and the telemetry routes accept a client's records whatever it says.

## Decision Drivers

- **One statement has to reach many records without writing them.** An operator serving a thousand people states a language once, and a change to that statement reaches the thousand without a thousand commits.
- **A record goes on saying only what somebody stated on it.** ADR 0002 refused a user-scoped configuration layer because *a layer whose absent key means inherit from below cannot carry what belongs to one person: the inheritance is silent and no reader can tell a value somebody chose from a value nobody did*. Whatever is built here has to keep that distinction readable, on the record and in every answer about it.
- **A forced value is a guarantee, and a guarantee is a row.** `backend/src/AGENTS.md` and [ADR 0031](0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md) put every guarantee in PostgreSQL, because the replica that would have refused is not the replica the second attempt reaches. A force two replicas can disagree about is not a force.
- **A person is never offered a control whose change will be refused,** and the question *who holds this?* has a one-word answer: the organization, or the deployment.
- **A property a later release adds costs the resolution nothing.** The set of governable properties follows the record's own type, and a list kept beside the type is a list that falls behind it.
- **An organization's rules stop at its edge.** A policy never reaches a user or a mail account outside the organization that holds it, and a record in no organization is governed by the deployment alone.
- **A deployment holding no policy behaves exactly as it did before this feature.**
- **Telemetry is said about a person.** The one client preference this record lets a policy force is a decision the person made about what may be said about them, so taking it from them has to be visible to them and reversible in full.
- **A rolling upgrade runs two builds against one database,** and nothing may assume every replica knows a concept this record introduces.

## Considered Options

- **Keep every setting on exactly one record.**
- **Copy a template into a record when it is created.**
- **Add a configuration layer per organization and per user.**
- **Hold a settings policy at the deployment and at each organization, and resolve each property through it explicitly.**

## Decision Outcome

Chosen option: **hold a settings policy at the deployment and at each organization, and resolve each property through it explicitly**, because it is the only one of the four that reaches many records with one statement, lets a value be held against the person it governs, and still leaves the record saying only what somebody stated on it.

The first nine sections record what issue 2347 settled. The seven that follow answer what it left open, and each says which question it answers.

### What a policy governs

A policy governs three things: **a user's record** — every property of `UserAccountOptions` but the list of mail accounts — **the user's display name**, which is a column beside that record rather than a key in it, and **a mail account** — every property of the account's declaration, with its address and its display name, which are columns too.

**Client preferences are not governed.** What somebody set about their own client — the theme, whether mail opens in tabs, `markReadOnOpen`, and the rest — is theirs: it takes no default, no forced value, and no editing restriction. One preference is the exception, and it is narrow: a policy may **force** `telemetryEnabled`, and may say nothing else about it. `markReadOnOpen` is never governed, because [ADR 0026](0026-marking-a-message-read-when-a-person-opens-it-in-the-client.md) makes marking a message read an act its reader authors, and an act somebody else forced is not one.

**A policy governs the properties of a record and never which records exist.** Whether a person may add or remove a mailbox, be assigned one, or exist at all stays the grant's and ADR 0014's to decide. The list of mail accounts in a user's record is therefore not a governable property: it is where the mail-account section of a policy begins, and a path naming it in the user section is refused saying so.

### What a policy says about a property

Three things, each optional:

- A **default** — the value used where the record states nothing.
- A **forced value** — the value used whatever the record states.
- Whether **the person may change it** — the editing restriction, which is a statement about who decides rather than about the value.

### Who holds one, and which policies a record reads

**The deployment holds one policy and each organization holds one, in the same shape.** An organization is where forcing and restricting matter most; the deployment may do both as well.

**A user or a mail account in no organization reads the deployment's policy alone. One in an organization reads its organization's and then the deployment's.** ADR 0014 keeps every assignment inside one organization, which is what makes that sentence sufficient for a mail account: an account is in at most one organization whoever it is assigned to, so it reads exactly one organization's policy, and the policy that governs a mailbox follows the mailbox rather than whoever happens to be reading it.

### How a value resolves

First hit wins:

1. the deployment's forced value,
2. the organization's forced value,
3. the record's own value,
4. the organization's default,
5. the deployment's default,
6. the built-in default.

The two halves of that order run in opposite directions on purpose. A force is the wider scope overruling the narrower one, so the deployment's is read first; a default is the narrower scope knowing its own people better, so the organization's is read first. An organization can therefore replace any default the deployment states and can force what the deployment only defaults, and it can never loosen what the deployment forces.

**Resolution is per property, where a property is a leaf of the record or a list.** A block is not resolved as a unit: forcing `Delivery:Enabled` leaves `Delivery:Port` to resolve on its own. **A list is one value**: a layer that states `Folders`, `TrustedSenders`, `ContactCollection:Exclusions`, or any other list replaces the layer below it whole and never merges with it entry by entry. Merging would need an identity per entry and a way to remove one, which is a second vocabulary to state what *this list, not that one* already says.

**A stored projection holds the effective value, and the commit that changes it rewrites it.** Two kinds of reader never resolve a record: authentication, which reads the two endpoint switches on a user's row, and every question asked of all accounts at once, which filters on the columns of `settings_mail_accounts` and `mail_account_folder_settings`. Those columns record today what the record states. Under a policy they record what the record is served: each is derived from the effective record and rewritten in the transaction of every write that changes it — a policy write, a write of the governed record, and a move of a user or a mail account into or out of an organization. Anything less leaves two readers disagreeing about one account: a forced value would be honoured by whoever reads that account and ignored by the query that chooses which accounts to read, and a forced screen on outgoing mail would fail open. A projection added later is held to the same rule. What a policy write rewrites is bounded by the judgement the same write already makes over those records, which *A policy that would leave a record invalid is refused* states.

**A projection says what it was derived from, so one that was written past the policy is found and repaired.** A commit is not the only writer of these columns: `MailAccountSettingsReconciliation` reads again, on every replica's convergence interval, every account whose columns trail its document, which is how a row written by a build older than the columns catches up — and it derives them from the account's own document. Under a policy it derives them from the effective record like every other writer, and what it looks for widens. Each projected row — a user's and a mail account's — carries a stamp only a build that knows policies writes: the version of the record the projection was derived from, and the organization it was derived under. A build that does not know policies leaves the stamp alone when it writes the record or moves it, exactly as a build older than the columns leaves `SettingsVersion` alone, so its write shows as a stamp that no longer matches the row. The reconciliation re-derives every such row from the effective record and stamps it.

**Every writer of a projection derives it from the policies it reads inside its own transaction, under the lock a policy write takes.** That holds for a record write, a move, and the reconciliation alike, and it is what a projection's correctness rests on rather than the stamp. A replica's held composition is never what a projection is derived from: a replica that has not yet adopted a policy change still serves the earlier composition, and a record it writes in that interval is judged and projected against the policy rows as they stand in the database, not against the policy it holds. The reconciliation is held to the same rule, and it is the writer for which it matters most, because its write is conditional on the record's version and a policy write moves no record's version. Deriving a row under one policy and committing it after another has landed would overwrite what that policy write had just rewritten, and stamp the row as matching. Under the lock that cannot happen: the policy write either commits first, and the reconciliation derives from it, or commits second, and rewrites the row itself. So the stamp carries no policy version and needs none — and adding one would oblige every policy write to restamp every row it governs, including the ones whose effective value it left alone.

The migration that adds the stamp fills it as current, since no policy exists when it runs and the effective value of every row is its own.

**Every read says what it resolved.** A read of a governed record answers, per property, the effective value, which of the six steps supplied it, and whether the reader may change it. That is what answers ADR 0002's objection rather than overruling it: the inheritance is no longer silent, and a value somebody chose is told apart from a value nobody did in the same answer that serves it.

### What forcing does to the record

**A forced value is never written into the record.** The record's own value is kept and ignored, so lifting the force brings it back, and the record goes on holding only what somebody stated on it.

**While a property is forced its own value is frozen.** A write that carries the property through as a read showed it passes, and leaves the own value exactly as it was. That is the forced value wherever the read answers the effective record, the record's own value wherever the read answers the stored document, and nothing at all on a route that takes only what changed. A write that states anything else is refused, naming the property and whether the organization or the deployment holds it. Stating the forced value has to pass because a caller that read the effective record and sends it back whole is carrying the property through, not changing it — and it must not overwrite the own value on the way, or lifting the force would restore the force's value under the person's name.

### How editing is restricted

Per property, in one of two modes a policy picks for each of its sections:

- **`AllExcept`** — everything is the person's to change except the properties the policy lists. This is what an unstated restriction means, with an empty list.
- **`NoneExcept`** — nothing is the person's to change except the properties the policy lists. This is the allow-list, and it is what makes a property a later release adds start out locked.

A path naming a block covers every property beneath it, in either mode.

### Three classes of property

**An ordinary property takes all three statements.** Nothing marks it; it is what a property is unless something says otherwise.

**An identity property takes no default and no forced value, only the editing restriction.** It says *who* or *which* rather than *how*, so its value is necessarily different on every record it could be stated for, and one statement for many records could only be wrong. Today those are a user's display name and `Portrait`; a mail account's address, its `DisplayName`, its `UserName`, and the sending identity under `Delivery` — `UserName`, `FromAddress`, and `FromDisplayName`; and every secret block, which *Secret blocks take no default and no forced value* below treats on its own.

**A property only an administrator writes takes a default and a forced value, and no editing mode ever makes it the person's to change.** Today those are `EndpointAccess` and `ClientTelemetryLevel`. A `NoneExcept` list naming one is refused when the policy is written rather than accepted and ignored. The class is a decision about the second of the two and not a description of it: nothing refuses a person writing `ClientTelemetryLevel` today, and the level somebody's client records at is what an operator raises to investigate a defect and pays a collector for, so it is not the person's to move. The class closes that.

**The display name is an identity property, and this record does not say whether it stays unique.** Issue 2342 asks whether a user's label stays unique across the deployment once organizations administer their own people. Nothing here answers it and nothing here depends on the answer: an editing restriction says who may change a name, never which names are free. A person may change theirs only where the grant that already decides it and every policy that governs them both allow it.

### A new property costs nothing

The governable properties are derived from the user record's and the account declaration's own types, and the class of a property is marked on the property where the type declares it. A property added to either is therefore governed as an ordinary one without a change to the resolution, and only one that belongs to another class is marked as such. A property that is neither governable nor explicitly classed fails a test rather than quietly escaping the policy.

### Client preferences, and what forcing telemetry costs a person

A policy may state a forced value for `telemetryEnabled`, at the deployment or at an organization, to on or to off. It takes no default — a default would be the policy answering a question the person has not been asked, which is exactly what the switch exists to prevent — and no editing restriction, because a switch that is neither forced nor the person's is not a state anybody could explain to them.

**Forcing it off costs a person nothing.** It is the answer that says least about them.

**Forcing it on takes a decision away from the person it is about, and this record says so rather than treating it as one more setting.** `ClientPreferences` documents the switch as *a decision about what may be said about this person*, theirs to set under the grant they already hold. Once a policy forces it on, their client reports which screens they opened and how long those took whatever they answered, and whoever holds the policy — an organization for its members, an operator for everybody — has taken that decision and owes whatever basis it needs for it. What MailFathom owes is narrower and is not optional:

- **The person is told before they try, not by a refusal.** The preferences read says the switch is forced and whether their organization or the deployment holds it, and the client draws it read-only with that sentence beside it, in text rather than in colour alone.
- **What they are told does not get shorter.** What is sent, where it goes, and what it never carries stay on the screen exactly as they are for somebody who chose it.
- **Their own answer is kept and comes back.** A refusal stored before the force is neither overwritten nor read, and it applies again the moment the force is lifted, so nobody is opted in for good by a rule that no longer exists.
- **A force raises nothing else.** It does not raise the level a client records at and it does not create a collector: a deployment that forwards no telemetry receives none whatever a policy forces, and a forced-on client is asked for exactly what an unforced one is.
- **The service applies the effective value itself.** The telemetry routes accept or drop a client's records by it and the session route agrees with them, so a forced *off* is enforced rather than trusted to the client. A forced *on* asks the client and cannot compel it: a client that sends nothing is refused nothing.

**The deployment's force wins over an organization's, here as everywhere.** So an organization that forces telemetry off for its members is overruled by a deployment that forces it on. That is the order doing what it does for every property, and it is accepted because the collector is the deployment's: whoever operates the deployment is who receives the records. An organization that cannot accept that is in the position ADR 0014 already describes — it needs a deployment of its own.

Whether forcing telemetry on over a person's refusal needs anything beyond that visibility under data-protection law is a question for whoever advises the organization or the operator doing it, not something this record settles.

### A forced value binds an administrator, and the editing restriction binds the person alone

*This answers the third open question.*

**A forced property is refused to everybody.** An administrator editing one record is refused a change to a property a policy forces, in the same words a person is. The alternative is a write that is accepted, stored, and ignored, which tells the administrator they changed something and changes nothing; and the administrator has a truthful way to get what they wanted, which is to change the policy.

**The editing restriction binds the person the record is about, acting for themselves.** It is checked on the routes somebody writes their own record, their own name, and the mail accounts assigned to them through, and for a mail account that means every user it is assigned to. A write made under an administrative permission is not judged by it: the restriction says the person does not decide this property, and somebody has to.

**An administrator-only property is refused to the person on those routes whatever any policy says,** and a person who also holds an administrative grant uses it through the administrative surface, which is the rule ADR 0014 already states — an operation that is both is two operations.

### The deployment restricts editing exactly as an organization does

*This answers the fourth open question.*

**One shape at both scopes.** The deployment's policy carries the same editing restriction, in the same two modes, as an organization's. Giving the two scopes different vocabularies would mean a rule that could be written at one and not at the other, and every reader would have to remember which.

**Where both speak, the more restrictive answer wins:** a property is the person's to change only where every policy governing the record leaves it so. An organization can lock what the deployment leaves open and can never open what the deployment locked, which is the same direction a force runs in.

### Secret blocks take no default and no forced value

*This answers the second open question.*

**A policy states nothing about the value of a secret block** — `Secrets:Password`, `OAuth:ClientSecret`, `OAuth:RefreshToken`, `TransportSecurity:TrustedCertificateAuthority`, `Delivery:Secrets:Password`, and any a later release adds. One named in a default or in a forced value is refused by name when the policy is written. The editing restriction reaches them like any identity property, so a policy can still say a person may not change a mailbox's credential.

Three reasons, and the first is sufficient:

- **Nothing can say for whom such a reference would be admissible.** A reference is judged against one user today — what their record already carries, or what was provisioned for them. A policy speaks for every record in its scope, so a reference in it is admissible for none of them under the rule that exists, and for all of them only under a rule nobody has written.
- **A held credential beside a record's own host is the repointing hazard as a feature.** A save that keeps a secret reference it was not shown and changes the host beside it is already refused, because the provisioned material would be presented to whatever was written there. A policy holding the secret while each mail account states its own server is that arrangement by design.
- **A credential is identity.** It is what one mailbox authenticates as.

The non-secret halves of the same blocks are ordinary: an organization can force a token endpoint, a client identifier, a host, a port, and a transport security posture, which is most of what *state the server once* asks for.

**A grant held below the deployment is bound in a policy write exactly as it is in an account write.** Forcing a host, a port, a token endpoint, or a transport security posture decides where a mailbox's credential is presented, whoever stated the credential, and a forced value is never written into the account, so no comparison of the account's stored settings would notice it. `UserRecordAdministration` already refuses an administrator of less than the whole deployment any change to a mail account's settings while a secret those settings name was not provisioned for a user the account is assigned to, because such an administrator is trusted with their organization's mailboxes and not with a credential the deployment holds for somebody else. A policy must not be a second way to the change that route refuses. So a write that changes a mail account's effective settings under such a grant is held to that bound whichever route it takes: the account's own write, a policy write stating a default or a forced value in the `MailAccounts` section, and a move of the account into or out of an organization. A policy write is judged against every account whose effective settings it changes — in the pass that judges their validity, under the same bound on how many records one write reads — and is refused naming the accounts in the way. Which settings decide where a credential goes is not enumerated, for the reason the per-account bound does not enumerate them: a list is what a setting added later would be missing from. A write that changes only an editing restriction moves no effective setting and is not judged, and a grant held at the deployment is not asked, as it is not today.

**What is given up is real** — an organization-wide `OAuth:ClientSecret` for one application registration, and a private certificate authority every mailbox of an organization trusts — and it is given up for the first version rather than refused. What reopens it is a rule saying for whom a policy-held reference is admissible and what it may be presented to; the likely shape is that a policy may hold one only where the same policy forces the host it is presented to.

### A policy that would leave a record invalid is refused, and a record that is invalid anyway is held back

*This answers the first open question.*

A policy can make a record it governs invalid without anybody writing that record: forcing `Delivery:Enabled` on for an account that names no submission host, or removing a default a hundred records were relying on for a property they are required to have.

**A policy is judged on its own first.** Every path names a property that exists, every value binds by the same strict binder and the same rules as the record it governs, and the class rules above hold. That half needs no record and is asked of every write.

**Then it is judged against the records it governs, and a write that would leave one of them invalid is refused.** The refusal names the property and the records in the way — the first of them up to a bound the answer states, and how many there are in all — and commits nothing. Accepting the policy and marking those records as out of policy was the alternative, and it was declined: a mark is a state somebody has to notice, on a record nobody wrote, and the person who finds it is the mailbox's user rather than the administrator who caused it.

**Two things bound that judgement.**

- **It is asked only of what the change can break.** A write that changes nothing but an editing restriction judges no record, because a restriction cannot make a record invalid; a write that changes one section's defaults or forced values judges that section's records and no others.
- **One write judges at most a number of records the deployment's configuration states.** A policy governing more records than that is refused before any of them is read, naming both figures, and is never committed half-judged. The bound is the operator's to raise, and what raising it costs is stated where they meet it: the judgement runs inside the write, and writes to the records it is judging wait for it.

**The rule is upheld inside the transaction of each write that can break it, under a lock those writes share** — a policy write, a write of a governed record, and a move of a user or a mail account into or out of an organization. The reconciliation that repairs a stored projection takes the same lock for the same reason, though it judges nothing: what it writes is derived from a policy, and must be derived from the one in force when it commits. This is how ADR 0014 holds the rule that an assignment stays inside one organization, and for the same reason: no constraint can express it. The attempt that commits second is judged against the first. A record write that loses is refused as any invalid record is; a policy write that loses is refused naming the record that arrived; a move into an organization whose policy would leave the record invalid is refused naming the property, and nothing is rewritten on the operator's behalf.

**That makes an invalid record unreachable through this build's own writes, and two routes still reach one:** a replica running a build that does not know policies, for the length of the upgrade that introduces them, and a later release whose rule is stricter than the one a stored record was accepted under. For both, the answer is the one the service already gives a document a build will not bind: **the record is held back** — served from the last composition that validated where the replica holds one, reported at `Error` naming the record and the property, with every other record unaffected. A record is never quietly served on effective settings that do not validate, and the policy is never quietly set aside for one record so that it does.

**A record may omit what a policy supplies.** `Language` is required of a user's record only where no policy states it, and a mail account may leave out a property its organization's or the deployment's default provides, including one that is otherwise required. The validators a start applies and a write applies run over the effective record, so a combination that would be refused in one declaration is refused however its halves were supplied.

### Both policies are rows, and neither is configuration

*This answers the fifth open question, and it does not take the answer that question was left with.*

**Each policy is a versioned document on a row of its own, in one table beside `settings_root`, `settings_accounts`, and `settings_mail_accounts`:** one row for the deployment and one per organization, the organization's keyed onto its organization and removed with it. A policy is read and written whole, against the version it was read at, and a superseded write is refused rather than composed over the one that won. No row is the same as a policy that states nothing.

**The deployment's policy is not a section of the persisted configuration document.** That was the recommendation, and it has a real argument for it: the policy is the deployment's own statement, so it would sit where every other one sits and inherit strict binding at start, the validated write through `IConfigurationWriter`, the reload, and provisioning from a chart. Four things decided against it.

- **A force would become something two replicas can disagree about.** Configuration is composed per process, and environment variables and command-line arguments outrank the persisted layer by design. A forced value is a guarantee, and a guarantee read from a source that differs between two replicas mid-rollout holds on one of them. The persisted layer could be made the only source allowed to state the section, and then nothing of *being configuration* is left but the row.
- **A stored projection is a row, and a policy has to reach it in its own commit.** A force on `EndpointAccess` moves the effective switches of every user it governs, which authentication reads from the users' rows, and a force on a mailbox's mode, its scanning, or its folders moves the columns every query over all accounts filters on. A policy on a row of its own rewrites both in the transaction that commits it. A policy in the configuration document is committed by one statement against `settings_root`, and one that arrived from a file is committed by nothing at all.
- **A policy is written whole against its own version; configuration is edited by path against the document's.** A policy held in the configuration document would be refused as superseded by an unrelated setting somebody changed, would have its lists written as indexed keys that merge entry by entry across sources — the opposite of *a list is one value* — and would be reachable by a generic configuration write that judges no record.
- **The organization's policy has to be built as a row whatever is decided here.** Organizations arrive and depart while the process runs, and an administrator holding a grant at one organization must not write the deployment's document. Holding the deployment's policy the same way is that path with no organization on it, where the alternative is a second store, a second write path, and a second way for the two to drift apart — under a decision that says the two have one shape.

**How this stands beside [ADR 0002](0002-configuration-reading-mapping-and-reload-boundary.md):** exactly where that record points. It closes the write side of configuration, sends *state a program modifies* to PostgreSQL, and says an administrative surface *may administer state in the database, which is a decision for whichever record introduces that state*. A policy is that state and this is that record. Nothing here writes a configuration source, and nothing here adds one: a policy is not an `IConfigurationSource`, no setting of the deployment is read through it, and the refusal of a user-scoped configuration layer in that record's second amendment stands with its reason intact — the reason is the driver above that made the resolution explicit and the source of every value part of the answer. `IConfigurationWriter` stays the one way a setting of the deployment changes; a policy is not a setting of the deployment but a statement about records, and it changes the way records do.

**What that costs is provisioning from a file.** An operator who installs from a chart states the deployment's policy afterwards, through `mfctl` or the administrative endpoint, as they already do for users, mail accounts, and roles. The single key this record adds to configuration is the bound in the section above, which is a ceiling of the deployment and belongs there.

**Who reads and writes one.** Reading takes `mailfathom.admin.read` and writing `mailfathom.admin.configuration.write`, which already covers *who this deployment serves and what it reads for them*. The deployment's policy needs the grant at the deployment; an organization's accepts it at that organization as well; and a target outside the caller's scope is answered as one that does not exist, as [ADR 0012](0012-authorization-model-named-permissions-and-where-they-are-enforced.md) answers every other. A grant at an organization writes less than a grant at the deployment does, and *Secret blocks take no default and no forced value* states the bound it writes under.

**What a policy document may hold.** By the class rules it holds no name, no address, no login, and no credential of anybody it governs. It can still carry personal data about third parties — a forced `TrustedSenders` is a list of addresses — so it is classified as the account documents it governs are: sensitive by default, never logged, and named in a refusal by path rather than by value.

### What a rolling upgrade does

*This answers the sixth open question.*

**The schema change is additive, and a build that does not know policies never reads the table.** Such a replica serves every record as it is stated: no default applied, no force, no restriction. So for the length of the upgrade that introduces the feature — and after any rollback past it — **a policy is in force on the replicas that know it and on no others**, and nothing a policy holds is promised until every replica runs a build that does and the reconciliation below has drained what the older ones wrote. An operator who needs a force to hold from its first second writes it once the rollout has finished, and the release that introduces the feature says so. A rollback past it lifts every force without a word, and says so too.

**Replicas that know policies agree on one,** because a policy is a row: each converges on its version by the worker that already converges records, within the interval that worker guarantees, and a held record is recomposed when its own version or the version of a policy governing it moves on. Until a replica has adopted a change it serves the composition it holds, which is the earlier policy and never none.

**A stored projection does not wait for convergence.** The effective `EndpointAccess` of every user a policy governs, and the queryable settings of every mail account it governs, are written by the commit that changes them — the policy write, the record write, or the move. So a session or a token stops or resumes on its holder's next request, and a query over every account chooses by the effective settings, on every replica: including one that has not adopted the policy and one that has never heard of policies. The one residual is the introducing upgrade: a replica that does not know policies writes a record's own switches, and an account's own settings, onto those columns when it writes that record, undoing the policy for that record. It does not stay undone. That write leaves the row's stamp behind, and the reconciliation on every replica that does know policies re-derives the row from the effective record on its next convergence interval, a bounded number of rows per interval. So for as long as an older replica runs it can undo a force again with each write and a newer one puts it back within an interval, and once the rollout has finished every row it touched is repaired without anybody writing the record or the policy again. The window in which a force can be found undone is therefore the rollout and the intervals it takes to drain what the rollout left, and nothing after that.

**A record written under a policy may be one an older build will not bind,** since it may omit what a default supplies. Such a replica answers it by its own rules: a held record stating no language reads as English, and a mail account missing a property that replica requires is held back there, reported, and served by the replicas that can compose it. Nothing crashes and no other record is affected.

**A policy a replica cannot bind is held back exactly as a record is.** Between two builds that both know policies, a policy can name a property or a value only the newer one carries. The older replica keeps the last version of that policy it bound and reports the one it rejected. A replica holding no earlier version does not serve the records that policy governs: it never falls back to serving them unresolved, because a force that lifts whenever a replica cannot read it is not a force. That is a wide failure for a deployment's policy, so two obligations follow. A policy naming something a release introduced is written after the rollout that brings it, and a release that removes or renames a governed property carries the migration that rewrites the policies naming it, as it does for the records.

### The vocabulary

*This answers the seventh open question.*

| Term | What it names |
|---|---|
| **Settings policy** | The feature, and the document one scope holds. *Policy* alone where nothing else could be meant. |
| **Scope** | Where a policy is held: **the deployment** or **an organization**. Those two words are also the whole answer to *who holds this?* |
| **Section** | The half of a policy about one kind of record: `Users` and `MailAccounts`. |
| **Default**, **forced value**, **editing restriction** | The three statements, written `Defaults`, `Forced`, and `Editing`. |
| **`AllExcept`**, **`NoneExcept`** | The two editing modes, written as `Editing:Mode` beside the list `Editing:Properties`. The names say what the list means, which *open* and *closed* would have left to memory. |
| **Own value**, **effective value** | What the record states, and what resolution answers. |
| **Held**, **inherited**, **own** | The three states a person sees: forced or not theirs to change; a default they are getting; a value they stated. |

**`Defaults` and `Forced` are each a sparse document in the shape of the record their section governs** — the same keys, nested the same way, bound by the same strict binder. The `Users` section adds two names that are not keys of the user's document: `DisplayName`, the column, which is accepted only in an editing list, and `ClientPreferences:TelemetryEnabled`, which is accepted only in `Forced` and is the one path beneath `ClientPreferences` a policy takes.

**A property path is written the way a path within a record already is:** the record's own key names joined by colons and compared without regard to case, relative to the record the section governs. `Language` and `EndpointAccess:McpEndpoint` are paths in the `Users` section; `Host`, `Delivery:Enabled`, and `TransportSecurity:ConnectionSecurity` are paths in the `MailAccounts` section, each relative to one account's declaration and never to its position in anybody's list. **A path naming a block covers every property beneath it. A path naming a list names the whole list**, and no path reaches inside one — there is no index segment, because a list is one value.

### What the model leaves out

- **An exception for one user or one group.** A policy speaks for its whole scope. An exception is a third layer, and a group's would be a fourth; each needs a place in the resolution order, and *who holds this?* stops having a one-word answer the moment one exists. Two sets of people an operator wants governed differently are two organizations, which is what an organization is for.
- **Narrowing the values a property may take,** as opposed to fixing one. It needs a constraint vocabulary — sets, ranges, patterns — which is a second validation language beside the binder that already bounds every property, to be kept agreeing with it forever. A forced value covers *exactly this*, and that is the case anybody has asked for.
- **A history of who changed a policy and when.** No administrative write keeps one today — not a user's record, not a mail account, not the configuration document, for which ADR 0002 refused it — and a history of policies alone would be an audit trail of one administrative act among many. It belongs to an audit of administrative acts as a whole. What this record leaves for it is the seam: every policy write is one commit against a version, by an authenticated principal holding a named permission at a named scope.
- **Client preferences other than `telemetryEnabled`.** They are the person's own.

### What breaks, by surface

Per [ADR 0004](0004-versioning-and-release-policy.md), against each of its four surfaces, and against the two APIs the children name beside them.

- **Database schema** — additive. One table, and on the rows that carry a projection — a user's and a mail account's — the stamp saying which version of the record, under which organization, the projection was derived from. What changes beside that is what the stored projections mean: the two endpoint columns on a user's row, and the queryable settings of a mail account — `SynchronizationMode`, `ClassifiesSpam`, `ScansFor`, and `ScreensOutgoingMailFor` on `settings_mail_accounts`, and `IsSynchronized`, `IsVisibleToTools`, `GeneratesEmbeddings`, and `IsClassifiedForSpam` on `mail_account_folder_settings` — hold the effective value where they held the record's own. With no policy the two are the same value, so a deployment holding none reads exactly what it read before.
- **Configuration schema** — additive. One key, the bound on how many records one policy write judges.
- **MCP tool contract** and **deployment contract** — unchanged.
- **Client API** — breaking. Reads of the record, the display name, a mail account, and the preferences gain per-property policy fields; the writes of each gain a refusal for a property a policy holds; and a person changing `ClientTelemetryLevel` on their own record is refused where it was accepted. A client that sends back what it read is unaffected.
- **Administrative API and `mfctl`** — breaking. The record and account reads gain the same fields, a write to a forced property is refused, and the policy routes and commands are new.

Each child names its own break in the pull request that makes it, with the operator's action.

### Consequences

- Good, because one statement reaches every record it governs without writing any of them, and changing it reaches them all at the next convergence.
- Good, because a record still holds only what somebody stated on it, so lifting a force or removing a default restores exactly what was there, and every read says where each value came from.
- Good, because a forced value is a row every replica reads, never a value composed per process.
- Good, because a property a later release adds is governed without a change to the resolution, and an allow-list locks it from its first day.
- Good, because an organization's administrator governs their own people and mailboxes under a grant held at that organization, and can never loosen what the deployment holds.
- Good, because both scopes are one store, one write path, and one way to converge.
- Neutral, because every reader of a governed setting reads the effective record rather than the stored one, which is a change at each of them and a test per reader.
- Neutral, because a list is replaced whole, so an organization that wants the deployment's trusted senders plus one of its own restates the list.
- Bad, because a policy write at the deployment judges every record of a section, inside the write, and record writes wait for it; a large deployment raises a bound to make one.
- Bad, because the deployment's policy cannot be provisioned from a chart or a file.
- Bad, because a secret block cannot be held by a policy, so an organization-wide client secret or certificate authority is still stated per mailbox.
- Bad, because a policy a replica cannot bind and holds no earlier version of takes every record it governs out of service on that replica, which for the deployment's policy is everybody.
- Bad, because for the length of the upgrade that introduces the feature a policy is in force on some replicas and not on others, and a rollback past it lifts every force silently.
- Bad, because a person whose organization or deployment forces telemetry on is reported on over their own refusal, which this record makes visible and reversible rather than impossible.

## Validation

- A unit test fails when a property of `UserAccountOptions` or of the account declaration is neither governable nor explicitly classed, and another when the resolution does not govern a property either type carries.
- Unit tests cover each of the six steps of the order, a record in no organization, an organization that states nothing, a list stated at more than one layer, and the more restrictive of two editing restrictions.
- Unit tests assert that a forced property's own value survives every write that passes and that a write stating anything else is refused, for a person and for an administrator alike.
- A unit test per class asserts what a policy write refuses: a default or a forced value for an identity property and for a secret block, an administrator-only property in a `NoneExcept` list, any client preference but `telemetryEnabled`, and anything but a forced value for that one.
- Unit tests assert what the losing attempt is answered with when a policy write and a record write, or a policy write and a move, land together.
- A unit test asserts what a replica that has not yet adopted a policy change serves, and what one that cannot bind a policy serves with and without an earlier version of it.
- A unit test asserts that a policy change moving the effective `EndpointAccess` of the users it governs writes their rows in the commit that changes it, and another that a policy change moving a queryable setting of the mail accounts it governs — a forced `Mode`, a forced screen on outgoing mail, a forced `Folders` — rewrites their columns in that commit, so that the query over every account and the read of one account agree.
- A unit test asserts that a row whose stamp no longer matches it — a record written, or moved between organizations, by a build that does not know policies — is found by the reconciliation and re-derived from the effective record rather than from the record's own document, for a user's endpoint switches and for a mail account's queryable settings, and that a row whose stamp matches is left alone.
- A unit test asserts that a policy write landing between a reconciliation's read of a row and its write leaves the row holding the new policy's value, and another that a record written by a replica still holding the earlier composition is projected from the policy rows in the database rather than from the composition it holds.
- A unit test asserts that a policy write under a grant held below the deployment is refused when it changes the effective settings of a mail account naming a secret that was not provisioned for a user the account is assigned to, that the same write under a grant held at the deployment passes, and that a move of such an account under the narrower grant is refused the same way.
- `Fathom review` reads this record's `describes:` marker, so a change under the paths it names is told which decision it is being read against. The marker names the code this decision is about as it exists today; it gains the policy's store, its administration, and the resolution as the children land them.

## Pros and Cons of the Options

### Keep every setting on exactly one record

ADR 0014 as it stood: a setting is the deployment's, the account's, or the person's, and nothing is inherited.

- Good, because it is free, and a record is the whole answer to what it is served.
- Bad, because a setting common to a thousand records is written a thousand times and changed a thousand times.
- Bad, because nothing can hold a value against the person it governs, so an organization's rule holds only until somebody edits their record back.

### Copy a template into a record when it is created

A new user or mail account starts from values an operator or an organization stated once.

- Good, because the record stays the whole answer and nothing resolves at read.
- Bad, because a change to the template reaches nobody who already exists.
- Bad, because the copy is indistinguishable from a choice, so no later reader can tell which of somebody's settings would follow a new default — the objection ADR 0002 raised.
- Bad, because it forces nothing and restricts nothing, which is most of what an organization asked for.

### Add a configuration layer per organization and per user

A configuration source per scope, composed by the provider merge that composes the deployment's own.

- Good, because binding, validation, and reload already exist for it.
- Bad, because ADR 0002's second amendment refused exactly this, for a reason that still holds: an absent key inherits silently.
- Bad, because the provider merge has one direction, and a force has to win over a narrower scope while a default has to lose to it.
- Bad, because arrays merge by index, so a list could never be one value.
- Bad, because it has nowhere to say that a person may not change a property.

### Hold a settings policy at the deployment and at each organization, and resolve each property through it explicitly

The chosen option.

- Good, because a default, a force, and an editing restriction are three statements with one order between them, and the order is written down rather than implied by where a source sits.
- Good, because the source of every effective value is part of every answer.
- Neutral, because it is a resolution of its own rather than the configuration provider's, written once and extended by each kind of record it governs.
- Bad, because it adds a store, a write path, and a judgement over every governed record that no simpler option needs.

## More Information

- Issue 2347 is the parent and holds what the owner decided on 2026-10-10. Its children implement this record: issue 2350 the store and its administration, issue 2352 the resolution for a user's record, issue 2353 for a mail account, issue 2354 the telemetry switch, issue 2351 the design, and issue 2355 the client.
- Issue 2349 settles that a mail account in no organization is assigned to at most one user. This record does not depend on it: such an account is governed by the deployment alone whoever it is assigned to.
- Issue 2342 asks whether a user's label stays unique across the deployment. This record leaves it open, and says so under *Three classes of property*.
- [ADR 0014](0014-single-tenant-multi-user-ownership-on-the-mail-account.md) said an organization is not a configuration layer. It is still `proposed`, so that paragraph is amended in place and names this record rather than being superseded by it.
- [ADR 0002](0002-configuration-reading-mapping-and-reload-boundary.md) closes the write side of configuration and sends state a program modifies to PostgreSQL, which is the authority both policies are stored under. It is not amended.
- [ADR 0012](0012-authorization-model-named-permissions-and-where-they-are-enforced.md) publishes the permissions a policy is read and written under and the scopes a grant is held at. It is not amended, and no permission is added.
- [ADR 0031](0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md) is where *a guarantee is a row* is recorded.
- Revisit the refusal of secret blocks when a rule exists for whom a policy-held reference is admissible. Revisit the bound on the judgement against a measurement of a policy write over a large deployment. Revisit the absence of a policy history when an audit of administrative acts is designed.
