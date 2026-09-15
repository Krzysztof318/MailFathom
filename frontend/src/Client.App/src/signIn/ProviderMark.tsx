// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { Icon } from '../controls/Icon';
import { providerMark } from './providerMarks';

// The one place a provider's mark is drawn, which is to this family what `controls/Icon.tsx` is to the symbols: the
// component every screen goes through rather than a path written into whichever control wanted one. It is a second
// component rather than a case inside that one because the two families differ in the thing `Icon.tsx` is built on —
// it names the symbols the client has as a type, and what is drawn here is matched at run time against a name an
// operator wrote into their own configuration, which no type can close over.
//
// What follows from that is the fallback: a server the bundle carries no mark for is the ordinary case rather than a
// gap, and it is drawn with the symbol the client already has for a credential.

/**
 * Draws the mark committed for a provider, or the credential symbol where the bundle carries none.
 *
 * @param name The provider's name as the deployment published it.
 * @param className The size the drawing control gives it, which is the control's decision rather than this one's.
 */
export function ProviderMark({ name, className }: { readonly name: string; readonly className: string }) {
    const mark = providerMark(name);

    return mark === undefined ? (
        <Icon name="key" className={className} />
    ) : (
        <svg aria-hidden="true" viewBox={mark.box} className={`shrink-0 ${className}`} style={{ fill: mark.tint }}>
            <path d={mark.outline} />
        </svg>
    );
}
