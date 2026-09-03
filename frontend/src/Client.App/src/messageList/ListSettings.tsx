// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useId, useState } from 'react';
import type { MailTimelineOrder } from '@mailfathom/client-backend';
import { CheckControl } from '../controls/CheckControl';
import { chip } from '../controls/chrome';
import { Icon } from '../controls/Icon';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import {
    dateRanges,
    narrowedToRange,
    narrowingsInForce,
    openingListing,
    selectableRange,
    type MailListDateRange,
    type MailListFilters,
    type MailListing,
} from './listing';

// How the folder in front of the reader is being read, folded away behind one control until they ask for it. A filter
// row that is always open costs the list two rows of its height on every screen to say what is usually "nothing is
// narrowed", so what stays visible is the count of narrowings in force — which is the whole of what somebody scanning
// a folder needs, and is what turns an empty-looking folder from a mailbox that lost mail into a filter they forgot.
//
// Each toggle keeps one answer or both, and never only the other: "read", "unflagged", and "without attachments" are
// not lists anybody asks for, and offering three states per filter would triple the controls to reach one nobody wants.

const orderNames: Readonly<Record<MailTimelineOrder, MessageKey>> = {
    newestFirst: 'list.newestFirst',
    oldestFirst: 'list.oldestFirst',
};

const rangeNames: Readonly<Record<MailListDateRange, MessageKey>> = {
    today: 'list.rangeToday',
    lastSevenDays: 'list.rangeLastSevenDays',
    lastThirtyDays: 'list.rangeLastThirtyDays',
    thisYear: 'list.rangeThisYear',
};

const orders: readonly MailTimelineOrder[] = ['newestFirst', 'oldestFirst'];

// The pill every choice in the panel is drawn as, and the tint it takes while it is the one in force. Stated once
// because the toggles, the orders, and the offered spans are the same control drawn three times, and a chip that is on
// is told apart from one that is off by its weight as well as by its tint — colour alone is not a state a reader who
// cannot see it can read.
const choice = `cursor-pointer px-2.75 py-1 text-sm ${chip}`;
const chosen = 'border-accent-line bg-accent-soft font-semibold text-accent-deep';

/** What the two ends of a typed range are shown as, which is nothing at all while an offered span is what set them. */
type TypedRange = Pick<MailListFilters, 'receivedFrom' | 'receivedTo'>;

