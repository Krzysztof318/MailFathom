// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { AnswerBlock, AttachmentEntry } from '@mailfathom/client-backend';
import { Icon } from '../../controls/Icon';
import { sizeOf } from '../../localization/octets';
import { useLocalization } from '../../localization/useLocalization';
import { kindOf } from '../../readingPane/fileKind';
import { AnswerBlockCard, UnrecognisedAnswerBlock } from '../AnswerBlockCard';
import { useAnswerSources } from '../answerSources';
import { attachmentAvailabilities, attachmentCounts } from './blockWording';

// The files a question is about, each with the message it arrived on. It is a file browser over mail, so it answers
// the three questions somebody has before deciding which one to open — what kind of file, how large, and which message
// carried it — and it answers all three **before anything is fetched**.
//
// **Nothing here downloads a file, and pressing an entry never starts one.** What an entry opens is the source it
// names, which is the message the file arrived on: an attachment is only ever reached through the mail that carried
// it, and a gallery that fetched three files to show three cards would be the cost this block exists to avoid.
//
// **Availability is stated rather than discovered.** A message may be known from its metadata while its content was
// never stored, and retention may have removed content the metadata outlived. Saying which of the three it is turns a
// failed download into something the screen never offered — and the size still stands on every one of them, because it
// is the message's own account of the file rather than a measurement of stored bytes.
//
// **The kind is drawn from what the message declared** and is never used to decide how to open anything, which is the
// rule `readingPane/fileKind.ts` states where the reading pane draws the same chip.
//
// **The gallery is not windowed**, which is the repository's rule rather than an omission: the contract bounds it at
// fifty entries and `frontend/src/AGENTS.md` § *Performance* windows a list that can exceed two hundred rows.

export function AttachmentGallery({ block }: { readonly block: AnswerBlock }) {
    const { locale, translate } = useLocalization();

    if (block.type !== 'attachmentGallery') {
        return <UnrecognisedAnswerBlock named={block.named} />;
    }

    const { entries } = block;

    return (
        <AnswerBlockCard
            label={translate('answer.galleryLabel')}
            meta={
                entries.length === 0
                    ? undefined
                    : translate(attachmentCounts[new Intl.PluralRules(locale).select(entries.length)], {
                          count: new Intl.NumberFormat(locale).format(entries.length),
                      })
            }
            note={entries.length === 0 ? translate('answer.galleryEmpty') : undefined}
            state={entries.length === 0 ? 'empty' : 'ready'}
        >
            <ul className="grid grid-cols-2 gap-2.5 workspace:grid-cols-3">
                {entries.map((entry, at) => (
                    <li className="flex" key={`${entry.source}-${String(at)}`}>
                        <FoundFile entry={entry} />
                    </li>
                ))}
            </ul>
        </AnswerBlockCard>
    );
}

/** One file found in mail, in whichever of its two states it is: a source this client can name, or one it cannot. */
function FoundFile({ entry }: { readonly entry: AttachmentEntry }) {
    const { locale, translate } = useLocalization();
    const { sources, follow } = useAnswerSources();

    const declared = sources.get(entry.source);
    const availability = attachmentAvailabilities[entry.availability];
    const size = sizeOf(entry.sizeOctets, locale);
    const carried = declared?.label ?? translate('answer.sourcePrivateName');

    const said = (
        <>
            <span className="flex items-center justify-between gap-1.5">
                <span className="text-2xs text-muted uppercase">{kindOf(entry.name, entry.mediaType ?? '')}</span>

                <span
                    className={`flex items-center gap-1 rounded-full px-2 py-0.5 text-2xs whitespace-nowrap ${availability.tint}`}
                >
                    <Icon className="size-3" name={availability.icon} />
                    {translate(availability.label)}
                </span>
            </span>

            <span className="text-sm leading-snug font-semibold text-pretty">{entry.name}</span>

            <span className="text-xs text-muted text-pretty">
                {translate('answer.galleryFrom', { size, message: carried })}
            </span>
        </>
    );

    const card = 'flex w-full flex-col gap-1.75 rounded-lg border border-line bg-sunken px-3.5 py-3.25 text-start';

    // A source that can be followed is a control and one that cannot is not: drawing the private entry as a button
    // would offer somebody a way into mail this client was never given, which is the rule `EvidenceList.tsx` states
    // where it draws the same two cases — and so would drawing one whose target names a kind written after this build.
    if ((declared?.target ?? null) === null || follow === null) {
        return <div className={card}>{said}</div>;
    }

    return (
        <button
            aria-label={translate('answer.galleryItem', {
                name: entry.name,
                size,
                availability: translate(availability.label),
                message: carried,
            })}
            className={`${card} transition hover:border-accent`}
            type="button"
            onClick={() => {
                follow(entry.source);
            }}
        >
            {said}
        </button>
    );
}
