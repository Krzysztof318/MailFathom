// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.RegularExpressions;

namespace MailFathom.Infrastructure.SensitiveContent;

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
/// Three mechanical differences separate a Go RE2 expression from the .NET one beside it, and every one of them is
/// applied here rather than left to a reader: a POSIX class such as <c>[[:alnum:]]</c> becomes its explicit range,
/// because .NET reads the former as a character set holding a bracket and a colon; a <c>(?P&lt;name&gt;)</c> group
/// becomes <c>(?&lt;name&gt;)</c>; and the group gitleaks reports as the secret is renamed <c>refine</c>, which is the
/// group the engine reports in place of the whole match, so a finding covers the credential rather than the quotation
/// mark or the space the expression needed in order to find its end.
/// </para>
/// <para>
/// Refreshing to a later gitleaks release is a reviewable diff of this file: take its <c>config/gitleaks.toml</c>,
/// apply those three transformations to the entries named below, and move <see cref="CorpusRevision" /> with them.
/// Adding an entry means adding the rule to <see cref="Rules" /> as well, since the catalog is composed from that
/// list and a pattern nothing lists is a pattern nothing runs.
/// </para>
/// </remarks>
internal static partial class GitleaksSecretRules
{
    /// <summary>The gitleaks release these expressions were taken from.</summary>
    public const string CorpusRevision = "8.30.1";

