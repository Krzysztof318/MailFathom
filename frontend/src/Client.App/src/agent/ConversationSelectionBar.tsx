// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { Control } from '../controls/Control';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useWideWorkspace } from '../shell/useWideWorkspace';

const selectionCounted: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'select.count.other',
    one: 'select.count.one',
    two: 'select.count.other',
    few: 'select.count.few',
    many: 'select.count.many',
    other: 'select.count.other',
};

/**
 * What stands over the history while conversations are picked out: a way to put the selection down, how many are
 * picked out, and archiving or deleting all of them. On a phone each act is drawn as its symbol alone.
 */
export function ConversationSelectionBar({
    count,
    onClear,
    onArchive,
    onAskDeletion,
}: {
    readonly count: number;
    readonly onClear: () => void;
    readonly onArchive: () => void;
    readonly onAskDeletion: () => void;
}) {
    const { locale, translate } = useLocalization();
    const wide = useWideWorkspace();

    return (
        <div
            role="toolbar"
            aria-label={translate('agent.selectionBar')}
            className="flex shrink-0 items-center gap-0.5 overflow-x-auto rounded-lg border border-accent-line bg-accent-soft px-2 py-1.5"
        >
            <Control label={translate('agent.clearSelection')} icon="close" shape="selectedSymbol" onPress={onClear} />

            <p role="status" className="me-1.5 ps-0.5 text-base font-semibold whitespace-nowrap text-accent-deep">
                {translate(selectionCounted[new Intl.PluralRules(locale).select(count)], {
                    count: new Intl.NumberFormat(locale).format(count),
                })}
            </p>

            <Control
                label={translate('agent.archive')}
                icon="archive"
                shape={wide ? 'selected' : 'selectedSymbol'}
                className="ms-auto"
                onPress={onArchive}
            />

            <Control
                label={translate('agent.delete')}
                icon="delete"
                shape={wide ? 'selected' : 'selectedSymbol'}
                onPress={onAskDeletion}
            />
        </div>
    );
}
