// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useLocalization } from '../localization/useLocalization';

// What is running, drawn in the two places the design project puts it: the foot of the sign-in form, and the foot of
// the settings screen. It is one component rather than a line written twice because the two say the same thing under
// the same rule — the deployment's version stands beside the client's once the deployment has answered, and the
// client's own stands alone before that, which is the only version anything on this machine knows.
//
// The product's name is part of it for the same reason the design project draws it there: a version with nothing in
// front of it is a number, and the foot of a screen is where somebody reads what they are running rather than what
// this particular pane is.
//
// Where it stands and what it stands on is the caller's, which is why the class name is handed in: the sign-in screen
// puts it at the end of a row and the settings screen centres it on a bar of its own, and neither is a property of
// the sentence.

export function VersionLine({
    deploymentVersion,
    className,
}: {
    /** What the deployment answered it is running, or `null` while nothing has answered — which is every sign-in. */
    readonly deploymentVersion: string | null;

    readonly className?: string;
}) {
    const { translate } = useLocalization();

    return (
        <p className={className}>
            {translate('shell.title')}{' '}
            {deploymentVersion === null
                ? translate('shell.clientVersion', { client: __MAILFATHOM_VERSION__ })
                : translate('shell.versions', {
                      client: __MAILFATHOM_VERSION__,
                      deployment: deploymentVersion,
                  })}
        </p>
    );
}
