---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-08-15
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Make a permission a named capability MailFathom publishes, grant it on the credential's own configuration entry or from a token's scopes, and enforce it in the use case as well as at the transport

<!-- describes: src/Host/Security/**, src/Host/Configuration/Access/**, src/Host/Api/**, src/Mcp/Tools/** -->

## Context and Problem Statement

Authentication is settled on both protected surfaces and authorization is not. `TransportAccessPolicy` asks two questions of a validated token — is this a subject the deployment serves, and does it carry every scope the entry that trusts its issuer requires — and a caller that answers both reaches everything that surface serves. A credential the operator configured, an API key or a registered client public key, is asked neither, deliberately: writing it into this deployment's configuration *is* the authorization decision. `docs/operations/mcp-endpoint.md` records the consequence in *What a credential decides, and what it does not*, and `docs/operations/admin-endpoint.md` records its own: every authenticated caller may perform every administrative operation, which is why an administrative key is as sensitive as the mailbox credentials it can place, the histories it can read, the spend it can begin, and the mail it can dispose of.

Issue 583 is the gate of issue 590's group, and what it has to settle is not one question but a set of them, each of which a later issue would otherwise decide on its own in an implementation diff: what a permission *is*, whose vocabulary names one, whether the set is closed, how each of the three credential kinds acquires one, what a credential naming none may do, whether the two surfaces share a vocabulary, where the check runs, what a refused caller is told, and whether the accounts a caller may reach belong to this model at all.

Two facts constrain the answer and both were checked rather than assumed.

**The MCP protocol has no place on a tool descriptor to state a required permission.** Checked on 2026-08-15 against the current draft specification: a `Tool` carries `name`, `title`, `description`, `icons`, `inputSchema`, `outputSchema` and `annotations`, and `ToolAnnotations` carries `title`, `readOnlyHint`, `destructiveHint`, `idempotentHint` and `openWorldHint`. Per-tool scope advertisement is an open item of the Authentication Interest Group's Tool Scopes working group and is not in the protocol. Section 13.5 of `specs/2026-07-22-mail-fathom-architecture-draft.md` says each tool declares an OAuth security scheme and a required `mail.read` scope; that predates the settled protocol, and this record supersedes it on the point, as section 2 of that draft provides for.

**The same specification does sanction varying the listing by the caller.** It states that the set of tools a server returns must not vary per connection or as a side effect of other requests, and then that it *may* vary by the authorization presented on the request — returning only the tools the caller's granted scopes permit — because credentials are per-request input rather than connection state. So the decision a descriptor cannot carry, the listing can.

One neighbouring question is already settled and is not reopened here. Issue 816 separated the scopes a deployment advertises from the scopes it requires, so `OAuthValidationOptions` carries `RequiredScopes` and `AdvertisedScopes` and `scopes_supported` is no longer the enforcement list read backwards.

## Decision Drivers

- **A grant nothing enforces is worse than no grant.** An operator who writes a permission and believes it bounds a credential has been given a false answer, so every name a grant may carry has to correspond to a check that exists.
- **A permission has to be legible to an operator before a request is refused.** The configuration entry is where they meet it, and the refusal they read afterwards has to be reconcilable with what they wrote.
- **Three credential kinds acquire authorization in materially different ways.** A key and a public key exist because somebody wrote them into this deployment's configuration; a token is issued by a server that decides for itself who receives one. A model that pretends the three are the same either asks a key for something nothing can put in it, or lets a token be admitted on the terms of a configured credential.
- **The deployment serves one owner's mail.** This is not a multi-tenant service, so the model does not have to express delegation, group mapping, or a policy language, and inventing them would be scaffolding for a shape nobody has.
- **Root `AGENTS.md` asks for authorization close to the use case as well as at the transport boundary**, so that a second entrypoint — a rule action, a worker, a command, a gateway added later — cannot widen access by omission.
- **What leaves the process is a different question from what the caller can read.** `ask_mail` sends mail content to a model provider, so whether a credential may reach it is a decision about egress and not only about retrieval.
- **The break is affordable and the record of it is not optional.** ADR 0004 permits a `0.y.z` minor to break the configuration schema, the MCP tool contract and the deployment contract; what it requires is that each break is named against its surface with the operator's action.

## Considered Options

- **A closed set of named permissions this repository publishes, granted on the credential's configuration entry or read from a token's scopes.**
- **Roles: a few named bundles — reader, operator, administrator — an entry selects one of.**
- **An operator's own vocabulary, mapped onto operations in configuration.**
- **Authorize tokens only, through scopes, and leave a configured credential unbounded.**

## Decision Outcome

Chosen option: **a closed set of named permissions this repository publishes**, because it is the only one of the four in which every name an operator can write corresponds to a check that exists, and the only one that reaches all three credential kinds without asking any of them for something it cannot carry.

The rest of this section settles each question issue 583 lists. Every child of issue 590 is written against these answers, and a child that departs from one says so on its own issue rather than deciding it again.

### The unit is a permission, and a scope is how a token carries one

A **permission** is a named capability. It is the unit everywhere inside MailFathom: the value an operator writes in a grant, the value a use case checks, and the value a refusal is counted under. It is not a role, because a role is a bundle whose meaning drifts as operations are added to it, and this deployment's operations are few enough to name individually.

An OAuth **scope** is not a second unit. It is the only way a token has of carrying a permission, so where a grant comes from a token, a scope bearing a published permission name grants that permission and nothing else does. A scope naming anything else — `openid`, `profile`, `offline_access`, another resource's scope — is ignored rather than refused, because a token legitimately carries scopes about the client's own session and about resources that are not this one.

`RequiredScopes` keeps the job it has and does not acquire this one. It decides **admission** — whether a caller reaches the surface at all — and a permission decides what an admitted caller may do. A value may not appear in both: a permission name written into `RequiredScopes` would turn a smaller grant into a closed door, refusing at the door a caller the deployment meant to serve less. Startup refuses the overlap. An operator who wants no token admitted without a MailFathom permission already has that, because a token granted nothing is refused on every call it makes.

### MailFathom owns the vocabulary, and the set is closed

The names are this repository's, published here, and validated at startup against this list. A name is an identity that has to survive a rename of whatever implements it, which is the shape this repository writes as a closed enumeration rather than as a plain enum, and a break in one is a configuration-schema break under ADR 0004.

A name is `mailfathom.<surface>[.<subject>].<verb>`, lowercase, dot-separated, and always a valid OAuth scope token so that the same string can travel in a `scope` claim.

| Permission | What it covers |
| --- | --- |
| `mailfathom.mail.read` | The MCP tools that read the local mailbox copy: `list_accounts`, `list_emails`, `get_email_content`, `search_emails`. |
| `mailfathom.mail.ask` | `ask_mail`, which answers from mail content by sending it to a model provider. |
| `mailfathom.admin.read` | The administrative reads that report the deployment's own state and no mail: the session, embedding status and the activation preview, the loaded rules, a run's progress, and the stopped-job list. |
| `mailfathom.admin.audit.read` | The per-account records derived from mail: the mailbox-mutation audit, the answering audit, the rules history, and the spam classifications. |
| `mailfathom.admin.operate` | Asking the deployment to do work it can already do: running rules over an account, classifying an account, retrying or dropping a stopped job, cancelling a reindex. |
| `mailfathom.admin.credentials.write` | Storing a mailbox refresh token. |
| `mailfathom.admin.spend` | Activating the declared embedding model, which is the one operation that starts a provider bill. |
| `mailfathom.admin.erase` | Erasing the mail stored for a folder an account no longer mirrors. |

The separations above are the ones an operator would plausibly want to grant apart: reading state, reading what was derived from mail, causing work, placing a credential, starting a bill, and destroying mail. That sentence is the rule the set is allocated under, and issue 587 maps every administrative route onto exactly one of these names rather than adding to them.

**No permission implies another.** An implication table is a second set of rules to keep true, and writing two names in a grant is cheaper than remembering which one carries which. In particular `mailfathom.mail.ask` does not imply `mailfathom.mail.read` and is not the weaker of the two: a cited answer returns mail content, so the documentation states that granting it is granting access to mail.

**A name is published when the capability exists.** `mailfathom.mail.send` is not in the table above and is not reserved here; issue 745 allocates it under this rule when there is a sending tool for it to govern. A grant that names a capability the deployment does not have is a grant that means nothing, which is the thing this record is trying to prevent.

### One vocabulary, two disjoint halves

Both surfaces draw from one published set, and the name says which surface it belongs to. A `mailfathom.admin.*` permission written on the MCP endpoint's grant, or a `mailfathom.mail.*` permission written on the administrative endpoint's, is refused at startup naming the configuration path. One set is one thing to publish, document and validate; the prefix gives the separation without a second registry, and refusing the cross-surface name turns a mistake into a startup failure rather than into a grant the operator believes they made.

### A grant belongs to the credential's configuration entry

An `Authentication[]` entry states what it grants, on both endpoints, and it always states it as `Permissions` — a list of published names, which is the ceiling on what any credential the entry admits may do. A second setting decides whether the credential itself may be granted less than that ceiling:

- **Without `PermissionsFromTokenScopes`, the grant is the ceiling.** Every credential the entry admits holds every permission it lists. This is the only form available to an entry stating an `ApiKey` or a `PublicKey` block, because neither credential can carry anything the deployment did not write.
- **With `PermissionsFromTokenScopes`, the grant is the ceiling narrowed by the token.** A token holds the published names its scopes carry *and* the entry lists, so the authorization server decides per subject within a bound the deployment fixed. Available only to an entry whose sole block is `OAuth`, so that the question is never asked of a credential that cannot answer it.

The ceiling is what keeps one uniform rule for the whole list — every entry says what it grants, in the same setting, whichever credential it admits — and it is what makes the advertised set below a finite thing rather than "whatever this deployment happens to enforce".

Startup refuses an entry whose `Permissions` is absent or empty, `PermissionsFromTokenScopes` on an entry carrying a key or a public key, and any name that is unknown or belongs to the other surface — each naming the configuration path including the entry's index, in the form every other refusal in this section already takes. Issue 584 owns the binding and the exact wording; what this record fixes is the semantics and that every one of those arrangements is a refusal rather than a default.

**The grant is a property of the entry and therefore of every credential the entry states.** An entry may carry several blocks, and until now which entry a block sat in was only a matter of how an operator grouped what they wrote. It stops being only that: two credentials that are to be granted differently are two entries. This is a change in what the existing shape means and is named in the changelog as one.

### What the deployment advertises is what a client can ask for

`scopes_supported` gains the `Permissions` of every entry that sets `PermissionsFromTokenScopes`, and nothing from an entry that does not. That follows from what the field means: it tells a client what to ask its authorization server for, and a permission the deployment grants from configuration is not something any client can ask for. It also tells an operator exactly which scopes to create in their authorization server — the union of those ceilings, read from the document rather than transcribed out of their own configuration file.

A permission name is refused in `RequiredScopes`, for the reason given above, and in `AdvertisedScopes`, because the grant that reads it already advertises it and an entry that reads none would be telling a client to ask for something nothing here grants. The composition issue 816 settled is otherwise unchanged: a required scope is advertised whether or not anything repeats it, and an advertised scope is never enforced.

### There is no default grant

A credential that names no permission is not admitted with nothing, and not admitted with everything. It is a startup refusal.

Denying everything silently would make an upgrade break at request time, where the operator learns from a client that stopped working. Granting everything would preserve today's behaviour, but it makes the safe configuration the one an operator has to go and find, and — decisively — every permission added later would silently widen every credential already configured. Refusing at startup is the only one of the three in which the upgrade is loud, happens before a request is served, and names the file and the setting. The operator's action is one line per entry, and it is written in the changelog against the configuration schema, per ADR 0004.

### The accounts a caller may reach are a second axis on the same grant

They are not permissions. A permission says what a caller may do and an account restriction says which mailbox it may do it to, and folding the second into the first — `mailfathom.mail.read` narrowed per account — would put the operator's own account aliases into a vocabulary this repository publishes.

So an entry may also carry `Accounts`, naming configured account aliases, and it restricts every permission the entry grants. It is always written in configuration and never read from a token's scopes, because an alias is the operator's name for something in their own file and has no business being minted as a scope in an authorization server.

`Accounts` absent means every configured account, now and as accounts are added. This is deliberately not the treatment `Permissions` gets, and the asymmetry is the point: a permission set is the whole of what this feature is, so leaving it unstated is leaving the feature unconfigured, while an account restriction narrows a permission the caller already holds and its absence is the posture every deployment has today. The cost is that configuring a second mailbox widens every unrestricted credential, which is what the deployment already does and what the documentation says on the page describing the setting.

### The transport refuses cheaply, and the use case is the authority

Both checks exist and they are not the same check.

- **At the transport**, authorization runs where `TransportAccessPolicy` runs today, behind the rate limiter, so the partitions and the existing refusals are unchanged. It is what keeps the tool listing honest and what refuses a call without reaching a use case.
- **In the use case**, the check is the authority. An entrypoint added later — a rule action, a worker, a command, a second protocol — reaches the use case without passing the transport, and a check that lived only in middleware is one the new entrypoint forgets. A use case never relies on the transport having already asked.

What the application layer receives is an application-owned contract describing the caller: the configured identity it was admitted under, the permissions it holds, and the accounts it may reach. Nothing from `System.Security.Claims`, ASP.NET Core, or the MCP SDK crosses that boundary, and no use case learns which credential admitted the caller. A host adapter populates it per request scope.

**Work no caller requested runs under the process's own identity**, which is a distinct kind of principal rather than a caller holding everything. A use case that may run without a caller admits the process identity by name; it must never be admitted by holding a permission, because a principal that passes an ordinary permission check is a caller with everything granted wearing a different label. A use case reached with neither a caller nor the process identity fails rather than defaulting to permitted.

### What a refused caller is told depends on who is reading it

**On the MCP surface, nothing.** A tool the caller's grant does not permit is absent from `tools/list`, which the protocol expressly allows and which is the only place the decision can be stated, since a descriptor has no field for it. No `_meta` extension is invented to say it instead: no client reads one, and a test asserting it would be asserting our own extension. A call naming a tool the caller may not reach is answered exactly as a call naming a tool that does not exist — the same shape, the same code, no new failure identity — because from the caller's side the two are the same fact and a distinguishable refusal would disclose a capability the deployment declined to offer it.

This composes with the availability rule `AskMailAdvertisement` already applies rather than replacing it. A tool may be unavailable, unauthorized, or both; the deployment's own switch is evaluated first, and no grant makes a capability the deployment does not have appear.

A listing that varies by caller may not be published in a way that lets one caller's listing serve another, so any cache scope or shared listing cache the protocol offers is per credential or absent.

Two consequences follow and are accepted rather than worked around. A caller cannot discover that it lacks a permission, which is what the operator reads the refusal metric and log of issue 589 for. And no `insufficient_scope` step-up challenge is issued for a missing permission, even where the grant comes from token scopes and the client could in principle acquire it: the listing already told the client what it may do, and a mechanism available only to the credential kind that can step up would make the surface's behaviour depend on which credential admitted the caller — the drift the shared access policy exists to prevent. The challenge keeps naming `RequiredScopes`, because admission is unchanged.

This settles issue 586's acceptance differently from the way that issue words it. That issue asks for a coded boundary failure naming the permission that would have sufficed; the answer here is the unknown-tool answer and no new code, and the operator learns the permission from the record rather than from the response.

**On the administrative surface, the permission that would have sufficed.** The caller there is `mfctl` in the operator's own hands, so the refusal uses the endpoint's existing safe failure shape and names the one permission, and nothing else: no route inventory, no other credential, nothing about the deployment's configuration. `mfctl` reports it in an operator's terms.

On both surfaces, a request naming an account outside the grant is answered indistinguishably from one naming an account the deployment does not configure. An answer never discloses that an account exists.

### What this model is not

No user directory, no group or tenant claim mapping, no delegation or impersonation, no policy language, no per-message rules, and no permission that varies with the content of a request. A later issue that wants one of these is proposing a decision this record did not take, and says so.

### Consequences

- Good, because every name an operator can write corresponds to a check that exists, and a name that does not is a startup failure rather than a grant nobody enforces.
- Good, because the three credential kinds are authorized on terms each can actually carry, and a configured credential stops being unbounded by construction.
- Good, because the listing decision has protocol sanction and the tool descriptor is left as the protocol defines it, so nothing here has to be unwound when the Tool Scopes working group settles.
- Good, because the use-case check makes an entrypoint added later safe by default rather than safe by the author remembering.
- Neutral, because one closed vocabulary spanning both surfaces is one register to publish and validate, at the cost of a startup refusal for a name written on the wrong surface.
- Neutral, because the grant moving to the entry makes an existing grouping convenience meaningful; an operator whose entries grouped several blocks for tidiness splits them when the grants differ.
- Bad, because no deployment upgrades without editing its configuration: every `Authentication[]` entry on both endpoints gains a grant, and startup refuses until it has one. This is a configuration-schema break under ADR 0004 and the changelog carries the operator's action.
- Bad, because an operator whose authorization server cannot mint custom scopes cannot use `PermissionsFromTokenScopes`, and writes the grant in configuration instead — which means the deployment rather than the authorization server decides what each admitted subject may do.
- Bad, because a caller refused on the MCP surface learns nothing, so diagnosing a client that stopped working requires reading the deployment's own record rather than the response.
- Bad, because adding a mail account widens every credential whose entry names no `Accounts`, which is today's behaviour preserved rather than a new hazard, and is stated where the setting is documented.

## Validation

- Startup refusals are unit-tested per arrangement: an unknown name, a cross-surface name, a name repeated between `RequiredScopes` and a grant, both grant forms on one entry, neither, and `PermissionsFromTokenScopes` beside a key. Each asserts the configuration path in the message, including the entry's index.
- `Boundaries.UnitTests` keeps `System.Security.Claims`, ASP.NET Core and MCP SDK types out of `Application` and `Domain`, so the caller contract cannot acquire a transport type without failing the build.
- Per-tool and per-route tests cover the listing with and without each grant, the call refused as an unknown tool, the administrative refusal naming its permission, and a use case refusing an unauthorized principal directly, with the transport absent.
- The refusal metric and log of issue 589 are what an operator observes the boundary through, and the redaction contract is asserted over them beside the rest of the telemetry surface.
- `docs/operations/configuration-reference.md`, `docs/operations/mcp-endpoint.md`, `docs/operations/admin-endpoint.md`, `docs/features/mcp-tools.md` and `docs/operations/mcp-client-oauth.md` state the vocabulary, the grant, the default that is not one, and what each surface tells a refused caller.

## Pros and Cons of the Options

### A closed set of named permissions this repository publishes

A permission is a name MailFathom defines and enforces. A configured credential is granted a list of them; a token either carries them as scopes or is granted a list beside its entry.

- Good, because a name and a check are introduced together, so the set cannot contain a grant that does nothing.
- Good, because it covers all three credential kinds without asking a configured key for a scope it can never hold.
- Good, because the names are a contract that can be documented once, validated at startup, and advertised to a client that has to ask an authorization server for them.
- Neutral, because it makes this repository responsible for a vocabulary an operator has to create in their own authorization server before a token can carry it.
- Bad, because adding a capability means allocating a name in a published set, which is a contract change rather than a local decision.

### Roles: a few named bundles an entry selects

An entry names one of `reader`, `operator`, `administrator`, and the bundle's contents are decided here.

- Good, because it is the smallest thing to configure and the easiest to explain.
- Neutral, because the initial contents would look much like the table above, grouped.
- Bad, because a bundle's meaning drifts: every capability added later joins some role, silently widening every credential that holds it — the same defect as a permissive default, arriving one release at a time.
- Bad, because the separations that matter here cut across any small set of bundles. Reading state and reading mail-derived history, or causing work and starting a bill, land in the same role unless the roles multiply until they are permissions with a worse name.

### An operator's own vocabulary, mapped onto operations in configuration

The operator invents names and writes a mapping from each name onto the operations it covers.

- Good, because it fits an authorization server whose scope names are already fixed by something else.
- Bad, because the mapping is a second configuration surface with its own errors, and it is exactly where a typo produces a grant nobody enforces.
- Bad, because nothing can be published: `scopes_supported` cannot advertise names the deployment invents per installation, and no documentation can state what a permission means.
- Bad, because a refusal cannot name a permission usefully when the name is per deployment.

### Authorize tokens only, and leave a configured credential unbounded

Keep today's reading — a configured credential's authorization is the act of configuring it — and build permissions only for OAuth.

- Good, because it changes nothing for existing deployments and needs no configuration break.
- Neutral, because it would satisfy the deployments whose clients all authenticate with tokens.
- Bad, because it leaves every key-authenticated caller where it is, and the administrative surface is where that costs most: an API key is what `mfctl` signs in with wherever no `OAuth` entry is configured, so the surface that stores mailbox credentials, starts bills and erases mail would go on granting each of those to any key that can read the session route.
- Bad, because it makes what a credential may do depend on how it authenticates, which is the drift `TransportAccessPolicy` was written as one shared judgement to avoid.

## More Information

- Issue 590 is the parent this record gates. Issue 584 writes the grant in configuration, 585 carries the caller into the application layer, 586 and 587 enforce on the two surfaces, 588 restricts the accounts, and 589 records the refusals. Issue 745 allocates `mailfathom.mail.send` when there is a sending tool.
- Issue 816 separated the advertised scopes from the required ones and shipped before this record, which is what leaves `scopes_supported` free to carry a permission a token may bring without that permission becoming a condition of admission.
- ADR 0003 governs the failure identities the children allocate; ADR 0004 governs which surfaces a `0.y.z` minor may break and what has to be written down when one does.
- Section 13.5 of `specs/2026-07-22-mail-fathom-architecture-draft.md` is superseded on per-tool scope advertisement, per section 2 of that draft.
- The protocol facts above were read from the current MCP draft specification on 2026-08-15: the `Tool` and `ToolAnnotations` field lists, and the statement that a returned tool set may vary by the authorization presented on the request.
- Revisit when the Tool Scopes working group settles per-tool scope advertisement in the protocol, or when a deployment serves more than one owner's mail — the second would reopen the accounts axis and everything this record says about a single owner.
