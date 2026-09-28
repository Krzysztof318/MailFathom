// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';

// The reply the field under a correspondence asked the deployment to write, which two places read and neither owns: the
// field at the foot of the reading column asks for it, and the card inside the thread draws it. The two are siblings a
// scroller apart, so what they share is held by the frame — `useReplyDraftingState.ts` — and handed to each of them.
//
// It is held in memory and nowhere else. A draft is words the deployment wrote about somebody's mail, which ADR 0028
// keeps off the device, and one that outlived a reload would be a copy of mail nobody asked to keep — so a reload, like
// *Discard draft*, is the end of it. What survives is a draft somebody chose to open in the composer, because what is
// being written is the one thing that rule lets the tab keep.

/** A reply asked for under a correspondence: still being written, or written and waiting on the reader. */
export interface ThreadDraft {
    /** The message the reply answers, which is the one the reading column was opened at. */
    readonly answering: string;

    /** What the deployment wrote, or `null` while it is still writing. */
    readonly body: string | null;
}

/** What the field and the card share about the reply being drafted. */
export interface ReplyDrafting {
    /** The reply asked for last, or `null` where none has been asked for or the last one was let go. */
    readonly draft: ThreadDraft | null;

    /**
     * Asks the deployment to write a reply.
     *
     * @param answering The message the reply answers.
     * @param typed What the reader typed into the field, which may be nothing: the press is a complete request.
     * @param passage The words the reader selected in what they are reading, or `null` where they selected none.
     */
    readonly draftReply: (answering: string, typed: string, passage: string | null) => void;

    /** Lets the reply go, which is *Discard draft* and *Open in composer* alike: the card has done its work. */
    readonly letGo: () => void;

    /**
     * Takes hold of the field the reply is asked for in, as the ref that field is drawn with.
     *
     * The card and the field are a scroller apart, so neither can hand the other a ref; the one thing both already
     * share is this.
     */
    readonly holdField: (field: HTMLInputElement | null) => void;

    /** Puts the keyboard back on that field, which is where focus goes when the card it drew is discarded. */
    readonly returnToField: () => void;
}

export const ReplyDraftingContext = createContext<ReplyDrafting | null>(null);

/** The reply being drafted under a correspondence, or `null` outside the frame that drafts one. */
export function useReplyDrafting(): ReplyDrafting | null {
    return useContext(ReplyDraftingContext);
}
