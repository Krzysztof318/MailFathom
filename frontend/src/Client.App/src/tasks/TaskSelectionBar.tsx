// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { PersonalTask } from '@mailfathom/client-backend';
import { Control } from '../controls/Control';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useWideWorkspace } from '../shell/useWideWorkspace';

// What stands where the toolbar stands while tasks are picked out, which is the design project's own bar: how many are
// selected, the three acts over the whole of them, and the way out.
//
// It replaces the toolbar rather than standing beside it, for the reason `mailSpace/SelectionBar.tsx` and
// `contactBook/ContactSelectionBar.tsx` give about theirs: the two answer the same question, and a screen offering
// both would be asking a reader which of two rows their next press belongs to. It is drawn at every width, because a
// selection with no way to act on it or leave it is a state a person cannot get out of.
//
// **Leaving is the close control and nothing else.** It clears the selection rather than acting on it, which is what
// makes picking tasks out safe to do by accident.
//
// The design writes the first act's label in Polish where the rest of that prototype is English, which is a slip in
// the project rather than a choice: the word here is the one the row's own menu uses for the same act, in the
// catalogue both read from.

// How many tasks are picked out, in the forms a language has for the noun. The mail list and the address book count
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

export function TaskSelectionBar({
    selected,
    onClear,
    onComplete,
    onSchedule,
    onAskErasure,
}: {
    /** The tasks picked out, in the order the list draws them. */
    readonly selected: readonly PersonalTask[];

    readonly onClear: () => void;

    /** Marks everything picked out as done, which is the one act here that is not destructive and is not confirmed. */
    readonly onComplete: () => void;

    readonly onSchedule: () => void;

    /** Raises the question the erasure stands behind, over everything picked out. */
    readonly onAskErasure: () => void;
}) {
    const { locale, translate } = useLocalization();
    const wide = useWideWorkspace();

    // Words beside the symbols where the composition has room for them, and the symbols alone where it has not, which
    // is how the design project draws this bar on a phone. The name is on the control either way.
    const shape = wide ? 'selected' : 'selectedSymbol';

    return (
        <div
            role="toolbar"
            aria-label={translate('tasks.selectionBar')}
            className="flex shrink-0 items-center gap-0.5 overflow-x-auto border-b border-accent-line bg-accent-soft px-3 py-2"
        >
            <Control label={translate('tasks.clearSelection')} icon="close" shape="selectedSymbol" onPress={onClear} />

            {/* Said rather than only drawn: a reader who picked four tasks out hears how many they are holding, and
                hears it change as they pick out a fifth. */}
            <p role="status" className="me-1.5 ps-0.5 text-base font-semibold whitespace-nowrap text-accent-deep">
                {translate(selectionCounted[new Intl.PluralRules(locale).select(selected.length)], {
                    count: new Intl.NumberFormat(locale).format(selected.length),
                })}
            </p>

            <span aria-hidden="true" className="mx-0.5 w-px self-stretch bg-accent-line" />

            <Control label={translate('tasks.markDone')} icon="check_circle" shape={shape} onPress={onComplete} />

            <Control
                label={translate('tasks.scheduleInCalendar')}
                icon="calendar_month"
                shape={shape}
                onPress={onSchedule}
            />

            <Control label={translate('tasks.deleteTasks')} icon="delete" shape={shape} onPress={onAskErasure} />
        </div>
    );
}
