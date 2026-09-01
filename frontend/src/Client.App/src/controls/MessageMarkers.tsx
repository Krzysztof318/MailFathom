// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ReactNode } from 'react';
import type { MailTimelineEntry } from '@mailfathom/client-backend';
import { useLocalization } from '../localization/useLocalization';

// What the mail server said about the message, in the order a reader scans for it. Each carries its own words, because
// a mark with no name is a mark nobody using a screen reader can see at all.
//
// It sits here rather than beside the mail list for the reason `ReceivedAt` does: the conversation draws the same three
// marks about the same message, and a second copy of them is how one screen comes to say a message carries a file and
// the other says it carries none.

/** The marks a message carries, pushed to the end of the line it is drawn on. */
export function MessageMarkers({ email }: { readonly email: MailTimelineEntry }) {
    const { translate } = useLocalization();

    return (
        <span className="ms-auto flex shrink-0 items-center gap-1">
            {email.answered ? (
                <Marker label={translate('list.answered')}>
                    <path d="M10 9V5l-7 7 7 7v-4.1c5 0 8.5 1.6 11 5.1-1-5-4-10-11-11Z" />
                </Marker>
            ) : null}

            {email.hasAttachments ? (
                <Marker label={translate('list.attachments', { count: String(email.attachmentCount) })}>
                    <path d="M16.5 6.5v9a4.5 4.5 0 1 1-9 0V5.5a3 3 0 1 1 6 0v9a1.5 1.5 0 1 1-3 0v-8H9v8a3 3 0 1 0 6 0v-9a4.5 4.5 0 1 0-9 0v10a6 6 0 0 0 12 0v-9h-1.5Z" />
                </Marker>
            ) : null}

            {email.flagged ? (
                <Marker label={translate('list.flagged')}>
                    <path d="m12 17.3-6.2 3.7 1.7-7L2 9.2l7.2-.6L12 2l2.8 6.6 7.2.6-5.5 4.8 1.7 7L12 17.3Z" />
                </Marker>
            ) : null}
        </span>
    );
}

function Marker({ label, children }: { readonly label: string; readonly children: ReactNode }) {
    return (
        <span className="text-muted">
            <svg viewBox="0 0 24 24" aria-hidden="true" className="size-3.5 fill-current">
                {children}
            </svg>
            <span className="sr-only">{label}</span>
        </span>
    );
}
