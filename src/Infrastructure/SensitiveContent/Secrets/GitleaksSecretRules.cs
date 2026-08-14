// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.RegularExpressions;

namespace MailFathom.Infrastructure.SensitiveContent.Secrets;

/// <summary>The third-party half of the secret corpus, carried across from the gitleaks rule data.</summary>
/// <remarks>
/// <para>
/// The detection engine's own corpus is oriented on Microsoft credential formats, and a mailbox receives GitHub
/// tokens, cloud access keys, payment keys, and model-provider keys instead. These expressions close that gap. They
/// are taken from the gitleaks rule data at the revision <see cref="CorpusRevision" /> names, under the MIT licence
/// recorded in <c>THIRD_PARTY_LICENSES.md</c>, and each keeps the corpus's own rule name so a suppression an
/// operator writes reads the same as the entry they looked up.
/// </para>
/// <para>
/// Only entries that identify a credential <i>by its own shape</i> are taken. gitleaks also ships rules that
/// recognise a secret by its proximity to a keyword, which need a surrounding assignment to fire and turn ordinary
/// prose into findings; a mailbox is prose, so those are left behind rather than tuned.
/// </para>
/// <para>
/// Four mechanical differences separate a Go RE2 expression from the .NET one beside it, and every one of them is
/// applied here rather than left to a reader: a POSIX class such as <c>[[:alnum:]]</c> becomes its explicit range,
/// because .NET reads the former as a character set holding a bracket and a colon; a <c>(?P&lt;name&gt;)</c> group
/// becomes <c>(?&lt;name&gt;)</c>; a trailing delimiter alternation becomes a negative lookahead over the alphabet the
/// credential is spelled in; and the part of the match that is the credential is named
/// <see cref="SecretRuleDefinition.SecretCaptureGroup" />, so a finding covers the credential rather than the quotation
/// mark or the space the expression needed in order to find its end.
/// </para>
/// <para>
/// <b>The third of those is what makes this corpus usable on mail at all.</b> gitleaks closes most of its expressions
/// with <c>(?:[\x60'"\s;]|\\[nr]|$)</c>, finding the end of a credential by requiring a backtick, a quotation mark,
/// whitespace, a semicolon, an escaped newline, or the end of the text after it. That is where a credential ends in
/// source control, and it is not where one ends in a message: a token closing a sentence, standing in a table, or
/// wrapped in brackets is followed by none of those, and the rule then reports nothing whatsoever rather than reporting
/// a shorter region. The lookahead states the condition that alternation was reaching for — a credential ends where its
/// own alphabet stops — so it holds for every character that can follow one instead of for an enumerated handful. It
/// consumes nothing, which is why the match becomes the credential and the named group goes wherever it existed only to
/// exclude a delimiter.
/// </para>
/// <para>
/// Where a rule alternates between credentials of different shapes, the lookahead belongs <i>inside</i> each branch. One
/// shared across them would forbid the union of their alphabets, so a token followed by a character its own branch never
/// contained would fail the lookahead, find nothing to backtrack into, and go unreported — the very miss this
/// transformation exists to remove, reintroduced by the transformation itself. <c>flyio-access-token</c>,
/// <c>openai-api-key</c>, and <c>vault-service-token</c> are the three rules that alternate this way.
/// </para>
/// <para>
/// Two rules keep a trailing form the transformation does not touch, because neither was ever blind to punctuation:
/// <c>openshift-user-token</c> ends in <c>(?:[^\w-]|\z)</c>, which succeeds before any character outside the
/// credential's alphabet, and <c>perplexity-api-key</c> carries <c>\b</c> as a further alternative of the delimiter
/// group, which does the same. Both are left exactly as the corpus declares them; rewriting a rule that already reads
/// mail correctly would spend a divergence on nothing.
/// </para>
/// <para>
/// <b>That fourth one is a judgement rather than a rewrite, and getting it wrong is silent.</b> A finding covers the
/// named group when there is one and the whole match when there is not, so a group placed around anything less than
/// the credential leaves the rest of it in the redacted text with nothing to report that it is still there. Name the
/// group only where the match is deliberately wider than the credential — an expression that consumes a delimiter to
/// find the credential's end, or a URL whose host is worth keeping readable — and leave it off wherever the whole
/// match is the credential, which includes every expression whose parentheses are there to repeat a block rather than
/// to capture one. Where gitleaks declares a <c>secretGroup</c>, that is the group; where it declares none, its own
/// finding is the whole match and so is this one.
/// </para>
/// <para>
/// One expression here deliberately says more than the corpus does, and it is marked at its own declaration:
/// <c>aws-amazon-bedrock-api-key-short-lived</c> runs past the constant opening that gitleaks matches, because
/// reporting where a key is and replacing one are different jobs and the corpus is written for the first.
/// </para>
/// <para>
/// Refreshing to a later gitleaks release is a reviewable diff of this file: take its <c>config/gitleaks.toml</c>,
/// apply those four transformations to the entries named below, carry across the divergences marked above, and move
/// <see cref="CorpusRevision" /> with them. An upstream expression arriving with the delimiter alternation restored is
/// the ordinary case rather than a signal, since the corpus it comes from is still written for source control. Adding
/// an entry means adding the rule to <see cref="Rules" /> as well, since the catalog is composed from that list and a
/// pattern nothing lists is a pattern nothing runs.
/// </para>
/// </remarks>
internal static partial class GitleaksSecretRules
{
    /// <summary>The gitleaks release these expressions were taken from.</summary>
    public const string CorpusRevision = "8.30.1";

