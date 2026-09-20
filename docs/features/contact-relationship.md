# Where a correspondence with one person stands

<!-- describes: backend/src/Application/Contacts/Relationship/**, backend/src/AI/ContactRelationships/**, backend/src/Host/Configuration/Chat/ContactRelationshipOptions.cs, backend/src/Host/Api/ClientContactRelationshipEndpoint.cs -->

Opening a contact answers who somebody is and, beside it,
[what the mail already holds about them](contacts.md#what-mail-already-holds-about-a-contact) — the conversations naming
them and the documents they sent. Both are lists, and reading a list is the work. **A relationship card is that list
read into a few sentences**: what the correspondence amounts to, one thing worth doing next, and a handful of
observations under labels this deployment chose, each leading back to the conversation or the document it rests on.

**Nothing here runs unless a person opened that contact.** There is no sweep over the address book, no schedule, and no
background pass: one bounded run, scoped to one person, when somebody asks for that person. A contact nobody opened
costs nothing at all, which is the whole answer to what runs unattended on this surface — nothing does.

## What a card carries

| Part | What it holds |
|---|---|
| **Note** | What this correspondence is and where it has got to, two or three sentences, at most 400 characters. |
| **Next action** | One concrete thing to do next, where the correspondence suggests one. |
| **Observations** | At most one value under each of three labels this deployment names. |

The three labels are fixed and are MailFathom's rather than a model's, so a card cannot grow a heading nobody designed:

| Aspect | What it answers |
|---|---|
| `ActivePeriod` | When this person's messages actually arrive, across the day or the week. |
| `OpenItem` | What the exchange appears to leave outstanding, on either side. |
| `Case` | Which matters the conversations are about, read across them rather than out of one. |

**Every statement cites what it rests on, and one that cites nothing is dropped.** A card is read *instead of* the
correspondence it was derived from — that is what makes it worth drawing — so a sentence with nothing behind it would
sit beside the sourced ones looking exactly like them. It is the rule
[a conversation's state](thread-state.md#what-a-statement-is) follows, and the opposite of
[a drafted reply's](reply-drafting.md#what-a-draft-carries), where an unsupported claim is the thing its author has to
see before they send. **The note is what the rest is drawn around**, so a run whose note falls away is a contact drawn
with no card however much survived beside it.

A citation is at most four sources, best first, spelled the way every other citation in this API is: a conversation as
the message it was last carried by, a document as that message's own attachment. Following one is the same
[`POST /api/client/citations/resolution`](../operations/client-endpoint.md#the-citation-route) a Discover answer's
sources are followed through.

## What it is derived from

**The correlation and nothing else.** One run is scoped to exactly what
[an opened contact was already answered with](contacts.md#what-mail-already-holds-about-a-contact): each conversation's
subject and when that person last wrote in it, and each document's file name, declared type, and when it arrived —
under that correlation's own window, its own bounds, and the scope the caller may read. A correspondence the caller may
not read is one this cannot derive from, rather than one it filters afterwards.

**It carries no message text at all**, and the instruction says so in those words. Subjects, file names, and instants
ground what an exchange is about and when it happens; they ground nothing about what anybody wrote, agreed, promised,
or asked inside a message, and a model that filled that gap would write exactly the paragraph a reader would believe
and could not check. It is also why there is no observation about how quickly this person answers or how they write:
both need the messages, and a label drawn over an answer nothing could ground is worse than no label.

**The person is not named in it.** Neither their display name nor any of their addresses is in the turn — a note reads
the same written about *them* as about a name, and the name is the one value in an opened contact that identifies
somebody outside the exchange.

The card is written in
[the language the person who opened the contact reads](../operations/configuration-sources.md#the-language-this-person-reads--language),
which their own record names. It is not a mailbox's language and could not be: a correlation spans every account the
caller is assigned, so no single mailbox is the contact's — and the contact is that person's own, the card is composed
on their opening, and nobody else ever reads the one they were shown.

## What reaches the provider, and what does not

The derivation is composed like every other AI operation here — one agent, its own instruction, **no tools, and no
retrieval** — so the only mail a model sees is what this turn published to it. The conversations and the documents are
numbered in one list and the answer cites those numbers, so a statement can name nothing outside the correspondence
this contact was correlated with, and a number the turn never published is dropped rather than followed.

Everything that goes out is scanned at `ChatPrompt` before it is sent, and every statement that comes back is scanned
again before it reaches the client, at `client_contact_relationship` —
[Sensitive-content scanning](sensitive-content-scanning.md) is what each scan does and what a scanner that cannot
answer withholds. The citations are deliberately not scanned on the way back: they are identifiers and a position this
deployment resolved out of its own store rather than text a producer wrote, and a redacted one would lead a reader
nowhere.

Nothing about a card reaches a log, a span attribute, or a telemetry event. What is recorded is counts — how many
conversations and documents the run read, and how many observations survived — and the alias of the endpoint that
answered.

## When no card is derived

**No card is an answer rather than a failure**, and a client reports it as the contact page it was already drawing:

| What happened | What comes back |
|---|---|
| This deployment derives no card — no chat endpoint, or the operator turned it off | `derived: false`, without the book or the mail being read at all |
| The readable mail says nothing about this person | `derived: false`, without a provider being reached |
| The deployment has spent what its operator allows a provider for the period | `derived: false` |
| The provider was unreachable, refused, or answered something unreadable | `derived: false` |
| Nothing the answer wrote could be checked against the correspondence | `derived: false` |
| No book in this caller's scope holds the contact | `404` |

**A spent allowance withholds the card rather than failing the read**, which is where this parts company with
[a drafted reply](reply-drafting.md#when-a-draft-is-not-written): a draft is what somebody pressed a button for, so a
ceiling has to be said out loud, while a card arrives because a page was opened — refusing the page over it would take
a contact away rather than a card. Every derivation is admitted against and charged to the same
[`MailAnswering`](../operations/configuration-ai.md#mailanswering) period ceilings a question is, which is what makes
it bounded rather than a second, unmetered way of spending.

## Turning it off

On wherever a chat endpoint is declared, and read nowhere else: a deployment with no endpoint derives nothing whatever
the key says. Turning it off is a supported deployment and a spend decision — an opened contact then draws the record,
the conversations, and the documents, which is the page every deployment drew before this existed. What it costs where
it is on is one provider call per contact somebody deliberately opened.

[AI configuration § `Chat:ContactRelationship`](../operations/configuration-ai.md#the-relationship-card--chatcontactrelationship)
holds the key, and [the client endpoint](../operations/client-endpoint.md#the-contact-relationship-route) the route it
is published on. It is published under `mailfathom.mail.ask` rather than under the reading grant, because that is what
the derivation does: a correspondence leaves this deployment for a chat provider.
