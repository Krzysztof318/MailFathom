// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { createContext, useContext } from 'react';
import type { MailAttachment } from '@mailfathom/client-backend';

// One file a message carries, opened inside the client rather than saved. It stands in front of the message it belongs
// to exactly as a conversation does — the workspace still holds that message, so closing the file returns to it — and
// where the person works in tabs it is a tab of its own beside whatever else is open.
//
// It carries the whole description the message published rather than an identifier to look the file up by. What the
// viewer needs before it fetches anything is what the file declares itself to be and how large it says it is: both
// decide whether the file can be shown at all, and re-reading the message to learn them would be a second read of
// something the reader already had in front of them.
//
// How a screen opens one is here as well, for the reason `deployment/attachmentExchange.ts` gives about the operation
// it carries: the row that opens a file sits three components below the frame that owns what is open, and none of the
// three between them has a reason to name a file it never opens.

/** The file being read, and the message it belongs to. */
export interface OpenedAttachment {
    /** The message the file belongs to, as a read of that message published it. */
    readonly storedEmailId: string;

    /** What the message said about the file: where it sits in the message, what it is called, and how large it is. */
    readonly attachment: MailAttachment;
}

/**
 * The file's identity as one string, which is what a tab holding it is keyed by.
 *
 * The position rather than the name, because that is the only identity a message's parts have — two files a message
 * carries may be called the same thing, and opening the second would otherwise find the first already open.
 */
export function attachmentKey(opened: OpenedAttachment): string {
    return `${opened.storedEmailId}:${opened.attachment.position.toFixed(0)}`;
}

export const OpenAttachmentContext = createContext<((opened: OpenedAttachment) => void) | null>(null);

/** Opens one file a message carries, in the reading column. */
export function useOpenAttachment(): (opened: OpenedAttachment) => void {
    const open = useContext(OpenAttachmentContext);

    if (open === null) {
        throw new Error('A component opened a file outside the OpenAttachmentContext that App.tsx supplies.');
    }

    return open;
}
