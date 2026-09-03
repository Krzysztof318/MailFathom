// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { Icon } from './Icon';
import type { IconName } from './icons';
import { controlShapes, labelledShape, type ControlShape } from './controlShapes';

// A control that does something, in the shapes the design project draws one. It is `PlannedControl`'s counterpart and
// the two share the shape table rather than a resemblance: what a reader sees when something becomes real is the same
// button gaining an action, not a second button that looks nearly like the first.
//
// A shape drawing the symbol alone carries the name for the accessibility tree, because a control nobody can name is
// one a reader cannot reach and a suite cannot assert on.

export function Control({
    label,
    icon,
    shape = 'labelled',
    className,
    onPress,
}: {
    readonly label: string;
    readonly icon?: IconName;
    readonly shape?: ControlShape;
    readonly className?: string;
    readonly onPress: () => void;
}) {
    const labelled = labelledShape(shape);

    return (
        <button
            type="button"
            aria-label={labelled ? undefined : label}
            title={label}
            className={`flex shrink-0 items-center whitespace-nowrap transition ${controlShapes[shape]} ${className ?? ''}`}
            onClick={onPress}
        >
            {icon === undefined ? null : <Icon name={icon} className={shape === 'floating' ? 'size-6' : 'size-4.5'} />}
            {labelled ? <span>{label}</span> : null}
        </button>
    );
}
