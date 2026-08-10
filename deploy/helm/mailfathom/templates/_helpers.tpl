{{- /*
Copyright © 2026 Krzysztof Kasprowicz
Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
Project repository: https://github.com/Krzysztof318/MailFathom
*/ -}}
{{/*
Shared naming, labels, and the two derivations that would otherwise be written differently in each template: the image
reference and the connection string. Everything here is namespaced under `mailfathom.` so a subchart added later cannot
collide with it.
*/}}

{{- define "mailfathom.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "mailfathom.fullname" -}}
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

{{- define "mailfathom.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{/*
The version a label may carry. On the nightly channel it is the nightly identifier rather than `appVersion`, because
`appVersion` describes a release and a nightly is not one; labelling it otherwise would make a nightly indistinguishable
from a release in every query that reads this label.

On the release channel it is `appVersion`, which the release run supplies when it packages the chart. Rendering the
chart directory straight out of the repository leaves it empty, and the label is then written empty rather than filled
with a guess: an unpackaged chart genuinely deploys no stated application version, and a fallback here would be the
second written version number Chart.yaml exists to avoid.
*/}}
{{- define "mailfathom.versionLabel" -}}
{{- if eq .Values.image.channel "nightly" -}}
{{- printf "nightly-%s" (default "unknown" .Values.image.tag) | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- .Chart.AppVersion | trunc 63 | trimSuffix "-" -}}
{{- end -}}
{{- end -}}

{{- define "mailfathom.labels" -}}
helm.sh/chart: {{ include "mailfathom.chart" . }}
{{ include "mailfathom.selectorLabels" . }}
app.kubernetes.io/version: {{ include "mailfathom.versionLabel" . | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
app.kubernetes.io/part-of: mailfathom
{{- if eq .Values.image.channel "nightly" }}
{{/* Readable from any `kubectl get -l`, so an unsupported deployment stays identifiable long after whoever installed
     it has forgotten which channel they chose. The value is the one the image carries as `io.mailfathom.release-channel`;
     the key is spelled with a slash because that is what Kubernetes requires of a label prefix and a dot is what OCI
     expects of an image label, so these are one name written the way each ecosystem reads it. */}}
io.mailfathom/release-channel: nightly
{{- end }}
{{- end -}}

{{- define "mailfathom.selectorLabels" -}}
app.kubernetes.io/name: {{ include "mailfathom.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}

{{- define "mailfathom.serviceAccountName" -}}
{{- if .Values.serviceAccount.create -}}
{{- default (include "mailfathom.fullname" .) .Values.serviceAccount.name -}}
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
{{- define "mailfathom.validate" -}}
{{- if eq .Values.image.channel "nightly" -}}
  {{- if ne .Values.image.nightlyAcknowledgement "i-understand-this-is-unsupported" -}}
    {{- fail "image.channel is 'nightly'. A nightly build is not a release: it is whatever main was the night it was built, its schema and configuration may move without notice, no support or upgrade path applies to it, and it is deleted once thirty newer nightlies exist. Set image.nightlyAcknowledgement=i-understand-this-is-unsupported to deploy one anyway." -}}
  {{- end -}}
  {{- if and .Values.image.tag (not (contains "-nightly." .Values.image.tag)) -}}
    {{- fail (printf "image.channel is 'nightly' but image.tag is %q, which carries no '-nightly.' identifier. A nightly is a nightly because of what it calls itself, not because of the registry it came from: name the immutable '<x.y.z>-nightly.<n>-<revision>' tag of one night's build rather than a release tag or the moving 'nightly' tag." .Values.image.tag) -}}
  {{- end -}}
{{- else -}}
  {{- if .Values.image.nightlyAcknowledgement -}}
    {{- fail "image.nightlyAcknowledgement is set while image.channel is 'release'. Remove it, or select the nightly channel deliberately." -}}
  {{- end -}}
  {{- if and .Values.image.tag (contains "-nightly." .Values.image.tag) -}}
    {{- fail (printf "image.channel is 'release' but image.tag is %q, which is a nightly identifier. A nightly carries no release promise; select the nightly channel deliberately, with the acknowledgement it requires." .Values.image.tag) -}}
  {{- end -}}
{{- end -}}

{{- if and .Values.image.tag .Values.image.digest -}}
  {{- fail "image.tag and image.digest are both set. Supply exactly one, so what a rollback goes back to is unambiguous." -}}
{{- end -}}
{{- if not (or .Values.image.tag .Values.image.digest) -}}
  {{- fail "Neither image.tag nor image.digest is set. The chart defaults to neither, so that no cluster runs a version nobody named; name the immutable reference your deployment uses." -}}
{{- end -}}
{{- if not .Values.image.repository -}}
  {{- fail "image.repository is not set. There is no default: a chart that guessed one would deploy an image nobody named." -}}
{{- end -}}

{{/*
Deploying a version other than the one this chart documents is allowed and sometimes necessary, but it is stated rather
than assumed. Two cases carry nothing to compare and are therefore not refusals: a deployment naming the image by
digest, which publishes no version, and an unpackaged chart, which declares no `appVersion` because only the release
run supplies one — see Chart.yaml.
*/}}
{{- if and
      (eq .Values.image.channel "release")
      .Values.image.tag
      .Chart.AppVersion
      (ne .Values.image.tag .Chart.AppVersion)
      (not .Values.image.allowVersionMismatch) -}}
  {{- fail (printf "image.tag is %q but this chart documents application version %q. Deploying a different version than the chart describes is allowed, but it has to be said: set image.allowVersionMismatch=true." .Values.image.tag .Chart.AppVersion) -}}
{{- end -}}

{{- if .Values.database.deploy.enabled -}}
  {{- if .Values.database.host -}}
    {{- fail (printf "database.host is %q while database.deploy.enabled is true. The chart is deploying the server and derives its address from the release name, so a second address here would name somewhere the release did not install. Clear it, or turn database.deploy.enabled off to use a server you already operate." .Values.database.host) -}}
  {{- end -}}
  {{- if eq .Values.database.deploy.superuserPasswordSecretKey .Values.database.passwordSecretKey -}}
    {{- fail "database.deploy.superuserPasswordSecretKey and database.passwordSecretKey name one key. The superuser initializes the database and MailFathom connects as an unprivileged role that owns it; one password for both would make MailFathom's own credential a superuser's." -}}
  {{- end -}}
{{- else -}}
  {{- if not .Values.database.host -}}
    {{- fail "database.host is not set and database.deploy.enabled is false. Name the PostgreSQL server you operate — it needs the vector extension — or turn database.deploy.enabled on and let the chart run one." -}}
  {{- end -}}
{{- end -}}
{{- if not .Values.secrets.existingSecret -}}
  {{- fail "secrets.existingSecret is not set. The chart creates no Secret and templates no credential; create one first and name it here." -}}
{{- end -}}
{{- end -}}

{{/*
The application image reference. A digest wins where both could apply, and the registry is omitted from the rendered
string when empty so Docker Hub's implicit default is not spelled out inconsistently across templates.
*/}}
{{- define "mailfathom.image" -}}
{{- $registry := .Values.image.registry -}}
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
The database objects the chart deploys are named after the release, like everything else, with `-postgres` appended so
one release's server is distinguishable from its application in any listing.
*/}}
{{- define "mailfathom.postgresFullname" -}}
{{- printf "%s-postgres" (include "mailfathom.fullname" .) | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{/*
Where MailFathom connects. A deployed server is reached by the name of its own Service, which is what keeps the address
one derivation rather than a value an operator has to keep in step with the release name; an external one is whatever
was named.
*/}}
{{- define "mailfathom.databaseHost" -}}
{{- if .Values.database.deploy.enabled -}}
{{- include "mailfathom.postgresFullname" . -}}
{{- else -}}
{{- .Values.database.host -}}
{{- end -}}
{{- end -}}

{{/*
The database pod's identity, and deliberately not the application's with a component label added. `app.kubernetes.io/name`
carries a name of its own because the application's Service and Deployment select on name and instance alone: a pod
answering to both of those would be routed HTTP requests by that Service and counted as a replica by that Deployment,
which selects the pods it owns the same way. A label a selector matches is not decoration, and the two workloads have
to be distinguishable by the labels the existing selectors already read — adding a component label to the application
instead would change an immutable `Deployment.spec.selector` and break every upgrade of an installed release.
*/}}
{{- define "mailfathom.postgresSelectorLabels" -}}
app.kubernetes.io/name: {{ printf "%s-postgres" (include "mailfathom.name" .) | trunc 63 | trimSuffix "-" }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}

{{/*
The version label is the database's own rather than MailFathom's, for the reason the label exists: it states what is
running in the object it is attached to, and a PostgreSQL pod labelled with the application's version would answer
every `kubectl get -l app.kubernetes.io/version` with something that is not in it.
*/}}
{{- define "mailfathom.postgresLabels" -}}
helm.sh/chart: {{ include "mailfathom.chart" . }}
{{ include "mailfathom.postgresSelectorLabels" . }}
app.kubernetes.io/version: {{ .Values.database.deploy.image.tag | trunc 63 | trimSuffix "-" | quote }}
app.kubernetes.io/component: database
app.kubernetes.io/managed-by: {{ .Release.Service }}
app.kubernetes.io/part-of: mailfathom
{{- end -}}

{{/*
The database image. It carries a registry by default, unlike the application image, because this reference is the
chart's own choice rather than a deployment's and is written the same way wherever it is read.
*/}}
{{- define "mailfathom.postgresImage" -}}
{{- $image := .Values.database.deploy.image -}}
{{- if $image.registry -}}
{{- printf "%s/%s:%s" $image.registry $image.repository $image.tag -}}
{{- else -}}
{{- printf "%s:%s" $image.repository $image.tag -}}
{{- end -}}
{{- end -}}

{{/*
The connection string, without the password. The password reaches MailFathom as a mounted file named by
`Persistence__Password__SecretReference`, so nothing here ever carries a credential — which is what makes the ConfigMap
and the rendered Deployment safe to review and to store.
*/}}
{{- define "mailfathom.connectionString" -}}
{{- $connection := printf "Host=%s;Port=%d;Database=%s;Username=%s" (include "mailfathom.databaseHost" .) (int .Values.database.port) .Values.database.name .Values.database.user -}}
{{- if .Values.database.extraConnectionParameters -}}
{{- $connection = printf "%s;%s" $connection (trimPrefix ";" .Values.database.extraConnectionParameters) -}}
{{- end -}}
{{- $connection -}}
{{- end -}}
