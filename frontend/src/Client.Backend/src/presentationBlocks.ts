// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { asRecord } from './json';

// What the blocks of a presentation plan carry, and the sources they rest on. It sits apart from `discoveryRun.ts`
// because that module is the route — when a run is read, what a cursor means, how a tail is asked for — and this is
// the document the route answers with. Nine block types will land here one at a time, and a route module growing a
// parser per type is a module that stops being about the route.
//
// **Everything here is read from untrusted input at the trust boundary**, so every bound the service composes a plan
// under is checked again on the way in rather than assumed: a plan is written by a model's output passing through the
// service's own refusals, and a client that trusted the second of those would be trusting the first.
//
// **Nothing here decides what a block looks like.** A verdict, a confidence, and a freshness each arrive as the value
// the contract closed them over, and what each is called in a language — and which of them is drawn in a warning
// tint — is the screen's.

/** What the correspondence does for a block, which is the contract's own four-valued verdict. */
export type BlockSupport = 'Supported' | 'Unsupported' | 'Conflicting' | 'Stale';

const supports: readonly BlockSupport[] = ['Supported', 'Unsupported', 'Conflicting', 'Stale'];

/** How far an answer is worth trusting beyond the sources it names, as the band the composition capped it to. */
export type AnswerConfidence = 'High' | 'Moderate' | 'Low';

const confidences: readonly AnswerConfidence[] = ['High', 'Moderate', 'Low'];

/** Whether the local copy a block rests on was current, was known to be behind the mail server, or was never established. */
export type SourceStaleness = 'Current' | 'Stale' | 'Unknown';

const stalenesses: readonly SourceStaleness[] = ['Current', 'Stale', 'Unknown'];

/** Whether somebody wrote the source or this deployment described a picture of it. */
export type SourceMedium = 'Written' | 'Depicted';

const mediums: readonly SourceMedium[] = ['Written', 'Depicted'];

/** Why a declared source yielded nothing, which is a state a reader acts on rather than a failure to report. */
export type UnreadableSource =
    'Encrypted' | 'Corrupt' | 'FormatNotRead' | 'TooLarge' | 'NoTextFound' | 'NotAttempted' | 'ReadingFailed';

const unreadables: readonly UnreadableSource[] = [
    'Encrypted',
    'Corrupt',
    'FormatNotRead',
    'TooLarge',
    'NoTextFound',
    'NotAttempted',
    'ReadingFailed',
];

/** What a citation points at: the message itself, one persisted passage of it, or one of its attachments. */
export type CitedSourceKind = 'email' | 'fragment' | 'attachment';

const sourceKinds: readonly CitedSourceKind[] = ['email', 'fragment', 'attachment'];

/** Whether an attachment a run found can actually be opened, which is a state a reader acts on rather than a failure. */
export type AttachmentAvailability = 'Stored' | 'NotStored' | 'Removed';

const availabilities: readonly AttachmentAvailability[] = ['Stored', 'NotStored', 'Removed'];

/**
 * The closed catalogue of columns a fact table compares values across.
 *
 * A column carries no heading, because a heading is words in somebody's language and this client is localized — so the
 * wire carries the identity and the screen draws the heading for it, which is only possible because the set is closed.
 * A table naming anything else is refused rather than drawn under a heading nobody can read.
 */
export type FactTableColumn =
    'subject' | 'party' | 'document' | 'reference' | 'amount' | 'quantity' | 'term' | 'version' | 'status' | 'date';

const columns: readonly FactTableColumn[] = [
    'subject',
    'party',
    'document',
    'reference',
    'amount',
    'quantity',
    'term',
    'version',
    'status',
    'date',
];

/**
 * Where one declared source is followed to, spelled as the plan publishes it.
 *
 * The three carry different members and each kind requires its own, which is what makes this a union rather than one
 * record with three optional halves: a passage citation with no passage and an attachment citation with no position
 * are both a target nothing can be followed to, and neither is representable here.
 *
 * It is carried back to the deployment unchanged when the citation is followed, so this is the plan's spelling rather
 * than a second one of this client's — a translation between two spellings of one identity is a place for them to
 * disagree.
 */
