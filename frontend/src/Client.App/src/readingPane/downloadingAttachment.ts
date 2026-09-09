// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState } from 'react';
import { readMailAttachment, type ClientSession, type MailAttachment } from '@mailfathom/client-backend';
import {
    deliveryFailureOf,
    useAttachmentExchange,
    type AttachmentExchange,
    type AttachmentDeliveryOutcome,
} from '../deployment/attachmentExchange';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useToasts } from '../toasts/useToasts';
import { savedAs } from './savedFileName';

// Downloading the files a message carries, which two surfaces offer: the strip under a message, and the viewer that
// opens one inside the client and says so where it cannot. Both reach it through the one hook below, because they
// differ in how many files they offer rather than in what one download is.
//
// **A download reports from the corner rather than from the message.** The design project raises a task for it — one
// that says the file is on its way, carries the way out of it, and turns into what became of it — and the client
// already has that surface in `toasts/useToasts.ts`. A line written under the words of the message instead was this
// client's own arrangement, and it put the answer to *did that file arrive* in the middle of something being read.
//
// What is left on the row is whether a file is arriving, which is the row's own question: it is what refuses a second
// press on a control somebody has already pressed.

const refusalMessages: Readonly<Record<Exclude<AttachmentDeliveryOutcome, 'delivered'>, MessageKey>> = {
    abandoned: 'attachment.abandoned',
    unauthenticated: 'attachment.refusedUnauthenticated',
    unauthorized: 'attachment.refusedUnauthorized',
    unavailable: 'attachment.refusedUnavailable',
    screened: 'attachment.refusedScreened',
    largerThanDescribed: 'attachment.refusedLargerThanDescribed',
};

/**
 * Asks the deployment for one file and hands it to the person's machine.
 *
 * @param session Who is asking, and where.
 * @param storedEmailId The message the file belongs to.
 * @param attachment What the message said about the file, which is what the request is bounded by.
 * @param exchange What carries the answer out of the client, which the composition root supplies.
 * @param abandoned Abandons the download, which is the way out of a wait.
 */
export function downloadAttachment(
    session: ClientSession,
    storedEmailId: string,
    attachment: MailAttachment,
    exchange: AttachmentExchange,
    abandoned: AbortSignal,
): Promise<AttachmentDeliveryOutcome> {
    // The request is composed by `Client.Backend` inside the span it opens around the whole download, which is what
    // puts this wait in the same trace as the deployment's work on it. What this supplies is the part that is the
    // screen's: where the file is saved, and the way out.
    return readMailAttachment(
        session,
        storedEmailId,
        attachment.position,
        attachment.sizeOctets,
        (request) =>
            exchange.deliver(request, savedAs(attachment.fileName, attachment.position), () => undefined, abandoned),
        deliveryFailureOf,
    );
}

/** The downloads one message's files are having, as the surface offering them holds them. */
export interface AttachmentDownloads {
    /** Whether the file at that position is on its way, which is what refuses a second press. */
    readonly arriving: (position: number) => boolean;

    /** Asks for one file, and does nothing where it is already arriving. Settles when that file has. */
    readonly start: (attachment: MailAttachment) => Promise<void>;
}

/**
 * Holds the downloads of one message's files and reports each of them as a task.
 *
 * @param session Who is asking, and where.
 * @param storedEmailId The message the files belong to.
 */
export function useAttachmentDownloads(session: ClientSession, storedEmailId: string): AttachmentDownloads {
    const { translate } = useLocalization();
    const exchange = useAttachmentExchange();
    const toasts = useToasts();
    const [arriving, setArriving] = useState<ReadonlySet<number>>(new Set());

    // The one thing a render does not own: a download in flight outlives the render that started it, and the way out of
    // it has to be reachable from the task that stops it and from the cleanup below alike.
    const running = useRef(new Map<number, AbortController>());

    // Whether the message these files belong to is still on the screen. Abandoning what is in flight is not enough on
    // its own: a caller asking for every file is a loop that asks for the next once the one before it has settled, and
    // a loop that kept going would start a download the cleanup below has already run past.
    const opened = useRef(true);

    // A download whose message has gone is a download nobody is waiting for, and letting it finish would write a file
    // to somebody's machine after they left the message it belongs to.
    useEffect(() => {
        const abandoning = running.current;
        opened.current = true;

        return () => {
            opened.current = false;

            for (const download of abandoning.values()) {
                download.abort();
            }
        };
    }, []);

    function record(position: number, on: boolean): void {
        setArriving((current) => {
            const moved = new Set(current);

            if (on) {
                moved.add(position);
            } else {
                moved.delete(position);
            }

            return moved;
        });
    }

    return {
        arriving: (position) => arriving.has(position),

        start: async (attachment) => {
            // A file already arriving is left to arrive. The control refuses a second press for the same reason, and a
            // caller asking for every file reaches ones somebody may already have asked for one at a time.
            if (!opened.current || running.current.has(attachment.position)) {
                return;
            }

            const named = attachment.fileName ?? translate('attachment.unnamed');
            const abandoning = new AbortController();

            running.current.set(attachment.position, abandoning);
            record(attachment.position, true);

            const settled = toasts.raiseOperation({
                title: translate('attachment.downloading'),
                body: named,
                stoppingLeavesBehind: translate('attachment.stoppingLeavesBehind', { name: named }),
                stop: () => {
                    abandoning.abort();
                },
            });

            const outcome = await downloadAttachment(session, storedEmailId, attachment, exchange, abandoning.signal);

            running.current.delete(attachment.position);
            record(attachment.position, false);

            settled(
                outcome === 'delivered'
                    ? { kind: 'success', title: translate('attachment.downloaded'), body: named }
                    : {
                          kind: 'error',
                          title: translate('attachment.notDownloaded'),
                          body: translate(refusalMessages[outcome]),
                      },
            );
        },
    };
}
