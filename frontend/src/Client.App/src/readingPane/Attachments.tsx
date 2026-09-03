// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState } from 'react';
import { readMailAttachment, type ClientSession, type MailAttachment } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';
import { deliveryFailureOf, useAttachmentDelivery } from '../deployment/attachmentDelivery';
import { Attachment, type Downloading } from './Attachment';
import { savedAs } from './savedFileName';

// The files one message carries, and the downloads of them. Both belong here rather than to a row, because downloading
// every file is an act on the message: the row that would own its own download cannot be asked to start one by the
// control beside the list, and a parent reaching into a child to start one is the effect `frontend/src/AGENTS.md`
// refuses. So the strip owns what each file is doing and the row draws it.
//
// The bulk download drives the same per-file download the chip does, one file after the next, rather than asking the
// deployment for a bundle: the route serves one part, and what a reader gets is the same files under the same names.
// Waiting for each before starting the next is what keeps a message carrying twenty files from opening twenty requests
// and holding twenty answers in memory at once. What that costs is that each file arrives as its own download, which is
// where this differs from the design project — that draws one archive, which no route serves.

export function Attachments({
    session,
    storedEmailId,
    attachments,
}: {
    readonly session: ClientSession;
    readonly storedEmailId: string;
    readonly attachments: readonly MailAttachment[];
}) {
    const { translate } = useLocalization();
    const deliver = useAttachmentDelivery();
    const [downloading, setDownloading] = useState<ReadonlyMap<number, Downloading>>(new Map());
    const [downloadingAll, setDownloadingAll] = useState(false);

    // The one thing a render does not own: a download in flight outlives the render that started it, and the way out of
    // it has to be reachable from the control that stops it and from the cleanup below alike.
    const running = useRef(new Map<number, AbortController>());

    // Whether the message these files belong to is still on the screen. Abandoning what is in flight is not enough on
    // its own: the bulk download is a loop that asks for the next file once the one before it has settled, and a loop
    // that kept going would start a download the cleanup below has already run past.
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

    function record(position: number, stage: Downloading): void {
        setDownloading((current) => new Map(current).set(position, stage));
    }

    async function start(attachment: MailAttachment): Promise<void> {
        // A file already arriving is left to arrive. The chip refuses a second press for the same reason, and the bulk
        // download reaches files somebody may already have asked for one at a time.
        if (!opened.current || running.current.has(attachment.position)) {
            return;
        }

        const abandoning = new AbortController();
        running.current.set(attachment.position, abandoning);
        record(attachment.position, { stage: 'arriving', octets: 0 });

        // The request is composed by `Client.Backend` inside the span it opens around the whole download, which is
        // what puts this wait in the same trace as the deployment's work on it. What this component supplies is the
        // part that is the screen's: where the file is saved, how much of it has arrived, and the way out.
        const outcome = await readMailAttachment(
            session,
            storedEmailId,
            attachment.position,
            attachment.sizeOctets,
            (request) =>
                deliver(
                    request,
                    savedAs(attachment.fileName, attachment.position),
                    (octets) => {
                        record(attachment.position, { stage: 'arriving', octets });
                    },
                    abandoning.signal,
                ),
            deliveryFailureOf,
        );

        running.current.delete(attachment.position);
        record(attachment.position, { stage: 'finished', outcome });
    }

    // Each file is asked for in turn and each answer is recorded against the file it belongs to, so one refusal is one
    // file's refusal: the files after it are still asked for, and the reader is told which one did not arrive.
    async function startAll(): Promise<void> {
        setDownloadingAll(true);

        for (const attachment of attachments) {
            await start(attachment);
        }

        setDownloadingAll(false);
    }

    return (
        // The bulk control is an item of this list rather than something beside it, which is how the design draws it:
        // one row that wraps as one, with the control following the last file however many lines the files took.
        <ul aria-label={translate('attachments.list')} className="flex flex-wrap items-center gap-2.25">
            {attachments.map((attachment) => (
                <Attachment
                    key={attachment.position}
                    attachment={attachment}
                    downloading={downloading.get(attachment.position)}
                    onDownload={() => {
                        void start(attachment);
                    }}
                    onStop={() => {
                        running.current.get(attachment.position)?.abort();
                    }}
                />
            ))}

            {/* Offered where there is more than one file to download, which is where it saves a press: the design
                project draws it on exactly that message and nothing else. */}
            {attachments.length > 1 ? (
                <li>
                    <button
                        aria-disabled={downloadingAll}
                        className="flex cursor-pointer items-center gap-1.5 rounded-md bg-accent-soft px-3 py-2.25 text-sm text-accent-deep transition hover:bg-accent-strong hover:text-on-accent aria-disabled:opacity-60"
                        type="button"
                        onClick={() => {
                            if (!downloadingAll) {
                                void startAll();
                            }
                        }}
                    >
                        <Icon name="download" className="size-4" />
                        {translate('attachments.downloadAll')}
                    </button>
                </li>
            ) : null}
        </ul>
    );
}
