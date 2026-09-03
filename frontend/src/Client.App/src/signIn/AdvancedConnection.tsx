// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { Icon } from '../controls/Icon';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { portOf, type ResolvedConnection } from './connection';

// Everything about the connection that is not the address itself, behind the one disclosure the design project puts on
// the sign-in screen: the permission an unsecured connection needs, the warning that permission raises, and what the
// address somebody typed actually resolved to.
//
// The three are one disclosure rather than three controls on the form because they answer one question — what is this
// password about to cross — and because none of them is the ordinary case. What is not folded away with them is the
// fact that the permission is on: a closed disclosure still says so, beside its own label, since a screen that hid the
// one setting weakening the connection would be hiding exactly the thing a reader came to check.
//
// Every value in the summary is read back out of what `Client.Backend` resolved rather than parsed a second time.
// `connection.ts` beside this is where that reading is, and the reason is there: the rule saying which addresses may
// carry a credential is the wire's, and a screen that re-derived it would eventually tell somebody their password is
// going over TLS while it goes in the clear.

export function AdvancedConnection({
    connection,
    clearTextPermitted,
    clearTextConfigured,
    onPermitClearText,
}: {
    readonly connection: ResolvedConnection | null;

    /** Whether an unsecured connection is permitted for whatever is being typed. */
    readonly clearTextPermitted: boolean;

    /**
     * Whether that permission arrived from configuration rather than from this screen.
     *
     * Where it did, the row states it and does not offer it: the decision was taken by whoever installed this client,
     * and nothing a person does here is written back over what a deployment configured.
     */
    readonly clearTextConfigured: boolean;

    readonly onPermitClearText: (permitted: boolean) => void;
}) {
    const { translate } = useLocalization();
    const nothing = translate('connect.nothingNamed');

    return (
        <details className="group">
            <summary className="flex cursor-pointer items-center gap-2 text-sm text-accent-strong">
                <Icon name="chevron_right" className="size-4 transition group-open:rotate-90" />
                {translate('connect.advanced')}

                {/* The mark a closed disclosure still carries. It is words rather than a colour, for the reason the
                    certificate row below is: what a password crosses is not a statement anybody may be left to infer
                    from a hue. */}
                {clearTextPermitted ? (
                    <span className="rounded-sm bg-warning-soft px-1.75 py-0.5 text-xs text-warning-text">
                        {translate('connect.withoutTls')}
                    </span>
                ) : null}
            </summary>

            <div className="mt-3 flex flex-col gap-3 rounded-xl border border-line bg-sunken p-4">
                <ClearTextPermission
                    permitted={clearTextPermitted}
                    configured={clearTextConfigured}
                    onPermit={onPermitClearText}
                />

                {clearTextPermitted ? (
                    <p className="flex items-start gap-2 rounded-lg border border-warning bg-warning-soft px-3 py-2 text-xs text-warning-text">
                        <Icon name="warning" className="mt-px size-4" />
                        {translate('connect.clearTextInForce')}
                    </p>
                ) : null}

                <dl className="flex flex-col gap-2.5 rounded-lg border border-line bg-panel px-3.5 py-3 text-sm">
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

                    {/* Said in words rather than by the colour of the row, because a statement about whether a password
                        is encrypted is exactly the one nobody may be left to infer from a hue. */}
                    <Detail
                        label="connect.certificate"
                        value={
                            connection === null
                                ? nothing
                                : translate(
                                      connection.secure ? 'connect.certificateChecked' : 'connect.certificateNone',
                                  )
                        }
                        weight={
                            connection === null ? '' : connection.secure ? 'text-healthy-text' : 'text-warning-text'
                        }
                        separated
                    />
                </dl>
            </div>
        </details>
    );
}

/**
 * The one control on this screen that gives something away.
 *
 * A bordered row that turns to the warning weight once it is on, drawn as the design project draws it, with a real
 * checkbox inside a `label` wrapping the whole row — so it is operable from the keyboard, announced as a checkbox, and
 * hittable by a finger. Where a deployment configured the permission the same row is drawn without one, because there
 * is no choice left to offer and a disabled control saying nothing about why is worse than a sentence that says it.
 */
function ClearTextPermission({
    permitted,
    configured,
    onPermit,
}: {
    readonly permitted: boolean;
    readonly configured: boolean;
    readonly onPermit: (permitted: boolean) => void;
}) {
    const { translate } = useLocalization();
    const border = permitted ? 'border-warning bg-warning-soft' : 'border-line bg-panel';

    const said = (
        <span className="flex flex-col gap-1">
            <span className="text-base font-medium text-text">{translate('connect.clearText')}</span>
            <span className="text-xs text-muted" id="sign-in-clear-text-explanation">
                {translate(configured ? 'connect.clearTextConfigured' : 'connect.clearTextExplanation')}
            </span>
        </span>
    );

    if (configured) {
        return <p className={`flex items-start gap-3 rounded-xl border p-3 ${border}`}>{said}</p>;
    }

    return (
        <label className={`flex cursor-pointer items-start gap-3 rounded-xl border p-3 transition ${border}`}>
            <input
                aria-describedby="sign-in-clear-text-explanation"
                // The name is pinned rather than taken from the label's contents, because the label wraps the whole
                // row — which is what makes the row a target somebody can hit — and the sentence about what this costs
                // would otherwise be read twice: once as the name of the control and again as its description.
                aria-label={translate('connect.clearText')}
                checked={permitted}
                className="mt-0.5 size-4 shrink-0 accent-warning"
                type="checkbox"
                onChange={(event) => {
                    onPermit(event.target.checked);
                }}
            />
            {said}
        </label>
    );
}

function Detail({
    label,
    value,
    weight,
    separated,
}: {
    readonly label: MessageKey;
    readonly value: string;
    readonly weight?: string;
    readonly separated?: boolean;
}) {
    const { translate } = useLocalization();

    return (
        <div
            className={`flex items-baseline justify-between gap-3 ${separated === true ? 'border-t border-line-soft pt-2.5' : ''}`}
        >
            <dt className="text-muted">{translate(label)}</dt>
            <dd className={`truncate font-medium ${weight ?? ''}`}>{value}</dd>
        </div>
    );
}
