---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-08-11
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Author a rule in the configuration the deployment already carries, and write its condition as one NCalc expression

<!-- describes: none -->

## Context and Problem Statement

Issue 251 wants the owner of a mailbox to declare recurring handling of their own mail — file it, copy it, delete it, mark it read — without an agent session and without a prompt. Issue 453 is its gate, and it asks the one question that could not be scoped past: where a rule is kept. A rule is authored state. It is written, edited, disabled, and removed by a person, and it changes far more often than anything else in a deployment, which is what makes its home a contract rather than an implementation detail.

The question does not stand alone. What an owner types is a condition, and the syntax they type it in is as visible a surface as the file they type it into. The two are read together, break together, and are versioned together, so deciding where a rule lives while leaving the syntax to whichever issue happens to implement it would leave the more visible half of one authoring contract with no durable record at all. This record therefore settles both.

MailFathom already answers the storage question one way for everything else it holds: configuration is validated, bound, and reloaded through the layer [ADR 0002](0002-configuration-reading-mapping-and-reload-boundary.md) defines, while state is in PostgreSQL under one append-only migration chain. The decision here is whether rules join the first of those two or become a third category with its own storage, its own management surface, and its own lifecycle.

## Decision Drivers

- **An instance should stay fully described by its configuration.** A deployment is provisioned from a chart, a unit file, and a configuration file, and what it will do to a mailbox is the most consequential thing it declares. Automation that lives somewhere else means a deployment can be reproduced exactly and still behave differently.
- **A rule acting on real mail is worth reviewing before it runs.** Configuration is diffable, reviewable, and keepable in a repository; a row in a table edited through a command is none of those without machinery built for the purpose.
- **Whatever holds a rule has to answer what a rule's version is.** An execution's idempotency identity is derived from the message occurrence, the rule version, the trigger generation, and the action, and issue 458 has to explain a past run afterwards. Both need a revision to name, so the identity of a revision is part of this decision rather than downstream of it.
- **An edit has to take effect without a restart, and an invalid edit must not take effect at all.** The options infrastructure discards an invalid reloaded value silently, which for a rule set means an owner mistypes a fact name and gets an instance that keeps running the previous rules while their file says otherwise.
- **The condition has to express a genuinely complex filter in a configuration file.** A sender domain, an attachment, an age, and a folder combined with `and`, `or`, and grouping is the ordinary case, and a predicate tree spelled out in YAML is the shape people avoid writing.
- **Nothing may evaluate arbitrary authored code.** Mail is attacker-controlled input and a condition is authored text read at startup; an evaluator whose cost or reach is a property of what was typed is not acceptable at any licence.
- **A dependency's obligations reach every operator, not only this repository.** MailFathom is self-hosted and redistributed as an image, and it has to stay distributable under commercial closed-source terms beside its own open-source ones, so a licence, a transitive graph, and a target framework are all part of the choice.
- **Both surfaces are public.** The configuration schema and the expression syntax are surfaces [ADR 0004](0004-versioning-and-release-policy.md) names, so whichever shape is chosen is one the project has to own and to break deliberately when it breaks it.

## Considered Options

Where a rule lives:

1. **A configuration section**, bound and reloaded through the existing configuration layer.
2. **A rules table in MailFathom's schema**, with a management surface — `mfctl`, the administrative endpoint, or MCP — that creates, edits, and deletes rules.
3. **Configuration as a seed and the database as the live copy**, with the file importing rules an operator may then edit at runtime.

How a condition is written:

1. **One NCalc expression** evaluating to a boolean.
2. **A closed set of typed predicates** combined by `and`, `or`, and `not` in the configuration schema itself, which is what issue 454 originally proposed.
3. **CEL**, through one of its two .NET implementations, `Cel` and `Cel.NET`, both Apache-2.0.
4. **A general scripting host** — C# scripting, Lua, or a JavaScript engine.

## Decision Outcome

Chosen: **a rule set is a configuration section, and a rule's condition is a single NCalc expression that evaluates to a boolean.**

