// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import type { ClientCapability } from './capabilities';

// What this credential may not do here, said once and in one place. Everything it may not do is simply absent from the
// screen, which is what keeps somebody from pressing an action that would be refused — and an absence nobody explained
// is a client that looks broken, so this is the sentence that turns one into the other.
//
// Each of these is about the credential rather than about the deployment, and the two are never worded alike: a grant
// is something whoever runs the deployment can give, and a deployment that does not do something at all is a different
// situation with a different next step. `accounts.notRefreshing` is that other kind, and it says so in its own words.

const capabilityNotices: Readonly<Record<ClientCapability, MessageKey>> = {
    readMail: 'grant.readMail',
    askMail: 'grant.askMail',
    markMailRead: 'grant.markMailRead',
};

export function GrantNotice({ withheld }: { readonly withheld: readonly ClientCapability[] }) {
    const { translate } = useLocalization();

    if (withheld.length === 0) {
        return null;
    }

    // A named region rather than a heading, because the frame's one heading belongs to the space below it and a second
    // one written above it would put the document's headings out of the order a reader moves through them in.
    return (
        <section aria-label={translate('grant.heading')} className="flex flex-col gap-2 text-sm text-muted">
            {withheld.map((capability) => (
                <p key={capability}>{translate(capabilityNotices[capability])}</p>
            ))}
        </section>
    );
}
