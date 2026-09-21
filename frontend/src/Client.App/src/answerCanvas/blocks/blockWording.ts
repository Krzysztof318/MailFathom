// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type {
    AnswerConfidence,
    AttachmentAvailability,
    BlockSupport,
    CitedSourceKind,
    DraftDisposition,
    FactTableColumn,
    SourceMedium,
    SourceStaleness,
    SuggestedActionImpact,
    SuggestedActionKind,
} from '@mailfathom/client-backend';
import type { IconName } from '../../controls/icons';
import type { MessageKey } from '../../localization/en';

// What a block says about its own honesty, in one place. Every renderer in the catalogue reads it, so a verdict that
// meant one thing on an answer and another on a list of messages is exactly what this exists to stop —
// the design project draws one legend for the whole catalogue, and a legend is a promise that the chip means the same
// thing wherever it appears.
//
// Every value here is a closed set the contract already carries, so each table is exhaustive by its own type: a member
// added to one of those sets fails to compile here rather than rendering as nothing.

/** How a verdict on what the correspondence does for a block is drawn. */
export interface SupportVerdict {
    readonly label: MessageKey;

    /** What the verdict means, which the design states as a legend and a screen states where the verdict stands. */
    readonly note: MessageKey;

    readonly icon: IconName;

    /** The chip's own tint, written as the tokens rather than as colours. */
    readonly tint: string;
}

const warned = 'bg-warning-soft text-warning-text';

/**
 * What each of the contract's four verdicts is called and drawn as.
 *
 * Three of the four are drawn in the warning tint, and that is the point of the set rather than an oversight: a fact
 * nothing backs, a fact read from a copy known to be behind, and two sources that disagree are each a reason to look
 * before acting, and drawing two of them as though they were as settled as the first would be the silent resolution
 * the contract exists to refuse.
 */
export const supportVerdicts: Readonly<Record<BlockSupport, SupportVerdict>> = {
    Supported: {
        label: 'answer.supported',
        note: 'answer.supportedNote',
        icon: 'check_circle',
        tint: 'bg-healthy-soft text-healthy-text',
    },
    Unsupported: {
        label: 'answer.unsupported',
        note: 'answer.unsupportedNote',
        icon: 'cancel',
        tint: warned,
    },
    Stale: {
        label: 'answer.outdated',
        note: 'answer.outdatedNote',
        icon: 'history',
        tint: warned,
    },
    Conflicting: {
        label: 'answer.conflicting',
        note: 'answer.conflictingNote',
        icon: 'sync_problem',
        tint: warned,
    },
};

/**
 * What each confidence band is called and drawn as.
 *
 * The design project draws the high band and no other, so the two beneath it are drawn in the shape it gives that one:
 * a chip in the same place, tinted by what the band actually says. Moderate is neutral rather than warned — a step of
 * inference somebody may want to check is not a defect — and low carries the warning tint, because the best reading of
 * partial sources is exactly the answer worth looking behind.
 */
export const confidenceBands: Readonly<
    Record<AnswerConfidence, { readonly label: MessageKey; readonly tint: string }>
> = {
    High: { label: 'answer.confidenceHigh', tint: 'bg-healthy-soft text-healthy-text' },
    Moderate: { label: 'answer.confidenceModerate', tint: 'bg-rail text-muted' },
    Low: { label: 'answer.confidenceLow', tint: warned },
};

/** What a source is, said as the design project's own two kinds: a message, or a file that came with one. */
export const sourceKinds: Readonly<Record<CitedSourceKind, { readonly label: MessageKey; readonly icon: IconName }>> = {
    // A passage is part of a message rather than a third kind of thing, and the design names two kinds of source. So a
    // fragment is drawn as the message it was cut from, which is also where following it takes somebody.
    email: { label: 'answer.sourceMessage', icon: 'mail' },
    fragment: { label: 'answer.sourceMessage', icon: 'mail' },
    attachment: { label: 'answer.sourceAttachment', icon: 'attach_file' },
};

