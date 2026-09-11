# Drafting a reply

<!-- describes: backend/src/Application/Emails/ReplyDrafts/**, backend/src/AI/ReplyDrafts/**, backend/src/Infrastructure/Persistence/ReplyDrafts/**, backend/src/Host/Configuration/Chat/ReplyDraftingOptions.cs, backend/src/Host/Api/ClientReplyDraftingEndpoint.cs -->

Answering a long correspondence begins with the blank composer, and most of the work in front of it is recovering what
was already said: what the other side asked, what was agreed three messages ago, who else is on the exchange, and how
this account normally writes to these people. **A drafted reply is that recovery written out as a first version** — the
text, what it asserts and what backs each assertion, and the people it proposes to reach — so somebody edits a draft
instead of reconstructing an exchange.

**It produces a local artifact and takes no act.** Nothing here sends, queues, appends to a mail server, or stores
anything at all: the reply arrives as text in the composer, and saving it as a draft or sending it stays where those
acts already are — [Mail delivery § A message that is written and not sent](mail-delivery.md#a-message-that-is-written-and-not-sent)
is the half that writes to a mailbox. That is what makes a wrong draft cost an edit rather than a retraction.

## What a draft carries

| Part | What it holds |
|---|---|
| **Body** | The reply as plain text, at most 8 000 characters. |
| **Claims** | What the reply asserts, at most twelve, in the order it was written. |
| **Proposed recipients** | The people the draft suggests it goes to, at most ten, every one of them named by the conversation itself. |

A **claim** is one sentence of the reply plus the messages backing it — at most four, best first, spelled as the
presentation plan spells a citation target, so following one is the same
[`POST /api/client/citations/resolution`](../operations/client-endpoint.md#the-citation-route) a Discover answer's
sources are followed through.

**A claim the correspondence does not back is kept and marked, never dropped.** That is the opposite of the rule
[a conversation's state](thread-state.md#what-a-statement-is) follows, and for the opposite reason: a statement nobody
supports has no business standing beside a correspondence, while an unsupported sentence *is already in the body*
somebody is about to send. Removing it from the list would leave the sentence there and the warning nowhere. So
`supported` is published beside the sources rather than left to a client to derive from their count.

**The proposed recipients are a proposal in the strict sense.** They are resolved inside this deployment out of the
conversation's own people, so a draft can propose nobody the exchange did not already carry, and the account's own
sending address is never among them. A client draws every one of them for a person to accept, change, or remove, and
adds none on its own.

## What it is written from

One read of the store resolves everything, out of the single message the request names:

- **The conversation**, at most the twenty most recent messages, at most 4 000 characters of each — the bound
  [a conversation's state](thread-state.md) reads a message under, because it is the same text: what a message added,
  with the history it quoted trimmed off. A reply answers where an exchange got to, so the recent end of it is what
  grounds a draft; the opening of a long exchange is what its derived state is for.
- **The way the account writes**, at most six of its own recent sent messages, at most 1 200 characters of each, taken
  from the folder playing the `Sent` role and never from the conversation being answered. Shorter than a message of the
  exchange because what is read out of it is a manner rather than a meaning — how somebody opens, how they sign off,
  whether they write in paragraphs or in lines. An operator who turned that derivation off leaves the bound at zero and
  the draft is written from the conversation alone.
- **What the person typed**, which is optional and is two separate things: a *selection*, the part of the
  correspondence they are actually answering, at most 4 000 characters; and an *instruction*, what they want said, at
  most 1 000 characters. Somebody writing more than that is writing the reply, which is the work they asked to be
  spared. A request carrying neither is the ordinary one — *reply to this*.

The correspondence is read across every account this user holds, with no folder narrowing and junk included, which is
the scope the conversation screen itself reads under: a reply is answered across an exchange rather than inside a
folder. A message that scope does not admit is a message this deployment has none of, and the request is answered as
one naming a message nobody holds.

**A request naming no message is the composer with nothing behind it**, which is somebody starting a message rather
than answering one. There is then no conversation to read and no manner to derive, so nothing above is read at all: the
instruction is the whole of what the message is written from, which is why it is required there and optional beside a
message. The draft cites nothing and proposes nobody, because the exchange those would come out of does not exist.

**The language is the correspondence's, and the person's where there is none.** A reply is written in the language the
exchange it answers is written in, because that is what the person receiving it reads — nothing here asks which
language the *author* prefers for a message going to somebody else. A message answering none has no exchange to read
that from, so it is written in the language this deployment recorded for the user. An instruction asking for a
particular language outranks both, being the one thing the person said about the message themselves.

## What reaches the provider, and what does not

The drafting is composed like every other AI operation here — one agent, its own instruction, **no tools, and no
retrieval** — so the only mail a model sees is the mail this turn published to it.

**No address ever leaves the deployment.** The conversation's people are published as numbered positions with a display
name and nothing else, and the model proposes recipients by naming positions back; the addresses are resolved from those
positions inside this deployment afterwards. The types the turn is composed of carry no address field at all, so a turn
carrying one does not compile. The same numbering grounds the citations: a message is published by its position, so a
claim can cite nothing the turn did not carry, and a position outside it is dropped rather than followed.

Everything that goes out is scanned at `ChatPrompt` before it is sent, and everything that comes back is scanned again
before it reaches the client, at an egress point of this path's own —
[Sensitive-content scanning](sensitive-content-scanning.md) is what each scan does and what a scanner that cannot answer
withholds. The addresses are deliberately not scanned on the way back: they are values this deployment resolved out of
its own store rather than text a producer wrote, and a redacted address is one nobody could send to.

Nothing about a draft reaches a log, a span attribute, or a telemetry event. What is recorded is counts — how many
messages grounded it, how many claims it carries, how many of those nothing backs, how many people it proposes — and
the alias of the endpoint that wrote it.

## When a draft is not written

**Nothing drafted is an answer rather than a failure**, and a client reports it as the composer somebody was already
looking at:

| What happened | What comes back |
|---|---|
| This deployment drafts no reply — no chat endpoint, or the operator turned it off | `drafted: false`, and the capability read said so before anybody pressed anything |
| The provider was unreachable, refused, or answered something unreadable | `drafted: false` |
| The conversation has no readable text stored | `drafted: false` |
| This user holds no such message, or holds no mail account at all | `404` |
| A request naming neither a message nor an instruction, which asks for a message out of nothing | `400` |
| The deployment has spent what its operator allows a provider for the period | `429` |

The spend ceiling is the one failure that travels, because falling back there would leave somebody pressing a button
the operator has already paid the last of the allowance for and being told nothing. Every drafting is admitted against
and charged to the same [`MailAnswering`](../operations/configuration-ai.md#mailanswering) period ceilings a question
is, which is what makes it bounded rather than a second, unmetered way of spending.

Asking a provider to write a reply to a conversation this deployment stored no text for is the one case worth stating
on its own: a model given nothing writes a fluent message resting on nothing at all, so the drafting answers with
nothing instead.

## Turning it on

Off by default, and off is a supported deployment: the composer offers no drafting and every reply is written by hand
exactly as before. Turning it on needs a declared chat endpoint and is a spend decision — one provider call per reply
somebody deliberately asked for.

[AI configuration § `Chat:ReplyDrafting`](../operations/configuration-ai.md#drafting-a-reply--chatreplydrafting) holds
the two keys, and [the client endpoint](../operations/client-endpoint.md#the-reply-drafting-routes) the two routes it is
published on. Both are published under `mailfathom.mail.ask` rather than under the reading grant, because that is what
drafting does: mail leaves this deployment for a chat provider.