Configuration wins because every property issue 251 asks of a rule — reviewable before it runs, reproducible from a repository, carried with the deployment, versioned by something that already exists — is a property configuration has and a table has to be given. A table would have to acquire an editing surface, an authorization model, a migration, an audit trail, and a versioning scheme, all so that a rule could be changed by a command instead of by an edit; that is a management subsystem bought to avoid a text editor.

NCalc wins because the argument that ruled out a general expression language does not describe it. That argument is about unbounded evaluation, and it is correct: an evaluator whose cost is a property of the authored text has no place reading a configuration file. NCalc's grammar admits values, operators, functions, and parameters, and nothing else — no loops, no comprehensions, no recursion, no assignment — so an expression cannot be made unbounded by its own shape, and it reaches nothing except the parameters and functions MailFathom registers into its environment. The closed surface the predicate design was built to guarantee therefore survives the choice; what it is closed over moves from predicates to facts.

### The rule set is a configuration section, validated on binding

A rule set is bound like any other section and reaches the code that evaluates it as an immutable snapshot, published only once it has been proven usable — the rule the host's `ValidatedSettingsSnapshot` already applies to every reloadable group. There is no rule table, no rule entity, no migration, and no rule row for erasure or retention to reach: a rule is the owner's authored text, held where the rest of their authored text is held.

Validation belongs to binding rather than to evaluation. A condition that does not parse, a fact that does not exist, an ill-typed comparison, an action combination that names two fates for one occurrence, and a destination folder that is not configured are each refused when the configuration is read, naming the rule and what was wrong. Issue 454 builds that pass; this record fixes when it runs, which is before any mail is seen.

### A revision is the rule set as bound, identified by a digest of it

A rule's version is the configuration revision that carried it, and that revision is identified by **a digest over the bound rule set, taken in declared order, rendered as a short lowercase hexadecimal prefix**. There is no per-rule version column, no rule audit table, and no version an operator declares by hand, because nothing edits a rule except editing the file.

Deriving the identity rather than declaring one is what makes it trustworthy: an authored version key can be forgotten, and an edit that changes the rules without changing the key is exactly the case a history has to be able to distinguish. A publication sequence number was the other candidate and is rejected for the opposite reason — it is process-local, so it restarts at zero and means something different on every replica, while two instances reading the same file must name the same revision.

The digest is taken over the rule set **after binding**, which is what keeps it from moving for the wrong reasons. A change to an unrelated configuration key does not produce a new rule revision, and neither does reformatting the file or reordering keys within one rule. Reordering the rules themselves does, because declared order is part of the contract.

Two consequences follow from the identity being a digest rather than a version number. Ordering is not readable from it, so a record that has to say which of two revisions came first carries its own timestamp and does not infer it. And the identity carries none of the authored text: a condition can legitimately contain an address the owner typed, so a run record that names a revision holds no personal data that the rule itself contributed, which a stored copy of the matched condition would.

### An edit takes effect on reload, and a run is bound to the revision it started under

A rule set is reloadable for new operations, in the classification [ADR 0002](0002-configuration-reading-mapping-and-reload-boundary.md) defines. A run reads the snapshot once when it starts and uses that instance for its duration, so a rule set edited while a mailbox is being evaluated finishes against the revision the run began with and the next run reads the new one. Applying an edit to work already in flight is the one thing the reload contract promises never happens, and a rule is not an exception to it.

What this deliberately does not do is reprocess. A rule sees mail arriving after the reload, and applying a newly written rule to mail already stored is an explicit act — the on-demand run issue 458 gives `mfctl` — rather than something an edit sets off on its own.

### An invalid reload is reported, and the previous rule set stays in effect

The default behavior of the options infrastructure is wrong here and is not used. A reloaded value that fails `IValidateOptions<T>` throws inside the change-token callback, on a thread pool thread with nowhere to report it, so the candidate disappears with no log line: the owner sees an edit that appears to take and nothing happens. For a rule set that is the worst available outcome, because the instance goes on acting on mail under rules the file no longer states.

A rule section therefore validates itself the way the reload path here already validates a candidate: an invalid candidate is refused with a message naming the rule and the defect, the last known good rule set stays in effect, and the refusal is logged rather than swallowed. A rule set that is invalid at startup is a startup failure instead, because there is no previous good set to fall back to. Issue 454 builds this; the behavior is fixed here.

