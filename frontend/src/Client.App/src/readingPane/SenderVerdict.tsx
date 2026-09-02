// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailSenderVerdict } from '@mailfathom/client-backend';
import { useLocalization } from '../localization/useLocalization';

// What the deployment established about who actually sent a message, drawn where it says something and drawn nowhere
// otherwise. A badge on every message is a badge nobody reads, and the mail this pane mostly shows is legitimate mail
// whose author authenticated and whom nobody has named — which is the state the service calls ordinary and which is
// therefore silent here.
//
// Nothing on this side combines the two outcomes or compares the authenticated domain against the displayed one. Both
// would be rules, and a rule is the service's: a provider that signs as itself while the author's own domain passes is
// authenticated exactly as it appears, and a client that decided otherwise would warn about ordinary mail.

export function SenderVerdict({ verdict }: { readonly verdict: MailSenderVerdict }) {
    const { translate } = useLocalization();

    const failed = verdict.authorAuthentication === 'Failed';
    const recognized = verdict.deploymentTrust === 'Trusted';

    if (!failed && !recognized) {
        return null;
    }

    return (
        <aside
            className={`flex flex-col gap-1 rounded-md border px-4 py-3 text-sm ${
                failed ? 'border-warning bg-warning-soft text-warning' : 'border-line-soft bg-healthy-soft text-healthy'
            }`}
        >
            <p className="font-medium">{translate(failed ? 'sender.failed' : 'sender.recognized')}</p>

            <p>
                {verdict.authenticatedDomain === null
                    ? translate('sender.authenticatedByNobody')
                    : translate('sender.authenticatedBy', { domain: verdict.authenticatedDomain })}
            </p>
        </aside>
    );
}
