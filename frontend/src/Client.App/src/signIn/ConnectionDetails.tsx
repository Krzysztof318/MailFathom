// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { Icon } from '../controls/Icon';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { portOf, type ResolvedConnection } from './connection';

// What the address somebody typed resolves to, behind the disclosure the design project puts on the sign-in screen.
// What it is for is the question the address field cannot answer on its own, and `connection.ts` beside this is where
// the answer is read. Where the entry resolves to nothing, this says nothing rather than guessing.

export function ConnectionDetails({ connection }: { readonly connection: ResolvedConnection | null }) {
    const { translate } = useLocalization();
    const nothing = translate('connect.nothingNamed');

    return (
        <details className="rounded-xl border border-line bg-sunken">
            <summary className="flex cursor-pointer items-center gap-2 px-4 py-3 text-sm text-accent-strong">
                <Icon name="expand_more" className="size-4" />
                {translate('connect.details')}
            </summary>

            <dl className="flex flex-col gap-2 border-t border-line-soft px-4 py-3 text-sm">
                <Detail
                    label="connect.protocol"
                    value={
                        connection === null
                            ? nothing
                            : translate(connection.secure ? 'connect.protocolOverTls' : 'connect.protocolClearText')
                    }
                />

                <Detail label="connect.host" value={connection?.authority ?? nothing} />

                <Detail
                    label="connect.port"
                    value={
                        connection === null
                            ? nothing
                            : (connection.port ?? translate('connect.portDefault', { port: portOf(connection) }))
                    }
                />

                {/* Said in words rather than by the colour of the row, because a statement about whether a password is
                    encrypted is exactly the one nobody may be left to infer from a hue. */}
                <Detail
                    label="connect.encryption"
                    value={
                        connection === null
                            ? nothing
                            : translate(connection.secure ? 'connect.encryptionInForce' : 'connect.encryptionNone')
                    }
                    weight={connection === null ? '' : connection.secure ? 'text-healthy-text' : 'text-warning-text'}
                />
            </dl>
        </details>
    );
}

function Detail({
    label,
    value,
    weight,
}: {
    readonly label: MessageKey;
    readonly value: string;
    readonly weight?: string;
}) {
    const { translate } = useLocalization();

    return (
        <div className="flex items-baseline justify-between gap-3">
            <dt className="text-muted">{translate(label)}</dt>
            <dd className={`truncate font-medium ${weight ?? ''}`}>{value}</dd>
        </div>
    );
}
