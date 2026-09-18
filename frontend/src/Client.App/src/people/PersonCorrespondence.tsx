// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ReactNode } from 'react';
import type { ClientFailureReason } from '@mailfathom/client-backend';
import { ReceivedAt } from '../controls/ReceivedAt';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { kindOf } from '../readingPane/fileKind';
import type { ContactCorrespondenceInForce } from './useContactCorrespondence';

// The two columns at the foot of a person's own page: the conversations this deployment's mail says they take part in,
// and the documents they sent. Both are read from one route — the correlation is computed over the mail index rather
// than stored — so they share a wait and a failure and each has an empty state of its own, because a person who has
// written and sent nothing is a different answer from a person who has written and attached nothing.
//
// **Neither column holds the rest of the page up.** The record is what the person's page is; this is what the mailbox
// adds to it, and a correlation that is still running or has failed leaves the name, the address and the acts above it
// exactly where they were.
//
// Every identity here is one another route is asked with directly, so a row opens the conversation or the file without
// anything having to be resolved first. What a document's row opens is the file, which is the same two values a search
// result cites one with: which file of which message, with the message's own read turning it into an opened file.

const correspondenceFailures: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'person.correspondenceUnauthenticated',
    unauthorized: 'person.correspondenceUnauthorized',
    unavailable: 'person.correspondenceUnavailable',
    unreadable: 'person.correspondenceUnreadable',
    missing: 'person.correspondenceMissing',
};

export function PersonCorrespondence({
    reading,
    onOpenThread,
    onOpenDocument,
}: {
    readonly reading: ContactCorrespondenceInForce;

    /** Opens one conversation at the message that last named this person. */
    readonly onOpenThread: (thread: {
        readonly threadId: string;
        readonly messageId: string;
        readonly subject: string | null;
    }) => void;

    /** Opens one file, named as which file of which message. */
    readonly onOpenDocument: (document: { readonly messageId: string; readonly position: number }) => void;
}) {
    const { translate } = useLocalization();
    const threads = reading.correspondence?.threads ?? [];
    const documents = reading.correspondence?.documents ?? [];

    return (
        <div className="flex flex-col gap-3 split:flex-row">
            <CorrespondenceColumn heading={translate('person.sharedThreads')} reading={reading}>
                {threads.length === 0 ? (
                    <p className="border-t border-line-soft pt-2.25 text-sm text-faint text-pretty">
                        {translate('person.noThreads')}
                    </p>
                ) : (
                    threads.map((thread) => (
                        <button
                            key={thread.threadId}
                            type="button"
                            className="flex flex-col items-start gap-0.75 border-t border-line-soft pt-2.25 text-start hover:text-accent-deep"
                            onClick={() => {
                                onOpenThread({
                                    threadId: thread.threadId,
                                    messageId: thread.latestMessageId,
                                    subject: thread.subject,
                                });
                            }}
                        >
                            <span className="text-base text-pretty">
                                {thread.subject ?? translate('list.noSubject')}
                            </span>
                            <ReceivedAt at={thread.lastCorrespondedAt} />
                        </button>
                    ))
                )}
            </CorrespondenceColumn>

            <CorrespondenceColumn heading={translate('person.documents')} reading={reading}>
                {documents.length === 0 ? (
                    <p className="border-t border-line-soft pt-2.25 text-sm text-faint text-pretty">
                        {translate('person.noDocuments')}
                    </p>
                ) : (
                    documents.map((document) => (
                        <button
                            key={`${document.messageId}:${document.position.toFixed(0)}`}
                            type="button"
                            className="flex items-center gap-2.25 border-t border-line-soft pt-2.25 text-start hover:text-accent-deep"
                            onClick={() => {
                                onOpenDocument({ messageId: document.messageId, position: document.position });
                            }}
                        >
                            <span className="shrink-0 text-xs tracking-wide text-muted uppercase">
                                {kindOf(document.fileName, document.mediaType)}
                            </span>
                            <span className="min-w-0 flex-1 truncate text-base">
                                {document.fileName ?? translate('person.unnamedDocument')}
                            </span>
                            <ReceivedAt at={document.receivedAt} />
                        </button>
                    ))
                )}
            </CorrespondenceColumn>
        </div>
    );
}

// One of the two columns: its heading, then whatever the correlation has to say — the wait, the failure and the way out
// of it, or the rows themselves. The three states are here rather than in each caller so that both columns say the same
// thing about one read.
function CorrespondenceColumn({
    heading,
    reading,
    children,
}: {
    readonly heading: string;
    readonly reading: ContactCorrespondenceInForce;
    readonly children: ReactNode;
}) {
    const { translate } = useLocalization();

    return (
        <section
            aria-label={heading}
            className="flex min-w-0 flex-1 flex-col gap-2.5 rounded-xl border border-line bg-panel px-4.25 py-3.75"
        >
            <h3 className="text-xs tracking-widest text-muted uppercase">{heading}</h3>

            {reading.reading ? (
                <p role="status" className="text-sm text-muted">
                    {translate('person.correspondenceReading')}
                </p>
            ) : reading.failure !== null ? (
                <div className="flex flex-col items-start gap-2">
                    <p role="alert" className="text-sm text-warning text-pretty">
                        {translate(correspondenceFailures[reading.failure])}
                    </p>
                    <SecondaryButton
                        label={translate('person.correspondenceAgain')}
                        shape="compact"
                        onActivate={reading.readAgain}
                    />
                </div>
            ) : (
                children
            )}
        </section>
    );
}
