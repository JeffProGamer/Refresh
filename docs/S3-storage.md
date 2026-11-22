# Optional S3 asset storage for Render
This fork now supports an opt-in S3 primary asset store. It is disabled by
 default; existing installations keep local filesystem storage. These explicit
S3 environment bindings are new functionality, not a general .NET config binder.

## Render environment variables
Keep your working `POSTGRES_CONNECTION_STRING` unchanged. Add:

| Key | Value |
| --- | --- |
| `S3__Enabled` | `true` |
| `S3__Endpoint` | `<HTTPS S3 service endpoint from your storage provider>` |
| `S3__BucketName` | `<existing bucket name>` |
| `S3__AccessKeyId` | `<storage access key ID>` |
| `S3__SecretAccessKey` | `<storage secret access key>` |
| `S3__Region` | `<signing region specified by provider>` |
| `S3__ForcePathStyle` | `true` (or `false` if required by provider) |

Use credentials for the object-storage service, not the PostgreSQL role password.
Do not assume a PostgreSQL hostname is an S3 endpoint. Obtain the endpoint,
region, bucket and credentials from the actual object-storage provider.
Endpoint must be HTTPS and must not include credentials, query parameters, or
an appended bucket name unless explicitly required by that service.
The bucket must already exist. The identity needs object read/write/delete and
bucket listing permissions. Secrets belong in Render environment settings,
never committed files. Invalid enabled settings fail startup without printing
credential values. Provider errors are not silently redirected to local disk.

## Optional remote archive asset source
For a remote HTTP archive that serves assets by SHA-1, add:

| Key | Value |
| --- | --- |
| `RemoteArchive__Enabled` | `true` |
| `RemoteArchive__BaseUrl` | `https://lbp.lbpbonsai.com/api/v3/assets/` or `<HTTPS raw Dry mirror base URL>` |

The provider must serve `HEAD` and `GET` requests for the lowercase
40-character SHA-1; returned bytes are checked against the requested SHA-1.
When the base URL ends in `/assets/`, the hash is appended without an extension
(as with Bonsai APIv3, for example
`https://lbp.lbpbonsai.com/api/v3/assets/`). Other base URLs use the
`<hash>.bin` format used by raw Dry mirrors. Downloads are read through and
cached to the primary asset store on first access (S3 when enabled, otherwise
local disk).
Keep the existing `dry.json` configuration for a local Dry archive; the remote
provider is independent of it.

To also search a compatible APIv3 archive catalog, set:

| Key | Value |
| --- | --- |
| `RemoteArchive__SearchApiUrl` | `https://lbp.lbpbonsai.com/api/v3/` |

This setting is optional and only used when `RemoteArchive__Enabled=true`. It
expects `GET <SearchApiUrl>/levels/search` to return the APIv3 search response
shape, including each level's `rootLevelHash`, `iconHash`, `gameVersion`,
`levelType`, and metadata. Search results are validated and imported into
PostgreSQL on demand. The root and icon hashes then resolve through the remote
asset provider; the remote API response does not provide a dependency list.
Configure `RemoteArchive__BaseUrl` separately to the HTTPS endpoint that
actually serves the requested hash in its supported URL format.

Primary uploads, derived images, and archive cache writes all use S3 when
enabled.

## Render server settings
The deployment JSON already enables forwarded client IP handling
(`UseForwardedIp: true`), binds to `0.0.0.0:10000`, and sets the public host.
`Server__PublicUrl` overrides both Bunkum's public URL and Refresh's generated
web URLs. Set these optional Refresh values in Render to customize identity,
Pod news, and the first-login notification:

| Key | Value |
| --- | --- |
| `Server__Name` | `LBP Reborn` |
| `Server__Description` | `LBP Reborn community server` |
| `Server__PublicUrl` | `https://lbpreborn.onrender.com` |
| `Server__GameConfigStorageUrl` | `https://lbpreborn.onrender.com/lbp` |
| `Server__LicenseText` | `Welcome to LBP Reborn!` |
| `Server__PodNewsTitle` | `LBP Reborn` |
| `Server__PodNewsText` | `<announcement shown in the in-game Pod news feed>` |
| `Server__WelcomeNotificationTitle` | `Welcome to LBP Reborn` |
| `Server__WelcomeNotificationText` | `<notification shown to newly registered players>` |

`Server__GameConfigStorageUrl` defaults to
`<Server__PublicUrl>/lbp` when omitted. `Server__LicenseText` is the welcome
license/EULA text, not the Pod news or new-user notification.

