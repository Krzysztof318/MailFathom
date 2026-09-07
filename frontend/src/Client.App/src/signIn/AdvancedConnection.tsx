// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { Icon } from '../controls/Icon';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { portOf, type ResolvedConnection } from './connection';

// Everything about the connection that is not the address itself, behind the one disclosure the design project puts
// under the sign-in form: what the address resolved to, row by row, and — where the address is being typed here — the
// permission an unsecured connection needs and the warning that permission raises; where the address was chosen on an
// earlier run, the way to change it.
//
// They are one disclosure rather than several controls on the form because they answer one question — what is this
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
    onChangeServer,
}: {
    readonly connection: ResolvedConnection | null;

    /** Whether an unsecured connection is permitted for the address this screen is about. */
    readonly clearTextPermitted: boolean;

    /**
     * Whether that permission arrived from configuration rather than from this screen.
     *
     * Where it did, the row states it and does not offer it: the decision was taken by whoever installed this client,
     * and nothing a person does here is written back over what a deployment configured.
     */
    readonly clearTextConfigured: boolean;

    /**
     * Offered while an address is being typed on this screen, and `null` where the address arrived with the
     * deployment: the permission belongs to the address, and an address nobody here can change is one whose
     * permission nobody here decides.
     */
    readonly onPermitClearText: ((permitted: boolean) => void) | null;

    /** Offered where somebody named the deployment themselves, which is the one address that can be changed here. */
    readonly onChangeServer?: (() => void) | undefined;
}) {
    const { translate } = useLocalization();
    const nothing = translate('connect.nothingNamed');

    return (
        <details className="group">
            <summary className="-ms-2 flex min-h-12 cursor-pointer items-center gap-1.5 self-start rounded-xl ps-2 pe-3 text-md font-medium text-accent transition hover:bg-hover hover:text-accent-strong workspace:-ms-1.25 workspace:min-h-7 workspace:ps-1.25 workspace:pe-2.25 workspace:text-base">
                <Icon name="chevron_right" className="size-4.5 transition group-open:rotate-90" />
                {translate('connect.advanced')}

                {/* The mark a closed disclosure still carries. It is words rather than a colour, for the reason the
                    certificate row below is: what a password crosses is not a statement anybody may be left to infer
                    from a hue. */}
                {clearTextPermitted ? (
                    <span className="ms-0.5 rounded-sm bg-warning-soft px-1.75 py-0.5 text-xs text-warning-text">
                        {translate('connect.withoutTls')}
                    </span>
                ) : null}
            </summary>

            <div className="mt-2.75 flex flex-col gap-2.75">
                <dl className="flex flex-col gap-2.75 rounded-xl border border-line bg-sunken px-3.75 py-3.25 text-sm">
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

                {onPermitClearText === null ? null : (
                    <ClearTextPermission
                        permitted={clearTextPermitted}
                        configured={clearTextConfigured}
                        onPermit={onPermitClearText}
                    />
                )}

                {onPermitClearText !== null && clearTextPermitted ? (
                    <p className="flex items-start gap-2.25 rounded-lg border border-warning bg-warning-soft px-3 py-2.5 text-sm text-warning-text">
                        <Icon name="warning" className="mt-px size-4" />
                        {translate('connect.clearTextInForce')}
                    </p>
                ) : null}

                {onChangeServer === undefined ? null : (
                    <button
                        className="flex min-h-13 items-center justify-center gap-2 rounded-full border border-line bg-panel px-4 text-lg font-semibold text-accent transition hover:border-accent hover:bg-hover hover:text-accent-strong workspace:min-h-11 workspace:rounded-xl workspace:text-base"
                        type="button"
                        onClick={onChangeServer}
                    >
                        <Icon name="dns" className="size-4.25" />
                        {translate('connect.changeServer')}
                    </button>
                )}
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
        <span className="flex min-w-0 flex-col gap-0.75">
            <span className="text-base font-medium text-text">{translate('connect.clearText')}</span>
            <span className="text-xs text-muted" id="sign-in-clear-text-explanation">
                {translate(configured ? 'connect.clearTextConfigured' : 'connect.clearTextExplanation')}
            </span>
        </span>
    );

    if (configured) {
        return (
            <p
                className={`flex items-start gap-2.75 rounded-xl border p-3.5 workspace:px-3.25 workspace:py-3 ${border}`}
            >
                {said}
            </p>
        );
    }

    return (
        <label
            className={`flex cursor-pointer items-start gap-2.75 rounded-xl border p-3.5 transition workspace:px-3.25 workspace:py-3 ${border}`}
        >
            <input
                aria-describedby="sign-in-clear-text-explanation"
                // The name is pinned rather than taken from the label's contents, because the label wraps the whole
                // row — which is what makes the row a target somebody can hit — and the sentence about what this costs
                // would otherwise be read twice: once as the name of the control and again as its description.
                aria-label={translate('connect.clearText')}
                checked={permitted}
                className="mt-px size-5.5 shrink-0 accent-warning workspace:size-4.5"
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
            className={`flex items-baseline justify-between gap-3 ${separated === true ? 'border-t border-line-soft pt-2.75' : ''}`}
        >
            <dt className="text-muted">{translate(label)}</dt>
            <dd className={`truncate font-medium ${weight ?? ''}`}>{value}</dd>
        </div>
    );
}