/**
 * What the medium of a source is called, where it is worth calling anything.
 *
 * A written source is the ordinary case and says nothing of itself. A depicted one is this deployment's own account of
 * what a picture shows rather than words anybody typed, and
 * [ADR 0030](https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0030-describing-an-image-attachment-in-words-and-ranking-a-depicted-match-below-a-written-one.md)
 * is what makes saying so an obligation rather than a nicety: presented as a quotation, a machine's reading of an
 * image is a guess promoted to evidence.
 */
export const sourceMediums: Readonly<Record<SourceMedium, MessageKey | null>> = {
    Written: null,
    Depicted: 'answer.sourceDepicted',
};

/** How current the local copy behind something was, said so that nobody has to work it out from a date alone. */
export const freshnessWords: Readonly<Record<SourceStaleness, MessageKey>> = {
    Current: 'answer.freshnessCurrent',
    Stale: 'answer.freshnessStale',
    Unknown: 'answer.freshnessUnknown',
};

/** The forms the count of sources a block rests on is said in, which Polish needs four of and English hides. */
export const citationCounts: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'answer.citationCount.other',
    one: 'answer.citationCount.one',
    two: 'answer.citationCount.other',
    few: 'answer.citationCount.few',
    many: 'answer.citationCount.many',
    other: 'answer.citationCount.other',
};

/** The same forms for the messages a list holds, which it says beside what they are ordered by. */
export const evidenceCounts: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'answer.evidenceCount.other',
    one: 'answer.evidenceCount.one',
    two: 'answer.evidenceCount.other',
    few: 'answer.evidenceCount.few',
    many: 'answer.evidenceCount.many',
    other: 'answer.evidenceCount.other',
};

/** The same forms for the events a chronology holds. */
export const timelineCounts: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'answer.timelineCount.other',
    one: 'answer.timelineCount.one',
    two: 'answer.timelineCount.other',
    few: 'answer.timelineCount.few',
    many: 'answer.timelineCount.many',
    other: 'answer.timelineCount.other',
};

/** The same forms for the rows a comparison holds. */
export const factTableRowCounts: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'answer.factTableCount.other',
    one: 'answer.factTableCount.one',
    two: 'answer.factTableCount.other',
    few: 'answer.factTableCount.few',
    many: 'answer.factTableCount.many',
    other: 'answer.factTableCount.other',
};

/** The same forms for the files a gallery holds. */
export const attachmentCounts: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'answer.galleryCount.other',
    one: 'answer.galleryCount.one',
    two: 'answer.galleryCount.other',
    few: 'answer.galleryCount.few',
    many: 'answer.galleryCount.many',
    other: 'answer.galleryCount.other',
};

/**
 * What each column of a fact table is headed, and which edge its values are aligned to.
 *
 * The heading is here rather than on the wire because it is words in somebody's language, and the run states an
 * identity out of a closed catalogue precisely so that this client can draw the heading in the reader's own — which is
 * the whole reason a producer cannot invent a column.
 *
 * `numeric` is the column's kind read for the one thing a client may do with it. A cell is always the text the
 * correspondence wrote, so what the kind settles is presentation: a counted quantity and a money amount are aligned to
 * the end of the column and held in step, and everything else reads as the language reads. It never settles a parse —
 * "roughly €40k" is a value this contract carries, and a client that reformatted it would assert a precision nobody
 * wrote. A date is text for the same reason, so it is aligned as words are.
 */
export const factTableColumns: Readonly<
    Record<FactTableColumn, { readonly heading: MessageKey; readonly numeric: boolean }>
> = {
    subject: { heading: 'answer.columnSubject', numeric: false },
    party: { heading: 'answer.columnParty', numeric: false },
    document: { heading: 'answer.columnDocument', numeric: false },
    reference: { heading: 'answer.columnReference', numeric: false },
    amount: { heading: 'answer.columnAmount', numeric: true },
    quantity: { heading: 'answer.columnQuantity', numeric: true },
    term: { heading: 'answer.columnTerm', numeric: false },
    version: { heading: 'answer.columnVersion', numeric: false },
    status: { heading: 'answer.columnStatus', numeric: false },
    date: { heading: 'answer.columnDate', numeric: false },
};

