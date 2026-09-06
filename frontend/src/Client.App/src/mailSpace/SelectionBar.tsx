// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { Control } from '../controls/Control';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { MailboxActControls } from '../mailboxActs/MailboxActControls';
import { actedMessages, useListedMail } from '../messageList/useListedMail';
import { useWorkspace } from '../workspace/useWorkspace';
import { useStripFit } from './useStripFit';

// What stands where the toolbar stands while messages are picked out, which is the design project's own bar: how many
// are selected, the five acts over the whole of them, a way to take the listing in at once, and the way out.
//
// It replaces the toolbar rather than standing beside it, because the two answer the same question — *what am I doing
// to mail* — and a screen offering both would be asking a reader which of two rows their next press belongs to. It is
// drawn at every width for that same reason: the narrow composition has no toolbar to replace, and a selection with no
// way to act on it or leave it would be a state a person cannot get out of.
//
// It is drawn on the accent tint rather than the accent fill, which is what tells it from the toolbar without shouting:
// the design draws it as the same strip in a different colour, with the same controls in the same places.
//
// **Leaving is the close control and nothing else.** It clears the selection rather than acting on it, which is what
// makes picking messages out safe to do by accident.
//
// The acts themselves are `mailboxActs/MailboxActControls.tsx`, drawn here at the weight the accent tint needs: the
// same five controls the toolbar draws, over everything selected instead of over what is open.

// How many messages are picked out, in the forms a language has for the noun. Selected rather than spelled, for the
// reason `TabStrip.tsx` gives about counting tabs: Polish needs three forms and English hides that it needs two.
const selectionCounted: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'select.count.other',
    one: 'select.count.one',
    two: 'select.count.other',
    few: 'select.count.few',
    many: 'select.count.many',
    other: 'select.count.other',
};

export function SelectionBar() {
    const { locale, translate } = useLocalization();
    const { workspace, revise } = useWorkspace();
    const listed = useListedMail();

    // The messages themselves rather than the identities the workspace holds, because an act has to name the account
    // each message is in and the folder it is leaving — and because a count of messages this client could not name is
    // a count of what pressing an act would not touch.
    const messages = actedMessages(listed, workspace.selected);

    // Words beside the symbols for as long as they fit the bar, and the symbols alone once they do not — measured
    // rather than decided at a width, which is the rule `useStripFit.ts` states. The name is on the control either
    // way, so nothing is lost to a reader who is not looking at it.
    const { strip, fit } = useStripFit(false, locale);
    const actShape = fit === 'symbols' ? 'selectedSymbol' : 'selected';

    // Clearing the selection takes this bar off the screen, and with it the control that was just pressed — so focus
    // goes back to the list before the selection goes, rather than being left on an element about to be removed. The
    // list is where the reader picked the messages out, and the row it left focus on is where they were.
    function clear(): void {
        listed.takeFocus();
        revise({ selected: [] });
    }

    return (
        <div
            ref={strip}
            role="toolbar"
            aria-label={translate('select.bar')}
            className="flex shrink-0 items-center gap-0.5 overflow-x-auto border-b border-accent-line bg-accent-soft px-3 py-2"
        >
            <Control label={translate('select.clear')} icon="close" shape="selectedSymbol" onPress={clear} />

            {/* Said rather than only drawn: a reader who picked out four messages with the keyboard hears how many
                they are holding, and hears it change as they pick out a fifth. */}
            <p role="status" className="me-1.5 ps-0.5 text-base font-semibold whitespace-nowrap text-accent-deep">
                {translate(selectionCounted[new Intl.PluralRules(locale).select(messages.length)], {
                    count: new Intl.NumberFormat(locale).format(messages.length),
                })}
            </p>

            <span aria-hidden="true" className="mx-0.5 w-px self-stretch bg-accent-line" />

            <MailboxActControls messages={messages} shape={actShape} onActed={clear} />

            {/* Against the far edge, which is a margin the measurement above knows to leave out of what the bar
                needs. */}
            <Control label={translate('select.all')} shape={actShape} className="ms-auto" onPress={listed.selectAll} />
        </div>
    );
}
