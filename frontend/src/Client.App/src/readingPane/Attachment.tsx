// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState } from 'react';
import { readMailAttachment, type ClientSession, type MailAttachment } from '@mailfathom/client-backend';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import {
    deliveryFailureOf,
    useAttachmentDelivery,
    type AttachmentDeliveryOutcome,
} from '../deployment/attachmentDelivery';
import { sizeOf } from './octets';
import { savedAs } from './savedFileName';

// One file a message carries. It is described before anything is fetched — what it is called, what it declares itself to
// be, and how large it is — so that opening a message costs the same whether the sender attached a note or a video, and
// so that a reader decides whether a file is worth having before it starts arriving.
//
// It is drawn as the design project draws it: a chip naming the kind of file, its name, and its size, which is the
// control that fetches it. The row is its own component because it is what gains state: a download in flight, how much
// of it has arrived, a way to stop it, and what became of it.

const refusalMessages: Readonly<Record<Exclude<AttachmentDeliveryOutcome, 'delivered'>, MessageKey>> = {
    abandoned: 'attachment.abandoned',
    unauthenticated: 'attachment.refusedUnauthenticated',
    unauthorized: 'attachment.refusedUnauthorized',
    unavailable: 'attachment.refusedUnavailable',
    largerThanDescribed: 'attachment.refusedLargerThanDescribed',
};

// The most of a kind the chip shows. A kind is a file's extension where the name has one and the media subtype where it
// does not, and either can be long enough to be a sentence; the chip is a glance rather than a description.
const longestKind = 8;

/** What the row is doing, which is one piece of state rather than a flag beside a count that has to agree with it. */
type Downloading =
    | { readonly stage: 'described' }
    | { readonly stage: 'arriving'; readonly octets: number }
    | { readonly stage: 'finished'; readonly outcome: AttachmentDeliveryOutcome };

export function Attachment({
    session,
    storedEmailId,
    attachment,
}: {
    readonly session: ClientSession;
    readonly storedEmailId: string;
    readonly attachment: MailAttachment;
}) {
    const { locale, translate } = useLocalization();
    const deliver = useAttachmentDelivery();
    const [downloading, setDownloading] = useState<Downloading>({ stage: 'described' });

    // The one thing a render does not own: a download in flight outlives the render that started it, and the way out of
    // it has to be reachable from the button that stops it and from the cleanup below alike.
    const running = useRef<AbortController | null>(null);

    // A download whose row has gone is a download nobody is waiting for, and letting it finish would write a file to
    // somebody's machine after they left the message it belongs to.
    useEffect(
        () => () => {
            running.current?.abort();
        },
        [],
    );

    const shown = attachment.fileName ?? translate('attachment.unnamed');
    const arriving = downloading.stage === 'arriving';

    function start(): void {
        if (arriving) {
            return;
        }

        const abandoning = new AbortController();
        running.current = abandoning;
        setDownloading({ stage: 'arriving', octets: 0 });

        // The request is composed by `Client.Backend` inside the span it opens around the whole download, which is
        // what puts this row's wait in the same trace as the deployment's work on it. What this component supplies is
        // the part that is the screen's: where the file is saved, how much of it has arrived, and the way out.
        void readMailAttachment(
            session,
            storedEmailId,
            attachment.position,
            attachment.sizeOctets,
            (request) =>
                deliver(
                    request,
                    savedAs(attachment.fileName, attachment.position),
                    (octets) => {
                        setDownloading({ stage: 'arriving', octets });
                    },
                    abandoning.signal,
                ),
            deliveryFailureOf,
        ).then((outcome) => {
            running.current = null;
            setDownloading({ stage: 'finished', outcome });
        });
    }

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
                onClick={start}
            >
                <span className="shrink-0 text-xs font-semibold tracking-wide text-muted uppercase">
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

            {downloading.stage === 'arriving' ? (
                <Arriving
                    octets={downloading.octets}
                    of={attachment.sizeOctets}
                    onStop={() => {
                        running.current?.abort();
                    }}
                />
            ) : null}

            {downloading.stage === 'finished' ? (
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

/** The kind of file the chip names: the name's extension where it has one, and the declared subtype otherwise. */
function kindOf(fileName: string | null, mediaType: string): string {
    const extension = fileName?.match(/\.([A-Za-z0-9]{1,8})$/)?.[1];
    const subtype = mediaType.split('/')[1]?.split(/[+.;]/)[0] ?? '';

    return (extension ?? subtype).slice(0, longestKind);
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