export type CitationTarget =
    /** The message as such, which is what a fact about the correspondence itself rests on. */
    | { readonly kind: 'email'; readonly email: string }

    /** One persisted passage of a message, which is what a fact taken from part of a long one rests on. */
    | { readonly kind: 'fragment'; readonly email: string; readonly fragment: string }

    /** One file a message carries, named by the position the download route is addressed with. */
    | { readonly kind: 'attachment'; readonly email: string; readonly attachmentPosition: number };

/** How current the local copy behind a block or an entry was, and when that was established. */
export interface SourceFreshness {
    readonly staleness: SourceStaleness;

    /** When the local copy was established, and `null` where nothing established it. */
    readonly observedAt: string | null;
}

/** One side of a disagreement between sources: what that part of the correspondence says, and which sources say it. */
export interface ConflictingClaim {
    readonly statement: string;

    /** The sources saying it, which are sources the block itself rests on. */
    readonly sources: readonly string[];
}

/** What the correspondence does for one block. */
export interface BlockEvidence {
    readonly support: BlockSupport;

    /** The sources the block rests on, in the order they are worth reading, and empty for an unsupported block. */
    readonly citations: readonly string[];

    readonly freshness: SourceFreshness;

    /** The sides of the disagreement, which only a conflicting block carries. */
    readonly conflictingClaims: readonly ConflictingClaim[];
}

/** One source a run's blocks rest on, declared before anything names it. */
export interface DeclaredSource {
    /** The name the blocks refer to this source by. */
    readonly id: string;

    /** Where it is followed to, or `null` where the run named a kind this contract does not carry. */
    readonly target: CitationTarget | null;

    /** What the source is called, which is what a screen puts on the citation rather than the identifier. */
    readonly label: string;

    readonly medium: SourceMedium;

    /** Why the source yielded nothing, and `null` where it was read. */
    readonly unreadable: UnreadableSource | null;
}

/** One synthesized answer, in the words the run wrote it in. */
export interface SynthesizedAnswer {
    readonly text: string;
    readonly confidence: AnswerConfidence;
}

/** One message an answer rests on, and the part of it worth reading. */
export interface EvidenceEntry {
    /** The source the entry presents, named as the run declared it. */
    readonly source: string;

    /** The part of the message worth reading, quoted from it. */
    readonly fragment: string;

    /** How well the entry answers the question, between `0` and `1` inclusive. */
    readonly relevance: number;

    readonly freshness: SourceFreshness;
}

/** One dated event on a timeline: what happened, what it happened to, and when the correspondence dates it. */
export interface TimelineEntry {
    /** When the event happened, as the correspondence dates it. */
    readonly occurredAt: string;

    /** What happened. */
    readonly summary: string;

    /** What it happened to — the matter, the document, or the thread it belongs to. */
    readonly subject: string;

    /** The sources this entry rests on, which may be none where nothing backs it. */
    readonly sources: readonly string[];
}

/**
 * One cell of a fact table: the value as the correspondence wrote it, and what that value was read from.
 *
 * A cell the correspondence says nothing about carries no value at all, which is a different thing from a cell whose
 * value is blank — and it rests on nothing, because there is nothing for a source to back.
 */
export interface FactTableCell {
    /** The value as the correspondence wrote it, or `null` where the correspondence says nothing. */
    readonly value: string | null;

    /** The sources this cell rests on, which is what makes one cell's evidence checkable rather than the table's. */
    readonly sources: readonly string[];
}

/** One row of a fact table, holding exactly one cell per column of the table it belongs to, in the columns' order. */
export interface FactTableRow {
    readonly cells: readonly FactTableCell[];
}

/** One file found in mail, as the message that carried it describes it. */
export interface AttachmentEntry {
    /** The source resolving to the attachment, which is how the file is reached and which message it came on. */
    readonly source: string;

    /** The file's name, normalized. */
    readonly name: string;

    /** The media type the message declared, and `null` where it declared none. It chooses a word, never a way to open. */
    readonly mediaType: string | null;

    /** How large the message says the file is, which an entry whose content was never stored still carries. */
    readonly sizeOctets: number;

    readonly availability: AttachmentAvailability;
}

