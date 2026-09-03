// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useId, useState, type ReactNode } from 'react';
import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';
import { looksLikeAnAddress, mostRecipientsInOneHeader } from './composition';

// One header of a message being written, as the design project draws it: the header's name, a chip per address with a
// way to take each one back off, and a field to write the next one in.
//
// **Completion is the platform's own.** A `datalist` gives the field a list to complete from with the keyboard path,
// the announcement, and the filtering already written — and what it costs is nothing, which is the whole argument
// against a listbox this client would have to build and test. What it completes from is handed in: today that is the
// people already in the conversation being answered, because the contact directory the service holds is not served to
// the client yet.

export function RecipientField({
    label,
    addresses,
    completions,
    onChanged,
    trailing,
}: {
    /** What this header is called, which is what the field is named by and what a chip's removal names. */
    readonly label: string;

    readonly addresses: readonly string[];

    /** Addresses worth offering as the field is typed in, which may be none. */
    readonly completions: readonly string[];

    readonly onChanged: (addresses: readonly string[]) => void;

    /** What stands at the end of the row, which the design puts the reveal for the copy headers in. */
    readonly trailing?: ReactNode;
}) {
    const { translate } = useLocalization();
    const [written, setWritten] = useState('');
    const [refused, setRefused] = useState<string | null>(null);
    const fieldId = useId();
    const completionsId = useId();

    // The field's own text is committed on Enter, on a comma, and on leaving the field, because all three are ways
    // somebody signals they have finished writing one address. What it refuses says why rather than doing nothing.
    function commit(text: string): void {
        const address = text.trim().replace(/,$/u, '');

        if (address === '') {
            setRefused(null);

            return;
        }

        if (!looksLikeAnAddress(address)) {
            setRefused(translate('compose.notAnAddress'));

            return;
        }

        if (addresses.includes(address)) {
            setRefused(translate('compose.alreadyAddressed', { address }));

            return;
        }

        if (addresses.length >= mostRecipientsInOneHeader) {
            setRefused(translate('compose.tooManyAddresses', { count: mostRecipientsInOneHeader.toFixed(0) }));

            return;
        }

        setWritten('');
        setRefused(null);
        onChanged([...addresses, address]);
    }

    return (
        <div className="flex flex-wrap items-center gap-2.5 border-b border-line-soft px-3.75 py-2.25">
            <label htmlFor={fieldId} className="w-11 shrink-0 text-sm text-muted">
                {label}
            </label>

            {addresses.map((address) => (
                <span
                    key={address}
                    className="flex items-center gap-1.75 rounded-4xl border border-line bg-rail px-2.5 py-0.75 text-base"
                >
                    {address}
                    <button
                        type="button"
                        aria-label={translate('compose.removeRecipient', { address, header: label })}
                        className="flex items-center rounded-xs text-faint transition hover:text-text"
                        onClick={() => {
                            onChanged(addresses.filter((kept) => kept !== address));
                        }}
                    >
                        <Icon name="close" className="size-3.5" />
                    </button>
                </span>
            ))}

            <input
                id={fieldId}
                list={completions.length === 0 ? undefined : completionsId}
                value={written}
                inputMode="email"
                autoComplete="off"
                placeholder={translate('compose.addRecipient')}
                className="min-w-40 flex-1 border-none bg-transparent text-base text-text outline-none placeholder:text-faint"
                onChange={(event) => {
                    setRefused(null);
                    setWritten(event.target.value);

                    // A comma is how one address is written after another, so it commits what stands before it rather
                    // than becoming part of an address nothing would accept.
                    if (event.target.value.endsWith(',')) {
                        commit(event.target.value);
                    }
                }}
                onKeyDown={(event) => {
                    if (event.key === 'Enter') {
                        event.preventDefault();
                        commit(written);
                    }
                }}
                onBlur={() => {
                    commit(written);
                }}
            />

            {trailing}

            {completions.length === 0 ? null : (
                <datalist id={completionsId}>
                    {completions.map((address) => (
                        <option key={address} value={address} />
                    ))}
                </datalist>
            )}

            {refused === null ? null : (
                <p role="alert" className="basis-full text-sm text-warning-text">
                    {refused}
                </p>
            )}
        </div>
    );
}