### Nothing authors a rule at runtime

Not `mfctl`, not MCP, not an administrative endpoint. `mfctl` runs the rules configuration declares and reports what happened, and that is the whole of its relationship to them.

This is the price of the decision rather than a detail of it, and it is stated plainly: a user interface over rules is foreclosed by this record, not deferred by it, and so is any runtime rule management. An owner who wants to change what their instance does edits a file and the instance reloads it. Reversing that is a new ADR superseding this one, not a feature added under it.

### The condition is one expression, and what NCalc does not provide MailFathom owns

The condition is a single expression returning a boolean; a non-boolean result is a refusal when the rule is read rather than a truthiness rule. What the library gives is the part nobody should write again: infix boolean and comparison operators an owner already knows, grouping, a parser, a precedence table, and an error position.

Three guarantees the predicate design would have had from its own shape are now MailFathom's to build, and naming them is most of why this record exists:

- **Validation.** `HasErrors()` reports syntax and nothing else — not whether a parameter exists, not whether a function is registered, and not whether a comparison is well typed — and NCalc has no static type checker. So MailFathom walks the parsed expression when the configuration binds and checks every parameter and function reference against the declared fact surface and its types.
- **Totality.** `Evaluate()` throws; `NCalcEvaluationException` derives from `NCalcException`. Totality is therefore a classification MailFathom applies rather than a property of the evaluator: a condition that cannot be evaluated is a rule that *failed*, visibly and with a reason, and never a rule that silently matched or silently did not.
- **Cost.** NCalc has no cost model. The bound comes from the fact surface being closed and each fact individually bounded, from limits on expression length and nesting depth applied when the rule is read, and from an evaluation timeout.

Facts reach an expression as declared, typed parameters resolved lazily, so a fact that costs I/O costs nothing in a rule that does not name it. The function surface is closed the same way: the functions MailFathom registers plus an explicitly kept subset of the built-ins, with everything else removed from the environment rather than left reachable and undocumented. A mathematical library is not what a mail rule needs.

### The dependency and its terms

The package is `NCalc` — the asynchronous one, because fact resolution reaches I/O — MIT, at 7.1.0 targeting `net10.0` directly, with `NCalc.Core` as the only entry in its graph, and roughly 4.5 million downloads across its history. It owns no schema, opens no connection, and reads no configuration of its own.

It is not taken here. This record is documentation and adds no package: the change that first uses NCalc pins it centrally in `backend/Directory.Packages.props`, regenerates the lock files in the same change set, and records it in `THIRD_PARTY_LICENSES.md`. That is issue 454.

### Both surfaces are public, and a break is named

The rule schema and the expression syntax are two of the surfaces [ADR 0004](0004-versioning-and-release-policy.md) governs. Below `1.0.0` a minor release may break either, and no deprecation window exists — so what a change to them decides is which shape is right, never whether the break may be taken. What it costs is the record: a break is named in the changelog against the surface it breaks, with the action the owner has to perform on their own file.

### Consequences

- Good, because an instance stays fully described by its configuration, and what it will do to a mailbox is reviewable in a diff before it runs and reproducible from a repository afterwards.
- Good, because a rule's version needs no mechanism of its own. The revision is derived from the rule set that was bound, so it is identical on every replica, survives a restart, and cannot be left stale by an edit that forgot to bump it.
- Good, because nothing is added to the database. There is no rule table, so no migration, no erasure path, no retention rule, and no second copy of authored text for the privacy review to reach.
- Good, because the syntax is one an owner is likely to recognize, and the parser, the precedence table, and the error position are maintained by somebody else under MIT with one transitive package.
- Neutral, because validation moves rather than disappears. A predicate schema would have been checked by the binder; an expression is checked by a pass MailFathom writes, which is more code for the same guarantee at the same moment.
- Neutral, because reload semantics are the ones the repository already committed to. A rule set is reloadable for new operations, like every other group that reaches a running operation.
- Bad, because runtime rule management is foreclosed. There is no interface an owner can click, no way to disable a rule from `mfctl` for an hour, and no path to multi-user rule authoring without superseding this record.
- Bad, because a revision identified by a digest is not readable backwards. A history can say a run used a rule set that is no longer the current one; recovering what that rule set said is the configuration's own version control, and an owner who does not keep their configuration in one has no way back to it.
- Bad, because an expression can fail at evaluation time in a way a predicate tree could not. Validation catches an unknown fact and an ill-typed comparison, but a null a fact legitimately produces on an unusual message is discovered on live mail, which is why the failure classification above is a requirement rather than a nicety.
- Bad, because the authoring surface now has to be documented completely or it is unusable. Every fact with its type, every function, the operators, the limits, and worked examples are an obligation this choice creates, and issue 454 carries it.

