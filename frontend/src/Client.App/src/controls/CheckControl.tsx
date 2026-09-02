// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { chip } from './chrome';

// A checkbox drawn as one of the design project's chips, because that is what a filter that is on looks like there: a
// pill that is tinted while it is in force. The box itself stays, so the state is carried by the control and not by
// the tint alone.

const checkable = `flex cursor-pointer items-center gap-1.5 px-2.25 py-0.75 text-sm ${chip}`;

export function CheckControl({
    label,
    on,
    onChange,
}: {
    readonly label: string;
    readonly on: boolean;
    readonly onChange: (on: boolean) => void;
}) {
    return (
        <label className={`${checkable} ${on ? 'border-accent-line bg-accent-soft text-accent-deep' : ''}`}>
            <input
                type="checkbox"
                className="accent-accent"
                checked={on}
                onChange={(event) => {
                    onChange(event.target.checked);
                }}
            />
            {label}
        </label>
    );
}
