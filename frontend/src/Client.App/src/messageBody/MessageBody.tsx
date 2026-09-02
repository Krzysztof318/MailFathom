// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef } from 'react';
import type {
    MailBody,
    MailBodyAvailability,
    MailDocument,
    MailDocumentBlock,
    MailDocumentRefusal,
} from '@mailfathom/client-backend';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import type { Locale } from '../localization/locale';
import { MessageBlocks } from './MessageBlocks';
import { splitQuotedHistory } from './quotedHistory';

// One message's body, drawn from the closed document tree the service reduced it to and never from markup. What this
// component owns is everything around the blocks: whether there is a body at all, whether the reduction refused it and
// the words are what is read instead, what the message asked to load from somebody else's server, and what a bound
// left out. None of it is silent — a fallback nobody is told about reads as a message the sender wrote badly.

const availabilityMessages: Readonly<Record<Exclude<MailBodyAvailability, 'Readable'>, MessageKey>> = {
    EncryptedNotReadableLocally: 'body.encryptedNotReadable',
    NotStoredExceededSizeLimit: 'body.notStoredExceededSizeLimit',
    NotStoredAwaitingStorageHeadroom: 'body.notStoredAwaitingStorageHeadroom',
};

const refusalMessages: Readonly<Record<Exclude<MailDocumentRefusal, 'None'>, MessageKey>> = {
    NoHtmlPart: 'body.refusedNoHtmlPart',
    ReductionFailed: 'body.refusedReductionFailed',
    NothingRenderable: 'body.refusedNothingRenderable',
};

export function MessageBody({
    body,
    asking,
    quotedHistoryOnRequest = false,
    onShowRemotePictures,
}: {
    readonly body: MailBody;
    readonly asking: boolean;

    /**
     * Whether the conversation this message quoted is folded away until a reader asks for it.
     *
     * Off where one message is what is being read, because there the quotation is the only context on the screen. On in
     * a thread, where the message it quotes is a row of its own a few lines up and drawing it again is the repetition
     * that makes a long conversation unreadable.
     */
    readonly quotedHistoryOnRequest?: boolean;

    readonly onShowRemotePictures: () => void;
}) {
    const { translate } = useLocalization();

    // The document that is actually drawn, which is one the reduction did not refuse. Everything else — a refusal, and
    // a body this deployment sent no document for — is the message read as words instead.
    const drawn = body.document?.refusal === 'None' ? body.document : null;

    if (body.availability !== 'Readable') {
        return <p className="text-warning">{translate(availabilityMessages[body.availability])}</p>;
    }

    return (
        <div className="flex flex-col gap-4">
            {body.document === null ? null : (
                <RemoteContent
                    document={body.document}
                    requested={body.remoteImagesRequested}
                    asking={asking}
                    onShow={onShowRemotePictures}
                />
            )}

            {drawn === null ? (
                <ReadAsWords body={body} />
            ) : (
                <article className="flex flex-col gap-3">
                    <Written blocks={drawn.blocks} quotedHistoryOnRequest={quotedHistoryOnRequest} />
                    {drawn.truncated ? <p className="text-sm text-muted">{translate('body.truncated')}</p> : null}
                </article>
            )}
        </div>
    );
}

// What the message says, with the conversation it quoted either under the words or one gesture away from them. The
// disclosure is the browser's own rather than one built out of a button and a piece of state, for the reason
// `readingPane/MessageHeaders.tsx` gives: it is operable from the keyboard and announces whether it is open, and it
// costs no code to be so.
function Written({
    blocks,
    quotedHistoryOnRequest,
}: {
    readonly blocks: readonly MailDocumentBlock[];
    readonly quotedHistoryOnRequest: boolean;
}) {
    const { translate } = useLocalization();
    const written = quotedHistoryOnRequest ? splitQuotedHistory(blocks) : null;

    if (written === null || written.quotedHistory.length === 0) {
        return <MessageBlocks blocks={blocks} />;
    }

    return (
        <>
            <MessageBlocks blocks={written.contribution} />

            <details>
                <summary className="cursor-pointer text-sm text-muted">{translate('body.quotedHistory')}</summary>

                <div className="mt-3 flex flex-col gap-3">
                    <MessageBlocks blocks={written.quotedHistory} />
                </div>
            </details>
        </>
    );
}

