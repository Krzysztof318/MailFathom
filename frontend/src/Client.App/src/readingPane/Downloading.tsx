// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import type { AttachmentDeliveryOutcome } from '../deployment/attachmentExchange';
import type { Download } from './downloadingAttachment';
import { sizeOf } from '../localization/octets';

// What a download is saying while it happens and once it has: how much has arrived with the way out beside it, and then
// the one sentence that says what became of it. Drawn once for the two surfaces that offer a download — the row under a
// message and the viewer that opens a file — because a reader who pressed the same control in two places is owed the
// same words, and two arrangements of them is how the two come to disagree.

const refusalMessages: Readonly<Record<Exclude<AttachmentDeliveryOutcome, 'delivered'>, MessageKey>> = {
    abandoned: 'attachment.abandoned',
    unauthenticated: 'attachment.refusedUnauthenticated',
    unauthorized: 'attachment.refusedUnauthorized',
    unavailable: 'attachment.refusedUnavailable',
    largerThanDescribed: 'attachment.refusedLargerThanDescribed',
};

export function Downloading({
    download,
    name,
    whole,
    onStop,
}: {
    readonly download: Download;

    /** What the file is called, which is what a finished download names. */
    readonly name: string;

    /** How many octets the message said the file holds, which is what a proportion is read against. */
    readonly whole: number;

    readonly onStop: () => void;
}) {
    const { translate } = useLocalization();

    if (download.stage === 'described') {
        return null;
    }

    if (download.stage === 'arriving') {
        return <Arriving octets={download.octets} of={whole} onStop={onStop} />;
    }

    return (
        <p
            className={download.outcome === 'delivered' ? 'text-muted' : 'text-warning'}
            role={download.outcome === 'delivered' ? undefined : 'alert'}
        >
            {download.outcome === 'delivered'
                ? translate('attachment.saved', { name })
                : translate(refusalMessages[download.outcome])}
        </p>
    );
}

// How much has arrived and the way to stop it, said in the place the file will appear. `progress` is the element the
// platform already has for this: it announces itself, it needs no role, and a reader on a screen reader is told a
// proportion rather than a number of octets they would have to divide themselves.
function Arriving({
    octets,
    of,
    onStop,
}: {
    readonly octets: number;
    readonly of: number;
    readonly onStop: () => void;
}) {
    const { locale, translate } = useLocalization();

    return (
        <div className="flex flex-wrap items-center gap-2">
            <progress aria-label={translate('attachment.arriving')} className="h-1 flex-1" max={of} value={octets} />

            <span className="text-muted">
                {translate('attachment.arrivingOf', { arrived: sizeOf(octets, locale), whole: sizeOf(of, locale) })}
            </span>

            <button
                className="rounded-md border border-line bg-panel px-2 py-0.5 text-text-soft"
                type="button"
                onClick={onStop}
            >
                {translate('attachment.stop')}
            </button>
        </div>
    );
}
