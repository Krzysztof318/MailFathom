// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useState } from 'react';
import type { MailAccount } from '@mailfathom/client-backend';
import { borderedControl } from '../controls/chrome';
import { CheckControl } from '../controls/CheckControl';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { folderRoleLabels, folderRoles, isMailFolderRole } from '../workspace/mailScope';
import {
    addressFilter,
    narrowings,
    selectableRange,
    valueOf,
    without,
    type MailSearchAsk,
    type MailSearchNarrowing,
} from './searchAsk';

// Every filter the search is under, drawn as an object a person can take off, and the one place another is added.
//
// The two halves answer the same obligation from opposite directions. A search narrowed by something nobody can see is
// a mailbox that appears to have lost mail — which is the defect the folder's own settings line exists to prevent, and
// it is worse here because an empty result reads as an absence rather than as a filter. So every filter in force is a
// visible object, removing one is a single press, and what a search is under is legible without opening anything.
//
// The panel is folded away because a search usually needs none of it: the scope somebody was looking at is already on
// the search, and typing words is the whole of what most searches are.

const narrowingLabels: Readonly<Record<MailSearchNarrowing, MessageKey>> = {
    account: 'search.narrowing.account',
    folder: 'search.narrowing.folder',
    sender: 'search.narrowing.sender',
    recipient: 'search.narrowing.recipient',
    receivedFrom: 'search.narrowing.receivedFrom',
    receivedTo: 'search.narrowing.receivedTo',
    unread: 'search.narrowing.unread',
    flagged: 'search.narrowing.flagged',
    hasAttachments: 'search.narrowing.hasAttachments',
    includeJunk: 'search.narrowing.includeJunk',
};

// A calendar day, which is what the two date filters hold and what a chip has to say out loud.
const dayNarrowings: readonly MailSearchNarrowing[] = ['receivedFrom', 'receivedTo'];

const control = `px-2 py-1 text-sm ${borderedControl}`;
const field = `w-full ${control}`;

// The two calendar days together, because neither can be judged without the other.
type DayRange = Pick<MailSearchAsk, 'receivedFrom' | 'receivedTo'>;