/** Somebody the correspondence names, by the name a reader recognizes and the address a client may act on. */
export interface BlockParticipant {
    /** The name a reader recognizes them by, which is never used as an identity. */
    readonly displayName: string;

    /** Their address, and `null` where the correspondence identifies them without one. */
    readonly address: string | null;
}

/** One person or organization a question is about, and where they stand in the correspondence. */
export interface PersonEntry {
    readonly person: BlockParticipant;

    /** Where they stand in the correspondence, as it reads rather than as anything knows it. */
    readonly relationship: string;

    /** When they were last in contact, and `null` where no message establishes it. */
    readonly lastContactAt: string | null;

    /** The sources this entry rests on, which a reader follows to see why the run says they are who it says. */
    readonly sources: readonly string[];
}

/** One thing a conversation settled or left open. */
export interface ThreadStatement {
    readonly text: string;
    readonly sources: readonly string[];
}

/** Something somebody undertook to do, and when they said they would. */
export interface ThreadCommitment {
    readonly text: string;

    /** Who undertook it, and `null` where the correspondence does not say. */
    readonly owedBy: BlockParticipant | null;

    /** When it was said to be due, and `null` where nothing says. */
    readonly dueAt: string | null;

    readonly sources: readonly string[];
}

/**
 * Where a conversation stands: who is taking part, what was agreed, what is open, and who owes what.
 *
 * Three lists rather than one, because the three read differently: somebody scanning for what they still owe should not
 * have to find it among what was settled. Any of the three is legitimately empty, and all three being empty at once is
 * a conversation this run found nothing to say about rather than a block to refuse.
 */
export interface ConversationStanding {
    readonly participants: readonly BlockParticipant[];
    readonly agreements: readonly ThreadStatement[];
    readonly openQuestions: readonly ThreadStatement[];
    readonly commitments: readonly ThreadCommitment[];
}

/** What has become of a draft a run composed, every member of which is local: the set holds none meaning sent. */
export type DraftDisposition = 'Composed' | 'Saved' | 'Queued';

const dispositions: readonly DraftDisposition[] = ['Composed', 'Saved', 'Queued'];

/** Text to be sent, where the answer to the question is a message rather than a fact about one. */
export interface ComposedDraft {
    /** Who it is addressed to, each of them named once. */
    readonly recipients: readonly BlockParticipant[];

    readonly subject: string;

    /** Its body, as plain text. */
    readonly body: string;

    readonly disposition: DraftDisposition;
}

/** The closed set of next steps a plan may suggest, so that nothing is proposed no client wrote a control for. */
export type SuggestedActionKind =
    | 'ReplyToThread'
    | 'ForwardEmail'
    | 'ComposeEmail'
    | 'FlagEmail'
    | 'OpenThread'
    | 'SearchAgain'
    | 'CreateMailRule';

const actionKinds: readonly SuggestedActionKind[] = [
    'ReplyToThread',
    'ForwardEmail',
    'ComposeEmail',
    'FlagEmail',
    'OpenThread',
    'SearchAgain',
    'CreateMailRule',
];

/** What taking a suggested action would change, ordered by what it costs to undo. */
export type SuggestedActionImpact = 'ReadsOnly' | 'ChangesMailbox' | 'SendsMail';

const impacts: readonly SuggestedActionImpact[] = ['ReadsOnly', 'ChangesMailbox', 'SendsMail'];

/** A next step somebody may take, with what it would change and why it is being suggested. */
export interface SuggestedAction {
    readonly action: SuggestedActionKind;

    /** Why it is being suggested, in the words the run wrote. */
    readonly reason: string;

    readonly impact: SuggestedActionImpact;

    /**
     * Whether it must be confirmed before it is taken, as the run stated it rather than as this client derived it.
     *
     * Stated rather than computed, because the two answer different questions: an action that changes nothing may still
     * need confirming where the run is unsure the suggestion fits. What a screen may never do is offer it *without*
     * confirmation because this member said so — which is why an action that leaves the deployment is refused below
     * when it claims otherwise.
     */
    readonly requiresConfirmation: boolean;
}