    /// <summary>Every rule this half of the corpus contributes.</summary>
    public static IReadOnlyList<SecretRuleDefinition> Rules { get; } =
    [
        SecretRuleDefinition.Compile(SecretCategories.CloudAccessKey, "alibaba-access-key-id", AlibabaAccessKeyId()),
        SecretRuleDefinition.Compile(SecretCategories.CloudAccessKey, "aws-access-token", AwsAccessToken()),
        SecretRuleDefinition.Compile(SecretCategories.CloudAccessKey, "aws-amazon-bedrock-api-key-long-lived", AwsAmazonBedrockApiKeyLongLived()),
        SecretRuleDefinition.Compile(SecretCategories.CloudAccessKey, "aws-amazon-bedrock-api-key-short-lived", AwsAmazonBedrockApiKeyShortLived()),
        SecretRuleDefinition.Compile(SecretCategories.CloudAccessKey, "azure-ad-client-secret", AzureAdClientSecret()),
        SecretRuleDefinition.Compile(SecretCategories.CloudAccessKey, "gcp-api-key", GcpApiKey()),
        SecretRuleDefinition.Compile(SecretCategories.PrivateKey, "age-secret-key", AgeSecretKey()),
        SecretRuleDefinition.Compile(SecretCategories.PrivateKey, "private-key", PrivateKey()),
        SecretRuleDefinition.Compile(SecretCategories.JsonWebToken, "jwt", Jwt()),
        SecretRuleDefinition.Compile(SecretCategories.JsonWebToken, "jwt-base64", JwtBase64()),
        SecretRuleDefinition.Compile(SecretCategories.CredentialUrl, "microsoft-teams-webhook", MicrosoftTeamsWebhook()),
        SecretRuleDefinition.Compile(SecretCategories.CredentialUrl, "sidekiq-sensitive-url", SidekiqSensitiveUrl()),
        SecretRuleDefinition.Compile(SecretCategories.CredentialUrl, "slack-webhook-url", SlackWebhookUrl()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "1password-secret-key", OnePasswordSecretKey()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "1password-service-account-token", OnePasswordServiceAccountToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "adobe-client-secret", AdobeClientSecret()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "airtable-personnal-access-token", AirtablePersonnalAccessToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "anthropic-admin-api-key", AnthropicAdminApiKey()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "anthropic-api-key", AnthropicApiKey()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "artifactory-api-key", ArtifactoryApiKey()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "artifactory-reference-token", ArtifactoryReferenceToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "authress-service-client-access-key", AuthressServiceClientAccessKey()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "clickhouse-cloud-api-secret-key", ClickhouseCloudApiSecretKey()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "clojars-api-token", ClojarsApiToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "cloudflare-origin-ca-key", CloudflareOriginCaKey()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "databricks-api-token", DatabricksApiToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "digitalocean-access-token", DigitaloceanAccessToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "digitalocean-pat", DigitaloceanPat()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "digitalocean-refresh-token", DigitaloceanRefreshToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "doppler-api-token", DopplerApiToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "duffel-api-token", DuffelApiToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "dynatrace-api-token", DynatraceApiToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "easypost-api-token", EasypostApiToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "easypost-test-api-token", EasypostTestApiToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "facebook-page-access-token", FacebookPageAccessToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "flutterwave-encryption-key", FlutterwaveEncryptionKey()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "flutterwave-public-key", FlutterwavePublicKey()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "flutterwave-secret-key", FlutterwaveSecretKey()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "flyio-access-token", FlyioAccessToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "frameio-api-token", FrameioApiToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "github-app-token", GithubAppToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "github-fine-grained-pat", GithubFineGrainedPat()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "github-oauth", GithubOauth()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "github-pat", GithubPat()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "github-refresh-token", GithubRefreshToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "gitlab-cicd-job-token", GitlabCicdJobToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "gitlab-deploy-token", GitlabDeployToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "gitlab-feature-flag-client-token", GitlabFeatureFlagClientToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "gitlab-feed-token", GitlabFeedToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "gitlab-incoming-mail-token", GitlabIncomingMailToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "gitlab-kubernetes-agent-token", GitlabKubernetesAgentToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "gitlab-oauth-app-secret", GitlabOauthAppSecret()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "gitlab-pat", GitlabPat()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "gitlab-pat-routable", GitlabPatRoutable()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "gitlab-ptt", GitlabPtt()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "gitlab-rrt", GitlabRrt()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "gitlab-runner-authentication-token", GitlabRunnerAuthenticationToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "gitlab-runner-authentication-token-routable", GitlabRunnerAuthenticationTokenRoutable()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "gitlab-scim-token", GitlabScimToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "gitlab-session-cookie", GitlabSessionCookie()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "grafana-api-key", GrafanaApiKey()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "grafana-cloud-api-token", GrafanaCloudApiToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "grafana-service-account-token", GrafanaServiceAccountToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "harness-api-key", HarnessApiKey()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "hashicorp-tf-api-token", HashicorpTfApiToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "heroku-api-key-v2", HerokuApiKeyV2()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "huggingface-access-token", HuggingfaceAccessToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "huggingface-organization-api-token", HuggingfaceOrganizationApiToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "infracost-api-token", InfracostApiToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "intra42-client-secret", Intra42ClientSecret()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "linear-api-key", LinearApiKey()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "maxmind-license-key", MaxmindLicenseKey()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "notion-api-token", NotionApiToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "npm-access-token", NpmAccessToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "octopus-deploy-api-key", OctopusDeployApiKey()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "openai-api-key", OpenaiApiKey()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "openshift-user-token", OpenshiftUserToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "perplexity-api-key", PerplexityApiKey()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "planetscale-api-token", PlanetscaleApiToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "planetscale-oauth-token", PlanetscaleOauthToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "planetscale-password", PlanetscalePassword()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "postman-api-token", PostmanApiToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "prefect-api-token", PrefectApiToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "pulumi-api-token", PulumiApiToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "pypi-upload-token", PypiUploadToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "readme-api-token", ReadmeApiToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "rubygems-api-token", RubygemsApiToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "scalingo-api-token", ScalingoApiToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "sendgrid-api-token", SendgridApiToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "sendinblue-api-token", SendinblueApiToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "sentry-org-token", SentryOrgToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "sentry-user-token", SentryUserToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "settlemint-application-access-token", SettlemintApplicationAccessToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "settlemint-personal-access-token", SettlemintPersonalAccessToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "settlemint-service-access-token", SettlemintServiceAccessToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "shippo-api-token", ShippoApiToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "shopify-access-token", ShopifyAccessToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "shopify-custom-access-token", ShopifyCustomAccessToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "shopify-private-app-access-token", ShopifyPrivateAppAccessToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "shopify-shared-secret", ShopifySharedSecret()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "slack-app-token", SlackAppToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "slack-bot-token", SlackBotToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "slack-config-access-token", SlackConfigAccessToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "slack-config-refresh-token", SlackConfigRefreshToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "slack-legacy-bot-token", SlackLegacyBotToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "slack-legacy-token", SlackLegacyToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "slack-legacy-workspace-token", SlackLegacyWorkspaceToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "slack-user-token", SlackUserToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "square-access-token", SquareAccessToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "stripe-access-token", StripeAccessToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "twilio-api-key", TwilioApiKey()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "vault-batch-token", VaultBatchToken()),
        SecretRuleDefinition.Compile(SecretCategories.ProviderToken, "vault-service-token", VaultServiceToken()),
    ];