## Validation

- Unit tests over the rule section require an invalid rule set to fail startup, and an invalid reloaded rule set to be refused, logged, and to leave the previously valid set serving.
- Unit tests require a rule set bound twice from the same configuration to produce the same revision identity, a change to an unrelated section to leave it unchanged, and a change to any rule or to the declared order of the rules to change it.
- Unit tests over evaluation require a throwing or timing-out condition to be classified as a failed execution with a reason, never as a match and never as a non-match, and require a non-boolean condition to be refused when the rule is read.
- Code review rejects any surface that creates, edits, or deletes a rule at runtime, in `mfctl`, in MCP, or in the administrative endpoint, and rejects a rule entity, a rule table, or a migration carrying one.
- `$check-docs-licenses` gates the change that first takes NCalc, and `THIRD_PARTY_LICENSES.md` carries it from that change onward.
- The feature page issue 454 owes documents the whole authoring surface, and the configuration reference documents the section's shape; documentation review rejects a fact or function reachable in an expression that neither page states.

## Pros and Cons of the Options

### A configuration section

- Good, because it reuses binding, validation, the reload publication contract, and the operational diagnostics that already exist, and adds no storage.
- Good, because a rule is diffable, reviewable, and reproducible, which is what makes automation over somebody's mailbox auditable without building an audit trail for it.
- Neutral, because editing requires access to the deployment's configuration, which for a self-hosted single-owner product is the same person.
- Bad, because there is no runtime authoring, and no partial or temporary change short of editing the file.

### A rules table with a management surface

- Good, because a rule could be created, edited, and disabled without touching the deployment, and a per-rule version and audit trail would follow naturally from rows.
- Neutral, because the schema itself is small; the table is not the cost.
- Bad, because the surface around it is not. It needs an editing API, an authorization model, validation on write as well as on read, a migration, and history — a configuration-management subsystem acquired to avoid a text editor.
- Bad, because the instance stops being described by its configuration. Two deployments provisioned identically behave differently, and reproducing one means reproducing its database.
- Bad, because a rule row is authored text pointing at mail, so erasure, retention, and export all gain an obligation that configuration does not carry.

### Configuration as a seed, the database as the live copy

- Good, because it appears to offer both: rules reviewable in a repository and editable at runtime.
- Bad, because it offers neither reliably. The file and the table diverge on the first runtime edit, and every subsequent question — which wins on restart, what an import does to an edited rule, what a diff of the file now means — has to be answered.
- Bad, because it carries the whole cost of the table option plus a synchronization contract on top of it.

### One NCalc expression

- Good, because a complex filter is writable in a configuration file with operators and grouping an owner already knows, and the parser, precedence, and error position are not MailFathom's to maintain.
- Good, because the grammar admits no loops, comprehensions, recursion, or assignment, and an expression reaches only the parameters and functions registered into its environment, so both cost and reach stay properties of what MailFathom declares rather than of what was typed.
- Good, because the package is MIT, targets `net10.0`, and pulls exactly one transitive package, so nothing about it reaches an operator who runs the image.
- Neutral, because its mathematical and string library is larger than a mail rule needs; the unneeded parts are removed from the environment rather than documented as available.
- Bad, because it has no static type checker and `HasErrors()` covers syntax only, so authoring-time validation is a pass MailFathom writes and maintains against its own fact surface.
- Bad, because `Evaluate()` throws rather than returning a failure, so totality is a classification applied around the evaluator instead of a guarantee from it.

