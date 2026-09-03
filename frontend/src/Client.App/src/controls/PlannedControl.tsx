// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { Icon } from './Icon';
import type { IconName } from './icons';
import { controlShapes, labelledShape, type ControlShape } from './controlShapes';
import { useLocalization } from '../localization/useLocalization';

// A control the design project draws for something the client cannot do yet. It is present because leaving it out
// would make the client a different product from the one that was designed, and it is inert because drawing it as
// though it worked would be worse: its name says so, it refuses activation, and it is drawn at the weight of something
// that is not there yet rather than at the weight of an action.
//
// `aria-disabled` rather than `disabled`, so a screen reader still reaches it and hears the name — a control that is
// silently absent from the tab order is one a reader cannot learn the product has.

export function PlannedControl({
    label,
    icon,
    shape = 'labelled',
    className,
}: {
    readonly label: string;
    readonly icon?: IconName;
    readonly shape?: ControlShape;
    readonly className?: string;
}) {
    const { translate } = useLocalization();
    const labelled = labelledShape(shape);

    return (
        <button
            type="button"
            aria-disabled="true"
            aria-label={translate('control.notBuiltYet', { control: label })}
            title={translate('control.notBuiltYet', { control: label })}
            className={`flex shrink-0 cursor-not-allowed items-center whitespace-nowrap opacity-60 transition ${controlShapes[shape]} ${className ?? ''}`}
        >
            {icon === undefined ? null : <Icon name={icon} className={shape === 'floating' ? 'size-6' : 'size-4.5'} />}
            {labelled ? <span>{label}</span> : null}
        </button>
    );
}