// The service's own bounds, restated because this side of the boundary refuses what it will not draw rather than
// trusting the side that composed it. Each is the constant the contract names: a block rests on at most twenty-four
// sources, presents at most six sides of a disagreement, lists at most fifty messages, events, or files, compares
// across at most eight columns, names at most thirty people and thirty participants, says at most twenty-five things
// about a conversation per list, addresses a draft to at most twenty recipients, and every free text it carries is one
// presentation text.
const mostCitations = 24;
const mostConflictingClaims = 6;
const mostEvidenceEntries = 50;
const mostTimelineEntries = 50;
const mostFactTableColumns = 8;
const mostFactTableRows = 50;
const mostAttachmentEntries = 50;
const mostPersonEntries = 30;
const mostParticipants = 30;
const mostStatements = 25;
const mostRecipients = 20;
const longestText = 4000;
const longestCitationId = 32;
const longestIdentity = 256;

// An address is bounded well above what any mail server accepts and well below a presentation text, because it is one
// value on one line of a screen rather than prose.
const longestAddress = 320;

/** What the correspondence does for one block, or `null` where what arrived is not evidence this client can read. */
export function parseBlockEvidence(value: unknown): BlockEvidence | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const support = oneOf(record['support'], supports);
    const citations = parseCitationIds(record['citations'], mostCitations);
    const freshness = parseFreshness(record['freshness']);

    // A block that named one source twice is refused rather than drawn: a screen numbers the sources it rests on by
    // their place in this list, so a repeated name is two different numbers for one source.
    if (support === null || citations === null || freshness === null || new Set(citations).size !== citations.length) {
        return null;
    }

    const conflictingClaims = parseConflictingClaims(record['conflictingClaims'], citations);

    return conflictingClaims === null || !matchesVerdict(support, citations, freshness, conflictingClaims)
        ? null
        : { support, citations, freshness, conflictingClaims };
}

/** One source a run declared, or `null` where what arrived is not a declaration this client can read. */
export function parseDeclaredSource(value: unknown): DeclaredSource | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const id = parseCitationId(record['id']);
    const label = parseText(record['label']);
    const medium = oneOf(record['medium'], mediums);
    const target = asRecord(record['target']);

    if (id === null || label === null || medium === null || target === null) {
        return null;
    }

    // An unreadable reason that is absent and one that is present as nothing are the same fact: the journal writes no
    // member at all and the route that serves it writes `null`, and a source that was read carries neither.
    const declared = record['unreadable'];
    const unreadable = declared === undefined || declared === null ? null : oneOf(declared, unreadables);

    if (declared !== undefined && declared !== null && unreadable === null) {
        return null;
    }

    // A target this client cannot read is `null` rather than a refusal of the source around it: a run naming a kind
    // written after this build was is a source that can still be named, numbered, and drawn — what it cannot be is
    // followed, which is a control the inspector does not offer rather than a plan to refuse.
    return { id, target: parseCitationTarget(target), label, medium, unreadable };
}

/** Where a declared source is followed to, or `null` where the target is not one of the three this client follows. */
function parseCitationTarget(target: Readonly<Record<string, unknown>>): CitationTarget | null {
    const kind = oneOf(target['kind'], sourceKinds);
    const email = parseIdentity(target['email']);

    if (kind === null || email === null) {
        return null;
    }

    switch (kind) {
        case 'email':
            return { kind, email };

        case 'fragment': {
            const fragment = parseIdentity(target['fragment']);

            return fragment === null ? null : { kind, email, fragment };
        }

        case 'attachment': {
            const attachmentPosition = target['attachmentPosition'];

            return typeof attachmentPosition === 'number' &&
                Number.isSafeInteger(attachmentPosition) &&
                attachmentPosition >= 0
                ? { kind, email, attachmentPosition }
                : null;
        }
    }
}

/** One synthesized answer, or `null` where what arrived is not one this client can draw. */
export function parseSynthesizedAnswer(value: unknown): SynthesizedAnswer | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const text = parseText(record['text']);
    const confidence = oneOf(record['confidence'], confidences);

    return text === null || confidence === null ? null : { text, confidence };
}

/**
 * The messages a block lists, or `null` where what arrived is not a list this client can draw.
 *
 * A list holding nothing is read rather than refused: the contract composes none, and an answer that drew an error
 * over a list which arrived empty would be reporting a defect where the honest reading is that there was nothing in it.
 */