/**
 * What each of the three availabilities is called and drawn as.
 *
 * A file held locally is the settled case and is drawn as one. The other two are not failures and are not drawn as
 * errors either: a message whose content was never stored is ordinary, and content retention removed is a fact about
 * the deployment rather than about this run. Both are warned rather than healthy because both mean *this is not a file
 * you can have from here*, which is what somebody choosing between three files needs to see at a glance.
 */
export const attachmentAvailabilities: Readonly<
    Record<AttachmentAvailability, { readonly label: MessageKey; readonly icon: IconName; readonly tint: string }>
> = {
    Stored: {
        label: 'answer.attachmentStored',
        icon: 'check_circle',
        tint: 'bg-healthy-soft text-healthy-text',
    },
    NotStored: { label: 'answer.attachmentNotStored', icon: 'cloud_off', tint: warned },
    Removed: { label: 'answer.attachmentRemoved', icon: 'history', tint: warned },
};

/** The same forms for the people a block names. */
export const personCounts: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'answer.peopleCount.other',
    one: 'answer.peopleCount.one',
    two: 'answer.peopleCount.other',
    few: 'answer.peopleCount.few',
    many: 'answer.peopleCount.many',
    other: 'answer.peopleCount.other',
};

/** The same forms for the people taking part in a conversation. */
export const participantCounts: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'threadStanding.participantCount.other',
    one: 'threadStanding.participantCount.one',
    two: 'threadStanding.participantCount.other',
    few: 'threadStanding.participantCount.few',
    many: 'threadStanding.participantCount.many',
    other: 'threadStanding.participantCount.other',
};

/**
 * What has become of a draft locally, which is the whole of what a draft block may say about itself.
 *
 * Each of the three says what it is *and* that nothing was sent, in one sentence rather than a state beside a
 * reassurance: the set the contract closed this over holds no member meaning sent, and a reader looking at text
 * addressed to somebody else is owed that where the text is rather than in a legend.
 */
export const draftDispositions: Readonly<Record<DraftDisposition, MessageKey>> = {
    Composed: 'draft.composed',
    Saved: 'draft.saved',
    Queued: 'draft.queued',
};

/** What each suggested step is called and drawn as, one entry per member of the set the contract closed it over. */
export const suggestedActions: Readonly<
    Record<SuggestedActionKind, { readonly label: MessageKey; readonly icon: IconName }>
> = {
    ReplyToThread: { label: 'suggestedAction.replyToThread', icon: 'reply' },
    ForwardEmail: { label: 'suggestedAction.forwardEmail', icon: 'forward' },
    ComposeEmail: { label: 'suggestedAction.composeEmail', icon: 'edit_square' },
    FlagEmail: { label: 'suggestedAction.flagEmail', icon: 'flag' },
    OpenThread: { label: 'suggestedAction.openThread', icon: 'topic' },
    SearchAgain: { label: 'suggestedAction.searchAgain', icon: 'search' },
    CreateMailRule: { label: 'suggestedAction.createMailRule', icon: 'tune' },
};

/**
 * What taking a step would change, said as what it costs to undo rather than as the member's own name.
 *
 * The design draws this as one *Effect:* line, and what makes the line worth reading is the undoing: opening a thread
 * costs nothing, filing a message is reversible by whoever filed it, and a message that has left the deployment cannot
 * be recalled by anything here.
 */
export const actionImpacts: Readonly<Record<SuggestedActionImpact, MessageKey>> = {
    ReadsOnly: 'suggestedAction.readsOnly',
    ChangesMailbox: 'suggestedAction.changesMailbox',
    SendsMail: 'suggestedAction.sendsMail',
};
