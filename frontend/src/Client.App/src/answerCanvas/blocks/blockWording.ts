// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type {
    AnswerConfidence,
    BlockSupport,
    CitedSourceKind,
    SourceMedium,
    SourceStaleness,
} from '@mailfathom/client-backend';
import type { IconName } from '../../controls/icons';
import type { MessageKey } from '../../localization/en';

// What a block says about its own honesty, in one place. Two renderers read it today and the other seven will, so a
// verdict that meant one thing on an answer and another on a list of messages is exactly what this exists to stop —
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