export function parseEvidenceEntries(value: unknown): readonly EvidenceEntry[] | null {
    return parsedItems(value, mostEvidenceEntries, parseEvidenceEntry);
}

/**
 * The events a timeline holds, or `null` where what arrived is not a course of events this client can draw.
 *
 * The order is the producer's and is never sorted here: a run that ordered by when something was agreed rather than by
 * when it was mentioned has said something a date column cannot, and a client that re-sorted would throw it away.
 *
 * A timeline holding nothing is read rather than refused, for the reason a list of messages holding nothing is: a
 * block that found no event in the period asked about is an answer, and drawing an error over it would report a defect.
 */
export function parseTimelineEntries(value: unknown): readonly TimelineEntry[] | null {
    return parsedItems(value, mostTimelineEntries, parseTimelineEntry);
}

/** The columns a table compares across, or `null` where one of them is not a column this client can draw a heading for. */
export function parseFactTableColumns(value: unknown): readonly FactTableColumn[] | null {
    const compared = parsedItems(value, mostFactTableColumns, (written) => oneOf(written, columns));

    if (compared === null) {
        return null;
    }

    // A table comparing across one column twice is two headings a reader cannot tell apart, which the plan refuses and
    // this refuses again rather than drawing.
    return new Set(compared).size === compared.length ? compared : null;
}

/**
 * The rows a table holds, or `null` where what arrived is not a set of rows this client can draw.
 *
 * @param value The rows as they arrived.
 * @param compared How many columns the table compares across, which every row holds exactly one cell per.
 * @remarks
 * A row whose cell count disagrees with the header is the one structural mistake this block can make, and it is a
 * comparison nobody can trust rather than a rendering to patch up with blanks.
 */
export function parseFactTableRows(value: unknown, compared: number): readonly FactTableRow[] | null {
    return parsedItems(value, mostFactTableRows, (written) => parseFactTableRow(written, compared));
}

/** The files a gallery holds, or `null` where what arrived is not a set of files this client can draw. */
export function parseAttachmentEntries(value: unknown): readonly AttachmentEntry[] | null {
    return parsedItems(value, mostAttachmentEntries, parseAttachmentEntry);
}

/**
 * The people a block names, or `null` where what arrived is not a set of people this client can draw.
 *
 * A block naming nobody is read rather than refused, for the reason a list of messages holding nothing is: the contract
 * composes none, so an empty one is a deployment that broke its own promise — and refusing it here would discard the
 * whole tail, which is every block the run had already composed. Drawn as a block that found nobody, it costs the
 * reader one empty card and keeps the answer.
 */
export function parsePersonEntries(value: unknown): readonly PersonEntry[] | null {
    return parsedItems(value, mostPersonEntries, parsePersonEntry);
}

/** Where a conversation stands, or `null` where what arrived is not a standing this client can draw. */
export function parseConversationStanding(value: unknown): ConversationStanding | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const participants = parsedItems(record['participants'], mostParticipants, parseParticipant);
    const agreements = parsedItems(record['agreements'], mostStatements, parseThreadStatement);
    const openQuestions = parsedItems(record['openQuestions'], mostStatements, parseThreadStatement);
    const commitments = parsedItems(record['commitments'], mostStatements, parseThreadCommitment);

    return participants === null || agreements === null || openQuestions === null || commitments === null
        ? null
        : { participants, agreements, openQuestions, commitments };
}

/**
 * A draft a run composed, or `null` where what arrived is not one this client can draw.
 *
 * A draft addressed to nobody, or carrying no subject or no body, is refused rather than drawn: the three are what a
 * message is, and a card offering somebody text to check with the recipients missing is worse than the block being
 * absent. A recipient named twice is refused for the same reason a citation named twice is — one person drawn as two.
 */
export function parseComposedDraft(value: unknown): ComposedDraft | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const recipients = parsedItems(record['recipients'], mostRecipients, parseRecipient);
    const subject = parseText(record['subject']);
    const body = parseText(record['body']);
    const disposition = oneOf(record['disposition'], dispositions);

    if (recipients === null || recipients.length === 0 || subject === null || body === null || disposition === null) {
        return null;
    }

    const addressed = recipients.map((recipient) => recipient.address);

    return new Set(addressed).size !== addressed.length ? null : { recipients, subject, body, disposition };
}

