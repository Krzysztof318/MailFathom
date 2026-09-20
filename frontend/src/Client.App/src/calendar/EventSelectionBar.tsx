// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { CalendarEvent } from '@mailfathom/client-backend';
import { Control } from '../controls/Control';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useWideWorkspace } from '../shell/useWideWorkspace';

// What stands where the toolbar stands while events are picked out, which is the design project's own bar: how many
// are selected, the acts over the whole of them, and the way out.
//
// It replaces the toolbar rather than standing beside it, for the reason the address book's own bar gives: the two
// answer the same question, and a screen offering both would be asking a reader which of two rows their next press
// belongs to. It is drawn at every width, because a selection with no way to act on it or leave it is a state a person
// cannot get out of.
//
// The design draws a second act on this bar — attaching reminders to everything picked out — and `/api/client`
// publishes nothing behind it, so it is left out here exactly as it is left out of the menu, and arrives with the
// change that gives an event a reminder at all.

// How many events are picked out, in the forms a language has for the noun. The mail list and the address book count
// their own selections with the same four entries, which is deliberate: *how many are selected* is one sentence in
// this client rather than one per list.
const selectionCounted: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'select.count.other',
    one: 'select.count.one',
    two: 'select.count.other',
    few: 'select.count.few',
    many: 'select.count.many',
    other: 'select.count.other',
};

export function EventSelectionBar({
    selected,
    onClear,
    onAskDeletion,
}: {
    /** The events picked out, in the order the view draws them. */
    readonly selected: readonly CalendarEvent[];

    readonly onClear: () => void;

    /** Raises the question the deletion stands behind, over everything picked out. */
    readonly onAskDeletion: () => void;
}) {
    const { locale, translate } = useLocalization();
    const wide = useWideWorkspace();

    return (
        <div
            role="toolbar"
            aria-label={translate('calendar.selectionBar')}
            className="flex shrink-0 items-center gap-0.5 overflow-x-auto border-b border-accent-line bg-accent-soft px-3 py-2"
        >
            <Control
                label={translate('calendar.clearSelection')}
                icon="close"
                shape="selectedSymbol"
                onPress={onClear}
            />

            {/* Said rather than only drawn: a reader who picked four events out hears how many they are holding, and
                hears it change as they pick out a fifth. */}
            <p role="status" className="me-1.5 ps-0.5 text-base font-semibold whitespace-nowrap text-accent-deep">
                {translate(selectionCounted[new Intl.PluralRules(locale).select(selected.length)], {
                    count: new Intl.NumberFormat(locale).format(selected.length),
                })}
            </p>

            <span aria-hidden="true" className="mx-0.5 w-px self-stretch bg-accent-line" />

            <Control
                label={translate('calendar.deleteEvents')}
                icon="delete"
                shape={wide ? 'selected' : 'selectedSymbol'}
                onPress={onAskDeletion}
            />
        </div>
    );
}
