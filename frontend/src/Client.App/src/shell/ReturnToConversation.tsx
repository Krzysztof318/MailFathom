// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';

/**
 * The one control back to the agent conversation somebody left to look at what a proposal pointed to.
 *
 * It is the frame's rather than the Agent screen's because it stands over whichever space was opened — a thread, the
 * calendar — and it is what keeps *go and look* from being the press that loses the place. The conversation itself is
 * not carried here: every space stays mounted, so the Agent is still holding the one that was open.
 */
export function ReturnToConversation({ onReturn }: { readonly onReturn: () => void }) {
    const { translate } = useLocalization();

    return (
        <button
            className="fixed bottom-19.5 left-1/2 z-40 inline-flex min-h-11 -translate-x-1/2 items-center gap-2 rounded-full bg-accent px-4.5 text-md font-semibold whitespace-nowrap text-on-accent shadow-overlay transition hover:bg-accent-strong workspace:bottom-5.5"
            type="button"
            onClick={onReturn}
        >
            <Icon className="size-4.5" name="arrow_back" />
            {translate('agent.returnToConversation')}
        </button>
    );
}