/**
 * A next step a run suggested, or `null` where what arrived is not one this client may offer.
 *
 * The one refusal that is about safety rather than shape: an action whose impact says mail leaves the deployment, or
 * that something in the mailbox changes, and which claims it needs no confirming, is not drawn at all. Nothing here can
 * recall a sent message, so a suggestion that arrived saying it may be taken unasked is either a deployment that broke
 * its own rule or something that reached the plan on the way — and a screen that drew it would be one control away from
 * acting on it. The service already refuses the sending half; this refuses both halves, on the side that would do it.
 */
export function parseSuggestedAction(value: unknown): SuggestedAction | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const action = oneOf(record['action'], actionKinds);
    const reason = parseText(record['reason']);
    const impact = oneOf(record['impact'], impacts);
    const requiresConfirmation = record['requiresConfirmation'];

    if (action === null || reason === null || impact === null || typeof requiresConfirmation !== 'boolean') {
        return null;
    }

    return impact !== 'ReadsOnly' && !requiresConfirmation ? null : { action, reason, impact, requiresConfirmation };
}

function parseTimelineEntry(value: unknown): TimelineEntry | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const occurredAt = parseInstant(record['occurredAt']);
    const summary = parseText(record['summary']);
    const subject = parseText(record['subject']);
    const sources = parseSources(record['sources']);

    return occurredAt === null || summary === null || subject === null || sources === null
        ? null
        : { occurredAt, summary, subject, sources };
}

function parseFactTableRow(value: unknown, compared: number): FactTableRow | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const cells = parsedItems(record['cells'], mostFactTableColumns, parseFactTableCell);

    if (cells === null) {
        return null;
    }

    return cells.length === compared ? { cells } : null;
}

function parseFactTableCell(value: unknown): FactTableCell | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    // A cell the correspondence says nothing about writes no member at all on the journal and `null` on the route that
    // serves it, and the two are the same fact.
    const stated = record['value'];
    const cell = stated === undefined || stated === null ? null : parseText(stated);
    const sources = parseSources(record['sources']);

    if (sources === null || (stated !== undefined && stated !== null && cell === null)) {
        return null;
    }

    // A cell with no value rests on nothing, because there is nothing for a source to back — a cell that named one
    // would be citing an absence.
    return cell === null && sources.length > 0 ? null : { value: cell, sources };
}

function parseAttachmentEntry(value: unknown): AttachmentEntry | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const source = parseCitationId(record['source']);
    const name = parseText(record['name']);
    const availability = oneOf(record['availability'], availabilities);
    const sizeOctets = record['sizeOctets'];

    const declared = record['mediaType'];
    const mediaType = declared === undefined || declared === null ? null : parseText(declared);

    if (source === null || name === null || availability === null) {
        return null;
    }

    if (declared !== undefined && declared !== null && mediaType === null) {
        return null;
    }

    return typeof sizeOctets === 'number' && Number.isSafeInteger(sizeOctets) && sizeOctets >= 0
        ? { source, name, mediaType, sizeOctets, availability }
        : null;
}

function parsePersonEntry(value: unknown): PersonEntry | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const person = parseParticipant(record);
    const relationship = parseText(record['relationship']);
    const lastContact = parseStatedInstant(record['lastContactAt']);
    const sources = parseSources(record['sources']);

    return person === null || relationship === null || lastContact === null || sources === null
        ? null
        : { person, relationship, lastContactAt: lastContact.at, sources };
}

function parseThreadStatement(value: unknown): ThreadStatement | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const text = parseText(record['text']);
    const sources = parseSources(record['sources']);

    return text === null || sources === null ? null : { text, sources };
}

function parseThreadCommitment(value: unknown): ThreadCommitment | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const text = parseText(record['text']);
    const due = parseStatedInstant(record['dueAt']);
    const sources = parseSources(record['sources']);

    if (text === null || due === null || sources === null) {
        return null;
    }

    // Absent and present as nothing are the same fact here, as they are for an instant nothing stated: the
    // correspondence did not say who keeps the commitment, and a client that invented a name for it would be asserting
    // what the mail did not.
    const named = record['owedBy'];
    const owedBy = named === undefined || named === null ? null : parseParticipant(named);

    return named !== undefined && named !== null && owedBy === null
        ? null
        : { text, owedBy, dueAt: due.at, sources };
}

