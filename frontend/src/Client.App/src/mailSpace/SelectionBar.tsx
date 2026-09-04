// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { Control } from '../controls/Control';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { MailboxActControls } from '../mailboxActs/MailboxActControls';
import { actedMessages, useListedMail } from '../messageList/useListedMail';
import { useWideWorkspace } from '../shell/useWideWorkspace';
import { useWorkspace } from '../workspace/useWorkspace';

// What stands where the toolbar stands while messages are picked out, which is the design project's own bar: how many
// are selected, the five acts over the whole of them, a way to take the listing in at once, and the way out.
//
// It replaces the toolbar rather than standing beside it, because the two answer the same question — *what am I doing
// to mail* — and a screen offering both would be asking a reader which of two rows their next press belongs to. It is
// drawn at every width for that same reason: the narrow composition has no toolbar to replace, and a selection with no
// way to act on it or leave it would be a state a person cannot get out of.
//
// **Leaving is the close control and nothing else.** It clears the selection rather than acting on it, which is what
// makes picking messages out safe to do by accident.
//
// The acts themselves are `mailboxActs/MailboxActControls.tsx`, drawn here at the weight the accent fill needs: the
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
    const wide = useWideWorkspace();

    // The messages themselves rather than the identities the workspace holds, because an act has to name the account
    // each message is in and the folder it is leaving — and because a count of messages this client could not name is
    // a count of what pressing an act would not touch.
    const messages = actedMessages(listed, workspace.selected);

    function clear(): void {
        revise({ selected: [] });
    }

    return (
        <div
            role="toolbar"
            aria-label={translate('select.bar')}
            className="flex shrink-0 items-center gap-0.5 overflow-x-auto bg-accent px-3.5 py-2 shadow-raised"
        >
            <Control label={translate('select.clear')} icon="close" shape="onAccentSymbol" onPress={clear} />

            {/* Said rather than only drawn: a reader who picked out four messages with the keyboard hears how many
                they are holding, and hears it change as they pick out a fifth.

                It wraps rather than holding one line, which is the design project's own bar at phone width: the count
                and the acts together are wider than the screen, and a count that refused to wrap would push the way to
                take the listing in at once off the end of a row nothing says can be scrolled. */}
            <p role="status" className="me-2 ps-0.5 text-base font-semibold text-balance text-on-accent">
                {translate(selectionCounted[new Intl.PluralRules(locale).select(messages.length)], {
                    count: new Intl.NumberFormat(locale).format(messages.length),
                })}
            </p>

            {/* Words beside the symbols where there is room for them, and the symbols alone where there is not — which
                is the design project's own bar at each width. The name is on the control either way, so nothing is
                lost to a reader who is not looking at it. */}
            <MailboxActControls messages={messages} shape={wide ? 'onAccent' : 'onAccentSymbol'} onActed={clear} />

            <span className="min-w-1 flex-1" />

            <Control label={translate('select.all')} shape="onAccent" onPress={listed.selectAll} />
        </div>
    );
}
