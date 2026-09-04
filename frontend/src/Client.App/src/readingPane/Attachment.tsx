// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailAttachment } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';
import { sizeOf } from '../localization/octets';
import { Downloading } from './Downloading';
import type { Download } from './downloadingAttachment';
import { kindOf } from './fileKind';

// One file a message carries. It is described before anything is fetched — what kind of file it is, what it is called,
// and how large it is — so that opening a message costs the same whether the sender attached a note or a video, and so
// that a reader decides whether a file is worth having before it starts arriving.
//
// It is drawn as the design project draws it: a chip naming the kind of file, its name, and its size, with a control of
// its own at the end. **The two are separate controls because they are separate acts** — the chip opens the file inside
// the client and the control beside it writes it to the person's machine — and a reader who wanted to look at something
// should not have to find it in a downloads folder afterwards. A file this client cannot draw opens all the same: what
// the viewer then says is that it cannot be shown, beside the download, which is a screen that answers rather than a
// control that does nothing.
//
// The row is its own component because it is what a reader can name — one file, and what became of asking for it. What
// it does not own is the asking: `Attachments.tsx` beside it holds every download, because downloading all of them is
// an act on the message rather than on any one row.

export function Attachment({
    attachment,
    downloading,
    onOpen,
    onDownload,
    onStop,
}: {
    readonly attachment: MailAttachment;

    /** What is becoming of this file, which is `described` where nobody has asked for it yet. */
    readonly downloading: Download;

    readonly onOpen: () => void;
    readonly onDownload: () => void;
    readonly onStop: () => void;
}) {
    const { locale, translate } = useLocalization();

    const shown = attachment.fileName ?? translate('attachment.unnamed');
    const arriving = downloading.stage === 'arriving';

    return (
        <li className="flex max-w-full flex-col gap-1 text-sm">
            <div className="flex max-w-full items-stretch overflow-hidden rounded-md border border-line bg-sunken transition hover:border-accent">
                <button
                    aria-label={translate('attachment.open', { name: shown })}
                    title={attachment.mediaType}
                    className="flex min-w-0 cursor-pointer items-center gap-2.25 px-3 py-2.25 text-start transition hover:bg-hover"
                    type="button"
                    onClick={onOpen}
                >
                    <span className="shrink-0 text-xs text-muted uppercase">
                        {kindOf(attachment.fileName, attachment.mediaType)}
                    </span>
                    <span className="min-w-0 truncate text-md text-text">{shown}</span>
                    <span className="shrink-0 text-faint">{sizeOf(attachment.sizeOctets, locale)}</span>
                </button>

                {/* `aria-disabled` rather than `disabled` while a download runs, for the reason the body's own button
                    gives: a disabled control is not focusable, so disabling the one somebody has just pressed drops
                    their focus to the top of the document. The handler is what refuses the second press. */}
                <button
                    aria-disabled={arriving}
                    aria-label={translate('attachment.download', { name: shown })}
                    className="flex w-8 shrink-0 cursor-pointer items-center justify-center border-s border-line text-muted transition hover:bg-hover hover:text-text aria-disabled:opacity-60"
                    type="button"
                    onClick={() => {
                        if (!arriving) {
                            onDownload();
                        }
                    }}
                >
                    <Icon name="download" className="size-4.25" />
                </button>
            </div>

            {/* Said where a sender wrote a name this client would not use. It is the case worth drawing carefully
                rather than one to hide: what is on the screen is not what the message wrote. */}
            {attachment.wasFileNameNormalized ? (
                <p className="text-muted">{translate('attachment.nameWasRewritten')}</p>
            ) : null}

            <Downloading download={downloading} name={shown} whole={attachment.sizeOctets} onStop={onStop} />
        </li>
    );
}