const noRangeTyped: TypedRange = { receivedFrom: null, receivedTo: null };

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

    // The radio group's name has to be unique in the document rather than in this file: a second list rendered beside
    // this one would otherwise share the group, and picking an order in one would take it off in the other.
    const orderGroup = useId();

    // The pair as typed, held only while it selects nothing. A refused range is still what the reader is looking at, so
    // it stays in the two controls and is what the next keystroke is judged against — a control that snapped back to
    // the range in force would take away the moment they just picked and leave them to remember it.
    const [refusedRange, setRefusedRange] = useState<TypedRange | null>(null);

    const inForce = narrowingsInForce(listing);
    const filters = listing.filters;

    // An offered span sets a start of its own, and the design draws the two fields empty while one is in force: the
    // span is what the reader chose, and showing the instant it happened to resolve to would invite them to correct a
    // value they never typed.
    const typed: TypedRange =
        refusedRange ??
        (filters.dateRange === null
            ? { receivedFrom: filters.receivedFrom, receivedTo: filters.receivedTo }
            : noRangeTyped);

    function narrow(change: Partial<MailListFilters>): void {
        onRead({ ...listing, filters: { ...filters, ...change } });
    }

    function pickRange(range: MailListDateRange): void {
        setRefusedRange(null);

        // Picking the span already in force takes it off, which is the one way back to a folder nothing narrows by
        // date without reaching for the two fields.
        onRead({
            ...listing,
            filters: narrowedToRange(filters, filters.dateRange === range ? null : range, new Date()),
        });
    }

    function received(change: Partial<TypedRange>): void {
        const moved = { ...typed, ...change };

        if (!selectableRange(moved.receivedFrom, moved.receivedTo)) {
            setRefusedRange(moved);

            return;
        }

        setRefusedRange(null);
        narrow({ dateRange: null, ...moved });
    }

    return (
        <details>
            <summary
                className={`flex w-fit cursor-pointer items-center gap-1 px-2 py-0.75 ${chip} ${
                    inForce === 0 ? '' : chosen
                }`}
            >
                <Icon name={inForce === 0 ? 'tune' : 'filter_alt'} className="size-4" />
                <span className="sr-only">{translate('list.filters')}</span>
                {inForce === 0 ? null : <span className="text-sm font-semibold">{inForce}</span>}
            </summary>

            {/* Drawn out to the edges of the column the way the design project draws it, rather than as a card inset
                inside the header: what is disclosed is a band the list starts underneath, and the line along its foot
                is what says where the list begins again. */}
            <div className="-mx-3 mt-1.5 flex flex-col gap-2.75 border-b border-line bg-sunken px-3 py-2.5">
                <div className="flex flex-wrap items-center gap-1.75">
                    <CheckControl
                        label={translate('list.onlyUnread')}
                        on={filters.unread === true}
                        onChange={(on) => {
                            narrow({ unread: on ? true : null });
                        }}
                    />

                    <CheckControl
                        label={translate('list.onlyFlagged')}
                        on={filters.flagged === true}
                        onChange={(on) => {
                            narrow({ flagged: on ? true : null });
                        }}
                    />

                    <CheckControl
                        label={translate('list.onlyWithAttachments')}
                        on={filters.hasAttachments === true}
                        onChange={(on) => {
                            narrow({ hasAttachments: on ? true : null });
                        }}
                    />

                    {/* Offered only where the list spans folders. A reader who has pointed at one folder is already
                        reading that folder and nothing else, so a control saying whether junk takes part would be one
                        that changes nothing — which says less about why than not offering it does. */}
                    {junkAskable ? (
                        <CheckControl
                            label={translate('list.includeJunk')}
                            on={filters.includeJunk}
                            onChange={(on) => {
                                narrow({ includeJunk: on });
                            }}
                        />
                    ) : null}
                </div>

                {/* Radio buttons rather than pressable chips, for the reason `shell/Preferences.tsx` gives about the
                    theme: the platform announces them as one group, moves between them with the arrow keys, and leaves
                    one tab stop for a choice that is always exactly one of its offerings. */}
                <fieldset className="flex flex-col gap-1.25">
                    <legend className={sectionLabel}>{translate('list.order')}</legend>

                    <div className="flex flex-wrap gap-1.5">
                        {orders.map((offered) => (
                            <label
                                key={offered}
                                className={`${choice} has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-accent ${
                                    listing.order === offered ? chosen : ''
                                }`}
                            >
                                <input
                                    type="radio"
                                    name={orderGroup}
                                    value={offered}
                                    checked={listing.order === offered}
                                    className="sr-only"
                                    onChange={() => {
                                        onRead({ ...listing, order: offered });
                                    }}
                                />
                                {translate(orderNames[offered])}
                            </label>
                        ))}
                    </div>
                </fieldset>

                <div className="flex flex-col gap-1.25">
                    <p className={sectionLabel}>{translate('list.dateRange')}</p>

                    {/* Pressable rather than radio buttons, unlike the order above: each span can be taken off again
                        by pressing it, and a radio group a reader cannot uncheck would leave the list narrowed by date
                        for as long as it was open. */}
                    <div className="flex flex-wrap gap-1.5">
                        {dateRanges.map((offered) => (
                            <button
                                key={offered}
                                type="button"
                                aria-pressed={filters.dateRange === offered}
                                className={`${choice} ${filters.dateRange === offered ? chosen : ''}`}
                                onClick={() => {
                                    pickRange(offered);
                                }}
                            >
                                {translate(rangeNames[offered])}
                            </button>
                        ))}
                    </div>

                    {/* The browser's own date control rather than a picker of ours: it is localized, keyboard operable,
                        and understood by every assistive technology already. */}
                    <div className="flex flex-wrap items-end gap-2">
                        <label className="flex flex-col gap-0.5 text-sm text-muted">
                            {translate('list.receivedFromField')}
                            <input
                                type="datetime-local"
                                className={dateField}
                                value={typed.receivedFrom ?? ''}
                                onChange={(event) => {
                                    received({ receivedFrom: event.target.value || null });
                                }}
                            />
                        </label>

                        <label className="flex flex-col gap-0.5 text-sm text-muted">
                            {translate('list.receivedToField')}
                            <input
                                type="datetime-local"
                                className={dateField}
                                value={typed.receivedTo ?? ''}
                                onChange={(event) => {
                                    received({ receivedTo: event.target.value || null });
                                }}
                            />
                        </label>
                    </div>

                    {refusedRange === null ? null : (
                        <p className="text-sm text-warning" role="alert">
                            {translate('list.rangeSelectsNothing')}
                        </p>
                    )}
                </div>

                <div className="flex items-center gap-2.5 border-t border-line-soft pt-2">
                    <p className="text-sm text-muted">
                        {inForce === 0
                            ? translate('list.noFiltersInForce')
                            : translate('list.filtersInForce', { count: String(inForce) })}
                    </p>

                    {inForce === 0 ? null : (
                        <button
                            type="button"
                            className="ms-auto cursor-pointer text-sm text-accent-deep underline-offset-2 hover:underline"
                            onClick={() => {
                                setRefusedRange(null);

                                // What the reader chose about junk is not one of the narrowings this clears: it widens
                                // the list, so taking it off would hide mail rather than reveal it.
                                onRead({
                                    ...openingListing,
                                    filters: { ...openingListing.filters, includeJunk: filters.includeJunk },
                                });
                            }}
                        >
                            {translate('list.clearFilters')}
                        </button>
                    )}
                </div>
            </div>
        </details>
    );
}

const dateField = 'rounded-md border border-line bg-panel px-2 py-1 text-sm text-text';

// What each group inside the panel is headed with, in the size and the weight the design project sets those labels
// in. Not the settings screen's own section label, which the design draws a shade fainter — two labels that differ in
// the design are two shapes rather than one drifting.
const sectionLabel = 'text-2xs tracking-widest text-muted uppercase';
