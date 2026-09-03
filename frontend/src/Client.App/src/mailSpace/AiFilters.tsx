// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { IconName } from '../controls/icons';
import { PlannedControl } from '../controls/PlannedControl';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';

// The section the design project draws under the mailbox tree: three views of the mailbox that MailFathom's own
// reading of the mail would produce. That reading is stage 3's, so what stands here is the room for it, drawn as the
// placeholders every unbuilt control in this client is drawn as.

const filters: readonly { readonly icon: IconName; readonly label: MessageKey }[] = [
    { icon: 'pending_actions', label: 'aiFilters.needsDecision' },
    { icon: 'handshake', label: 'aiFilters.commitments' },
    { icon: 'schedule', label: 'aiFilters.deadlinesThisWeek' },
];

export function AiFilters({ folded }: { readonly folded: boolean }) {
    const { translate } = useLocalization();

    return (
        <section
            aria-label={translate('aiFilters.heading')}
            className="mt-4 flex flex-col gap-1.5 border-t border-line pt-3.5"
        >
            {/* The heading names a group a reader can see the whole of; the rail draws the group as its symbols and the
                section's own name is what a reader who cannot see them is given instead. */}
            {folded ? null : (
                <p className="px-2.75 text-xs tracking-widest text-muted uppercase">{translate('aiFilters.heading')}</p>
            )}

            {filters.map((filter) => (
                <PlannedControl
                    key={filter.icon}
                    label={translate(filter.label)}
                    icon={filter.icon}
                    shape={folded ? 'symbol' : 'labelled'}
                    className={folded ? 'self-center' : 'justify-start'}
                />
            ))}
        </section>
    );
}