### A closed set of typed predicates in the configuration schema

- Good, because validation, totality, and cost would all follow from the schema, checked by the binder with no expression walk to write.
- Neutral, because the fact surface is closed either way; this option closes over predicates instead of facts.
- Bad, because a compound condition becomes a nested structure in YAML. The combination issue 251 describes as ordinary — a domain, an attachment, and an age — is several levels deep before it says anything, and is the shape people give up writing.
- Bad, because every new combining shape is a schema change and a release. Grouping, negation over a group, and comparison against another fact are each features here and are each free in an expression language.

### CEL

- Good, because the language is designed for exactly this position — a non-Turing-complete expression evaluated against a declared environment — and it is specified, so the syntax has a definition outside any one implementation.
- Neutral, because both .NET implementations are Apache-2.0, so nothing about the licence is an obstacle.
- Bad, because neither targets `net10.0`, and both bring a graph out of proportion to the need. `Cel` pulls the ANTLR runtime, `Google.Protobuf`, and `TimeZoneConverter`; `Cel.NET` pulls the ANTLR runtime and its build tasks, `Google.Protobuf`, `Grpc.Net.Client`, `Apache.Avro`, `Newtonsoft.Json`, and `NodaTime`. Every one of those is a register entry, a supply-chain surface, and a version to track, for a boolean over a dozen facts.
- Bad, because adoption is thin next to the need. `Cel` is at 0.3.3 and `Cel.NET` is a port of CEL-Java; neither is a component this repository would want to be the one that finds a bug in.
- Bad, because the protobuf-shaped type system is the wrong impedance for a fact surface that is a handful of strings, timestamps, sizes, and booleans.

### A general scripting host

- Good, because anything an owner might want to express is expressible.
- Bad, because that is the objection. A scripting host evaluates authored code with a cost and a reach that are properties of what was typed, which is precisely what a configuration file read at startup must not contain.
- Bad, because sandboxing one is a security commitment MailFathom would be making on every operator's behalf, permanently, for a filter.

## More Information

- Issue 453 records the decision and its consequences; issue 251 is the feature, and issues 454 to 458 build the fact surface and validation, the evaluation step in the account synchronization run, the action set over the mutation record, and the `mfctl` on-demand run with its history.
- An execution's idempotency identity is the message occurrence, the rule version, the trigger generation, and the action, and rule ordering, stop-or-continue behavior, and conflict resolution are each required to be deterministic and reviewable without invoking a model.
- [ADR 0002](0002-configuration-reading-mapping-and-reload-boundary.md) defines the binding, mapping, and reload classification this section takes, and its last-known-good rule is what an invalid rule set falls back to. [ADR 0004](0004-versioning-and-release-policy.md) is why the schema and the syntax are breakable in a minor and why a break is written down. [ADR 0007](0007-remote-mailbox-mutation-boundary-and-write-session.md) is why a rule's actions reach the mailbox through a write session a read path cannot obtain. [ADR 0009](0009-durable-job-store-and-execution-identity.md) records the job store that this feature deliberately does not use.
- NCalc's documentation is at <https://ncalc.github.io/ncalc/> and its repository at <https://github.com/ncalc/ncalc>; `NCalcException` and its derived evaluation exception are documented at <https://ncalc.github.io/ncalc/api/NCalc.Exceptions.NCalcException.html>. The package, its MIT licence, its `net10.0` target, and its single `NCalc.Core` dependency are at <https://www.nuget.org/packages/NCalc>.
- The CEL specification is at <https://github.com/google/cel-spec>; the two .NET implementations weighed are <https://www.nuget.org/packages/Cel> and <https://www.nuget.org/packages/Cel.NET>.
- The `describes:` marker names nothing because none of this code exists yet. It gains its paths when issue 454 lands the rule section and its validation, which is one of the two edits an accepted ADR is permitted.
- Revisit when an owner genuinely needs to change a rule without changing their configuration — a temporary disablement during an incident is the plausible case — or when the fact surface grows past what a single expression reads clearly, or if a rule set ever has to be authored by somebody who is not the operator of the instance.
