// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailAttachment } from '@mailfathom/client-backend';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import type { AttachmentDeliveryOutcome } from '../deployment/attachmentDelivery';
import { sizeOf } from '../localization/octets';
import { kindOf } from './fileKind';

// One file a message carries. It is described before anything is fetched — what kind of file it is, what it is called,
// and how large it is — so that opening a message costs the same whether the sender attached a note or a video, and so
// that a reader decides whether a file is worth having before it starts arriving.
//
// It is drawn as the design project draws it: a chip naming the kind of file, its name, and its size, which is the
// control that fetches it. The row is its own component because it is what a reader can name — one file, and what
// became of asking for it. What it does not own is the asking: `Attachments.tsx` beside it holds every download,
// because downloading all of them is an act on the message rather than on any one row.

const refusalMessages: Readonly<Record<Exclude<AttachmentDeliveryOutcome, 'delivered'>, MessageKey>> = {
    abandoned: 'attachment.abandoned',
    unauthenticated: 'attachment.refusedUnauthenticated',
    unauthorized: 'attachment.refusedUnauthorized',
    unavailable: 'attachment.refusedUnavailable',
    largerThanDescribed: 'attachment.refusedLargerThanDescribed',
};

/** What is becoming of one file, which is one piece of state rather than a flag beside a count that has to agree. */
export type Downloading =
    | { readonly stage: 'arriving'; readonly octets: number }
    | { readonly stage: 'finished'; readonly outcome: AttachmentDeliveryOutcome };

export function Attachment({
    attachment,
    downloading,
    onDownload,
    onStop,
}: {
    readonly attachment: MailAttachment;

    /** What is becoming of this file, or nothing where nobody has asked for it yet. */
    readonly downloading: Downloading | undefined;

    readonly onDownload: () => void;
    readonly onStop: () => void;
}) {
    const { locale, translate } = useLocalization();

    const shown = attachment.fileName ?? translate('attachment.unnamed');
    const arriving = downloading?.stage === 'arriving';

    return (
        <li className="flex max-w-full flex-col gap-1 text-sm">
            {/* `aria-disabled` rather than `disabled` while a download runs, for the reason the body's own button
                gives: a disabled control is not focusable, so disabling the one somebody has just pressed drops their
                focus to the top of the document. The handler is what refuses the second press. */}
            <button
                aria-disabled={arriving}
                aria-label={translate('attachment.download', { name: shown })}
                title={attachment.mediaType}
                className="flex max-w-full cursor-pointer items-center gap-2.25 rounded-md border border-line bg-sunken px-3 py-2.25 text-start transition hover:border-accent hover:bg-hover aria-disabled:opacity-60"
                type="button"
                onClick={() => {
                    if (!arriving) {
                        onDownload();
                    }
                }}
            >
                <span className="shrink-0 text-xs text-muted uppercase">
                    {kindOf(attachment.fileName, attachment.mediaType)}
                </span>
                <span className="min-w-0 truncate text-md text-text">{shown}</span>
                <span className="shrink-0 text-faint">{sizeOf(attachment.sizeOctets, locale)}</span>
            </button>

            {/* Said where a sender wrote a name this client would not use. It is the case worth drawing carefully
                rather than one to hide: what is on the screen is not what the message wrote. */}
            {attachment.wasFileNameNormalized ? (
                <p className="text-muted">{translate('attachment.nameWasRewritten')}</p>
            ) : null}

            {downloading?.stage === 'arriving' ? (
                <Arriving octets={downloading.octets} of={attachment.sizeOctets} onStop={onStop} />
            ) : null}

            {downloading?.stage === 'finished' ? (
                <p
                    className={downloading.outcome === 'delivered' ? 'text-muted' : 'text-warning'}
                    role={downloading.outcome === 'delivered' ? undefined : 'alert'}
                >
                    {downloading.outcome === 'delivered'
                        ? translate('attachment.saved', { name: shown })
                        : translate(refusalMessages[downloading.outcome])}
                </p>
            ) : null}
        </li>
    );
}

// How much has arrived and the way to stop it, said in the place the file will appear. `progress` is the element the
// platform already has for this: it announces itself, it needs no role, and a reader on a screen reader is told a
// proportion rather than a number of octets they would have to divide themselves.
function Arriving({
    octets,
    of,
    onStop,
}: {
    readonly octets: number;
    readonly of: number;
    readonly onStop: () => void;
}) {
    const { locale, translate } = useLocalization();

    return (
        <div className="flex flex-wrap items-center gap-2">
            <progress aria-label={translate('attachment.arriving')} className="h-1 flex-1" max={of} value={octets} />

            <span className="text-muted">
                {translate('attachment.arrivingOf', { arrived: sizeOf(octets, locale), whole: sizeOf(of, locale) })}
            </span>

            <button
                className="rounded-md border border-line bg-panel px-2 py-0.5 text-text-soft"
                type="button"
                onClick={onStop}
            >
                {translate('attachment.stop')}
            </button>
        </div>
    );
}
