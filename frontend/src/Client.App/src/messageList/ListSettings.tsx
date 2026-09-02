// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailTimelineOrder } from '@mailfathom/client-backend';
import { CheckControl } from '../controls/CheckControl';
import { chip } from '../controls/chrome';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import type { MailListFilters, MailListing } from './listing';

// How the folder in front of the reader is being read, stated on the screen rather than held somewhere they cannot see.
// A list narrowed by a filter nobody can see is a folder that appears to have lost mail, which is the defect this
// exists to prevent — so every filter in force is a control that is visibly on, and turning it off is one press.
//
// Each filter keeps one answer or both, and never only the other: "read", "unflagged", and "without attachments" are
// not lists anybody asks for, and offering three states per filter would triple the controls to reach one nobody wants.

const orderNames: Readonly<Record<MailTimelineOrder, MessageKey>> = {
    newestFirst: 'list.newestFirst',
    oldestFirst: 'list.oldestFirst',
};

const orders: readonly MailTimelineOrder[] = ['newestFirst', 'oldestFirst'];

export function ListSettings({
    listing,
    junkAskable,
    onRead,
}: {
    readonly listing: MailListing;
    readonly junkAskable: boolean;
    readonly onRead: (listing: MailListing) => void;
}) {
    const { translate } = useLocalization();

    function narrow(change: Partial<MailListFilters>): void {
        onRead({ ...listing, filters: { ...listing.filters, ...change } });
    }

    return (
        <div className="flex flex-wrap items-center gap-1">
            <select
                aria-label={translate('list.order')}
                className={`px-2.25 py-0.75 text-sm ${chip}`}
                value={listing.order}
                onChange={(event) => {
                    if (isOrder(event.target.value)) {
                        onRead({ ...listing, order: event.target.value });
                    }
                }}
            >
                {orders.map((offered) => (
                    <option key={offered} value={offered}>
                        {translate(orderNames[offered])}
                    </option>
                ))}
            </select>

            <CheckControl
                label={translate('list.onlyUnread')}
                on={listing.filters.unread === true}
                onChange={(on) => {
                    narrow({ unread: on ? true : null });
                }}
            />

            <CheckControl
                label={translate('list.onlyFlagged')}
                on={listing.filters.flagged === true}
                onChange={(on) => {
                    narrow({ flagged: on ? true : null });
                }}
            />

            <CheckControl
                label={translate('list.onlyWithAttachments')}
                on={listing.filters.hasAttachments === true}
                onChange={(on) => {
                    narrow({ hasAttachments: on ? true : null });
                }}
            />

            {/* Offered only where the list spans folders. A reader who has pointed at one folder is already reading
                that folder and nothing else, so a control saying whether junk takes part would be one that changes
                nothing — which says less about why than not offering it does. */}
            {junkAskable ? (
                <CheckControl
                    label={translate('list.includeJunk')}
                    on={listing.filters.includeJunk}
                    onChange={(on) => {
                        narrow({ includeJunk: on });
                    }}
                />
            ) : null}
        </div>
    );
}

function isOrder(value: string): value is MailTimelineOrder {
    return value === 'newestFirst' || value === 'oldestFirst';
}
