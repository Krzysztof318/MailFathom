// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useState, type ReactNode } from 'react';
import type { MailDocumentLink } from '@mailfathom/client-backend';
import { useLocalization, type Translate } from '../localization/useLocalization';
import { useLinkOpener } from '../shellOperations/linkOpener';

// A link whose words say one thing and whose target says another is the oldest trick in mail, and a reading pane that
// opens it without showing where it goes is participating. So where the link goes is drawn beside it rather than
// hidden in a tooltip nothing reaches from a keyboard, and the judgement about whether it deceives is the service's,
// carried on the link, so two clients cannot come to disagree about it.

// Which sentence says why. Whether to warn at all is `worthWarningAbout` at the call site, so a deployment that flags a
// link for a reason this client does not enumerate still warns somebody — under the last sentence here.
function warningsFor(link: MailDocumentLink, place: string, translate: Translate): string[] {
    const said: string[] = [];

    if (link.deception === 'DisplayedHostDiffers') {
        said.push(translate('link.warningDisplayedHostDiffers', { host: place }));
    }

    if (link.asciiHost !== null) {
        said.push(translate('link.warningAsciiHost', { host: place, asciiHost: link.asciiHost }));
    }

    return said.length === 0 ? [translate('link.warningWorthChecking', { host: place })] : said;
}

export function MessageLink({ link, children }: { readonly link: MailDocumentLink; readonly children: ReactNode }) {
    const { translate } = useLocalization();
    const openLink = useLinkOpener();
    const [refused, setRefused] = useState(false);

    // The place the link goes, as a person recognizes it. A host is absent for the one scheme that has none, and the
    // whole target is what says where a `mailto:` link goes.
    const place = link.host ?? link.target;

    return (
        <>
            <a
                className="text-accent underline decoration-accent/50 underline-offset-2"
                href={link.target}
                rel="noopener noreferrer"
                target="_blank"
                onClick={(event) => {
                    // The application asks for the link to be opened rather than letting the document navigate, which
                    // is what makes the answer the same on both heads.
                    event.preventDefault();
                    setRefused(false);
                    void openLink(link.target).catch(() => {
                        setRefused(true);
                    });
                }}
            >
                {children}
            </a>{' '}
            <span className="text-sm text-muted">{translate('link.goesTo', { host: place })}</span>
            {link.worthWarningAbout
                ? warningsFor(link, place, translate).map((warning) => (
                      <span className="text-sm text-warning" key={warning}>
                          {' '}
                          {warning}
                      </span>
                  ))
                : null}
            {refused ? <span className="text-sm text-warning"> {translate('link.couldNotOpen')}</span> : null}
        </>
    );
}
