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
| `RemoteArchive__BaseUrl` | `<HTTPS archive endpoint base URL ending in />` |

The provider must serve `HEAD` and `GET` requests at
`<BaseUrl>/<lowercase-40-character-sha1>.bin`; returned bytes are checked
against the requested SHA-1. Downloads are read through and cached to the
primary asset store on first access (S3 when enabled, otherwise local disk).
Keep the existing `dry.json` configuration for a local Dry archive; the remote
provider is independent of it.

This is hash-based asset retrieval only. It does not import an external level
catalog or make levels searchable by title, nor does it implement retail PS4
authentication. A specific catalog API and compatible metadata/import format
are required for archive-wide title search.

Primary uploads, derived images, and archive cache writes all use S3 when
enabled.

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
