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
import { savedAs } from './savedFileName';

// Downloading one file a message carries, which two surfaces offer: the strip under a message, and the viewer that
// opens one inside the client and says so where it cannot. What is written once is the asking — where the file is
// saved, how its arrival is reported, and what its outcome is called — because the two surfaces differ in how many
// downloads they hold at a time rather than in what one download is.

/** What a download is doing, which is one piece of state rather than a flag beside a count that has to agree with it. */
export type Download =
    | { readonly stage: 'described' }
    | { readonly stage: 'arriving'; readonly octets: number }
    | { readonly stage: 'finished'; readonly outcome: AttachmentDeliveryOutcome };

/**
 * Asks the deployment for one file and hands it to the person's machine.
 *
 * @param session Who is asking, and where.
 * @param storedEmailId The message the file belongs to.
 * @param attachment What the message said about the file, which is what the request is bounded by.
 * @param exchange What carries the answer out of the client, which the composition root supplies.
 * @param arrived Told how much has arrived so far, as it arrives.
 * @param abandoned Abandons the download, which is the way out of a wait.
 */
export function downloadAttachment(
    session: ClientSession,
    storedEmailId: string,
    attachment: MailAttachment,
    exchange: AttachmentExchange,
    arrived: (octets: number) => void,
    abandoned: AbortSignal,
): Promise<AttachmentDeliveryOutcome> {
    // The request is composed by `Client.Backend` inside the span it opens around the whole download, which is what
    // puts this wait in the same trace as the deployment's work on it. What this supplies is the part that is the
    // screen's: where the file is saved, how much of it has arrived, and the way out.
    return readMailAttachment(
        session,
        storedEmailId,
        attachment.position,
        attachment.sizeOctets,
        (request) => exchange.deliver(request, savedAs(attachment.fileName, attachment.position), arrived, abandoned),
        deliveryFailureOf,
    );
}

/** A download, as the screen offering one at a time holds it. */
export interface DownloadingAttachment {
    readonly download: Download;

    /** Starts the download, and does nothing where one is already arriving. */
    readonly start: () => void;

    /** Abandons the download in flight, which is the way out of a wait. */
    readonly stop: () => void;
}

/**
 * Holds one file's download, for a surface that shows that file alone.
 *
 * @param session Who is asking, and where.
 * @param storedEmailId The message the file belongs to.
 * @param attachment What the message said about the file, which is what the request is bounded by.
 */
export function useDownloadingAttachment(
    session: ClientSession,
    storedEmailId: string,
    attachment: MailAttachment,
): DownloadingAttachment {
    const exchange = useAttachmentExchange();
    const [download, setDownload] = useState<Download>({ stage: 'described' });

    // The one thing a render does not own: a download in flight outlives the render that started it, and the way out of
    // it has to be reachable from the button that stops it and from the cleanup below alike.
    const running = useRef<AbortController | null>(null);

    // A download whose screen has gone is a download nobody is waiting for, and letting it finish would write a file to
    // somebody's machine after they left the message it belongs to.
    useEffect(
        () => () => {
            running.current?.abort();
        },
        [],
    );

    return {
        download,

        start: () => {
            if (download.stage === 'arriving') {
                return;
            }

            const abandoning = new AbortController();
            running.current = abandoning;
            setDownload({ stage: 'arriving', octets: 0 });

            void downloadAttachment(
                session,
                storedEmailId,
                attachment,
                exchange,
                (octets) => {
                    setDownload({ stage: 'arriving', octets });
                },
                abandoning.signal,
            ).then((outcome) => {
                running.current = null;
                setDownload({ stage: 'finished', outcome });
            });
        },

        stop: () => {
            running.current?.abort();
        },
    };
}