    [GeneratedRegex(@"\b(?<refine>LTAI(?i)[a-z0-9]{20})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex AlibabaAccessKeyId();

    [GeneratedRegex(@"\b(?<refine>(?:A3T[A-Z0-9]|AKIA|ASIA|ABIA|ACCA)[A-Z2-7]{16})\b", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex AwsAccessToken();

    [GeneratedRegex(@"\b(?<refine>ABSK[A-Za-z0-9+/]{109,269}={0,2})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex AwsAmazonBedrockApiKeyLongLived();

    [GeneratedRegex(@"bedrock-api-key-YmVkcm9jay5hbWF6b25hd3MuY29t", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex AwsAmazonBedrockApiKeyShortLived();

    [GeneratedRegex(@"(?:^|[\\'""\x60\s>=:(,)])(?<refine>[a-zA-Z0-9_~.]{3}\dQ~[a-zA-Z0-9_~.-]{31,34})(?:$|[\\'""\x60\s<),])", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex AzureAdClientSecret();

    [GeneratedRegex(@"\b(?<refine>AIza[\w-]{35})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GcpApiKey();

    [GeneratedRegex(@"AGE-SECRET-KEY-1[QPZRY9X8GF2TVDW0S3JN54KHCE6MUA7L]{58}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex AgeSecretKey();

    [GeneratedRegex(@"(?i)-----BEGIN[ A-Z0-9_-]{0,100}PRIVATE KEY(?: BLOCK)?-----[\s\S-]{64,}?KEY(?: BLOCK)?-----", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex PrivateKey();

    [GeneratedRegex(@"\b(?<refine>ey[a-zA-Z0-9]{17,}\.ey[a-zA-Z0-9\/\\_-]{17,}\.(?:[a-zA-Z0-9\/\\_-]{10,}={0,2})?)(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex Jwt();

    [GeneratedRegex(@"\bZXlK(?:(?<alg>aGJHY2lPaU)|(?<apu>aGNIVWlPaU)|(?<apv>aGNIWWlPaU)|(?<aud>aGRXUWlPaU)|(?<b64>aU5qUWlP)|(?<crit>amNtbDBJanBi)|(?<cty>amRIa2lPaU)|(?<epk>bGNHc2lPbn)|(?<enc>bGJtTWlPaU)|(?<jku>cWEzVWlPaU)|(?<jwk>cWQyc2lPb)|(?<iss>cGMzTWlPaU)|(?<iv>cGRpSTZJ)|(?<kid>cmFXUWlP)|(?<key_ops>clpYbGZiM0J6SWpwY)|(?<kty>cmRIa2lPaUp)|(?<nonce>dWIyNWpaU0k2)|(?<p2c>d01tTWlP)|(?<p2s>d01uTWlPaU)|(?<ppt>d2NIUWlPaU)|(?<sub>emRXSWlPaU)|(?<svt>emRuUWlP)|(?<tag>MFlXY2lPaU)|(?<typ>MGVYQWlPaUp)|(?<url>MWNtd2l)|(?<use>MWMyVWlPaUp)|(?<ver>MlpYSWlPaU)|(?<version>MlpYSnphVzl1SWpv)|(?<x>NElqb2)|(?<x5c>NE5XTWlP)|(?<x5t>NE5YUWlPaU)|(?<x5ts256>NE5YUWpVekkxTmlJNkl)|(?<x5u>NE5YVWlPaU)|(?<zip>NmFYQWlPaU))[a-zA-Z0-9\/\\_+\-\r\n]{40,}={0,2}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex JwtBase64();

    [GeneratedRegex(@"https://[a-z0-9]+\.webhook\.office\.com/webhookb2/[a-z0-9]{8}-(?<refine>[a-z0-9]{4}-){3}[a-z0-9]{12}@[a-z0-9]{8}-([a-z0-9]{4}-){3}[a-z0-9]{12}/IncomingWebhook/[a-z0-9]{32}/[a-z0-9]{8}-([a-z0-9]{4}-){3}[a-z0-9]{12}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex MicrosoftTeamsWebhook();

    [GeneratedRegex(@"(?i)\bhttps?://(?<refine>[a-f0-9]{8}:[a-f0-9]{8})@(?:gems.contribsys.com|enterprise.contribsys.com)(?:[\/|\#|\?|:]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SidekiqSensitiveUrl();

    [GeneratedRegex(@"(?:https?://)?hooks.slack.com/(?:services|workflows|triggers)/[A-Za-z0-9+/]{43,56}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SlackWebhookUrl();

    [GeneratedRegex(@"\bA3-[A-Z0-9]{6}-(?:(?:[A-Z0-9]{11})|(?:[A-Z0-9]{6}-[A-Z0-9]{5}))-[A-Z0-9]{5}-[A-Z0-9]{5}-[A-Z0-9]{5}\b", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex OnePasswordSecretKey();

    [GeneratedRegex(@"ops_eyJ[a-zA-Z0-9+/]{250,}={0,3}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex OnePasswordServiceAccountToken();

    [GeneratedRegex(@"\b(?<refine>p8e-(?i)[a-z0-9]{32})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex AdobeClientSecret();

    [GeneratedRegex(@"\b(?<refine>pat[a-zA-Z0-9]{14}\.[a-f0-9]{64})\b", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex AirtablePersonnalAccessToken();

    [GeneratedRegex(@"\b(?<refine>sk-ant-admin01-[a-zA-Z0-9_\-]{93}AA)(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex AnthropicAdminApiKey();

    [GeneratedRegex(@"\b(?<refine>sk-ant-api03-[a-zA-Z0-9_\-]{93}AA)(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex AnthropicApiKey();

    [GeneratedRegex(@"\bAKCp[A-Za-z0-9]{69}\b", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex ArtifactoryApiKey();

    [GeneratedRegex(@"\bcmVmd[A-Za-z0-9]{59}\b", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex ArtifactoryReferenceToken();

    [GeneratedRegex(@"\b(?<refine>(?:sc|ext|scauth|authress)_(?i)[a-z0-9]{5,30}\.[a-z0-9]{4,6}\.(?-i:acc)[_-][a-z0-9-]{10,32}\.[a-z0-9+/_=-]{30,120})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex AuthressServiceClientAccessKey();

    [GeneratedRegex(@"\b(?<refine>4b1d[A-Za-z0-9]{38})\b", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex ClickhouseCloudApiSecretKey();

    [GeneratedRegex(@"(?i)CLOJARS_[a-z0-9]{60}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex ClojarsApiToken();

    [GeneratedRegex(@"\b(?<refine>v1\.0-[a-f0-9]{24}-[a-f0-9]{146})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex CloudflareOriginCaKey();

    [GeneratedRegex(@"\b(?<refine>dapi[a-f0-9]{32}(?:-\d)?)(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex DatabricksApiToken();

    [GeneratedRegex(@"\b(?<refine>doo_v1_[a-f0-9]{64})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex DigitaloceanAccessToken();

    [GeneratedRegex(@"\b(?<refine>dop_v1_[a-f0-9]{64})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex DigitaloceanPat();

    [GeneratedRegex(@"(?i)\b(?<refine>dor_v1_[a-f0-9]{64})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex DigitaloceanRefreshToken();

    [GeneratedRegex(@"dp\.pt\.(?i)[a-z0-9]{43}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex DopplerApiToken();

    [GeneratedRegex(@"duffel_(?:test|live)_(?i)[a-z0-9_\-=]{43}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex DuffelApiToken();

    [GeneratedRegex(@"dt0c01\.(?i)[a-z0-9]{24}\.[a-z0-9]{64}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex DynatraceApiToken();

    [GeneratedRegex(@"\bEZAK(?i)[a-z0-9]{54}\b", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex EasypostApiToken();

    [GeneratedRegex(@"\bEZTK(?i)[a-z0-9]{54}\b", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex EasypostTestApiToken();

    [GeneratedRegex(@"\b(?<refine>EAA[MC](?i)[a-z0-9]{100,})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex FacebookPageAccessToken();

    [GeneratedRegex(@"FLWSECK_TEST-(?i)[a-h0-9]{12}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex FlutterwaveEncryptionKey();

    [GeneratedRegex(@"FLWPUBK_TEST-(?i)[a-h0-9]{32}-X", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex FlutterwavePublicKey();

    [GeneratedRegex(@"FLWSECK_TEST-(?i)[a-h0-9]{32}-X", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex FlutterwaveSecretKey();

    [GeneratedRegex(@"\b(?<refine>(?:fo1_[\w-]{43}|fm1[ar]_[a-zA-Z0-9+\/]{100,}={0,3}|fm2_[a-zA-Z0-9+\/]{100,}={0,3}))(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex FlyioAccessToken();

    [GeneratedRegex(@"fio-u-(?i)[a-z0-9\-_=]{64}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex FrameioApiToken();

    [GeneratedRegex(@"(?:ghu|ghs)_[0-9a-zA-Z]{36}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GithubAppToken();

    [GeneratedRegex(@"github_pat_\w{82}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GithubFineGrainedPat();

    [GeneratedRegex(@"gho_[0-9a-zA-Z]{36}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GithubOauth();

    [GeneratedRegex(@"ghp_[0-9a-zA-Z]{36}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GithubPat();

    [GeneratedRegex(@"ghr_[0-9a-zA-Z]{36}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GithubRefreshToken();

    [GeneratedRegex(@"glcbt-[0-9a-zA-Z]{1,5}_[0-9a-zA-Z_-]{20}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GitlabCicdJobToken();

    [GeneratedRegex(@"gldt-[0-9a-zA-Z_\-]{20}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GitlabDeployToken();

    [GeneratedRegex(@"glffct-[0-9a-zA-Z_\-]{20}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GitlabFeatureFlagClientToken();

    [GeneratedRegex(@"glft-[0-9a-zA-Z_\-]{20}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GitlabFeedToken();

    [GeneratedRegex(@"glimt-[0-9a-zA-Z_\-]{25}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GitlabIncomingMailToken();

    [GeneratedRegex(@"glagent-[0-9a-zA-Z_\-]{50}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GitlabKubernetesAgentToken();

    [GeneratedRegex(@"gloas-[0-9a-zA-Z_\-]{64}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GitlabOauthAppSecret();

    [GeneratedRegex(@"glpat-[\w-]{20}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GitlabPat();

    [GeneratedRegex(@"\bglpat-[0-9a-zA-Z_-]{27,300}\.[0-9a-z]{2}[0-9a-z]{7}\b", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GitlabPatRoutable();

    [GeneratedRegex(@"glptt-[0-9a-f]{40}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GitlabPtt();

    [GeneratedRegex(@"GR1348941[\w-]{20}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GitlabRrt();

    [GeneratedRegex(@"glrt-[0-9a-zA-Z_\-]{20}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GitlabRunnerAuthenticationToken();

    [GeneratedRegex(@"\bglrt-t\d_[0-9a-zA-Z_\-]{27,300}\.[0-9a-z]{2}[0-9a-z]{7}\b", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GitlabRunnerAuthenticationTokenRoutable();

    [GeneratedRegex(@"glsoat-[0-9a-zA-Z_\-]{20}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GitlabScimToken();

    [GeneratedRegex(@"_gitlab_session=[0-9a-z]{32}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GitlabSessionCookie();

    [GeneratedRegex(@"(?i)\b(?<refine>eyJrIjoi[A-Za-z0-9]{70,400}={0,3})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GrafanaApiKey();

    [GeneratedRegex(@"(?i)\b(?<refine>glc_[A-Za-z0-9+/]{32,400}={0,3})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GrafanaCloudApiToken();

    [GeneratedRegex(@"(?i)\b(?<refine>glsa_[A-Za-z0-9]{32}_[A-Fa-f0-9]{8})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex GrafanaServiceAccountToken();

    [GeneratedRegex(@"(?:pat|sat)\.[a-zA-Z0-9_-]{22}\.[a-zA-Z0-9]{24}\.[a-zA-Z0-9]{20}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex HarnessApiKey();

    [GeneratedRegex(@"(?i)[a-z0-9]{14}\.(?-i:atlasv1)\.[a-z0-9\-_=]{60,70}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex HashicorpTfApiToken();

    [GeneratedRegex(@"\b(?<refine>(HRKU-AA[0-9a-zA-Z_-]{58}))(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex HerokuApiKeyV2();

    [GeneratedRegex(@"\b(?<refine>hf_(?i:[a-z]{34}))(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex HuggingfaceAccessToken();

    [GeneratedRegex(@"\b(?<refine>api_org_(?i:[a-z]{34}))(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex HuggingfaceOrganizationApiToken();

    [GeneratedRegex(@"\b(?<refine>ico-[a-zA-Z0-9]{32})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex InfracostApiToken();

    [GeneratedRegex(@"\b(?<refine>s-s4t2(?:ud|af)-(?i)[abcdef0123456789]{64})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex Intra42ClientSecret();

    [GeneratedRegex(@"lin_api_(?i)[a-z0-9]{40}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex LinearApiKey();

    [GeneratedRegex(@"\b(?<refine>[A-Za-z0-9]{6}_[A-Za-z0-9]{29}_mmk)(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex MaxmindLicenseKey();

    [GeneratedRegex(@"\b(?<refine>ntn_[0-9]{11}[A-Za-z0-9]{32}[A-Za-z0-9]{3})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex NotionApiToken();

    [GeneratedRegex(@"(?i)\b(?<refine>npm_[a-z0-9]{36})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex NpmAccessToken();

    [GeneratedRegex(@"\b(?<refine>API-[A-Z0-9]{26})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex OctopusDeployApiKey();

    [GeneratedRegex(@"\b(?<refine>sk-(?:proj|svcacct|admin)-(?:[A-Za-z0-9_-]{74}|[A-Za-z0-9_-]{58})T3BlbkFJ(?:[A-Za-z0-9_-]{74}|[A-Za-z0-9_-]{58})\b|sk-[a-zA-Z0-9]{20}T3BlbkFJ[a-zA-Z0-9]{20})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex OpenaiApiKey();

    [GeneratedRegex(@"\b(?<refine>sha256~[\w-]{43})(?:[^\w-]|\z)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex OpenshiftUserToken();

    [GeneratedRegex(@"\b(?<refine>pplx-[a-zA-Z0-9]{48})(?:[\x60'""\s;]|\\[nr]|$|\b)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex PerplexityApiKey();

    [GeneratedRegex(@"\b(?<refine>pscale_tkn_(?i)[\w=\.-]{32,64})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex PlanetscaleApiToken();

    [GeneratedRegex(@"\b(?<refine>pscale_oauth_[\w=\.-]{32,64})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex PlanetscaleOauthToken();

    [GeneratedRegex(@"(?i)\b(?<refine>pscale_pw_(?i)[\w=\.-]{32,64})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex PlanetscalePassword();

    [GeneratedRegex(@"\b(?<refine>PMAK-(?i)[a-f0-9]{24}\-[a-f0-9]{34})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex PostmanApiToken();

    [GeneratedRegex(@"\b(?<refine>pnu_[a-zA-Z0-9]{36})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex PrefectApiToken();

    [GeneratedRegex(@"\b(?<refine>pul-[a-f0-9]{40})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex PulumiApiToken();

    [GeneratedRegex(@"pypi-AgEIcHlwaS5vcmc[\w-]{50,1000}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex PypiUploadToken();

    [GeneratedRegex(@"\b(?<refine>rdme_[a-z0-9]{70})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex ReadmeApiToken();

    [GeneratedRegex(@"\b(?<refine>rubygems_[a-f0-9]{48})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex RubygemsApiToken();

    [GeneratedRegex(@"\b(?<refine>tk-us-[\w-]{48})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex ScalingoApiToken();

    [GeneratedRegex(@"\b(?<refine>SG\.(?i)[a-z0-9=_\-\.]{66})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SendgridApiToken();

    [GeneratedRegex(@"\b(?<refine>xkeysib-[a-f0-9]{64}\-(?i)[a-z0-9]{16})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SendinblueApiToken();

    [GeneratedRegex(@"\bsntrys_eyJpYXQiO[a-zA-Z0-9+/]{10,200}(?:LCJyZWdpb25fdXJs|InJlZ2lvbl91cmwi|cmVnaW9uX3VybCI6)[a-zA-Z0-9+/]{10,200}={0,2}_[a-zA-Z0-9+/]{43}(?:[^a-zA-Z0-9+/]|\z)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SentryOrgToken();

    [GeneratedRegex(@"\b(?<refine>sntryu_[a-f0-9]{64})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SentryUserToken();

    [GeneratedRegex(@"\b(?<refine>sm_aat_[a-zA-Z0-9]{16})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SettlemintApplicationAccessToken();

    [GeneratedRegex(@"\b(?<refine>sm_pat_[a-zA-Z0-9]{16})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SettlemintPersonalAccessToken();

    [GeneratedRegex(@"\b(?<refine>sm_sat_[a-zA-Z0-9]{16})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SettlemintServiceAccessToken();

    [GeneratedRegex(@"\b(?<refine>shippo_(?:live|test)_[a-fA-F0-9]{40})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex ShippoApiToken();

    [GeneratedRegex(@"shpat_[a-fA-F0-9]{32}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex ShopifyAccessToken();

    [GeneratedRegex(@"shpca_[a-fA-F0-9]{32}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex ShopifyCustomAccessToken();

    [GeneratedRegex(@"shppa_[a-fA-F0-9]{32}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex ShopifyPrivateAppAccessToken();

    [GeneratedRegex(@"shpss_[a-fA-F0-9]{32}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex ShopifySharedSecret();

    [GeneratedRegex(@"(?i)xapp-\d-[A-Z0-9]+-\d+-[a-z0-9]+", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SlackAppToken();

    [GeneratedRegex(@"xoxb-[0-9]{10,13}-[0-9]{10,13}[a-zA-Z0-9-]*", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SlackBotToken();

    [GeneratedRegex(@"(?i)xoxe.xox[bp]-\d-[A-Z0-9]{163,166}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SlackConfigAccessToken();

    [GeneratedRegex(@"(?i)xoxe-\d-[A-Z0-9]{146}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SlackConfigRefreshToken();

    [GeneratedRegex(@"xoxb-[0-9]{8,14}-[a-zA-Z0-9]{18,26}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SlackLegacyBotToken();

    [GeneratedRegex(@"xox[os]-\d+-\d+-\d+-[a-fA-F\d]+", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SlackLegacyToken();

    [GeneratedRegex(@"xox[ar]-(?:\d-)?[0-9a-zA-Z]{8,48}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SlackLegacyWorkspaceToken();

    [GeneratedRegex(@"xox[pe](?:-[0-9]{10,13}){3}-[a-zA-Z0-9-]{28,34}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SlackUserToken();

    [GeneratedRegex(@"\b(?<refine>(?:EAAA|sq0atp-)[\w-]{22,60})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex SquareAccessToken();

    [GeneratedRegex(@"\b(?<refine>(?:sk|rk)_(?:test|live|prod)_[a-zA-Z0-9]{10,99})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex StripeAccessToken();

    [GeneratedRegex(@"SK[0-9a-fA-F]{32}", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex TwilioApiKey();

    [GeneratedRegex(@"\b(?<refine>hvb\.[\w-]{138,300})(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex VaultBatchToken();

    [GeneratedRegex(@"\b(?<refine>(?:hvs\.[\w-]{90,120}|s\.(?i:[a-z0-9]{24})))(?:[\x60'""\s;]|\\[nr]|$)", SecretRegexEngine.MatchOptions, SecretRegexEngine.MatchTimeoutMilliseconds)]
    private static partial Regex VaultServiceToken();
}
