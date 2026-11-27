# Phase 45: Azure Blob Storage foundation

## Scope

Azure Blob Storage joins Local and S3-compatible storage for managed message attachments. The
provider uses a private container and keeps all downloads behind the existing conversation
authorization checks. The admin provider page accepts an Azure Storage connection string, container,
and optional object-key prefix. The connection test writes, reads, and deletes a probe without
saving configuration.

Each new attachment stores the provider name with its database record. When an administrator changes
the default provider, the API pins legacy attachment rows with no provider value to the currently
active provider before saving the new settings. Existing files are not copied, so a provider change
is not a migration.

## Configuration

```json
{
  "Storage": {
    "Provider": "AzureBlob",
    "AzureBlob": {
      "ConnectionString": "<server-side secret>",
      "Container": "chatx-files",
      "Prefix": "attachments"
    }
  }
}
```

Keep the container private and do not expose the connection string to clients. Environment or
command-line values remain locked in the admin UI, matching the existing provider settings policy.
Storage provider changes saved through admin require an API restart.

## Supported operations

The Local, S3-compatible, and Azure Blob implementations support upload, download, and deletion.
The admin test performs a temporary upload/read/delete probe. The attachment record and its message
identity remain unchanged when the default provider is switched.

## Database changes

SQLite and PostgreSQL add nullable `attachments.storage_provider` (`TEXT` / `varchar(32)`). Null is
reserved for rows created before this release; the admin provider-change path pins these rows before
the active default changes.

## Out of scope

This phase does not copy stored bytes between providers, pause uploads, reconcile changes, verify a
destination corpus, switch attachment references, or retain rollback copies. Those belong to the
bidirectional migration and verified cutover phases.
