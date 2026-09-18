// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useLocalization } from '../localization/useLocalization';
import { ReceivedAt } from '../controls/ReceivedAt';

// What MailFathom has made of a relationship, which is the card the design project draws at the head of a person's
// page: the reading itself, the action it proposes, the patterns it noticed, and the figures beside them.
//
// **The reading is not built yet.** It is authored by the run that gives an opened contact a relationship note and a
// suggested next action, and until that lands this card says so rather than standing empty or being left off the page:
// a section that vanished would leave a reader unable to tell a person MailFathom has nothing to say about from a
// screen that forgot to draw it. The empty state is therefore the whole of this card's own content, and it says which
// part of the product it is waiting for rather than apologising.
//
// **The figures beside it are the mailbox's rather than the reading's**, which is why one of them is drawn today: when
// this person was last in touch is read off the correlation the two columns below are drawn from, and it waits with
// them rather than with the reading above it. The design draws two more — how much is owed to this person and which
// cases they appear in — and this deployment holds neither, which is a correction owed to the design rather than a
// figure to invent here.

export function RelationshipReading({
    lastCorrespondedAt,
    reading,
}: {
    /** When this person was last in touch, or `null` where the correlation found nothing or has not answered. */
    readonly lastCorrespondedAt: string | null;

    /** Whether the correlation the figure is read from is still running. */
    readonly reading: boolean;
}) {
    const { translate } = useLocalization();

    return (
        <section
            aria-label={translate('person.relationship')}
            className="flex flex-col gap-2.25 rounded-xl border border-line border-s-4 border-s-accent bg-panel px-4.25 py-3.75"
        >
            <div className="flex items-center gap-2.25">
                <h3 className="text-xs tracking-widest text-muted uppercase">{translate('person.relationship')}</h3>
                <span className="rounded-sm bg-accent px-1.5 py-0.5 text-2xs font-semibold text-on-accent">
                    {translate('person.authored')}
                </span>
            </div>

            <p className="text-md text-text-soft text-pretty">{translate('person.relationshipPending')}</p>

            <div className="flex flex-col gap-2 rounded-lg bg-sunken px-3 py-2.5">
                <p className="text-xs tracking-wide text-muted uppercase">{translate('person.lastContact')}</p>

                {reading ? (
                    <p role="status" className="text-sm text-muted">
                        {translate('person.correspondenceReading')}
                    </p>
                ) : lastCorrespondedAt === null ? (
                    <p className="text-sm text-faint">{translate('person.neverInTouch')}</p>
                ) : (
                    <p className="text-base">
                        <ReceivedAt at={lastCorrespondedAt} />
                    </p>
                )}
            </div>
        </section>
    );
}