export function SearchFilters({
    ask,
    accounts,
    onNarrow,
}: {
    readonly ask: MailSearchAsk;
    readonly accounts: readonly MailAccount[];
    readonly onNarrow: (ask: MailSearchAsk) => void;
}) {
    const { locale, translate } = useLocalization();

    // The pair as typed, held only while it selects nothing. A refused range is still what the reader is looking at, so
    // it stays in the two controls and is what the next keystroke is judged against — a control that snapped back to
    // the filter in force would take away the day they just picked and leave them to remember it.
    const [refusedRange, setRefusedRange] = useState<DayRange | null>(null);

    const inForce = narrowings(ask);

    // What the search covers when nothing narrows it to a mailbox. A chip cannot say it — there is no filter there to
    // take off — and leaving it unsaid is what makes an empty result read as an absence rather than as a search over
    // everything that found nothing.
    const everywhere = ask.account === null && ask.folder === null;

    const shownRange: DayRange = refusedRange ?? { receivedFrom: ask.receivedFrom, receivedTo: ask.receivedTo };

    function received(change: Partial<DayRange>): void {
        const moved = { ...shownRange, ...change };

        // A range whose end falls before its start selects nothing, and the deployment refuses it rather than
        // answering an empty page. Saying so here is where the reader can see which of the two days to move.
        if (!selectableRange(moved.receivedFrom, moved.receivedTo)) {
            setRefusedRange(moved);

            return;
        }

        setRefusedRange(null);
        onNarrow({ ...ask, ...moved });
    }

    return (
        <div className="flex flex-col gap-2">
            {everywhere ? <p className="text-sm text-muted">{translate('search.everywhere')}</p> : null}

            {inForce.length === 0 ? null : (
                <ul aria-label={translate('search.filters')} className="flex flex-wrap items-center gap-2">
                    {inForce.map((narrowing) => (
                        <FilterInForce
                            key={narrowing}
                            label={labelOf(
                                narrowing,
                                valueOf(ask, narrowing),
                                accounts,
                                locale,
                                translate,
                                dayNarrowings.includes(narrowing),
                            )}
                            onRemove={() => {
                                setRefusedRange(null);
                                onNarrow(without(ask, narrowing));
                            }}
                        />
                    ))}
                </ul>
            )}

            <details className="text-sm">
                <summary className="cursor-pointer text-muted">{translate('search.narrow')}</summary>

                <div className="mt-2 flex flex-col gap-3 border-s-2 border-line-soft ps-3">
                    <div className="flex flex-wrap items-end gap-3">
                        <label className="flex flex-col gap-1">
                            {translate('search.narrowing.accountField')}
                            <select
                                className={control}
                                value={ask.account ?? ''}
                                onChange={(event) => {
                                    onNarrow({ ...ask, account: event.target.value || null });
                                }}
                            >
                                <option value="">{translate('search.everyAccount')}</option>
                                {accounts.map((account) => (
                                    <option key={account.id} value={account.id}>
                                        {account.displayName}
                                    </option>
                                ))}
                            </select>
                        </label>

                        {/* The folders offered are the roles rather than every folder of every account: a role is a
                            closed set this client already names, it is what somebody means by "in my sent mail", and
                            it needs no second read of the folders route to offer. A folder that plays no role reaches
                            a search by being what the reader was looking at when they started it, which is on the
                            search as a filter they can see and take off.
                            ponytail: roles only; offer the whole tree here once a screen holds one already read. */}
                        <label className="flex flex-col gap-1">
                            {translate('search.narrowing.folderField')}
                            <select
                                className={control}
                                value={ask.folder ?? ''}
                                onChange={(event) => {
                                    onNarrow({ ...ask, folder: event.target.value || null });
                                }}
                            >
                                <option value="">{translate('search.everyFolder')}</option>

                                {/* A folder the reader pointed at is not one of the roles, so it is offered back as
                                    itself rather than silently dropped by the control that would otherwise not hold
                                    it. */}
                                {ask.folder !== null && roleIn(ask.folder) === null ? (
                                    <option value={ask.folder}>{ask.folder}</option>
                                ) : null}

                                {folderRoles.map((role) => (
                                    <option key={role} value={`role:${role}`}>
                                        {translate(folderRoleLabels[role])}
                                    </option>
                                ))}
                            </select>
                        </label>
                    </div>

                    <div className="flex flex-wrap items-start gap-3">
                        <AddressFilter
                            label={translate('search.narrowing.senderField')}
                            onAdd={(address) => {
                                onNarrow({ ...ask, sender: address });
                            }}
                        />

                        <AddressFilter
                            label={translate('search.narrowing.recipientField')}
                            onAdd={(address) => {
                                onNarrow({ ...ask, recipient: address });
                            }}
                        />
                    </div>

                    <div className="flex flex-col gap-1">
                        <div className="flex flex-wrap items-end gap-3">
                            {/* The browser's own date control rather than a picker of ours: it is localized, keyboard
                                operable, and understood by every assistive technology already. */}
                            <label className="flex flex-col gap-1">
                                {translate('search.narrowing.receivedFromField')}
                                <input
                                    type="date"
                                    className={control}
                                    value={shownRange.receivedFrom ?? ''}
                                    onChange={(event) => {
                                        received({ receivedFrom: event.target.value || null });
                                    }}
                                />
                            </label>

                            <label className="flex flex-col gap-1">
                                {translate('search.narrowing.receivedToField')}
                                <input
                                    type="date"
                                    className={control}
                                    value={shownRange.receivedTo ?? ''}
                                    onChange={(event) => {
                                        received({ receivedTo: event.target.value || null });
                                    }}
                                />
                            </label>
                        </div>

                        {refusedRange !== null ? (
                            <p className="text-sm text-warning" role="alert">
                                {translate('search.rangeSelectsNothing')}
                            </p>
                        ) : null}
                    </div>

                    <div className="flex flex-wrap items-center gap-2">
                        <CheckControl
                            label={translate('search.narrowing.unread')}
                            on={ask.unread === true}
                            onChange={(on) => {
                                onNarrow({ ...ask, unread: on ? true : null });
                            }}
                        />

                        <CheckControl
                            label={translate('search.narrowing.flagged')}
                            on={ask.flagged === true}
                            onChange={(on) => {
                                onNarrow({ ...ask, flagged: on ? true : null });
                            }}
                        />

                        <CheckControl
                            label={translate('search.narrowing.hasAttachments')}
                            on={ask.hasAttachments === true}
                            onChange={(on) => {
                                onNarrow({ ...ask, hasAttachments: on ? true : null });
                            }}
                        />

                        <CheckControl
                            label={translate('search.narrowing.includeJunk')}
                            on={ask.includeJunk}
                            onChange={(on) => {
                                onNarrow({ ...ask, includeJunk: on });
                            }}
                        />
                    </div>
                </div>
            </details>
        </div>
    );
}

