// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useState } from 'react';
import type { ClientSession, MailAttachment } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';
import { useOpenAttachment } from '../workspace/openAttachment';
import { Attachment } from './Attachment';
import { useAttachmentDownloads } from './downloadingAttachment';

// The files one message carries, and the downloads of them. Both belong here rather than to a row, because downloading
// every file is an act on the message: the row that would own its own download cannot be asked to start one by the
// control beside the list, and a parent reaching into a child to start one is the effect `frontend/src/AGENTS.md`
// refuses. So the strip owns what each file is doing and the row draws it.
//
// Opening a file is the other act it passes down, and it belongs here for the opposite reason: the file the viewer is
// handed carries the message it came from, which a row is never told and this component already knows.
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
    const open = useOpenAttachment();
    const downloads = useAttachmentDownloads(session, storedEmailId);
    const [downloadingAll, setDownloadingAll] = useState(false);

    // Each file is asked for in turn and each answer is recorded against the file it belongs to, so one refusal is one
    // file's refusal: the files after it are still asked for, and the reader is told which one did not arrive.
    async function startAll(): Promise<void> {
        setDownloadingAll(true);

        for (const attachment of attachments) {
            await downloads.start(attachment);
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
                    arriving={downloads.arriving(attachment.position)}
                    onOpen={() => {
                        open({ storedEmailId, attachment });
                    }}
                    onDownload={() => {
                        void downloads.start(attachment);
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
