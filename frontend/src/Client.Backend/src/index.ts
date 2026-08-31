// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

export {
    deploymentSessionRoute,
    reachDeployment,
    resolveDeploymentEntry,
    type DeploymentEntryRefusal,
    type DeploymentEntryResult,
    type DeploymentGreeting,
} from './deployment';
export { failureReasonForStatus, type ClientFailure, type ClientFailureReason, type ClientResult } from './failure';
export {
    mailAccountsRoute,
    readMailAccounts,
    type MailAccount,
    type MailAccountDirectory,
    type MailSynchronizationState,
} from './mailAccounts';
export { clientRoutePrefix, headersFor, routeFor, type ClientSession, type DeploymentAddress } from './session';
export { longestResponseBody, type ClientRequest, type ClientResponse, type MailFathomTransport } from './transport';