// One filter in force. It is a list item with a button in it rather than a button that removes itself, because what a
// reader has to be able to say is both things: which filter this is, and that taking it off is one press.
function FilterInForce({ label, onRemove }: { readonly label: string; readonly onRemove: () => void }) {
    const { translate } = useLocalization();

    return (
        <li className="flex items-center gap-1 rounded-md bg-accent-soft px-2 py-1 text-sm text-accent-strong">
            {label}

            <button
                type="button"
                aria-label={translate('search.remove', { filter: label })}
                className="rounded-sm px-1 leading-none transition hover:bg-hover"
                onClick={onRemove}
            >
                {/* Drawn rather than written, for the reason the list's own marks are drawn: a glyph is a string
                    somebody reads in one language, and what names this control is the label above. */}
                <svg viewBox="0 0 24 24" aria-hidden="true" className="size-3 fill-current">
                    <path d="M19 6.41 17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12 19 6.41Z" />
                </svg>
            </button>
        </li>
    );
}

// An address a search is narrowed to, typed and then added. It is added on submit rather than as it is typed, because
// a search restarted on every keystroke would be a page read per letter — and because half an address is a filter
// nothing matches.
function AddressFilter({ label, onAdd }: { readonly label: string; readonly onAdd: (address: string) => void }) {
    const { translate } = useLocalization();
    const [typed, setTyped] = useState('');
    const [refused, setRefused] = useState(false);

    return (
        <form
            className="flex flex-col gap-1"
            onSubmit={(event) => {
                event.preventDefault();

                const address = addressFilter(typed);

                if (address === null) {
                    setRefused(true);

                    return;
                }

                setRefused(false);
                setTyped('');
                onAdd(address);
            }}
        >
            <label className="flex flex-col gap-1">
                {label}
                {/* Ordinary text with an address keyboard rather than `type="email"`: the browser's own constraint
                    would refuse the submit before this form saw it, and what a reader would then read is the
                    browser's sentence rather than the one that names this field. */}
                <input
                    type="text"
                    inputMode="email"
                    className={field}
                    value={typed}
                    onChange={(event) => {
                        setRefused(false);
                        setTyped(event.target.value);
                    }}
                />
            </label>

            <button className={`${control} self-start`} type="submit">
                {translate('search.addFilter')}
            </button>

            {refused ? (
                <p className="text-sm text-warning" role="alert">
                    {translate('search.notAnAddress')}
                </p>
            ) : null}
        </form>
    );
}

// What a chip says. A filter holding a value says which one, and the account says the name the reader knows it by
// rather than the identifier the route is asked with.
function labelOf(
    narrowing: MailSearchNarrowing,
    value: string | null,
    accounts: readonly MailAccount[],
    locale: string,
    translate: (key: MessageKey, values?: Readonly<Record<string, string>>) => string,
    isDay: boolean,
): string {
    if (value === null) {
        return translate(narrowingLabels[narrowing]);
    }

    const named = accounts.find((account) => account.id === value)?.displayName ?? value;

    return translate(narrowingLabels[narrowing], { value: isDay ? dayIn(locale, value) : named });
}

// A calendar day as the reader's language writes one. Formatted by `Intl` rather than shown as the machine-readable
// value the control holds, which is the same day spelled for a filesystem.
function dayIn(locale: string, day: string): string {
    const at = new Date(`${day}T00:00:00`);

    return Number.isNaN(at.getTime()) ? day : new Intl.DateTimeFormat(locale, { dateStyle: 'long' }).format(at);
}

// The role a folder filter stands for, or `null` where it names a folder of one account instead.
function roleIn(folder: string | null): string | null {
    const named = folder?.startsWith('role:') === true ? folder.slice('role:'.length) : null;

    return named !== null && isMailFolderRole(named) ? `role:${named}` : null;
}
