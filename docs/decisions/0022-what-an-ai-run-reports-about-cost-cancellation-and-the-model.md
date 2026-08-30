---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-08-31
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Report a run's own consumption rather than a price, keep the deployment's remaining allowance private while naming when a refused period turns over, make cancelling stop the provider call and keep what arrived, and publish the answering endpoint by a name the operator chose

<!-- describes: backend/src/Application/Retrieval/AskMail/**, backend/src/AI/Orchestration/**, frontend/** -->

## Context and Problem Statement

Every question asked in Discover is a model call over retrieved mail, and both halves of it are spent: tokens against a provider's bill, and seconds of somebody's attention. The service side of that already exists and is decided. [Mail answering § What one question may spend](../features/mail-answering.md#what-one-question-may-spend) bounds a run three ways — retrieved characters, provider calls, and tokens — and bounds every run of a period two ways again, over a fixed window anchored at the Unix epoch. `MailAnsweringBudgetScope` already separates the two refusals, `MailAnsweringRunOutcome` already carries `Cancelled` as an ending distinct from `Failed`, and the audit entry already records which endpoint conducted the run.

What none of that answers is what reaches the person who asked. `MailAnsweringBudgetExhaustedException` deliberately names no ceiling, no count, and no model, because its audience is an MCP caller — a program the operator granted, which cannot act on a figure and has no business learning how much a deployment spends on somebody's mail. The client has a different audience: the mailbox owner, signed in over `/api/client` with their own credential, watching a run they started. Telling that person nothing produces exactly the failure the client exists to avoid, which is that somebody who asks a question and receives nothing, slowly, cannot tell a bounded system from a broken one.

Three of the questions underneath that are the client's and one is not. What a run reports about its cost, what stopping one means, and how a refusal reads are all decisions about what a person is told. Whether the model that answered is disclosed is a decision about trust and about the deployment: the architecture keeps a provider a configuration choice and never compiles one in, so the model is a property of a run rather than of the product, and a result from a small fast model and a result from a large one look identical on screen.

The fifth question is the one a live Case forces. A Case updates without anybody asking it to, so a cost figure attached to whoever happens to be looking would attribute somebody else's spend to them, and a figure attached to nobody would leave unattended spend invisible.

Recorded on issue [#1145](https://github.com/Krzysztof318/MailFathom/issues/1145). It implements nothing: [#1172](https://github.com/Krzysztof318/MailFathom/issues/1172) builds the service side of what this names, [#1181](https://github.com/Krzysztof318/MailFathom/issues/1181) builds the screen around it, and [#1167](https://github.com/Krzysztof318/MailFathom/issues/1167) is the parent both sit under. Under whose identity a live Case updates, and how often, stays with [#1144](https://github.com/Krzysztof318/MailFathom/issues/1144); this record settles only how what such an update spends is accounted and shown.

## Decision Drivers

- **A refusal a person cannot act on is worse than no answer.** A spend ceiling is the deployment behaving exactly as its operator configured it, and rendering that as an error produces somebody retrying three times something that will not become cheaper.
- **A number about a mailbox is a number about a person.** [Mail answering § What never reaches a log](../features/mail-answering.md#what-never-reaches-a-log) already holds that what a period consumed is published as counts to an operator's meter and to nobody else, and [ADR 0014](0014-single-tenant-multi-user-ownership-on-the-mail-account.md) means a deployment can serve several owners who never see each other's mail.
- **A figure that is routinely wrong destroys the figure that is right.** A run is a conversation whose length is the model's decision, so anything shown before it starts is a guess, and a guess people learn to ignore takes the true figure with it.
- **What the client shows must not become a second public contract by accident.** The MCP surface's answer to all of this is already decided and published; a client that needed the same shape would be changing a contract for a caller that never asked.
- **Cancelling has to be real.** A cancelled run that keeps executing while the client stops listening costs the same and merely hides the cost, which is the one outcome that would make the control a lie.
- **MailFathom never hosts a model and never holds a price list.** What a token costs is a contract between the operator and a provider, and this product has no access to it and no business guessing at it.
- **Nothing here may add a place mail content can leak.** A cost record, a progress event, and a model attribution are counts, instants, and names the operator chose, and each of them has to stay that.

## Considered Options

**A — what a run reports about cost, and in what unit:**

- A1 — money, converted from tokens against a price table the operator configures.
- A2 — the provider's own billing unit, tokens, shown raw.
- A3 — the run's own consumption against the bound it runs under, with the absolute counts beside it and no currency anywhere.
- A4 — nothing; cost is the operator's concern and the client shows only that work is happening.

**B — what cancelling a run does:**

- B1 — the client stops listening and the run finishes unobserved.
- B2 — the run is cancelled at the provider call and the retrieval, is recorded as cancelled, keeps what it published, and is charged for what it already spent.
- B3 — as B2, but the run's admission is returned to the period so cancelling costs nothing at all.

**C — how a spend ceiling refusing a run reaches the client:**

- C1 — as the failure it is on the MCP surface, carrying `57001` and a message naming nothing.
- C2 — as a state per scope, the period refusal naming the instant it turns over and the run refusal naming what the person can change, neither naming what was spent.
- C3 — as C2, and additionally naming how much of the period's allowance is left.

**D — whether the model that answered is shown:**

- D1 — nothing is shown.
- D2 — the endpoint alias the operator declared, and nothing else.
- D3 — `Chat:Model`, the name a request is routed under.
- D4 — the alias always, and beside it a model name the operator declares specifically for publication, absent by default.

**E — whether cost is per run, per Case, or per person:**

- E1 — per person, each owner carrying a running total of what they have spent.
- E2 — per run, with a Case owning the runs it caused, and no per-person total anywhere.
- E3 — per Case only, an ad-hoc question belonging to no Case reporting nothing.

## Decision Outcome

Chosen: **A3, B2, C2, D4, and E2** — a run reports what it has itself consumed against the bound it runs under, cancelling stops the provider call and keeps what arrived while still costing what it spent, a spent ceiling arrives as one of two states neither of which names a consumed amount, the answering endpoint is published by a name the operator chose for that purpose, and the run is the unit of cost with a Case owning the runs it caused.

### The unit is the run's own consumption, and never money

A run reports three counts, each against the ceiling it will be stopped by: provider calls made of the calls one run may make, tokens observed of the tokens one run may spend, and retrieved characters sent of the characters one run may draw out of the mailbox. Those are the three ceilings the service already applies, so the figure a person reads is the same quantity that will stop the run rather than a second measure invented for display. The retrieval count carries the number of messages those characters came from beside it, because a character total is the bound and a message count is the thing a person has an intuition about.

**Nothing is converted to currency, here or anywhere else in the client.** MailFathom holds no price list, a price is per-contract and moves without telling anybody, and a wrong number denominated in money is a claim about somebody's bill. An operator who wants money computes it from the tokens their own meter already publishes, against the contract only they have.

**The figure is a floor rather than a bill, and says so.** Tokens are counted from what a provider reported, so a call that was abandoned mid-flight — by a cancellation, by a timeout, by a dropped connection — reports no usage and advances the count by nothing, while the provider may still bill it. The client shows what MailFathom observed and never implies it is the whole of what was spent.

### Before, during, and after are three different reports, and only one of them is a number that moves

**Before** a run there is no estimate. What is shown instead is the envelope: what one question may spend on this deployment, and which endpoint will answer it. Both are known without asking anything, and neither is a prediction. A forecast would have to guess how many times the model will ask for mail, which is the one thing about a run nothing can know in advance.

**During** a run the three counts accrue, carried on the run's own event stream rather than polled — the events [#1170](https://github.com/Krzysztof318/MailFathom/issues/1170) defines are what a progress figure rides on, and a second channel for cost would be a second thing to keep in step with the first.

**After** a run the final counts stay with the result, beside the outcome and the endpoint that produced it. They belong to the artifact rather than to the application: a cost figure in permanent chrome teaches people to watch a meter while they work, and a cost figure on the answer is legible exactly when somebody asks whether that answer was expensive.

### Cancelling stops the run, keeps what arrived, and does not refund

Cancelling cancels the run's token, which reaches the in-flight provider call and the retrieval and stops both, and no further provider call is admitted. The run ends as `MailAnsweringRunOutcome.Cancelled`, which is already a value distinct from `Failed`, so a stopped run never reads afterwards as a broken one.

**Every block that had arrived stays.** The presentation plan is marked incomplete rather than discarded, its citations stay resolvable, and nothing already shown is withdrawn — somebody who stops a run because the first block answered them keeps the first block. That is the whole reason the control is worth having.

**What was spent stays spent.** Calls already made are already charged to the run and to the period, and the run's admission is not returned to the period's run count. B3 was refused for the mechanism rather than for the principle: the allowance is taken when a question is admitted precisely so that a run in flight occupies its place, and handing it back on cancellation would let a client cycle admissions and spend the period's tokens without ever consuming its runs. Cancelling buys the remainder and never a refund, and the client says that in those words rather than letting stopping read as undoing.

### A spent ceiling is a state, and it never says how much

The two scopes `MailAnsweringBudgetScope` already separates become two states, because only one of them becomes answerable by waiting:

- **The period is spent.** The state says this deployment has spent what it allows answering to cost for now, that nothing about the question caused it, and **when the allowance returns** — the roll-over instant, which the fixed epoch-anchored window makes a function of the clock and the configured period rather than of anybody's activity. The action is re-enabled at that instant instead of offering a retry that will be refused.
- **The question is spent.** The state says the run reached what one question may cost and was stopped before an answer was written, and offers the one thing the person can change, which is asking for less. There is no instant to name, because asking the same question again reaches the same ceiling by the same route, and there is nothing to keep, because this ceiling stops the run rather than cutting its retrieval.

**Neither state names a consumed amount, and C3 is refused for that reason.** How much of the period is left is a fact about what every owner of the deployment has been asking, and on a deployment serving several people it would report one person's activity to another. The roll-over instant carries no such thing.

**Both states name the deployment rather than the person.** The ceiling is deployment-wide, so a state phrased as *you have run out* would be false wherever more than one person shares the allowance, and would leave somebody trying to be more frugal about a limit they did not reach.

The MCP surface is unchanged: `MailAnsweringBudgetExhaustedException` continues to carry `57001` and to name nothing, for exactly the reason it already gives. What this record adds is that the client-facing failure carries the scope and, for a period refusal, the roll-over instant, so the client can render the two states without inferring either.

### The endpoint is published by a name the operator chose to publish

The client shows the endpoint alias with every run, and beside it a model name the operator declares for publication and which is absent unless they do. `Chat:Alias` is already the name every log line, metric tag, and failure message calls the endpoint, and the audit entry already records it, so it is the identity the deployment already has for its answering endpoint.

**`Chat:Model` is not that identity and is not published.** The configuration reference is explicit that it is what a request is routed under, which for a cloud deployment is the operator's own resource name rather than the vendor's model identifier — a value that can carry a tenant, a project, or an environment in it. Publishing it to every signed-in client would disclose deployment topology to answer a question about model quality, which is the wrong trade in both directions.

So a second key on the `Chat` section carries what may be published — the vendor's identifier as the operator wishes it stated, `gpt-4o` while the request routes to `prod-eu-4o-2` — and empty publishes nothing beyond the alias. Disclosure is declared rather than inferred, which is the same shape this project uses wherever a deployment property reaches a client.

The attribution is **per run, never per block.** A declaration reload landing mid-question changes the next question and not that one, so every block of one run came from one endpoint under one declaration, and a per-block attribution would repeat one fact as many times as the plan has parts.

### The run is the unit of cost; a Case owns the runs it caused

Every charge belongs to exactly one run, and every run belongs to exactly one cause: a question somebody asked, or an update a live Case ran. A Case's history therefore shows the runs it ran and what each of them consumed, which is what makes unattended spend legible without attaching it to whoever opened the Case afterwards.

**There is no per-person total, because there is no per-person ceiling.** E1 was refused on that ground: a running total shown per owner states a bound that does not exist, and the first thing somebody does with such a figure is ask to be given more, which is a per-person allowance this record does not create and [#1145](https://github.com/Krzysztof318/MailFathom/issues/1145) puts out of scope. What a person sees is their own runs, and the Case histories their membership admits them to.

### Consequences

- Good, because a person watching a run sees the same three quantities that will stop it, so *is this working* and *is this about to be refused* are answered by one figure rather than by a spinner and a surprise.
- Good, because a refusal is legible without publishing anything about how much a deployment or anybody on it has spent, which keeps the client's honesty and the multi-owner privacy boundary from pulling against each other.
- Good, because cancelling is defined at the provider call rather than at the listener, so the control means what a person takes it to mean.
- Good, because the model attribution is a declaration, so a deployment discloses what its operator decided to disclose and no more.
- Neutral, because the client learns a deployment's run bounds and the length of its period, both being configuration rather than activity — which is a disclosure to a signed-in owner that the MCP surface deliberately does not make, and is stated here rather than left to be noticed.
- Neutral, because a live Case's cost is visible to the Case's members and to nobody else, which follows the Case's own membership rather than adding a rule about cost.
- Bad, because the figure is a floor: a call abandoned in flight may be billed by the provider and counted here as nothing, so a person reading the number after a cancellation is reading less than they were charged.
- Bad, because two owners share one allowance and one of them can exhaust it for the other, and this record makes that legible without fixing it — a per-owner ceiling is work neither this decision nor its issue carries.
- Bad, because the second model key is one more thing an operator has to set to get a useful answer on screen, and a deployment that leaves it empty shows an alias that may mean nothing to the person reading it.

## Validation

By review of the changes that implement it, against the acceptance of [#1172](https://github.com/Krzysztof318/MailFathom/issues/1172) and [#1181](https://github.com/Krzysztof318/MailFathom/issues/1181), and by the tests those carry: that a cancelled run records `Cancelled` and makes no further provider call, that a period refusal carries its roll-over instant and a run refusal does not, that no client-facing cost payload carries a consumed period total, and that the MCP surface's refusal message is unchanged. The rule that no cost or progress payload carries mail content, a query, or an address is held by the same tests that hold it for the audit entry and the span today.

## Pros and Cons of the Options

### A1 — money, from a price table the operator configures

- Good, because money is the unit a person acts on without being taught anything.
- Bad, because the table is a copy of a contract MailFathom cannot see, and it is wrong from the first price change nobody mirrors into it.
- Bad, because it makes this product a party to somebody's billing, which is a claim it cannot stand behind for a cost it did not incur.

### A2 — raw tokens

- Good, because it is exactly the unit a provider bills in, so an operator can reconcile it.
- Neutral, because it is already what the meter publishes for the operator.
- Bad, because a token count without its ceiling tells the person nothing about whether they can ask for more, which is the only decision they have.

### A4 — show nothing but progress

- Good, because it publishes nothing and cannot be wrong.
- Bad, because it leaves the refusal states as the first time anybody hears about cost, which is the failure this record exists to prevent.

### B1 — stop listening and let the run finish

- Good, because it needs nothing on the service side.
- Bad, because it costs exactly what not cancelling costs, so the control is a lie told with a button.

### B3 — return the run's admission on cancellation

- Good, because it makes stopping free, which is what somebody would guess it does.
- Bad, because the admission is what keeps concurrent questions from all believing they are first, and returning it turns the run ceiling into something a client can cycle while spending the token ceiling underneath it.

### C1 — the MCP failure, rendered as it is

- Good, because there is one behaviour on both surfaces and nothing new to keep in step.
- Bad, because a message naming nothing is precisely what a person cannot act on, and it arrives looking like a fault in the deployment.

### C3 — name the remaining allowance

- Good, because a person could see a refusal coming rather than meeting it.
- Bad, because on a deployment serving several owners the remaining allowance is a report of what the others have been doing.

### D1 — show nothing

- Good, because it discloses nothing about the deployment.
- Bad, because two answers of very different quality are then indistinguishable, which is a trust question rather than a curiosity.

### D2 — the alias alone

- Good, because it publishes only a name the operator already chose and already sees in their own logs.
- Bad, because a deployment declaring one endpoint gives every run the same name, which distinguishes nothing and answers the question in form only.

### D3 — publish `Chat:Model`

- Good, because it is the most specific thing the deployment knows about what answered.
- Bad, because it is a routing name rather than a model identifier, and it can carry a tenant or an environment out to every signed-in client.

### E1 — a per-person total

- Good, because it makes each person's own spend visible to them.
- Bad, because it implies a per-person ceiling the service does not have, and the obvious next request is to raise one that does not exist.

### E3 — per Case only

- Good, because it puts cost where unattended spend happens.
- Bad, because an ordinary question belongs to no Case and would report nothing, which is the common case reporting least.

## More Information

- [Mail answering § What one question may spend](../features/mail-answering.md#what-one-question-may-spend) and [§ And a ceiling over every run of a period](../features/mail-answering.md#and-a-ceiling-over-every-run-of-a-period) hold the ceilings this record reports against, including why the window is fixed and anchored at the epoch and why the ledger is process-local.
- [The presentation plan](../features/presentation-plan.md) is the contract a run's blocks arrive in, and it defers cost, cancellation, and a run's events to this record.
- [ADR 0011](0011-reaching-a-provider-outside-the-openai-wire-protocol.md) is why a model is a configuration choice rather than a compiled-in one, which is what makes the attribution question exist at all.
- [ADR 0014](0014-single-tenant-multi-user-ownership-on-the-mail-account.md) is why the remaining allowance is somebody else's activity.
- [#1144](https://github.com/Krzysztof318/MailFathom/issues/1144) settles under whose identity a live Case updates and how often; this record settles only how what it spends is accounted and shown.
- Revisit if a per-owner spend ceiling is introduced, at which point a per-person figure would state a real bound rather than an imagined one, or if a deployment gains more than one declared chat endpoint per run, at which point the per-run attribution above would need to say which of them produced which part.
