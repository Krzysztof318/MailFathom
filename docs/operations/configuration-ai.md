# AI configuration

<!-- describes: backend/src/Host/Configuration/Embeddings/**, backend/src/Host/Configuration/Chat/**, backend/src/Host/Configuration/Answering/**, backend/src/Host/Configuration/Spam/**, backend/src/Host/Configuration/SensitiveContent/**, backend/src/Host/Configuration/Providers/**, backend/src/AI/Descriptions/**, backend/src/Application/Emails/Extraction/Images/** -->

Every key deciding what leaves this process for a model provider and what it may cost: what is scanned before it goes,
which endpoint and model each capability reaches, what one question and one period may spend, what classification does
with what it concludes, and what the two backfills work through. The tables read as
[the configuration reference](configuration-reference.md#how-to-read-the-tables) says they do, and that page is the map
to the rest of the sections.

## `SensitiveContent`

What this deployment scans mail for before that mail is copied into a derived store or handed out. A configuration root
of its own, because it is a property of the deployment rather than of its database, its accounts, or its providers, and
because the switches it holds reach several of those at once. [Sensitive-content
scanning](../features/sensitive-content-scanning.md) records what a finding is, what replaces it, and why a scanner that
cannot answer refuses the operation it guards.

Both scanners are off by default, and an absent section is that default rather than a startup failure. `Secrets` runs in
this process. `Pii` reaches an analyzer deployed beside it, configured in the block below, and switching it on with
nowhere to ask **fails startup** rather than running unprotected.

**This section is the floor rather than the whole answer.** Each owner's record carries a scanning block of its own, and
the posture their mail is read under is the stricter of the two: an owner may switch on a scanner this section left off
and add a scanner to what stops their outgoing mail, and may do neither in the other direction. The keys below the two
switches — the analyzer's address, the ceiling, the timeout, the concurrency, and the rebuild — stay wholly the
deployment's, and the concurrency is one budget every owner shares. [Each owner's own
posture](../features/sensitive-content-scanning.md#each-owners-own-posture) is the rule, and
[`Accounts:<n>:SensitiveContent`](configuration-sources.md#what-an-owner-may-say-about-scanning-their-own-mail) is where
it is written.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `SensitiveContent:Secrets:Enabled` | bool | `false` | A scanner switched on with no detector registered fails startup. It is the floor for every owner: one may switch it on for their own mail and none may switch it off | restart |
| `SensitiveContent:Secrets:Categories:<n>` | string | unset | Must name a category the scanner detects; the list replaces the scanner's defaults, and an absent list yields them | restart |
| `SensitiveContent:Secrets:Suppressions:<n>:Category` | string | — | Must name a category the scanner detects; naming one never switches it on | restart |
| `SensitiveContent:Secrets:Suppressions:<n>:Rule` | string | — | Must name a rule that category holds | restart |
| `SensitiveContent:Pii:Enabled` | bool | `false` | As above, for the personal-data scanner. An owner may switch it on for their own mail only where the analyzer address below names one | restart |
| `SensitiveContent:Pii:Categories:<n>` | string | unset | As above | restart |
| `SensitiveContent:Pii:Suppressions:<n>:Category` | string | — | As above | restart |
| `SensitiveContent:Pii:Suppressions:<n>:Rule` | string | — | As above | restart |
| `SensitiveContent:PersonalDataAnalyzer:Endpoint` | string | unset | Required once `Pii` is on, and an absolute `http` or `https` address. It is also what makes the personal-data scanner available to an owner: a record switching that scanner on where this names no address is refused at the write, naming this key | restart |
| `SensitiveContent:PersonalDataAnalyzer:Languages:<n>` | string | unset | Two lowercase letters each, naming a language the analyzer loads a model for and registers recognizers in; an absent list yields `en`. At most eight, since one scan asks once per language inside a single `ScanTimeout`. The order is not read — the set is deduplicated and ordered before use — and the set is part of the derivation stamp | restart |
| `SensitiveContent:PersonalDataAnalyzer:MinimumConfidence` | double | `0.4` | 0 – 1 inclusive, compared inclusively by the analyzer. It decides which regions are replaced, so it is part of the derivation stamp and changing it marks earlier-derived rows stale | restart |
| `SensitiveContent:MaximumAnalyzedCharacters` | int | `200000` | 1 – 10000000; text beyond it is dropped from the result rather than handed on unscanned. On the derived path that is what is *stored*, so lowering it truncates every message indexed afterwards and the value is part of the derivation stamp | restart |
| `SensitiveContent:ScanTimeout` | TimeSpan | `00:00:15` | One second to two minutes, per call to one scanner — which for the personal-data scanner covers every configured language together rather than each. A scan that misses it is refused rather than served unscanned, and on the derivation path that refusal ends the synchronization run carrying it, so a budget below what the analyzer spends on a large body leaves a folder repeating the same batch. It also bounds one personal-data readiness scrape whole, so naming more languages costs more analyzer requests and never a longer scrape | restart |
| `SensitiveContent:MaximumConcurrentScans` | int | `4` | 1 – 256, across the process and across every owner it serves | restart |
| `SensitiveContent:RebuildStaleDerivedData` | bool | `false` | Read only while a scanner is on for somebody; re-derives every message whose derived text predates its own owner's current configuration | restart |
| `SensitiveContent:ScreenOutgoingMailFor:<n>` | string | `Secrets` | Each entry names a scanner — `Secrets` or `Pii`, matched ignoring capitalization — whose findings cancel a send or a draft save. An absent key is the default; a written empty array screens nothing; a scanner named here that is switched off screens nothing. An owner may name more than this and never fewer | restart |

**Screening outgoing mail refuses acts rather than rewriting messages**, which is why what it screens for is a key of
its own rather than the scanner switches above. A credential in a message somebody is sending is what it exists for and
almost no correspondence carries one, so the default is `Secrets` and a deployment that switched that scanner on gets
it without asking. `Pii` is not in the default and adding it is a deliberate posture: ordinary mail is made of names
and addresses, so screening for personal data refuses very nearly every message a caller tries to send. A deployment
running the personal-data scanner and not the secret one therefore screens no outgoing mail until it names `Pii` here,
because the default names a scanner it has switched off. Writing
`"ScreenOutgoingMailFor": []` keeps both scanners redacting everywhere else and stops them cancelling anything.
[Outgoing mail is screened rather than
redacted](../features/sensitive-content-scanning.md#outgoing-mail-is-screened-rather-than-redacted) holds which acts it
covers, what is read, and what a caller is told.

**The rebuild switch spends a whole mailbox.** Switching a scanner on, or widening what it looks for, protects what is
derived from that moment onward and reaches nothing already extracted, chunked, or embedded — the host reports how many
messages that leaves behind every time it starts. This key is what re-derives them, and it costs one full re-indexing of
the affected messages: each is read, extracted, scanned, re-chunked, and re-embedded, so a deployment with a hosted
embedding endpoint pays that provider again for every one. It rides the extraction backfill rather than a worker of its
own, so `MailExtractionBackfill:Enabled` has to be on for it to perform anything, and
[`MailExtractionBackfill`](#mailextractionbackfill)'s interval and batch size are what pace the spend. Switch it back off
once the count reaches zero. [Derived data](../features/sensitive-content-scanning.md#derived-data-is-written-redacted-and-stamped)
records what is stamped, what makes a row stale, and why nothing rewrites stored text in place.

A category name is matched against what the scanner declares, ignoring capitalization, and the declared spelling is what
survives the match — so a placeholder in redacted text does not depend on how the name was written here. A name that
matches nothing **fails startup and quotes both the value and the categories the scanner does detect**, rather than
being dropped by the binder and leaving the section reading as protection that is on. So does a suppression naming a
rule that does not exist. A suppression inside a category this deployment does not look for is accepted and inert.

The `Secrets` scanner declares these seven categories. Six are on when `Categories` names none; the seventh is not, and
listing categories **replaces** the default set, so switching the entropy heuristic on means naming every category
wanted alongside it.

| Category | What it finds | On by default |
| --- | --- | --- |
| `ProviderToken` | An API token, key, or session credential a named service issued and prefixes as its own | yes |
| `CloudAccessKey` | An access key or client secret for a cloud platform's own control plane | yes |
| `PrivateKey` | A private key or certificate bundle, armoured as PEM or encoded whole | yes |
| `JsonWebToken` | A JSON Web Token, in its ordinary form or encoded a second time | yes |
| `ConnectionString` | A connection string carrying the credential it connects with | yes |
| `CredentialUrl` | A URL carrying a credential in its user information, its path, or its query | yes |
| `HighEntropyString` | A string dense enough to be a credential, recognised by its randomness rather than its shape | **no** |

A rule name inside them is the corpus entry's own name — `github-pat` and `aws-access-token` from the gitleaks rule
data, `AzureCosmosDBIdentifiableKey` and `UrlCredentials` from the detection engine's own corpus, and
`database-connection-uri-credential` from MailFathom's. [Sensitive-content
scanning](../features/sensitive-content-scanning.md#the-secret-scanner) records where each corpus comes from and what
the entropy heuristic costs.

The `Pii` scanner declares these eleven categories. The first five are on when `Categories` names none, and listing
categories **replaces** that set, so adding a personal name means naming the five wanted alongside it.

| Category | What it finds | On by default |
| --- | --- | --- |
| `PaymentCard` | A payment card number | yes |
| `BankAccount` | An IBAN or another bank account number | yes |
| `NationalIdentifier` | A national identification, social-security, or tax number | yes |
| `IdentityDocument` | A passport, identity-card, or driving-licence number | yes |
| `HealthIdentifier` | A number that names a person inside a health system | yes |
| `PersonName` | A personal name | **no** |
| `EmailAddress` | An email address | **no** |
| `PostalAddress` | A postal address, or a place named precisely enough to be one | **no** |
| `PhoneNumber` | A telephone number | **no** |
| `Date` | A date or a time, absolute or relative | **no** |
| `NetworkAddress` | An address that identifies a machine, whether the network assigned it or the hardware carries it | **no** |

A rule name inside them is the **analyzer's** own entity name, spelled as the analyzer spells it — `CREDIT_CARD`,
`IBAN_CODE`, `US_SSN`, `UK_NHS` — which is what lets a suppression silence one recognizer inside a category that stays
on. An operator never names one in `Categories`: those are the units this product publishes, and the mapping between the
two is MailFathom's. [The personal-data
scanner](../features/sensitive-content-scanning.md#the-personal-data-scanner) records what each of the six optional
categories costs retrieval, why the endpoint belongs inside the deployment, and what the confidence floor is protecting
against.

The analyzer block is read only while `Pii` is on. An address left behind under a scanner nobody runs is accepted and
inert, for the reason a category list under one is: it describes no protection, so refusing to start over it would be
refusing over a comment. The reverse — the scanner on with no address, a relative or non-HTTP address, a language that is
not two lowercase letters, more than eight of them, or a floor outside 0 to 1 — fails startup naming the key.

**Every code in `Languages` is one the analyzer has to have been built for**, and setting the key is only the last of the
steps that make a language work: the model, the image, the analyzer's recognizer registry, its engine, and then this key.
A code the analyzer registers nothing for leaves the deployment unready **naming that code**, rather than falling back to
English or quietly contributing nothing. Which codes are named decides which of the eleven categories above can find
anything — under `pl` alone the shipped registry knows one national identifier and no identity document at all, which is
the reason to name the languages a mixed mailbox actually carries rather than choosing between them. [The analyzer's
languages](personal-data-analyzer-languages.md) records what each language reaches and what adding one takes.

**Each language is a request, and they share one budget.** A scan states one language per call, so a deployment naming
two asks the analyzer twice over the same text, one after the other, and merges what came back; `ScanTimeout` bounds that
whole scan rather than each call, and `MaximumConcurrentScans` still counts scans rather than requests. Two languages
reporting the same value over the same span are one finding carrying the stronger score, and overlapping regions merge
into one placeholder as they already did.

**The readiness probe judges a category across the set.** A category is reachable when at least one configured language
recognises an entity of it, so adding a language never turns a ready deployment unready — while a category no configured
language reaches still refuses the start, naming the category as it does today.

The analyzed ceiling defaults to the same number as `EmailContent:MaxCharactersPerRead`, so an ordinary content read is
analyzed whole and only something pathological reaches it.

## `SpamClassification`

Whether mail is classified as spam, and where. A root of its own rather than a per-account block, because what it
switches on reaches the mailbox reads as well as the classification. [Spam classification](../features/spam-classification.md)
records what a classification holds, which facts the deterministic stage reads, and why a scanner never overturns a
provider's own verdict.

**The section is read for each owner this deployment still serves from a configuration source.** Junk is a judgement
about somebody's own mailbox, so the posture below is that owner's rather than the deployment's, and an owner whose
document has been written states it in [their own record](configuration-sources.md#one-owners-own-classification-posture)
instead — at which point this section stops reaching them. The keys the two split into are named after the table.

Every switch is off by default, and an absent section is that default rather than a startup failure. The deterministic
stage works alone and is the whole of the feature without a sidecar; `UseScanner` adds the Apache SpamAssassin daemon
described below.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `SpamClassification:Enabled` | bool | `false` | | reload |
| `SpamClassification:UseScanner` | bool | `false` | Asking for a scanner while `Enabled` is false fails startup, because a scanner is only consulted where classification runs | restart |
| `SpamClassification:ScannedFolders:<n>` | string | unset | A usable folder alias; an absent list is every account's inbox mapping, and an explicitly empty list is no folder at all | reload |
| `SpamClassification:ScannerThreshold` | double | unset | 0.1 – 1000; unset keeps the threshold the scanner itself answered with | reload |
| `SpamClassification:ClassificationWait` | TimeSpan | `00:15:00` | 1 s – 7 days; how long a stored message may wait for a verdict before it is derived from anyway | reload |
| `SpamClassification:RunBatchSize` | int | `50` | 1 – 10 000 | reload |
| `SpamClassification:MaxRunBatchesPerPass` | int | `4` | 1 – 1 000 | reload |
| `SpamClassification:Scanner:Host` | string | unset | Required once `UseScanner` is on, and a host name or IP address rather than a URL or an address with the port on it | restart |
| `SpamClassification:Scanner:Port` | int | `783` | 1 – 65535 | restart |
| `SpamClassification:Scanner:ScanTimeoutSeconds` | int | `30` | 1 – 120 | restart |
| `SpamClassification:Scanner:MaximumMessageBytes` | int | `512000` | 32 000 – 33 554 432 | restart |
| `SpamClassification:Scanner:MaximumConcurrentScans` | int | `5` | 1 – 64 | restart |
| `SpamClassification:Actions:MoveToJunkFolder` | bool | `false` | Asking for it while `Enabled` is false fails startup, and so does an account that maps no destination to file into | reload |
| `SpamClassification:Actions:MarkAsRead` | bool | `false` | Asking for it while `Enabled` is false fails startup | reload |
| `SpamClassification:Actions:JunkFolder` | string | `role:Junk` | A folder alias, or a role written as `role:<name>`; every configured account has to map it once filing is on | reload |
| `SpamClassification:Actions:Threshold` | double | unset | 0.1 – 1000; unset acts on every spam verdict, and a value judges what a scanner scored | reload |

**Eight of those keys are one owner's decision and the rest are the deployment's.** `Enabled`, `UseScanner`,
`ScannedFolders`, `ScannerThreshold`, and the four settings under `Actions` are what an owner decides about their own
mail, and are the whole of what their record may carry. `ClassificationWait`, `RunBatchSize`, `MaxRunBatchesPerPass`,
and the `Scanner` block are the deployment's, because each of them is what the process holds open or spends rather than
a judgement about anybody's mailbox — an owner record naming one is refused. The bounds a threshold is judged against,
`0.1` to `1000`, are the deployment's too and apply to an owner's value unchanged.

**An owner's `UseScanner` asks for the deployment's scanner rather than deciding that one exists.** Whether any scanner
is registered is read from this section alone, at startup, so an owner switching the key on where the deployment
registered none is neither refused nor a failed start: their mail is classified by the deterministic stage, exactly as
it would be with the key off. Everything else in the eight means the same for an owner as it does here.

`UseScanner` and the `Scanner` block are read once, at startup: whether a scanner exists at all decides what is
constructed and whether the host refuses to start without a daemon, which a reload cannot revisit. That is this
section's key; the paragraph above is what an owner's own copy of it can and cannot do. Everything else in
this section is read per classification.

`ClassificationWait` bounds the ordering rather than a scan. Wherever classification is on, a message it covers is not
chunked, embedded, or offered to the rule set until a verdict exists — and this is how long that may hold before the
message is derived from regardless, which is what stops a classifier nobody noticed was wedged from silently stopping
the index. Lengthening it delays mail of a classified folder by that much longer in the worst case; shortening it
narrows the window in which a verdict can arrive first. Zero is refused, because a wait of none releases every message
before anything could have scored it.
[Junk is kept out of what a deployment derives from mail](../features/spam-classification.md#junk-is-kept-out-of-what-a-deployment-derives-from-mail)
records what each answer means and what is counted.

**A scanner switched on with no daemon answering fails startup**, with error code `81003` naming the key to repair
rather than the address it tried. That is deliberate asymmetry with what one message gets, where a failed scan leaves
the deterministic verdict standing: an instance whose sidecar never came up would classify everything from headers alone
and look healthy doing it. The bounds are validated whether or not the scanner is switched on, so a value written wrong
is reported before the run that first switches scanning on rather than during it.

The daemon receives whole messages, so it belongs inside the deployment's own trust boundary; the feature page states
what an address outside it gives up, and what the rule-update and DNS postures cost. The deployment assets carry the
sidecar itself — [Kubernetes](deployment-kubernetes.md#spam-scanning),
[Compose](deployment-compose.md#spam-scanning), and [Quadlet](deployment-quadlet.md#spam-scanning).

The default scope follows the folder **role** rather than the text `INBOX`: it is whichever alias each of that owner's
own accounts maps to `Inbox`, so a server presenting the inbox under another name is classified without the scope being
restated here. For an owner served from this section those accounts are
[`MailSynchronization:Accounts`](configuration-mail.md#one-account--mailsynchronizationaccountsn); for an owner declared
in the top-level `Accounts` collection or read from their own record they are that owner's own `MailAccounts`. The two shapes of an unset list are deliberately
distinguishable — writing no key asks for that default, and writing an empty list asks for no folder, which switches the
work off without switching the section off.

A folder alias that this system could never have issued **fails startup and names itself**, rather than being dropped by
the binder and leaving the section reading as a scope that is covered. So does a threshold outside the range above: one
at or below zero files every message whatever a scanner answered, and one beyond the ceiling can never be reached, so
both are a typed digit rather than an intent.

The section is read per classification rather than captured, so a reload takes effect on the next one. What a reload
never does is revisit a message already classified: replacing a verdict is an explicit operation.

Which folder is left out of `list_emails` and `search_emails` is not configured here. It is the folder mapped to the
`Junk` special use in [`MailSynchronization`](configuration-mail.md#one-account--mailsynchronizationaccountsn), and it is withheld whether or
not this section switches anything on.

The `Actions` block is the only part of this section that writes to a mailbox, and both of its switches are off. Each
works alone: filing moves the message on the server, marking read sets its `\Seen` flag, and turning both on sets the
flag first, because a relocation can renumber the message. Nothing else is ever done — no delete, no other flag, no
folder created, nothing sent.

`JunkFolder` **does not have to be a folder MailFathom mirrors, and for most deployments it should not be**: mapping it
with `synchronize: false` files spam out of the instance entirely, under the account's own
`AuthoredDeleteEmailDisposition`. What it does have to be is a folder every configured account maps, because
classification asks for none to be created — an account that maps no destination **fails startup naming that account**,
rather than leaving its spam unfiled with nothing said about why. A folder the account only maps is resolved against the
server the first time a filing needs it, exactly as a rule's destination is.

`RunBatchSize` and `MaxRunBatchesPerPass` bound one pass of the [classification
run](../features/spam-classification.md#classifying-the-mail-you-already-have) an operator asks for, and neither is a
schedule: a pass is a step of the account's synchronization run, so how often one happens is that run's interval. What
these decide is how much of a mailbox one pass takes in hand — raising them walks a mailbox nobody has scored in fewer
account runs, and lowering them shortens the stretch an interrupted pass has to cover again and leaves more of each run
for the folders it exists to fetch. Both defaults are smaller than the rule pass's, because a classification reads the
stored message and, with a scanner configured, sends the whole of it across a socket and waits for a score.

`Threshold` judges what a scanner scored, in the scanner's own scale, so an operator can label at `ScannerThreshold` and
move mail only from a higher score. It reaches no other stage, exactly as `ScannerThreshold` does not: a verdict resting
on a provider's header or on where the receiving server filed the message carries no score in this scale, and is acted
on. Raising it is deliberately not the same edit as switching classification off — the verdicts go on being recorded.


## `Embeddings`

What this deployment intends to embed with. Writing nothing is a supported deployment: no vectors are produced,
semantic search is unavailable, and lexical search serves exactly as before. Declaring a chain does not start
spending — an activation does. [Embedding generation](../features/embedding-generation.md) records what a declaration
means and what it costs.

Nothing here is a switch for semantic search, and none of these keys turns it on. What a search reports as its semantic
capability follows from three facts this section does not hold: whether a profile has been activated, whether the
declaration below still names that profile's identity, and whether the last call to the endpoint chain was answered.
A search never fails because one of them is not true — it answers lexically and says which of the three states it is in.
[Email search](../features/email-search.md#what-the-three-capability-states-mean) states what each means for a caller
and what an operator does about it. Editing a key in this section and restarting therefore changes what is embedded
next, never what a search is currently able to do; only an activation does that.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `Embeddings:AllowTrimVectors` | bool | `false` | with it off, a declared dimension above 2000 is refused at startup; with it on, a wider answer is cut to the declared width and renormalized | restart |
| `Embeddings:MaxPassagesPerRequest` | int | `64` | 1 – 2048; the batch bound, applied before the provider sees a request | restart |
| `Embeddings:RequestTimeout` | TimeSpan | `00:01:00` | positive; one request to one endpoint | restart |
| `Embeddings:MaxQueuedEmails` | int | `1024` | 1 – 1000000; newly synchronized messages that may wait to be embedded at once, beyond which synchronization stops offering and the backfill reaches the rest | restart |

### What an instance is willing to spend

The five keys below bound cost rather than correctness, and they are validated whether or not a chain is declared:
passages are cut for every synchronized message on an instance that has chosen no provider, so a ceiling left
unvalidated would be one already applying. None of them is part of an embedding profile — they decide how many vectors
exist and never what one means, so moving any of them leaves every stored vector as comparable as it was. [Embedding
generation](../features/embedding-generation.md#what-an-instance-is-willing-to-spend) records what each bounds and why.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `Embeddings:MaxCharactersPerEmail` | int | `200000` | 1000 – 10000000; how much of one message's extracted text is cut into passages. A message beyond it is bounded rather than refused — its opening is embedded and the length its text had is recorded on the message | restart |
| `Embeddings:MaxRequestsPerMinute` | int | `0` | 0 – 100000; `0` paces nothing, which is the default. For a provider whose quota is stated per minute; a caller takes the next free slot and waits for it | restart |
| `Embeddings:MaxInputCharactersPerPeriod` | long | `50000000` | zero or positive; the characters one period may send a provider, counted as sent rather than as stored. `0` declares no ceiling at all, which is supported and means an enabled feature can produce a bill nobody agreed to | restart |
| `Embeddings:MaxInputCharactersPerPeriodPerOwner` | long | `0` | zero or positive; the characters one period may send for any **one** owner. `0` declares no per-owner ceiling, which is what a deployment serving one owner wants and what leaves a deployment serving several exposed to one person's backfill spending the whole window | restart |
| `Embeddings:SpendPeriod` | TimeSpan | `1.00:00:00` | 1 min – 31 days; the fixed window the ceiling is counted over, anchored at the Unix epoch so every restart places it identically | restart |

Reaching `MaxInputCharactersPerPeriod` pauses embedding until the period rolls over, and resumes without anybody
acting; nothing is dropped, because a passage with no vector is what the backfill selects on. The ceiling binds to
within one batch: a batch is admitted whenever anything at all is left and is then paid for whole, because weighing it
against what remains would stall a deployment whose ceiling is smaller than one batch for ever.

**The two ceilings answer different questions and a request has to pass both.** `MaxInputCharactersPerPeriod` bounds
the bill; `MaxInputCharactersPerPeriodPerOwner` bounds any one person's share of it, over the same window and the same
unit. Reaching the deployment's pauses the worker and ends the backfill sweep, because nothing more can be spent for
anybody. Reaching one owner's stops that owner's mail alone: the worker carries on with the next message and the
backfill steps past theirs, so everybody else keeps being embedded and their own passages wait for the roll-over.

Which of the two a refusal met is read from the log line, which names the key to raise — and raising an owner's share
answers nothing while the deployment itself has stopped spending, so the distinction is the whole point of reporting it.
The backfill's counters separate them as well, because the deployment's ceiling ends the sweep and is reported as its
outcome while an owner's is counted on `mailfathom.embedding.backfill.owner_ceiling`. The counter the worker embedding
arriving mail keeps does not: both bounds reach it as one `spend_ceiling_reached` outcome, so on that path the log is
what tells them apart.

Leaving `MaxInputCharactersPerPeriodPerOwner` unset is the default and is right for a deployment serving one owner,
whose spending the aggregate ceiling already bounds. What it exposes on a deployment serving several is one owner's
backfill consuming the whole window before anybody else's arriving mail is embedded — which is a wait rather than a
loss, and a wait that repeats every period until somebody sets a share. The embedding profile and the backfill's resume
position stay deployment-wide by decision: two owners' vectors share an index and have to mean the same thing, and one
walk over the mail visits every message whoever it belongs to.

The default is chosen to bind. Fifty million characters a day is roughly twelve million tokens and embeds something
like sixteen thousand ordinary messages, so an instance keeping up with arriving mail never meets it and one working
through a decade of archive is paced rather than surprised — raise it deliberately for an initial backfill, having seen
the number.

**Concurrency is not here.** How many provider calls may be in flight at once is
`Resilience:AiProviderInvocation:ConcurrencyLimit`, which is the one setting that owns that question; [outbound
resilience](../architecture/outbound-resilience.md) holds it, and a second limiter beside it would make two keys answer
for one behaviour.

### Describing an image attachment — `Embeddings:ImageDescription`

**Off, and off is what a deployment that has not read this gets.** With it on, an image attachment may be sent whole to
the declared chat endpoint, which writes down what the picture shows in ordinary text. A photograph of a document
discloses the document, so this is a disclosure at least as large as sending message text for embedding — and it is the
one egress in this system that **no content scan covers**, because the sensitive-content guard detects regions in a
string and there is no such operation for a picture. The operator's decision here is the whole of the control, which is
why nothing turns it on for you.

It needs a chat endpoint as well as this key. An instance that declared none describes nothing whatever this says, and
reports that as the reason rather than as a failure.

**A picture carrying words is transcribed rather than described.** Most image attachments in mail are scans,
photographed pages, screenshots, and receipts, and what somebody searching for one of those types is a number, a date,
or a name printed on it — never "a scanned invoice". So the instruction asks for the text itself, read out in full and
in order with its numbers and identifiers unchanged, and leaves the description to whatever the text does not already
say. That is the model reading what it was shown; nothing here rasterizes a page or reaches a recognition engine, so a
model that cannot read the page writes what it can see instead, and a dedicated OCR step for documents nothing else
reads stays worth having.

**What has landed is the mechanism and its bounds**: the port that turns one image attachment into text or into a
recorded reason, the allow-list, the size and grid ceilings, and this switch. Nothing offers an attachment to it yet, so
turning it on changes no behaviour today and sends nothing; what a description becomes once it is produced — a passage,
an embedding, a place in a ranked result —
is [ADR 0030](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0030-describing-an-image-attachment-in-words-and-ranking-a-depicted-match-below-a-written-one.md)
and is not delivered.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `Embeddings:ImageDescription:Enabled` | bool | `false` | with it off nothing is read and nothing is sent; with it on, and a chat endpoint declared, an image attachment's octets leave the deployment | restart |
| `Embeddings:ImageDescription:MaxPixels` | long | `40000000` | 1 – 1000000000; the largest pixel grid an image may **declare** and still be sent. A value outside the range stops the start, naming this key | restart |

**What is sent and what is refused.** The allow-list is deliberately short — PNG, JPEG, WebP, and GIF — and membership
is decided from the octets rather than from the media type the sender wrote, so a part naming one format and carrying
another is judged on what it carries. Everything else is refused with a reason recorded against the attachment: a
format outside the list, a file larger than `Chat:MaxRequestImageOctets`, a grid larger than `MaxPixels`, a header that
does not hold the format it claims, and a provider that timed out, was unavailable, or refused.

**A chat endpoint carrying no image cannot be the one describing them.** `Chat:MaxRequestImageOctets: 0` is a
supported declaration on its own — it is the right one for a model that cannot read a picture — but writing it beside
this switch stops the start naming that key, because the alternative is every picture in the mailbox being refused as
too large, which reads as a property of the pictures rather than of the endpoint.

**SVG is excluded by name rather than left unsupported.** It is XML a renderer executes as a document, with script and
external references available to whoever composed it, and nothing here is a renderer with a security team behind it. A
part declaring `image/svg+xml` is refused before its octets are read; a part declaring anything else and carrying
markup is refused when they are.

**`MaxPixels` is the decompression-bomb bound, and it is stated in pixels because a file's size does not bound one.** A
compressed image of a few kilobytes may declare a grid of billions, and what a decoder then allocates follows the grid.
MailFathom never decodes an image — it reads the header and forwards the octets — so what this protects is the provider
that does, and a grid this deployment would not have decoded is not one to make somebody else decode either. Forty
megapixels is well past any camera a person attaches a photograph from.

The port's own contract obliges whatever calls it to call it from a background step, after a message is stored, and
never from a read path — so that a tool call and a client request never wait on a provider describing a picture. Nothing
calls it yet, so that is an obligation on the caller rather than a scheduling this deployment performs.

### One endpoint — `Embeddings:Endpoints:<n>`

An ordered chain. Every entry declares the same geometry and reaches the same vector space, so a failing endpoint
falls through to the next without changing what any stored vector means; startup refuses a chain whose entries
disagree, naming both aliases and the property.

An entry declares any service reachable over the OpenAI wire protocol, not one of a fixed set: `Provider`, `Model`, and
`ModelVersion` are what the profile records, while `RoutedModelName` — or `Model` where that is empty — is the string a
request is routed on. Two rules bind the address and the credential, and one implementation applies them to this section
and to `Chat` alike: an address is absolute HTTP or HTTPS, with a plain `http` one refused wherever the endpoint holds a
credential, because the request would publish it to everything on the path; and exactly one of `ApiKey`,
`EntraCredential`, and `Unauthenticated` is declared, because none of them is what a forgotten reference looks like and
two leaves unsaid which one a request presents. [Embedding
generation § An endpoint is any
service that speaks the OpenAI wire
protocol](../features/embedding-generation.md#an-endpoint-is-any-service-that-speaks-the-openai-wire-protocol) holds
both rules with their reasons, what each setting decides, and a worked example of an endpoint that is neither OpenAI
nor Azure. [Embedding generation § A model server you run
yourself](../features/embedding-generation.md#a-model-server-you-run-yourself) covers the plain-address case, what it
gains, what it gives up, and the startup warning an instance holding such an endpoint writes. [Provider
endpoints](provider-endpoints.md) is the register of services somebody checked — what each one's `Address` and
credential are, whether it serves an embeddings route at all, and whether `SupportsRequestedDimension` may stay at its
default.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:Alias` | string | — | required, unique within the chain; what a log line, a metric tag, a resilience circuit, and a failure message call this endpoint | restart |
| `…:Provider` | string | — | required, at most 64 characters; the vendor whose model defines the space, not the endpoint it is reached at | restart |
| `…:Model` | string | — | required, at most 128 characters; the vendor's published model identifier | restart |
| `…:ModelVersion` | string | *(empty)* | at most 64 characters; empty is a vendor that versions nothing, which is the ordinary case | restart |
| `…:RoutedModelName` | string | *(empty)* | at most 128 characters; what is sent as the model of a request where that differs — a cloud deployment's own name. Empty means it equals `Model` | restart |
| `…:Dimension` | int | — | 1 – 16000, and 1 – 2000 unless `AllowTrimVectors` is on; the width the stored vectors have and the profile records | restart |
| `…:DistanceMetric` | enum | `Cosine` | `Cosine`, `InnerProduct`, `EuclideanDistance` | restart |
| `…:InputCharacterLimit` | int | `8000` | positive; what a passage is cut to before it is sent, which is what the model saw and therefore part of what a vector means | restart |
| `…:PassageInstruction` | string | *(empty)* | at most 512 characters; empty for a model that requires none. Whitespace is refused, because it would register a second profile for a space identical to one already registered | restart |
| `…:NormalizeVectors` | bool | `true` | whether the space's vectors are of unit length | restart |
| `…:Address` | string | *(empty)* | absolute HTTP or HTTPS; a plain `http` one only for an endpoint declaring `Unauthenticated`. Empty uses the provider library's default. A cloud resource's OpenAI-compatible address ends in `/openai/v1/` | restart |
| `…:SupportsRequestedDimension` | bool | `true` | whether the endpoint honours a requested width, so the narrower space is asked for rather than cut out of a wider answer | restart |
| `…:ApiKey` | secret block | *(absent)* | the provider key. Exactly one of this, `EntraCredential`, and `Unauthenticated` is declared | restart, value read per request |
| `…:Unauthenticated` | bool | `false` | that this endpoint asks for no credential, so a request presents none — the shape of a model server you run yourself. Written rather than inferred from the other two being absent, because that is what a forgotten key reference looks like | restart |

### Microsoft Entra credential — `Embeddings:Endpoints:<n>:EntraCredential`

For an endpoint where no key exists to provision. All four shapes are non-interactive by construction: MailFathom is a
background service with nobody at a keyboard, and `DefaultAzureCredential` is deliberately not used because its chain
reaches an interactive browser credential and the developer-tool credentials of whoever is signed in on the host.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `…:Kind` | enum | `ManagedIdentity` | `ManagedIdentity`, `WorkloadIdentity`, `ClientSecret`, `ClientCertificate`. `ApiKey` and `Unauthenticated` are refused here; a key is declared as one, and an endpoint needing no credential says so on the endpoint | restart |
| `…:TokenScope` | string | `https://ai.azure.com/.default` | required; the audience an access token is minted for. Declared rather than derived from the address, so a renamed service does not silently mint tokens for the wrong audience | restart |
| `…:TenantId` | string | *(empty)* | required for `ClientSecret` and `ClientCertificate` | restart |
| `…:ClientId` | string | *(empty)* | required for `ClientSecret` and `ClientCertificate`; optional for `ManagedIdentity`, where it selects a user-assigned identity | restart |
| `…:ClientSecret` | secret block | *(absent)* | required for `ClientSecret` | restart |
| `…:CertificatePath` | string | *(empty)* | required for `ClientCertificate`; a PKCS#12 file the process account can read | restart |
| `…:CertificatePassword` | secret block | *(absent)* | where the certificate file has one | restart |

## `Chat`

What this deployment generates text with. A root of its own beside `Embeddings` rather than a block inside it, because
the two are separate choices with separate consequences: without an embedding provider semantic search is off and
lexical search continues, while without a chat provider search is unaffected and only the answering capability stops
being offered. Writing nothing is a supported deployment, exactly as writing no `Embeddings` section is. [Chat
generation](../features/chat-generation.md) records what a declaration means and what one call may spend.

One endpoint rather than an ordered chain. A fallback embedding endpoint is another route to one vector space and
startup proves it; nothing proves that of two chat models, so falling through would answer a person in a different
model's voice with nothing above able to tell. An operator who wants failover puts a gateway in front of the declared
endpoint.

The endpoint is any service reachable over the OpenAI wire protocol, under the same two rules the embedding chain
follows and through the same implementation of them: an absolute HTTP or HTTPS address with a plain `http` one refused
wherever a credential is held, and exactly one of `ApiKey`, `EntraCredential`, and `Unauthenticated`. [Chat generation §
An endpoint is any service that speaks the OpenAI wire
protocol](../features/chat-generation.md#an-endpoint-is-any-service-that-speaks-the-openai-wire-protocol) carries a
worked example of one that is neither OpenAI nor Azure, and `Chat:Api` is the key most often decided by which of the
two paths such a service serves. [Provider endpoints](provider-endpoints.md) records which paths each checked service
was found to serve.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `Chat:Alias` | string | *(empty)* | writing one is what configures a chat provider at all; unique across every AI endpoint the deployment declares, embedding endpoints included. A section carrying other settings without it is refused rather than ignored | reload to rename, restart to declare or remove |
| `Chat:Model` | string | — | required once an alias is written; what a request is routed to, which for a cloud deployment is the deployment's own name rather than the vendor's model identifier | reload |
| `Chat:Address` | string | *(empty)* | absolute HTTP or HTTPS; a plain `http` one only for an endpoint declaring `Chat:Unauthenticated`. Empty uses the provider library's default. A cloud resource's OpenAI-compatible address ends in `/openai/v1/` | reload |
| `Chat:Api` | enum | `ChatCompletions` | `ChatCompletions` or `Responses`; which of the provider's two request APIs a call goes to under the declared address. Declared rather than derived, because the routed model name is the operator's own deployment name and nothing about it says which paths the server serves. State `Responses` for a reasoning model that refuses function tools beside a stated effort; a server that does not serve that path answers *request refused* | reload |
| `Chat:MaxOutputTokens` | int | `1024` | 1 – 200000; what one answer may occupy. Reaching it is not a failure — the answer arrives marked as cut short | reload |
| `Chat:Temperature` | float | *(unset)* | 0 – 2; left unset sends nothing, which is required by the models that reject the parameter outright | reload |
| `Chat:TopP` | float | *(unset)* | 0 – 1; unset the same way, and for the same reason | reload |
| `Chat:ReasoningEffort` | string | *(unset)* | the level the model documents, written as the provider spells it — `none`, `minimal`, `low`, `medium`, `high`, `xhigh`, or whatever a later model adds. Unset sends no reasoning parameter at all, which a model that does not reason requires. `none` is not the same as unset — it states an effort of none and sends it, which is what a provider refusing tools beside an unstated effort asks for. Startup checks the shape and not the vocabulary, because which levels exist belongs to the model; a level this deployment's model does not accept refuses the request rather than falling back | reload |
| `Chat:MaxMessagesPerRequest` | int | `64` | 1 – 512; the turns one request carries, refused rather than truncated | reload |
| `Chat:MaxRequestCharacters` | int | `120000` | 1 – 4000000; what those turns may add up to. Stated in characters rather than tokens because counting tokens would mean carrying the model's own tokenizer; set it below what the model's context window allows | reload |
| `Chat:MaxRequestImageOctets` | int | `4194304` | 0 – 67108864; the octets the images of one request may add up to. `0` declares an endpoint that is sent no image at all, which is right for a model that cannot read one. Providers carry an image base64-encoded, a third larger again, so set this below the figure a provider publishes for itself | reload |
| `Chat:RequestTimeout` | TimeSpan | `00:02:00` | positive; one request. Longer than an embedding request's by default, because generating an answer takes as long as the answer is | reload |
| `Chat:ApiKey` | secret block | *(absent)* | the provider key. Exactly one of this, `EntraCredential`, and `Unauthenticated` is declared | reload, value read per request |
| `Chat:Unauthenticated` | bool | `false` | that this endpoint asks for no credential, so a request presents none — the shape of a model server you run yourself. Written rather than inferred from the other two being absent, because that is what a forgotten key reference looks like | reload |

**What a reload changes here, and what it does not.** Everything the declared endpoint says is read again per
question, so correcting a model the provider refused — the ordinary case, because a wrong model is only discovered from
a refusal — costs an edit rather than a restart of a process that is synchronizing mailboxes and holding an IMAP IDLE
connection. A run already in flight keeps the declaration it began with, so a reload landing mid-question changes the
next question and not that one. A candidate that breaks any rule in the table is refused whole, logged with the key to
fix, and leaves the previous declaration answering; the process stays up either way. What stays a restart is the pair
that decides which services this deployment registered at all: whether `Chat:Alias` names an endpoint, and whether
`Chat:RelevanceFilter:Enabled` turns the second pass on. Renaming a declared alias reloads, because the credential and
the resilience circuit are both looked up by whatever the declaration in force calls it; going from no chat section to
one, or the reverse, does not, and is refused with that message rather than silently ignored.

**What the declared model has to be able to do.** `ask_mail` answers by offering the model a retrieval tool and reading mail when the model calls it, so a model that cannot be given function tools cannot answer a question here whatever else is written above. That is what the two settings in the middle of the table exist for: a current reasoning model refuses function tools beside an *unstated* reasoning effort and names the responses API as the way to have both, so such a model needs `Chat:Api` set to `Responses` and `Chat:ReasoningEffort` written — including written as `none`, which states an effort rather than omitting the parameter. A model this deployment cannot use is not detected at startup, because nothing here can ask a provider what a routed name supports without paying for a call; it surfaces as *request refused* on the first question. [Chat generation](../features/chat-generation.md#two-apis-and-the-deployment-says-which) holds the whole reasoning, and [Mail answering](../features/mail-answering.md) describes the run that imposes the requirement.

### Microsoft Entra credential — `Chat:EntraCredential`

The same block, with the same keys, defaults, and rules as
[`Embeddings:Endpoints:<n>:EntraCredential`](#microsoft-entra-credential--embeddingsendpointsnentracredential) above.
One credential source resolves both sections, which is why the alias uniqueness rule spans them. Its keys reload here
and take a restart there, for the reason the table above gives: this section is read again per question and the
embedding chain is read once while the host composes itself.

### Relevance filter — `Chat:RelevanceFilter`

The optional second pass over a retrieval: each candidate the fused ranking produced is put to the declared chat
endpoint on its own, and the ones the model scores below the threshold are dropped before an answer is written. A block
inside `Chat` rather than a root of its own, because the pass judges with that endpoint and has nowhere to send a
question without one — removing the chat section removes this with it.

Off by default, and off is a supported deployment: retrieval then hands over the fused ranking exactly as hybrid search
produced it. Turning it on is a spend decision as much as a quality one — it costs one provider call per candidate on
every lookup a question makes. [Mail answering § An optional second pass](../features/mail-answering.md#an-optional-second-pass-the-model-decides-what-answers)
describes what it drops, what it keeps, and what it does when the provider cannot tell it.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `Chat:RelevanceFilter:Enabled` | bool | `false` | turning it on requires a declared `Chat:Alias`, and a `Chat:MaxMessagesPerRequest` of at least 2, because a judgement is an instruction and a candidate | restart |
| `Chat:RelevanceFilter:MaxCandidates` | int | *(unset)* | 1 – [`MailAnswering:MaxPassagesPerRetrieval`](#mailanswering), which is everything one retrieval hands over; a higher value would name candidates that never exist and is refused at startup rather than accepted and never met. Unset judges every passage the retrieval hands over, which is why there is no literal default here: one would go on saying a number of its own after the retrieval it follows was narrowed or widened. The ceiling on what one lookup spends and how long it takes; set below what retrieval returns it buys a weaker filter rather than a shorter result, because a passage nobody judged keeps its place | reload |
| `Chat:RelevanceFilter:MinimumRelevance` | int | `50` | 1 – 100, on the scale the model answers a judgement on. A threshold of 0 is refused: it would pay for a judgement that can drop nothing | reload |

## `MailAnswering`

What answering one question is allowed to cost, and how much of a mailbox may leave the process to do it. A root of its
own beside `Chat` rather than a block inside it, because the two answer different questions: `Chat` says which endpoint
generates text and what one *call* to it may carry, while this bounds a *run* — the conversation in which a model looks
mail up, reads it, and writes an answer — and the aggregate over every run of a period.

Unlike the provider sections, an absent section is not an absent capability. Every deployment has these ceilings and
writing nothing takes the conservative defaults below, because an absent provider is a capability nobody asked for while
an absent ceiling would be a bill nobody asked for. [Mail answering § What one question may
spend](../features/mail-answering.md#what-one-question-may-spend) describes what each one does when it is reached, and
what a caller is told.

The period is a **fixed window anchored at the Unix epoch**, placed exactly as
[`Embeddings:SpendPeriod`](#embeddings) places the other spend ceiling: an `AggregatePeriod` of one hour begins on the
hour, so a refused caller has a roll-over instant to come back at. A client that spends the whole allowance at the end
of one window and again at the start of the next has therefore spent twice the ceiling across an interval of the same
length.

Unlike the embedding ceiling, this ledger is **process-local and not durable**: a restart begins the current window with
nothing spent. The difference is deliberate and is stated rather than implied — an embedding sweep charges inside a
transaction that was committing vectors anyway, while answering opens no write of its own, so a durable count here would
put a database write on the path of every provider call in every run.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `MailAnswering:MaxPassagesPerRetrieval` | int | `20` | 1 – 50; how many messages one lookup may draw on. Capped by what one search can rank, because a retrieval is answered from a search window, and defaulted to the window `search_emails` itself returns so that asking a question reaches as many messages per lookup as searching for the same thing would | restart |
| `MailAnswering:MaxCharactersPerPassage` | int | `1200` | 1 – 100000; how much of any single message a lookup may draw out. Separate from the count above, because one enormous extract and a spread across several messages say different things about a mailbox | restart |
| `MailAnswering:MaxRetrievedCharactersPerRun` | int | `20000` | 1 – 10000000, and at least `MaxCharactersPerPassage` or no lookup could hand over even one passage. The ceiling on how much retrieved mail leaves the process to answer one question, whatever the model asks for, and the one that decides how many lookups a run fits: a lookup whose every passage reached the per-passage ceiling would exhaust a run on its own. Reaching it cuts rather than refuses: the run answers from what it has and the response says the mailbox was not read in full | restart |
| `MailAnswering:MaxProviderCallsPerRun` | int | `8` | 1 – 1000; the ceiling that holds whatever the provider reports, because a run is a tool loop whose length is the model's decision. Reaching it stops the run with `57001`. The run is also bounded by wall clock: raising this means raising [`McpEndpoint:RequestTimeout:Duration`](configuration-endpoints.md#request-timeout) with it, or the extra calls are bought and then abandoned with a `504` | restart |
| `MailAnswering:MaxTokensPerRun` | long | `80000` | 1 – 100000000; the cost ceiling, stated in the unit a provider bills by. Checked before each call against what the calls before it reported, so the call that crosses it is paid for — what a call will cost is not knowable until it is answered. Reaching it stops the run with `57001` | restart |
| `MailAnswering:MaxAnswerCharacters` | int | `20000` | 1 – 1000000; how much of the model's answer one response carries. Cut rather than refused, and the response says it was cut | restart |
| `MailAnswering:MaxCitations` | int | `20` | 1 – 1000; how many messages one response names. Cut the same way and reported the same way | restart |
| `MailAnswering:AggregatePeriod` | TimeSpan | `01:00:00` | positive; how long one period lasts before what was spent in it is forgotten. An hour rather than a day, because a ceiling an operator only meets once a day is one they meet after the spend has happened | restart |
| `MailAnswering:MaxRunsPerPeriod` | int | `30` | 1 – 1000000; the ceiling on how enthusiastic a client may be. Nothing about the MCP surface stops one from asking a hundred questions in a minute, and without this a per-run ceiling bounds each of those hundred and none of the total. A question over it is refused with `57001` | restart |
| `MailAnswering:MaxTokensPerPeriod` | long | `300000` | 1 – 10000000000; the same ceiling in what a provider bills. Checked before a run begins, against what the runs of this period have consumed so far | restart |

## `EmbeddingBackfill`

The sweep that gives mail stored before the active profile its passages and its vectors. A root of its own rather than
a block inside `Embeddings`, because what an instance embeds with is a commitment and how fast it works through the
mail it already had is a rate an operator changes while watching a bill. Every key here is a pacing control:
`BatchSize` × `MaxBatchesPerRun` is the most one run may spend, and `Interval` is how often that is paid.
[Embedding backfill](../features/embedding-backfill.md) describes what it reaches and why it repeats.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `EmbeddingBackfill:Enabled` | bool | `true` | turning it off stops the spending within one interval and loses nothing already embedded | restart |
| `EmbeddingBackfill:Interval` | TimeSpan | `00:00:30` | 1 s – 24 h; the pause between runs while messages still await embedding | restart |
| `EmbeddingBackfill:IdleSweepInterval` | TimeSpan | `00:15:00` | 1 s – 24 h; the pause before a sweep starts again after one reached the end | restart |
| `EmbeddingBackfill:BatchSize` | int | `20` | 1 – 500 | restart |
| `EmbeddingBackfill:MaxBatchesPerRun` | int | `5` | 1 – 1000 | restart |

## `MailExtractionBackfill`

The worker that extracts text for messages stored before extraction existed or before a limit was raised.

| Key | Type | Default | Constraint | Change |
| --- | --- | --- | --- | --- |
| `MailExtractionBackfill:Enabled` | bool | `true` | — | restart |
| `MailExtractionBackfill:Interval` | TimeSpan | `00:00:30` | 1 s – 1 h | restart |
| `MailExtractionBackfill:BatchSize` | int | `50` | 1 – 500 | restart |
| `MailExtractionBackfill:MaxBatchesPerRun` | int | `10` | 1 – 1000 | restart |
