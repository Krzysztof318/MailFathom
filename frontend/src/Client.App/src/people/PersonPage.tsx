// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef } from 'react';
import type { Contact } from '@mailfathom/client-backend';
import { useComposing } from '../composer/useComposing';
import { Control } from '../controls/Control';
import { Icon } from '../controls/Icon';
import { SecondaryButton } from '../controls/SecondaryButton';
import { SenderAvatar } from '../controls/SenderAvatar';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { PersonCorrespondence } from './PersonCorrespondence';
import { RelationshipReading } from './RelationshipReading';
import type { ContactCorrespondenceInForce } from './useContactCorrespondence';

// One person as the design project draws them: who they are and how to reach them, the acts that belong to the record
// itself, and then what MailFathom and the mailbox have to say about them.
//
// **What the record admits is read off its origin rather than from a control being pressed.** A contact one of this
// user's mailboxes collected is theirs to take on or to erase and nobody's to amend, and the page says so on the record
// rather than leaving somebody to discover it from a refusal. Erasing is offered either way, because somebody asking to
// be taken out of a book is not answered with which book they happen to be in.
//
// **The way back is part of the page rather than of the frame**, and it is drawn only where the list and the person
// cannot stand together: at that width the two are one pane the reader moves between, so the control that returns is on
// the thing they moved to.
//
// **Opening somebody is a view change, so focus comes with it.** This page is not remounted between two people — the
// record and the correlation change underneath it — so nothing would move focus by itself, and at the width where the
// list is made inert behind this page there is not even a stale element left to hold it. The shape is the one
// `readingPane/ReadingPane.tsx` uses for the same thing: a ref holding who focus was last placed on, which survives
// StrictMode's second invocation where a flag would not.

export function PersonPage({
    contact,
    correspondence,
    writable,
    promoting,
    promotionSaid,
    onBack,
    onPromote,
    onAskErasure,
    onOpenThread,
    onOpenDocument,
}: {
    readonly contact: Contact;
    readonly correspondence: ContactCorrespondenceInForce;

    /** Whether this credential may change the book at all, which is what the two acts on the record are behind. */
    readonly writable: boolean;

    /** Whether a promotion asked for from this page has not answered yet. */
    readonly promoting: boolean;

    /** What the last promotion said, or `null` where none has been asked for. */
    readonly promotionSaid: MessageKey | null;

    /** Returns to the list, or `null` where the list is standing beside this page rather than behind it. */
    readonly onBack: (() => void) | null;

    readonly onPromote: () => void;
    readonly onAskErasure: () => void;

    readonly onOpenThread: (thread: {
        readonly threadId: string;
        readonly messageId: string;
        readonly subject: string | null;
    }) => void;

    readonly onOpenDocument: (document: { readonly messageId: string; readonly position: number }) => void;
}) {
    const { translate } = useLocalization();
    const composing = useComposing();
    const collected = contact.origin === 'Collected';

    const opened = useRef<HTMLDivElement>(null);
    // Starting at nobody rather than at whoever this mounted with, unlike the reading pane: that pane is mounted by
    // landing in the client, and this one is mounted by somebody opening a person out of the list beside it.
    const focusedOn = useRef<string | null>(null);

    useEffect(() => {
        if (focusedOn.current !== contact.id) {
            focusedOn.current = contact.id;
            opened.current?.focus();
        }
    }, [contact.id]);

    // When this person was last in touch, which the correlation answers rather than the record: the conversations come
    // back most recent first, so the first of them is the answer and no arithmetic is owed.
    const lastCorrespondedAt = correspondence.correspondence?.threads[0]?.lastCorrespondedAt ?? null;

    return (
        <div
            ref={opened}
            role="region"
            tabIndex={-1}
            aria-label={contact.displayName}
            className="flex min-h-0 min-w-0 flex-1 flex-col"
        >
            {onBack === null ? null : (
                <div className="shrink-0 border-b border-line bg-sunken px-3 py-2">
                    <Control label={translate('people.backToList')} icon="arrow_back" shape="named" onPress={onBack} />
                </div>
            )}

            <div className="flex shrink-0 flex-wrap items-center gap-3.5 border-b border-line bg-sunken px-4 py-3.5 workspace:px-6 workspace:py-4.5">
                <SenderAvatar displayName={contact.displayName} address={contact.preferredAddress} place="person" />

                <div className="flex min-w-0 flex-col gap-0.75">
                    <div className="flex min-w-0 items-center gap-2.25">
                        <h2 className="truncate text-2xl font-semibold tracking-tight">{contact.displayName}</h2>

                        {collected ? (
                            <span className="shrink-0 rounded-sm border border-line-strong px-1.75 py-0.5 text-2xs tracking-widest text-muted uppercase">
                                {translate('people.collected')}
                            </span>
                        ) : null}
                    </div>

                    <p className="truncate text-sm text-accent-deep">{contact.preferredAddress}</p>

                    {collected ? (
                        <p className="flex items-center gap-1.5 text-sm text-muted text-pretty">
                            <Icon name="lock" className="size-4" />
                            {translate('people.collectedReadOnly')}
                        </p>
                    ) : null}
                </div>

                <div className="ms-auto flex shrink-0 items-center gap-2.25">
                    {writable ? (
                        <Control
                            label={translate('people.deleteContact')}
                            icon="delete"
                            shape="symbol"
                            onPress={onAskErasure}
                        />
                    ) : null}

                    {collected && writable ? (
                        <SecondaryButton
                            label={translate(promoting ? 'people.promoting' : 'people.promote')}
                            shape="form"
                            onActivate={onPromote}
                        />
                    ) : null}

                    {composing.offered ? (
                        <Control
                            label={translate('people.write')}
                            icon="edit"
                            shape="primary"
                            onPress={() => {
                                composing.compose({ kind: 'new', to: [contact.preferredAddress] });
                            }}
                        />
                    ) : null}
                </div>

                {promotionSaid === null ? null : (
                    <p role="status" className="basis-full text-sm text-muted text-pretty">
                        {translate(promotionSaid)}
                    </p>
                )}
            </div>

            <div className="flex min-h-0 flex-1 flex-col gap-3.5 overflow-y-auto px-4 py-3.5 workspace:px-6 workspace:py-4.5">
                <RelationshipReading lastCorrespondedAt={lastCorrespondedAt} reading={correspondence.reading} />

                <PersonCorrespondence
                    reading={correspondence}
                    onOpenThread={onOpenThread}
                    onOpenDocument={onOpenDocument}
                />
            </div>
        </div>
    );
}
