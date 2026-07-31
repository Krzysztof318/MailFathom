{{/*
Shared naming, labels, and the two derivations that would otherwise be written differently in each template: the image
reference and the connection string. Everything here is namespaced under `mailmcp.` so a subchart added later cannot
collide with it.
*/}}

{{- define "mailmcp.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "mailmcp.fullname" -}}
{{- if .Values.fullnameOverride -}}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- $name := default .Chart.Name .Values.nameOverride -}}
{{- if contains $name .Release.Name -}}
{{- .Release.Name | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" -}}
{{- end -}}
{{- end -}}
{{- end -}}

{{- define "mailmcp.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{/*
The version a label may carry. On the nightly channel it is the nightly identifier rather than `appVersion`, because
`appVersion` describes a release and a nightly is not one; labelling it otherwise would make a nightly indistinguishable
from a release in every query that reads this label.
*/}}
{{- define "mailmcp.versionLabel" -}}
{{- if eq .Values.image.channel "nightly" -}}
{{- printf "nightly-%s" (default "unknown" .Values.image.tag) | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- .Chart.AppVersion | trunc 63 | trimSuffix "-" -}}
{{- end -}}
{{- end -}}

{{- define "mailmcp.labels" -}}
helm.sh/chart: {{ include "mailmcp.chart" . }}
{{ include "mailmcp.selectorLabels" . }}
app.kubernetes.io/version: {{ include "mailmcp.versionLabel" . | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
app.kubernetes.io/part-of: mailmcp
{{- if eq .Values.image.channel "nightly" }}
{{/* Readable from any `kubectl get -l`, so an unsupported deployment stays identifiable long after whoever installed
     it has forgotten which channel they chose. */}}
io.mailmcp/release-channel: ghcr-nightly-unsupported
{{- end }}
{{- end -}}

{{- define "mailmcp.selectorLabels" -}}
app.kubernetes.io/name: {{ include "mailmcp.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}

{{- define "mailmcp.serviceAccountName" -}}
{{- if .Values.serviceAccount.create -}}
{{- default (include "mailmcp.fullname" .) .Values.serviceAccount.name -}}
{{- else -}}
{{- default "default" .Values.serviceAccount.name -}}
{{- end -}}
{{- end -}}

{{/*
Rejects a values document the JSON schema cannot express, and does it once so every template inherits the same answer.
The schema covers shape — a required field, a pattern, a type — while these are relationships between fields, which a
draft-07 schema can state only as unreadable `allOf`/`if` chains that report which subschema failed rather than what is
wrong.
*/}}
{{- define "mailmcp.validate" -}}
{{- if eq .Values.image.channel "nightly" -}}
  {{- if ne .Values.image.nightlyAcknowledgement "i-understand-this-is-unsupported" -}}
    {{- fail "image.channel is 'nightly'. A nightly build is not a release: it is whatever main happened to be when someone dispatched a build, its schema and configuration may move without notice, and no support or upgrade path applies to it. Set image.nightlyAcknowledgement=i-understand-this-is-unsupported to deploy one anyway." -}}
  {{- end -}}
  {{- if and .Values.image.registry (ne .Values.image.registry "ghcr.io") -}}
    {{- fail (printf "image.channel is 'nightly' but image.registry is %q. Nightly builds are published only to ghcr.io; naming another registry would present a nightly as a release from somewhere it was never published." .Values.image.registry) -}}
  {{- end -}}
{{- else -}}
  {{- if .Values.image.nightlyAcknowledgement -}}
    {{- fail "image.nightlyAcknowledgement is set while image.channel is 'release'. Remove it, or select the nightly channel deliberately." -}}
  {{- end -}}
{{- end -}}

{{- if and .Values.image.tag .Values.image.digest -}}
  {{- fail "image.tag and image.digest are both set. Supply exactly one, so what a rollback goes back to is unambiguous." -}}
{{- end -}}
{{- if not (or .Values.image.tag .Values.image.digest) -}}
  {{- fail "Neither image.tag nor image.digest is set. MailMcp publishes no release yet, so the chart cannot default to one; name the immutable reference your deployment uses." -}}
{{- end -}}
{{- if not .Values.image.repository -}}
  {{- fail "image.repository is not set. There is no default: a chart that guessed one would deploy an image nobody named." -}}
{{- end -}}

{{/* Inactive while appVersion is the unreleased placeholder, and binds on its own once a real version is stamped. */}}
{{- if and
      (eq .Values.image.channel "release")
      .Values.image.tag
      (ne .Chart.AppVersion "0.0.0-unreleased")
      (ne .Values.image.tag .Chart.AppVersion)
      (not .Values.image.allowVersionMismatch) -}}
  {{- fail (printf "image.tag is %q but this chart documents application version %q. Deploying a different version than the chart describes is allowed, but it has to be said: set image.allowVersionMismatch=true." .Values.image.tag .Chart.AppVersion) -}}
{{- end -}}

{{- if not .Values.database.host -}}
  {{- fail "database.host is not set. The chart installs no database: MailMcp needs PostgreSQL with the vector extension, and the store holding every synchronized message belongs to whoever operates it." -}}
{{- end -}}
{{- if not .Values.secrets.existingSecret -}}
  {{- fail "secrets.existingSecret is not set. The chart creates no Secret and templates no credential; create one first and name it here." -}}
{{- end -}}
{{- end -}}

{{/*
The application image reference. A digest wins where both could apply, and the registry is omitted from the rendered
string when empty so Docker Hub's implicit default is not spelled out inconsistently across templates.
*/}}
{{- define "mailmcp.image" -}}
{{- $registry := .Values.image.registry -}}
{{- if eq .Values.image.channel "nightly" -}}
{{- $registry = "ghcr.io" -}}
{{- end -}}
{{- $repository := .Values.image.repository -}}
{{- if $registry -}}
{{- $repository = printf "%s/%s" $registry $repository -}}
{{- end -}}
{{- if .Values.image.digest -}}
{{- printf "%s@%s" $repository .Values.image.digest -}}
{{- else -}}
{{- printf "%s:%s" $repository .Values.image.tag -}}
{{- end -}}
{{- end -}}

{{/*
The connection string, without the password. The password reaches MailMcp as a mounted file named by
`Persistence__Password__SecretReference`, so nothing here ever carries a credential — which is what makes the ConfigMap
and the rendered Deployment safe to review and to store.
*/}}
{{- define "mailmcp.connectionString" -}}
{{- $connection := printf "Host=%s;Port=%d;Database=%s;Username=%s" .Values.database.host (int .Values.database.port) .Values.database.name .Values.database.user -}}
{{- if .Values.database.extraConnectionParameters -}}
{{- $connection = printf "%s;%s" $connection (trimPrefix ";" .Values.database.extraConnectionParameters) -}}
{{- end -}}
{{- $connection -}}
{{- end -}}