function ReadAsWords({ body }: { readonly body: MailBody }) {
    const { translate } = useLocalization();

    const refusal = body.document?.refusal ?? 'None';
    const reason = refusal === 'None' ? 'body.notReduced' : refusalMessages[refusal];

    return (
        <article className="flex flex-col gap-3">
            <p className="text-sm text-muted">{translate(reason)}</p>
            <p className="whitespace-pre-wrap">{body.plainText.text}</p>
            {body.plainText.truncation === 'None' ? null : (
                <p className="text-sm text-muted">{translate('body.textTruncated')}</p>
            )}
        </article>
    );
}

// What the message asked to load from somebody else's server, and the reader's own answer to it. Asking is a request
// this read carries and nothing on either side writes down: the addresses were removed while the tree was built, so
// there is nothing here to decline, and opening the message again asks again.
function RemoteContent({
    document,
    requested,
    asking,
    onShow,
}: {
    readonly document: MailDocument;
    readonly requested: boolean;
    readonly asking: boolean;
    readonly onShow: () => void;
}) {
    const { locale, translate } = useLocalization();

    // Where the answered notice replaces the button somebody pressed, their focus would fall to the document body and
    // keyboard reading would restart at the top of the message. So the notice takes the focus the button had, and only
    // where the button was actually on the screen first: a message read with the pictures already asked for shows the
    // notice from its first paint and takes focus from nobody.
    const answered = useRef<HTMLElement>(null);
    const asked = useRef(false);

    useEffect(() => {
        if (!requested) {
            asked.current = true;
        } else if (asked.current) {
            answered.current?.focus();
        }
    }, [requested]);

    if (requested) {
        return (
            <aside
                className="flex flex-col gap-1 rounded-md border border-line-soft bg-sunken px-4 py-3 text-sm"
                ref={answered}
                tabIndex={-1}
            >
                <p>{translate('body.remotePicturesShown')}</p>
                <p className="text-muted">
                    {translate('body.remotePicturesShownCount', {
                        count: count(locale, document.retainedRemoteImageCount),
                    })}
                </p>
                <UndrawnPictures undrawn={document.undrawnInlineImageCount} />
            </aside>
        );
    }

    if (document.removedRemoteReferenceCount === 0) {
        return <UndrawnPictures undrawn={document.undrawnInlineImageCount} />;
    }

    return (
        <aside className="flex flex-col items-start gap-2 rounded-md border border-line-soft bg-sunken px-4 py-3 text-sm">
            <p>{translate('body.remoteContentRemoved')}</p>
            <p className="text-muted">
                {translate('body.remoteContentRemovedCount', {
                    count: count(locale, document.removedRemoteReferenceCount),
                })}
            </p>
            <p className="text-muted">{translate('body.showRemotePicturesReveals')}</p>

            {/* The button stays where it is while the read it started is in flight: what a reader clicked is what
                their focus is on, and a message swapped for a one-line notice would drop that focus to the top of the
                document and move everything below it under their cursor. It is `aria-disabled` rather than `disabled`
                for the same reason — a disabled control is not focusable, so disabling the element somebody has just
                pressed is itself what drops their focus to the body. The handler is what refuses the second press. */}
            <button
                aria-disabled={asking}
                className="rounded-md bg-accent px-3 py-1 font-medium text-on-accent aria-disabled:opacity-60"
                type="button"
                onClick={() => {
                    if (!asking) {
                        onShow();
                    }
                }}
            >
                {translate('body.showRemotePictures')}
            </button>

            {asking ? <p className="text-muted">{translate('body.remotePicturesLoading')}</p> : null}
            <UndrawnPictures undrawn={document.undrawnInlineImageCount} />
        </aside>
    );
}

function UndrawnPictures({ undrawn }: { readonly undrawn: number }) {
    const { locale, translate } = useLocalization();

    return undrawn === 0 ? null : (
        <p className="text-sm text-muted">
            {translate('body.undrawnPicturesCount', { count: count(locale, undrawn) })}
        </p>
    );
}

// A number a person reads is written by the platform under the active locale rather than by a catalogue, which is the
// same rule a date and a relative time follow.
function count(locale: Locale, value: number): string {
    return new Intl.NumberFormat(locale).format(value);
}
