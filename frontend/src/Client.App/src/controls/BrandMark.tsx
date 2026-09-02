// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import mark from '../assets/mailfathom-mark.png';

// MailFathom's own mark, which two surfaces draw: the brand panel the sign-in screen stands beside, and the top of the
// navigation rail. Stated once because two arrangements of one image is how a product starts having two logos, and the
// file is in the bundle for the reason the typeface and the icons are — a deployment that keeps mail on its own server
// hands nobody else a request for it.
//
// It is decorative wherever a wordmark stands beside it and it names the product where it stands alone, which is why
// the caller says which: the rail draws the mark by itself and a reader has to be told what it is, and the sign-in
// panel writes `MailFathom` in words right next to it, where reading the name twice is noise.

export function BrandMark({ label, className }: { readonly label?: string; readonly className?: string }) {
    return (
        <img
            src={mark}
            alt={label ?? ''}
            aria-hidden={label === undefined ? 'true' : undefined}
            className={`shrink-0 rounded-xl ${className ?? 'size-10'}`}
        />
    );
}