    /// <summary>The revision of the transformations this file applies to that release's expressions on the way in.</summary>
    /// <remarks>
    /// Deliberately separate from the release beside it, which names somebody else's artifact and moves only when that
    /// artifact does. This names the half MailFathom is answerable for — what the four differences in the remarks above
    /// did to the expressions they were applied to — and it moves whenever one of them changes what a rule matches. A
    /// redaction is reproducible against a stated corpus and nothing else, so a text redacted under the earlier
    /// transformation is a different result, and something that stored one has to be able to say which it stored.
    /// </remarks>
    public const string TransformationRevision = "1";

    /// <summary>Every rule this half of the corpus contributes.</summary>
    public static IReadOnlyList<SecretRuleDefinition> Rules { get; } =
    [
        SecretRuleDefinition.Compile(SecretCategories.CloudAccessKey, "alibaba-access-key-id", AlibabaAccessKeyId),
        SecretRuleDefinition.Compile(SecretCategories.CloudAccessKey, "aws-access-token", AwsAccessToken),
        SecretRuleDefinition.Compile(SecretCategories.CloudAccessKey, "aws-amazon-bedrock-api-key-long-lived", AwsAmazonBedrockApiKeyLongLived),
        SecretRuleDefinition.Compile(SecretCategories.CloudAccessKey, "aws-amazon-bedrock-api-key-short-lived", AwsAmazonBedrockApiKeyShortLived),
        SecretRuleDefinition.Compile(SecretCategories.CloudAccessKey, "azure-ad-client-secret", AzureAdClientSecret),
        SecretRuleDefinition.Compile(SecretCategories.CloudAccessKey, "gcp-api-key", GcpApiKey),
        SecretRuleDefinition.Compile(SecretCategories.PrivateKey, "age-secret-key", AgeSecretKey),
        SecretRuleDefinition.Compile(SecretCategories.PrivateKey, "private-key", PrivateKey),
        SecretRuleDefinition.Compile(SecretCategories.JsonWebToken, "jwt", Jwt),
        SecretRuleDefinition.Compile(SecretCategories.JsonWebToken, "jwt-base64", JwtBase64),
        SecretRuleDefinition.Compile(SecretCategories.CredentialUrl, "microsoft-teams-webhook", MicrosoftTeamsWebhook),
        SecretRuleDefinition.Compile(SecretCategories.CredentialUrl, "sidekiq-sensitive-url", SidekiqSensitiveUrl),
        SecretRuleDefinition.Compile(SecretCategories.CredentialUrl, "slack-webhook-url", SlackWebhookUrl),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "1password-secret-key", OnePasswordSecretKey),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "1password-service-account-token", OnePasswordServiceAccountToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "adobe-client-secret", AdobeClientSecret),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "airtable-personnal-access-token", AirtablePersonnalAccessToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "anthropic-admin-api-key", AnthropicAdminApiKey),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "anthropic-api-key", AnthropicApiKey),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "artifactory-api-key", ArtifactoryApiKey),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "artifactory-reference-token", ArtifactoryReferenceToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "authress-service-client-access-key", AuthressServiceClientAccessKey),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "clickhouse-cloud-api-secret-key", ClickhouseCloudApiSecretKey),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "clojars-api-token", ClojarsApiToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "cloudflare-origin-ca-key", CloudflareOriginCaKey),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "databricks-api-token", DatabricksApiToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "digitalocean-access-token", DigitaloceanAccessToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "digitalocean-pat", DigitaloceanPat),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "digitalocean-refresh-token", DigitaloceanRefreshToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "doppler-api-token", DopplerApiToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "duffel-api-token", DuffelApiToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "dynatrace-api-token", DynatraceApiToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "easypost-api-token", EasypostApiToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "easypost-test-api-token", EasypostTestApiToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "facebook-page-access-token", FacebookPageAccessToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "flutterwave-encryption-key", FlutterwaveEncryptionKey),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "flutterwave-public-key", FlutterwavePublicKey),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "flutterwave-secret-key", FlutterwaveSecretKey),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "flyio-access-token", FlyioAccessToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "frameio-api-token", FrameioApiToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "github-app-token", GithubAppToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "github-fine-grained-pat", GithubFineGrainedPat),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "github-oauth", GithubOauth),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "github-pat", GithubPat),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "github-refresh-token", GithubRefreshToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "gitlab-cicd-job-token", GitlabCicdJobToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "gitlab-deploy-token", GitlabDeployToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "gitlab-feature-flag-client-token", GitlabFeatureFlagClientToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "gitlab-feed-token", GitlabFeedToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "gitlab-incoming-mail-token", GitlabIncomingMailToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "gitlab-kubernetes-agent-token", GitlabKubernetesAgentToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "gitlab-oauth-app-secret", GitlabOauthAppSecret),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "gitlab-pat", GitlabPat),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "gitlab-pat-routable", GitlabPatRoutable),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "gitlab-ptt", GitlabPtt),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "gitlab-rrt", GitlabRrt),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "gitlab-runner-authentication-token", GitlabRunnerAuthenticationToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "gitlab-runner-authentication-token-routable", GitlabRunnerAuthenticationTokenRoutable),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "gitlab-scim-token", GitlabScimToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "gitlab-session-cookie", GitlabSessionCookie),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "grafana-api-key", GrafanaApiKey),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "grafana-cloud-api-token", GrafanaCloudApiToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "grafana-service-account-token", GrafanaServiceAccountToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "harness-api-key", HarnessApiKey),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "hashicorp-tf-api-token", HashicorpTfApiToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "heroku-api-key-v2", HerokuApiKeyV2),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "huggingface-access-token", HuggingfaceAccessToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "huggingface-organization-api-token", HuggingfaceOrganizationApiToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "infracost-api-token", InfracostApiToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "intra42-client-secret", Intra42ClientSecret),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "linear-api-key", LinearApiKey),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "maxmind-license-key", MaxmindLicenseKey),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "notion-api-token", NotionApiToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "npm-access-token", NpmAccessToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "octopus-deploy-api-key", OctopusDeployApiKey),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "openai-api-key", OpenaiApiKey),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "openshift-user-token", OpenshiftUserToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "perplexity-api-key", PerplexityApiKey),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "planetscale-api-token", PlanetscaleApiToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "planetscale-oauth-token", PlanetscaleOauthToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "planetscale-password", PlanetscalePassword),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "postman-api-token", PostmanApiToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "prefect-api-token", PrefectApiToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "pulumi-api-token", PulumiApiToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "pypi-upload-token", PypiUploadToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "readme-api-token", ReadmeApiToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "rubygems-api-token", RubygemsApiToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "scalingo-api-token", ScalingoApiToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "sendgrid-api-token", SendgridApiToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "sendinblue-api-token", SendinblueApiToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "sentry-org-token", SentryOrgToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "sentry-user-token", SentryUserToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "settlemint-application-access-token", SettlemintApplicationAccessToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "settlemint-personal-access-token", SettlemintPersonalAccessToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "settlemint-service-access-token", SettlemintServiceAccessToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "shippo-api-token", ShippoApiToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "shopify-access-token", ShopifyAccessToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "shopify-custom-access-token", ShopifyCustomAccessToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "shopify-private-app-access-token", ShopifyPrivateAppAccessToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "shopify-shared-secret", ShopifySharedSecret),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "slack-app-token", SlackAppToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "slack-bot-token", SlackBotToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "slack-config-access-token", SlackConfigAccessToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "slack-config-refresh-token", SlackConfigRefreshToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "slack-legacy-bot-token", SlackLegacyBotToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "slack-legacy-token", SlackLegacyToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "slack-legacy-workspace-token", SlackLegacyWorkspaceToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "slack-user-token", SlackUserToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "square-access-token", SquareAccessToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "stripe-access-token", StripeAccessToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "twilio-api-key", TwilioApiKey),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "vault-batch-token", VaultBatchToken),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "vault-service-token", VaultServiceToken),
    ];

    [GeneratedRegex(@"\bLTAI(?i)[a-z0-9]{20}(?![a-zA-Z0-9])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex AlibabaAccessKeyId { get; }

    [GeneratedRegex(@"\b(?<refine>(?:A3T[A-Z0-9]|AKIA|ASIA|ABIA|ACCA)[A-Z2-7]{16})\b", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex AwsAccessToken { get; }

    [GeneratedRegex(@"\bABSK[A-Za-z0-9+/]{109,269}={0,2}(?![A-Za-z0-9+/=])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex AwsAmazonBedrockApiKeyLongLived { get; }

    // Extended past what the corpus carries. Upstream matches the constant opening alone — `bedrock-api-key-` and the
    // base64 of the service host — which is enough to report where a key is and not enough to replace one: the encoded
    // credential follows that constant and would survive redaction. The run after it is unbounded above on purpose,
    // since a ceiling that a longer key outgrew would leave its tail in the text for the same reason.
    [GeneratedRegex(@"\bbedrock-api-key-YmVkcm9jay5hbWF6b25hd3MuY29t[A-Za-z0-9+/]{20,}={0,2}(?![A-Za-z0-9+/=])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex AwsAmazonBedrockApiKeyShortLived { get; }

    [GeneratedRegex(@"(?:^|[\\'""\x60\s>=:(,)])(?<refine>[a-zA-Z0-9_~.]{3}\dQ~[a-zA-Z0-9_~.-]{31,34})(?:$|[\\'""\x60\s<),])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex AzureAdClientSecret { get; }

    [GeneratedRegex(@"\bAIza[\w-]{35}(?![\w\-])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GcpApiKey { get; }

    [GeneratedRegex(@"AGE-SECRET-KEY-1[QPZRY9X8GF2TVDW0S3JN54KHCE6MUA7L]{58}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex AgeSecretKey { get; }

    [GeneratedRegex(@"(?i)-----BEGIN[ A-Z0-9_-]{0,100}PRIVATE KEY(?: BLOCK)?-----[\s\S-]{64,}?KEY(?: BLOCK)?-----", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex PrivateKey { get; }

    [GeneratedRegex(@"\bey[a-zA-Z0-9]{17,}\.ey[a-zA-Z0-9\/\\_-]{17,}\.(?:[a-zA-Z0-9\/\\_-]{10,}={0,2})?(?![a-zA-Z0-9\/\\_=\-])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex Jwt { get; }

    [GeneratedRegex(@"\bZXlK(?:(?<alg>aGJHY2lPaU)|(?<apu>aGNIVWlPaU)|(?<apv>aGNIWWlPaU)|(?<aud>aGRXUWlPaU)|(?<b64>aU5qUWlP)|(?<crit>amNtbDBJanBi)|(?<cty>amRIa2lPaU)|(?<epk>bGNHc2lPbn)|(?<enc>bGJtTWlPaU)|(?<jku>cWEzVWlPaU)|(?<jwk>cWQyc2lPb)|(?<iss>cGMzTWlPaU)|(?<iv>cGRpSTZJ)|(?<kid>cmFXUWlP)|(?<key_ops>clpYbGZiM0J6SWpwY)|(?<kty>cmRIa2lPaUp)|(?<nonce>dWIyNWpaU0k2)|(?<p2c>d01tTWlP)|(?<p2s>d01uTWlPaU)|(?<ppt>d2NIUWlPaU)|(?<sub>emRXSWlPaU)|(?<svt>emRuUWlP)|(?<tag>MFlXY2lPaU)|(?<typ>MGVYQWlPaUp)|(?<url>MWNtd2l)|(?<use>MWMyVWlPaUp)|(?<ver>MlpYSWlPaU)|(?<version>MlpYSnphVzl1SWpv)|(?<x>NElqb2)|(?<x5c>NE5XTWlP)|(?<x5t>NE5YUWlPaU)|(?<x5ts256>NE5YUWpVekkxTmlJNkl)|(?<x5u>NE5YVWlPaU)|(?<zip>NmFYQWlPaU))[a-zA-Z0-9\/\\_+\-\r\n]{40,}={0,2}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex JwtBase64 { get; }

    // No `refine` group: the whole URL is the credential, since anyone holding it can post into the channel. The
    // parentheses that remain repeat a GUID block and name nothing, which `ExplicitCapture` already makes non-capturing.
    [GeneratedRegex(@"https://[a-z0-9]+\.webhook\.office\.com/webhookb2/[a-z0-9]{8}-([a-z0-9]{4}-){3}[a-z0-9]{12}@[a-z0-9]{8}-([a-z0-9]{4}-){3}[a-z0-9]{12}/IncomingWebhook/[a-z0-9]{32}/[a-z0-9]{8}-([a-z0-9]{4}-){3}[a-z0-9]{12}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex MicrosoftTeamsWebhook { get; }

    [GeneratedRegex(@"(?i)\bhttps?://(?<refine>[a-f0-9]{8}:[a-f0-9]{8})@(?:gems.contribsys.com|enterprise.contribsys.com)(?:[\/|\#|\?|:]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SidekiqSensitiveUrl { get; }

    [GeneratedRegex(@"(?:https?://)?hooks.slack.com/(?:services|workflows|triggers)/[A-Za-z0-9+/]{43,56}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SlackWebhookUrl { get; }

    [GeneratedRegex(@"\bA3-[A-Z0-9]{6}-(?:(?:[A-Z0-9]{11})|(?:[A-Z0-9]{6}-[A-Z0-9]{5}))-[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}\b", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex OnePasswordSecretKey { get; }

    [GeneratedRegex(@"ops_eyJ[a-zA-Z0-9+/]{250,}={0,3}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex OnePasswordServiceAccountToken { get; }

    [GeneratedRegex(@"\bp8e-(?i)[a-z0-9]{32}(?![a-zA-Z0-9])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex AdobeClientSecret { get; }

    [GeneratedRegex(@"\b(?<refine>pat[a-zA-Z0-9]{14}\.[a-f0-9]{64})\b", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex AirtablePersonnalAccessToken { get; }

    [GeneratedRegex(@"\bsk-ant-admin01-[a-zA-Z0-9_\-]{93}AA(?![a-zA-Z0-9_\-])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex AnthropicAdminApiKey { get; }

    [GeneratedRegex(@"\bsk-ant-api03-[a-zA-Z0-9_\-]{93}AA(?![a-zA-Z0-9_\-])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex AnthropicApiKey { get; }

    [GeneratedRegex(@"\bAKCp[A-Za-z0-9]{69}\b", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex ArtifactoryApiKey { get; }

    [GeneratedRegex(@"\bcmVmd[A-Za-z0-9]{59}\b", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex ArtifactoryReferenceToken { get; }

    [GeneratedRegex(@"\b(?:sc|ext|scauth|authress)_(?i)[a-z0-9]{5,30}\.[a-z0-9]{4,6}\.(?-i:acc)[_-][a-z0-9-]{10,32}\.[a-z0-9+/_=-]{30,120}(?![a-zA-Z0-9+/_=\-])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex AuthressServiceClientAccessKey { get; }

    [GeneratedRegex(@"\b(?<refine>4b1d[A-Za-z0-9]{38})\b", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex ClickhouseCloudApiSecretKey { get; }

    [GeneratedRegex(@"(?i)CLOJARS_[a-z0-9]{60}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex ClojarsApiToken { get; }

    [GeneratedRegex(@"\bv1\.0-[a-f0-9]{24}-[a-f0-9]{146}(?![a-f0-9])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex CloudflareOriginCaKey { get; }

    // The lookahead names the token's own alphabet and leaves the hyphen out, so a key followed by a hyphen and
    // something that is not a digit is still reported. That is the wider of the two readings on purpose: redacting a
    // region that turns out not to be a credential costs a reader some text, and missing one costs them the credential.
    [GeneratedRegex(@"\bdapi[a-f0-9]{32}(?:-\d)?(?![a-f0-9])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex DatabricksApiToken { get; }

    [GeneratedRegex(@"\bdoo_v1_[a-f0-9]{64}(?![a-f0-9])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex DigitaloceanAccessToken { get; }

    [GeneratedRegex(@"\bdop_v1_[a-f0-9]{64}(?![a-f0-9])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex DigitaloceanPat { get; }

    [GeneratedRegex(@"(?i)\bdor_v1_[a-f0-9]{64}(?![a-fA-F0-9])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex DigitaloceanRefreshToken { get; }

    [GeneratedRegex(@"dp\.pt\.(?i)[a-z0-9]{43}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex DopplerApiToken { get; }

    [GeneratedRegex(@"duffel_(?:test|live)_(?i)[a-z0-9_\-=]{43}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex DuffelApiToken { get; }

    [GeneratedRegex(@"dt0c01\.(?i)[a-z0-9]{24}\.[a-z0-9]{64}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex DynatraceApiToken { get; }

    [GeneratedRegex(@"\bEZAK(?i)[a-z0-9]{54}\b", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex EasypostApiToken { get; }

    [GeneratedRegex(@"\bEZTK(?i)[a-z0-9]{54}\b", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex EasypostTestApiToken { get; }

    [GeneratedRegex(@"\bEAA[MC](?i)[a-z0-9]{100,}(?![a-zA-Z0-9])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex FacebookPageAccessToken { get; }

    [GeneratedRegex(@"FLWSECK_TEST-(?i)[a-h0-9]{12}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex FlutterwaveEncryptionKey { get; }

    [GeneratedRegex(@"FLWPUBK_TEST-(?i)[a-h0-9]{32}-X", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex FlutterwavePublicKey { get; }

    [GeneratedRegex(@"FLWSECK_TEST-(?i)[a-h0-9]{32}-X", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex FlutterwaveSecretKey { get; }

    // One lookahead per branch, because the three spell a credential in different alphabets: a shared one would forbid
    // their union and drop a token whose own alphabet had ended, which is the miss this rule was rewritten to stop.
    [GeneratedRegex(@"\b(?:fo1_[\w-]{43}(?![\w-])|fm1[ar]_[a-zA-Z0-9+\/]{100,}={0,3}(?![a-zA-Z0-9+/])|fm2_[a-zA-Z0-9+\/]{100,}={0,3}(?![a-zA-Z0-9+/]))", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex FlyioAccessToken { get; }

    [GeneratedRegex(@"fio-u-(?i)[a-z0-9\-_=]{64}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex FrameioApiToken { get; }

    [GeneratedRegex(@"(?:ghu|ghs)_[0-9a-zA-Z]{36}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GithubAppToken { get; }

    [GeneratedRegex(@"github_pat_\w{82}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GithubFineGrainedPat { get; }

    [GeneratedRegex(@"gho_[0-9a-zA-Z]{36}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GithubOauth { get; }

    [GeneratedRegex(@"ghp_[0-9a-zA-Z]{36}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GithubPat { get; }

    [GeneratedRegex(@"ghr_[0-9a-zA-Z]{36}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GithubRefreshToken { get; }

    [GeneratedRegex(@"glcbt-[0-9a-zA-Z]{1,5}_[0-9a-zA-Z_-]{20}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GitlabCicdJobToken { get; }

    [GeneratedRegex(@"gldt-[0-9a-zA-Z_\-]{20}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GitlabDeployToken { get; }

    [GeneratedRegex(@"glffct-[0-9a-zA-Z_\-]{20}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GitlabFeatureFlagClientToken { get; }

    [GeneratedRegex(@"glft-[0-9a-zA-Z_\-]{20}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GitlabFeedToken { get; }

    [GeneratedRegex(@"glimt-[0-9a-zA-Z_\-]{25}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GitlabIncomingMailToken { get; }

    [GeneratedRegex(@"glagent-[0-9a-zA-Z_\-]{50}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GitlabKubernetesAgentToken { get; }

    [GeneratedRegex(@"gloas-[0-9a-zA-Z_\-]{64}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GitlabOauthAppSecret { get; }

    [GeneratedRegex(@"glpat-[\w-]{20}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GitlabPat { get; }

    [GeneratedRegex(@"\bglpat-[0-9a-zA-Z_-]{27,300}\.[0-9a-z]{2}[0-9a-z]{7}\b", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GitlabPatRoutable { get; }

    [GeneratedRegex(@"glptt-[0-9a-f]{40}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GitlabPtt { get; }

    [GeneratedRegex(@"GR1348941[\w-]{20}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GitlabRrt { get; }

    [GeneratedRegex(@"glrt-[0-9a-zA-Z_\-]{20}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GitlabRunnerAuthenticationToken { get; }

    [GeneratedRegex(@"\bglrt-t\d_[0-9a-zA-Z_\-]{27,300}\.[0-9a-z]{2}[0-9a-z]{7}\b", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GitlabRunnerAuthenticationTokenRoutable { get; }

    [GeneratedRegex(@"glsoat-[0-9a-zA-Z_\-]{20}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GitlabScimToken { get; }

    [GeneratedRegex(@"_gitlab_session=[0-9a-z]{32}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GitlabSessionCookie { get; }

    [GeneratedRegex(@"(?i)\beyJrIjoi[A-Za-z0-9]{70,400}={0,3}(?![A-Za-z0-9=])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GrafanaApiKey { get; }

    [GeneratedRegex(@"(?i)\bglc_[A-Za-z0-9+/]{32,400}={0,3}(?![A-Za-z0-9+/=])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GrafanaCloudApiToken { get; }

    [GeneratedRegex(@"(?i)\bglsa_[A-Za-z0-9]{32}_[A-Fa-f0-9]{8}(?![A-Fa-f0-9])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GrafanaServiceAccountToken { get; }

    [GeneratedRegex(@"(?:pat|sat)\.[a-zA-Z0-9_-]{22}\.[a-zA-Z0-9]{24}\.[a-zA-Z0-9]{20}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex HarnessApiKey { get; }

    [GeneratedRegex(@"(?i)[a-z0-9]{14}\.(?-i:atlasv1)\.[a-z0-9\-_=]{60,70}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex HashicorpTfApiToken { get; }

    [GeneratedRegex(@"\b(HRKU-AA[0-9a-zA-Z_-]{58})(?![0-9a-zA-Z_\-])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex HerokuApiKeyV2 { get; }

    [GeneratedRegex(@"\bhf_(?i:[a-z]{34})(?![a-zA-Z])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex HuggingfaceAccessToken { get; }

    [GeneratedRegex(@"\bapi_org_(?i:[a-z]{34})(?![a-zA-Z])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex HuggingfaceOrganizationApiToken { get; }

    [GeneratedRegex(@"\bico-[a-zA-Z0-9]{32}(?![a-zA-Z0-9])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex InfracostApiToken { get; }

    [GeneratedRegex(@"\bs-s4t2(?:ud|af)-(?i)[abcdef0123456789]{64}(?![a-fA-F0-9])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex Intra42ClientSecret { get; }

    [GeneratedRegex(@"lin_api_(?i)[a-z0-9]{40}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex LinearApiKey { get; }

    [GeneratedRegex(@"\b[A-Za-z0-9]{6}_[A-Za-z0-9]{29}_mmk(?![A-Za-z0-9])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex MaxmindLicenseKey { get; }

    [GeneratedRegex(@"\bntn_[0-9]{11}[A-Za-z0-9]{32}[A-Za-z0-9]{3}(?![A-Za-z0-9])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex NotionApiToken { get; }

    [GeneratedRegex(@"(?i)\bnpm_[a-z0-9]{36}(?![a-zA-Z0-9])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex NpmAccessToken { get; }

    [GeneratedRegex(@"\bAPI-[A-Z0-9]{26}(?![A-Z0-9])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex OctopusDeployApiKey { get; }

    // The project branch's own alphabet carries the hyphen, which is why the word boundary gitleaks ends it with cannot
    // stay: a suffix ending in one, followed by anything else non-word, puts no transition where the boundary looks for
    // it, and the branch fails rather than reporting a shorter region. The legacy branch is alphanumeric throughout and
    // says so itself instead of borrowing the wider set.
    [GeneratedRegex(@"\b(?:sk-(?:proj|svcacct|admin)-(?:[A-Za-z0-9_-]{74}|[A-Za-z0-9_-]{58})T3BlbkFJ(?:[A-Za-z0-9_-]{74}|[A-Za-z0-9_-]{58})(?![A-Za-z0-9_])|sk-[a-zA-Z0-9]{20}T3BlbkFJ[a-zA-Z0-9]{20}(?![a-zA-Z0-9]))", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex OpenaiApiKey { get; }

    [GeneratedRegex(@"\b(?<refine>sha256~[\w-]{43})(?:[^\w-]|\z)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex OpenshiftUserToken { get; }

    [GeneratedRegex(@"\b(?<refine>pplx-[a-zA-Z0-9]{48})(?:[\x60'""\s;]|\\[nr]|$|\b)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex PerplexityApiKey { get; }

    [GeneratedRegex(@"\bpscale_tkn_(?i)[\w=\.-]{32,64}(?![\w=\.\-])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex PlanetscaleApiToken { get; }

    [GeneratedRegex(@"\bpscale_oauth_[\w=\.-]{32,64}(?![\w=\.\-])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex PlanetscaleOauthToken { get; }

    [GeneratedRegex(@"(?i)\bpscale_pw_(?i)[\w=\.-]{32,64}(?![\w=\.\-])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex PlanetscalePassword { get; }

    [GeneratedRegex(@"\bPMAK-(?i)[a-f0-9]{24}\-[a-f0-9]{34}(?![a-fA-F0-9])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex PostmanApiToken { get; }

    [GeneratedRegex(@"\bpnu_[a-zA-Z0-9]{36}(?![a-zA-Z0-9])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex PrefectApiToken { get; }

    [GeneratedRegex(@"\bpul-[a-f0-9]{40}(?![a-f0-9])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex PulumiApiToken { get; }

    [GeneratedRegex(@"pypi-AgEIcHlwaS5vcmc[\w-]{50,1000}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex PypiUploadToken { get; }

    [GeneratedRegex(@"\brdme_[a-z0-9]{70}(?![a-z0-9])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex ReadmeApiToken { get; }

    [GeneratedRegex(@"\brubygems_[a-f0-9]{48}(?![a-f0-9])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex RubygemsApiToken { get; }

    [GeneratedRegex(@"\btk-us-[\w-]{48}(?![\w\-])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex ScalingoApiToken { get; }

    // The credential's own alphabet carries a dot, and its length is exact, so the lookahead leaves the dot out: a
    // sixty-seventh character cannot belong to a sixty-six-character token, and keeping it in would miss every key that
    // ends a sentence — which is the shape this rule is most often written in.
    [GeneratedRegex(@"\bSG\.(?i)[a-z0-9=_\-\.]{66}(?![a-zA-Z0-9=_\-])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SendgridApiToken { get; }

    [GeneratedRegex(@"\bxkeysib-[a-f0-9]{64}\-(?i)[a-z0-9]{16}(?![a-zA-Z0-9])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SendinblueApiToken { get; }

    [GeneratedRegex(@"\bsntrys_eyJpYXQiO[a-zA-Z0-9+/]{10,200}(?:LCJyZWdpb25fdXJs|InJlZ2lvbl91cmwi|cmVnaW9uX3VybCI6)[a-zA-Z0-9+/]{10,200}={0,2}_[a-zA-Z0-9+/]{43}(?:[^a-zA-Z0-9+/]|\z)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SentryOrgToken { get; }

    [GeneratedRegex(@"\bsntryu_[a-f0-9]{64}(?![a-f0-9])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SentryUserToken { get; }

    [GeneratedRegex(@"\bsm_aat_[a-zA-Z0-9]{16}(?![a-zA-Z0-9])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SettlemintApplicationAccessToken { get; }

    [GeneratedRegex(@"\bsm_pat_[a-zA-Z0-9]{16}(?![a-zA-Z0-9])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SettlemintPersonalAccessToken { get; }

    [GeneratedRegex(@"\bsm_sat_[a-zA-Z0-9]{16}(?![a-zA-Z0-9])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SettlemintServiceAccessToken { get; }

    [GeneratedRegex(@"\bshippo_(?:live|test)_[a-fA-F0-9]{40}(?![a-fA-F0-9])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex ShippoApiToken { get; }

    [GeneratedRegex(@"shpat_[a-fA-F0-9]{32}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex ShopifyAccessToken { get; }

    [GeneratedRegex(@"shpca_[a-fA-F0-9]{32}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex ShopifyCustomAccessToken { get; }

    [GeneratedRegex(@"shppa_[a-fA-F0-9]{32}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex ShopifyPrivateAppAccessToken { get; }

    [GeneratedRegex(@"shpss_[a-fA-F0-9]{32}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex ShopifySharedSecret { get; }

    [GeneratedRegex(@"(?i)xapp-\d-[A-Z0-9]+-\d+-[a-z0-9]+", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SlackAppToken { get; }

    [GeneratedRegex(@"xoxb-[0-9]{10,13}-[0-9]{10,13}[a-zA-Z0-9-]*", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SlackBotToken { get; }

    [GeneratedRegex(@"(?i)xoxe.xox[bp]-\d-[A-Z0-9]{163,166}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SlackConfigAccessToken { get; }

    [GeneratedRegex(@"(?i)xoxe-\d-[A-Z0-9]{146}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SlackConfigRefreshToken { get; }

    [GeneratedRegex(@"xoxb-[0-9]{8,14}-[a-zA-Z0-9]{18,26}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SlackLegacyBotToken { get; }

    [GeneratedRegex(@"xox[os]-\d+-\d+-\d+-[a-fA-F\d]+", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SlackLegacyToken { get; }

    [GeneratedRegex(@"xox[ar]-(?:\d-)?[0-9a-zA-Z]{8,48}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SlackLegacyWorkspaceToken { get; }

    [GeneratedRegex(@"xox[pe](?:-[0-9]{10,13}){3}-[a-zA-Z0-9-]{28,34}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SlackUserToken { get; }

    [GeneratedRegex(@"\b(?:EAAA|sq0atp-)[\w-]{22,60}(?![\w\-])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SquareAccessToken { get; }

    [GeneratedRegex(@"\b(?:sk|rk)_(?:test|live|prod)_[a-zA-Z0-9]{10,99}(?![a-zA-Z0-9])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex StripeAccessToken { get; }

    [GeneratedRegex(@"SK[0-9a-fA-F]{32}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex TwilioApiKey { get; }

    [GeneratedRegex(@"\bhvb\.[\w-]{138,300}(?![\w\-])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex VaultBatchToken { get; }

    // The legacy branch is alphanumeric where the current one also takes an underscore and a hyphen, and its lookahead
    // spells both cases because the case-insensitive group above it ends before the lookahead begins.
    [GeneratedRegex(@"\b(?:hvs\.[\w-]{90,120}(?![\w-])|s\.(?i:[a-z0-9]{24})(?![a-zA-Z0-9]))", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex VaultServiceToken { get; }
}
