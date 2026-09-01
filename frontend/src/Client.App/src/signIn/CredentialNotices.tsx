// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { RefObject } from 'react';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';

// What the client has to say about the credential itself rather than about anything it was used to read. Each of these
// is something the store or the deployment did that the person would otherwise learn from the client behaving oddly on
// a later start, and each is stated in the same shape because they arrive together: being turned away is what asks the
// store to forget, and a store that refuses is a second sentence about the same moment.
//
// It renders on both screens for that reason. Two of the three are read on the way back to the sign-in form, and the
// third is read inside the frame, because a credential that could not be kept is learned about at the moment somebody
// successfully signs in.

/** What the client has to say about the credential this machine holds, whichever screen is on it when it says so. */
export type CredentialNotice = 'credentialNoLongerAccepted' | 'passwordNotKept' | 'passwordNotRemoved';

const noticeMessages: Readonly<Record<CredentialNotice, MessageKey>> = {
    credentialNoLongerAccepted: 'signIn.noLongerAccepted',
    passwordNotKept: 'signIn.notKept',
    passwordNotRemoved: 'signIn.notRemoved',
};

/**
 * Everything the client currently has to say about the credential, or nothing where it has nothing.
 *
 * The block takes focus rather than each sentence announcing itself: every one of these is inserted in the same commit
 * as its own text, which is the case a live region does not announce, and the screen behind it has just changed. So
 * whoever placed focus for that change places it here instead when there is something to read first.
 */
export function CredentialNotices({
    notices,
    ref,
}: {
    readonly notices: readonly CredentialNotice[];
    readonly ref?: RefObject<HTMLDivElement | null>;
}) {
    const { translate } = useLocalization();

    if (notices.length === 0) {
        return null;
    }

    return (
        <div ref={ref} tabIndex={-1} className="flex flex-col gap-2 text-warning">
            {notices.map((notice) => (
                <p key={notice} role="status">
                    {translate(noticeMessages[notice])}
                </p>
            ))}
        </div>
    );
}