/**
 * Somebody the correspondence names, read from the record that names them.
 *
 * The display name is the block's own presentation text and the address is the mail address beside it, which the
 * contract carries as a value of its own — so the name a reader sees and the name a message happened to write in a
 * header are two different strings, and this takes the first.
 */
function parseParticipant(value: unknown): BlockParticipant | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const displayName = parseText(record['displayName']);
    if (displayName === null) {
        return null;
    }

    const named = record['address'];
    if (named === undefined || named === null) {
        return { displayName, address: null };
    }

    const address = parseAddress(named);

    return address === null ? null : { displayName, address: address.address };
}

/**
 * One recipient of a draft, which is a mail address rather than a name with an address beside it.
 *
 * So the two halves arrive the other way round from every other participant in this contract: the address is what the
 * draft is addressed to and is always there, and the display name is whatever the correspondence wrote beside it.
 */
function parseRecipient(value: unknown): BlockParticipant | null {
    const address = parseAddress(value);

    return address === null ? null : { displayName: address.displayName ?? address.address, address: address.address };
}

/** One mail address as the contract carries it, or `null` where what arrived is not one. */
function parseAddress(value: unknown): { readonly address: string; readonly displayName: string | null } | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const address = record['address'];
    if (typeof address !== 'string' || address.length === 0 || address.length > longestAddress) {
        return null;
    }

    // The comparison form the contract carries beside it is read by nothing here: it exists so that two addresses
    // differing in case compare equal, which is a question this side of the boundary never asks.
    const named = record['displayName'];
    if (named === undefined || named === null) {
        return { address, displayName: null };
    }

    return typeof named === 'string' && named.length > 0 && named.length <= longestAddress
        ? { address, displayName: named }
        : null;
}

/**
 * The citations one item within a block rests on, or `null` where they are not citations this client can number.
 *
 * An item may rest on nothing, which is how a row nothing backs is presented beside rows that are backed. What is
 * refused is a name given twice, because a screen numbers a source by its place among the ones the block names and a
 * repeat is two numbers for one source.
 */
function parseSources(value: unknown): readonly string[] | null {
    const sources = parseCitationIds(value, mostCitations);

    if (sources === null) {
        return null;
    }

    return new Set(sources).size === sources.length ? sources : null;
}

/** Every item of a bounded list read the same way, or `null` where the list or one of its items is not readable. */
function parsedItems<TItem>(
    value: unknown,
    most: number,
    parse: (written: unknown) => TItem | null,
): readonly TItem[] | null {
    if (!Array.isArray(value) || value.length > most) {
        return null;
    }

    const items: TItem[] = [];
    for (const written of value) {
        const item = parse(written);
        if (item === null) {
            return null;
        }

        items.push(item);
    }

    return items;
}

function parseEvidenceEntry(value: unknown): EvidenceEntry | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const source = parseCitationId(record['source']);
    const fragment = parseText(record['fragment']);
    const relevance = record['relevance'];
    const freshness = parseFreshness(record['freshness']);

    if (source === null || fragment === null || freshness === null) {
        return null;
    }

    return typeof relevance === 'number' && Number.isFinite(relevance) && relevance >= 0 && relevance <= 1
        ? { source, fragment, relevance, freshness }
        : null;
}

/**
 * Whether what the block says about itself holds together, which is the shape the plan composes each verdict under.
 *
 * A verdict is drawn as a chip and the sides of a disagreement are drawn as a list, each read from a different member,
 * so a block whose members disagree with each other draws two contradictory things at once — a settled *supported*
 * over a list of sources that do not agree, or a *supported* over nothing that backs it. The plan refuses every one of
 * these, and so does this: what is stated here is that rule and not a stricter one of this client's own.
 */