### Copy-paste Render variable checklist
Set the following keys, replacing angle-bracketed values with the credentials
or endpoint values from your providers. Keep secrets in Render's environment
settings; do not put their actual values in deployment files.

```text
POSTGRES_CONNECTION_STRING=<Neon PostgreSQL connection string>
S3__Enabled=true
S3__Endpoint=<HTTPS S3-compatible endpoint>
S3__BucketName=lbpreborn-assets
S3__AccessKeyId=<S3 access key ID>
S3__SecretAccessKey=<S3 secret access key>
S3__Region=<S3 signing region>
S3__ForcePathStyle=true
RemoteArchive__Enabled=true
RemoteArchive__BaseUrl=https://lbp.lbpbonsai.com/api/v3/assets/
RemoteArchive__SearchApiUrl=https://lbp.lbpbonsai.com/api/v3/
Server__Name=LBP Reborn
Server__Description=LBP Reborn community server
Server__PublicUrl=https://lbpreborn.onrender.com
Server__GameConfigStorageUrl=https://lbpreborn.onrender.com/lbp
Server__LicenseText=Welcome to LBP Reborn!
Server__PodNewsTitle=LBP Reborn
Server__PodNewsText=<your Pod news announcement>
Server__WelcomeNotificationTitle=Welcome to LBP Reborn
Server__WelcomeNotificationText=<your new-player welcome notification>
```

`S3__*` values and remote archive settings can be omitted if those optional
services are not being used. The S3 bucket must exist before deployment. The
archive asset base URL is provider-specific; the search API URL is not a
substitute for the binary asset endpoint. For a raw Dry mirror, replace the
Bonsai `RemoteArchive__BaseUrl` with its base URL (the provider must serve
`<hash>.bin`). The Docker image currently sets
      `BUNKUM_DATA_FOLDER=/refresh/data` and `PORT=10000`; the listener port is
actually read from the outer deployment's `deploy/render/bunkum.json`, so keep
`ListenPort` at
`10000` unless both the Render port and deployment configuration are changed.

## Features and deployment limitations
Publishing, photos, icons, hearts, reviews, comments, playlists, pins,
trophies, scoreboards, and matchmaking are implemented as server endpoints;
they are not individually switched on by environment variables. Login and
registration defaults are in the outer deployment's
`deploy/render/refreshGameServer.json`, not a
general `__` environment-variable binder. The configured PSN flow still
verifies Sony-issued supported tickets. No `Auth__EnablePs4Support` or
`Auth__AllowUnverifiedPs4Tickets` options are provided: trusting an
unverified retail PS4 ticket would allow identity spoofing, and the server
does not implement a verified PS4 ticket format.

Bunkum trusts `X-Forwarded-For` when `UseForwardedIp` is enabled. It does not
currently interpret `X-Forwarded-Proto`; set the public URL explicitly as
`https://...` so generated links use HTTPS. There is no configurable
per-request body-size or upload timeout setting in the HTTP listener. The
configured user file-size quota is a cumulative per-user quota (100 MiB by
default), not a request-size limit. Render's ingress and memory constraints
still apply to large uploads.

The Refresh repository does not contain a `refresh-web` source tree or a verified
prebuilt frontend bundle. Bunkum can serve static files from its `web`
subdirectory under its data directory when files are supplied, but the outer
deployment Dockerfile does not include a web UI. No unverified external
frontend download/build step has been
added; the API can be available while the root browser page has no dashboard.

## Safe rollout
1. Back up any existing local assets before redeploying; this change does not
   automatically migrate them. Database records alone do not contain asset bytes.
2. Commit and push the changes in the **Refresh** repository using GitHub Desktop.
3. Update the server revision pinned in the outer deployment Dockerfile to the
   resulting Refresh commit; commit and push that deployment change separately.
4. Add the variables above in Render and redeploy. Look for
   `Primary asset storage: S3.` in the startup logs.
5. Upload a disposable asset through a supported client/API. Confirm an object
   appears in your bucket, download it and compare bytes, then redeploy and
   confirm it remains downloadable. Also test image conversion and deletion.

Do not treat startup alone as proof of bucket access: the client is created at
startup but S3 network requests happen when storage is used. Unit tests use a
fake SDK client and do not prove real endpoint compatibility. Keep the current
live revision until your new configuration is ready.

## Implementation notes
The adapter is in Refresh.Core, not a modified Bunkum submodule. It supports
paginated listing, 204 delete responses, seekable reads, and upload-on-dispose
streams for image encoding. Read and image-output buffers live in memory;
large assets and concurrent downloads increase memory usage. Review your Render
memory budget before opening uploads to the public.
