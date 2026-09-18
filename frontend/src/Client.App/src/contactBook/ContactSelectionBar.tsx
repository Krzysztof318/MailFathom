// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { Contact } from '@mailfathom/client-backend';
import { useComposing } from '../composer/useComposing';
import { Control } from '../controls/Control';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useWideWorkspace } from '../shell/useWideWorkspace';

// What stands where the toolbar stands while people are picked out, which is the design project's own bar: how many
// are selected, the acts over the whole of them, and the way out.
//
// It replaces the toolbar rather than standing beside it, for the reason `mailSpace/SelectionBar.tsx` gives about its
// own: the two answer the same question, and a screen offering both would be asking a reader which of two rows their
// next press belongs to. It is drawn at every width, because a selection with no way to act on it or leave it is a
// state a person cannot get out of.
//
// **Leaving is the close control and nothing else.** It clears the selection rather than acting on it, which is what
// makes picking people out safe to do by accident.
//
// The design draws a third act on this bar — exporting the people picked out — and the client surface publishes no
// route behind it: exporting a contact is deliberately absent from `/api/client`, because a data-subject request is
// answered from the surface an operator answers one from. So it is left out here rather than drawn as a control that
// would do nothing, and closing that gap is a decision about what a client export *is* rather than a control to add.

// How many people are picked out, in the forms a language has for the noun. The mail list counts its own selection with
// the same four entries, which is deliberate: *how many are selected* is one sentence in this client rather than one
// per list.
const selectionCounted: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'select.count.other',
    one: 'select.count.one',
    two: 'select.count.other',
    few: 'select.count.few',
    many: 'select.count.many',
    other: 'select.count.other',
};

export function ContactSelectionBar({
    selected,
    erasable,
    onClear,
    onAskErasure,
}: {
    /** The people picked out, in the order the list draws them. */
    readonly selected: readonly Contact[];

    /** Whether this credential may take somebody out of the book at all. */
    readonly erasable: boolean;

    readonly onClear: () => void;

    /** Raises the question the erasure stands behind, over everything picked out. */
    readonly onAskErasure: () => void;
}) {
    const { locale, translate } = useLocalization();
    const composing = useComposing();
    const wide = useWideWorkspace();

    // Words beside the symbols where the composition has room for them, and the symbols alone where it has not, which
    // is how the design project draws this bar on a phone. The name is on the control either way.
    const shape = wide ? 'selected' : 'selectedSymbol';

    return (
        <div
            role="toolbar"
            aria-label={translate('people.selectionBar')}
            className="flex shrink-0 items-center gap-0.5 overflow-x-auto border-b border-accent-line bg-accent-soft px-3 py-2"
        >
            <Control label={translate('people.clearSelection')} icon="close" shape="selectedSymbol" onPress={onClear} />

            {/* Said rather than only drawn: a reader who picked four people out hears how many they are holding, and
                hears it change as they pick out a fifth. */}
            <p role="status" className="me-1.5 ps-0.5 text-base font-semibold whitespace-nowrap text-accent-deep">
                {translate(selectionCounted[new Intl.PluralRules(locale).select(selected.length)], {
                    count: new Intl.NumberFormat(locale).format(selected.length),
                })}
            </p>

            <span aria-hidden="true" className="mx-0.5 w-px self-stretch bg-accent-line" />

            {composing.offered ? (
                <Control
                    label={translate('people.write')}
                    icon="edit"
                    shape={shape}
                    onPress={() => {
                        composing.compose({ kind: 'new', to: selected.map((contact) => contact.preferredAddress) });
                    }}
                />
            ) : null}

            {erasable ? (
                <Control
                    label={translate('people.deleteContacts')}
                    icon="delete"
                    shape={shape}
                    onPress={onAskErasure}
                />
            ) : null}
        </div>
    );
}
