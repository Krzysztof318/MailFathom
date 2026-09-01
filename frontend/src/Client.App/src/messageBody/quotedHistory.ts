// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailDocumentBlock } from '@mailfathom/client-backend';

// Where a reply stops being what somebody wrote and starts being what they were answering. A conversation is mostly
// repetition — each reply carries the one before it — so a thread that drew every message whole would be the same
// paragraph eight times with a decreasing indent.
//
// The rule is the trailing quotation and nothing cleverer: a message's own words come first and the history it quoted
// sits under them, so the run of quotations at the end of the document is the history and everything above it is the
// contribution. A quotation somebody replied underneath or between stays where it is, because there it is part of what
// the message said rather than a copy of what it answered.
//
// It is a presentation decision and it is the client's: the service publishes the document a message reduces to and
// judges no block of it, and the trimmed opening it publishes beside that is two hundred characters for a list row
// rather than a message anybody reads.

/** A message's own words, and the history it quoted underneath them. */
export interface SplitDocument {
    /** What this message added, in document order. */
    readonly contribution: readonly MailDocumentBlock[];

    /** The quotation it ended on, which a reader asks for rather than reads through. Empty where it ended on none. */
    readonly quotedHistory: readonly MailDocumentBlock[];
}

/**
 * Splits a message's blocks at the quotation it ends on.
 *
 * A message that is nothing but quotation — a forward, or a reply whose own words the reduction did not keep — is
 * answered whole rather than folded away, because a contribution of nothing is a screen with the message missing from
 * it rather than a tidy one.
 *
 * @param blocks The document's top-level blocks, in the order the message wrote them.
 * @returns What the message added, and the quotation under it.
 */
export function splitQuotedHistory(blocks: readonly MailDocumentBlock[]): SplitDocument {
    let at = blocks.length;

    while (at > 0 && trailing(blocks[at - 1])) {
        at -= 1;
    }

    const quotedHistory = blocks.slice(at);

    return at === 0 || !quotedHistory.some((block) => block.type === 'quote')
        ? { contribution: blocks, quotedHistory: [] }
        : { contribution: blocks.slice(0, at), quotedHistory };
}

// A separator immediately above the quotation is the rule a mail client draws between a reply and what it answers, so
// it belongs to the history rather than being left behind as a line under nothing. It is only ever taken as part of a
// run that a quotation anchors, which is what keeps a message ending on a rule from losing it.
function trailing(block: MailDocumentBlock | undefined): boolean {
    return block?.type === 'quote' || block?.type === 'separator';
}