function matchesVerdict(
    support: BlockSupport,
    citations: readonly string[],
    freshness: SourceFreshness,
    conflictingClaims: readonly ConflictingClaim[],
): boolean {
    if (support === 'Conflicting') {
        // A conflict is between sources and is presented as both of its sides, so neither half of it can be one thing.
        return citations.length >= 2 && conflictingClaims.length >= 2;
    }

    if (conflictingClaims.length > 0) {
        return false;
    }

    switch (support) {
        case 'Supported':
            return citations.length > 0 && freshness.staleness !== 'Stale';

        case 'Unsupported':
            return citations.length === 0;

        case 'Stale':
            return citations.length > 0 && freshness.staleness === 'Stale';
    }
}

/**
 * The sides of a disagreement, or `null` where what arrived is not a set of sides this client can draw.
 *
 * A side names sources the block itself rests on, which the plan enforces and this checks again: a side naming
 * anything else is a source with no place in the block's own list, and a screen that numbers a citation by that place
 * would draw it as the source before the first one rather than refusing it.
 */
function parseConflictingClaims(value: unknown, citations: readonly string[]): readonly ConflictingClaim[] | null {
    // The member is absent on a block that carries none, which every block but a conflicting one is.
    if (value === undefined || value === null) {
        return [];
    }

    if (!Array.isArray(value) || value.length > mostConflictingClaims) {
        return null;
    }

    const restedOn = new Set(citations);
    const claims: ConflictingClaim[] = [];
    for (const written of value) {
        const record = asRecord(written);
        const statement = record === null ? null : parseText(record['statement']);
        const sources = record === null ? null : parseCitationIds(record['sources'], mostCitations);

        if (statement === null || sources === null || sources.length === 0) {
            return null;
        }

        if (sources.some((source) => !restedOn.has(source)) || new Set(sources).size !== sources.length) {
            return null;
        }

        claims.push({ statement, sources });
    }

    return claims;
}

function parseFreshness(value: unknown): SourceFreshness | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const staleness = oneOf(record['staleness'], stalenesses);
    if (staleness === null) {
        return null;
    }

    const observedAt = record['observedAt'];
    if (observedAt === undefined || observedAt === null) {
        return { staleness, observedAt: null };
    }

    const at = parseInstant(observedAt);

    return at === null ? null : { staleness, observedAt: at };
}

/** An instant as the service wrote it, left as it arrived: what it says is read where it is worded, never here. */
function parseInstant(value: unknown): string | null {
    return typeof value === 'string' && value.length > 0 && value.length <= longestText ? value : null;
}

/**
 * An instant the correspondence may legitimately not have stated, or `null` where it stated one this client cannot read.
 *
 * The two are one value in what a block carries — a last contact nothing established and a due date nobody wrote are
 * both `null` — which is exactly why they are told apart here. Nothing said is the correspondence being silent, and
 * something said that is not an instant is a producer that broke the contract, so the answer is wrapped rather than
 * flattened: a bare `null` could not say which of the two happened.
 */
function parseStatedInstant(value: unknown): { readonly at: string | null } | null {
    if (value === undefined || value === null) {
        return { at: null };
    }

    const at = parseInstant(value);

    return at === null ? null : { at };
}

function parseCitationIds(value: unknown, most: number): readonly string[] | null {
    if (!Array.isArray(value) || value.length > most) {
        return null;
    }

    const ids: string[] = [];
    for (const written of value) {
        const id = parseCitationId(written);
        if (id === null) {
            return null;
        }

        ids.push(id);
    }

    return ids;
}

function parseCitationId(value: unknown): string | null {
    return typeof value === 'string' && value.length > 0 && value.length <= longestCitationId ? value : null;
}

// The identities a target names are this deployment's own, written as the bare UUID every other client route names a
// message by. The bound is the one `citedPassages.ts` posts them back under, so a target this parser admits is one that
// can actually be followed rather than one the request would refuse.
function parseIdentity(value: unknown): string | null {
    return typeof value === 'string' && value.length > 0 && value.length <= longestIdentity ? value : null;
}

function parseText(value: unknown): string | null {
    return typeof value === 'string' && value.length > 0 && value.length <= longestText ? value : null;
}

function oneOf<TValue extends string>(value: unknown, values: readonly TValue[]): TValue | null {
    return typeof value === 'string' && (values as readonly string[]).includes(value) ? (value as TValue) : null;
}
