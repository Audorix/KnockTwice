using System.Security.Cryptography;
using Audorix.KnockTwice.Application.Common;

namespace Audorix.KnockTwice.Application.Attachments;

/// <summary>Non-migrating preflight. It checks sources and destination access without changing attachment metadata or provider settings.</summary>
public sealed class StorageMigrationService(IAttachmentStore attachments, IAttachmentStorageProviderRegistry providers)
{
    private const int PageSize = 250;
    private const int ReportFileLimit = 2000;
    private const long MaximumFileBytes = 5L * 1024 * 1024 * 1024;

    public async Task<StorageMigrationDryRunResult> DryRunAsync(string? sourceProvider, string? destinationProvider, CancellationToken cancellationToken)
    {
        var sourceName = NormalizeProvider(sourceProvider);
        var destinationName = NormalizeProvider(destinationProvider);
        if (sourceName == destinationName) throw new RequestValidationException("destinationProvider", "providers_must_differ");

        var source = providers.Resolve(sourceName);
        var destination = providers.Resolve(destinationName);
        var files = new List<StorageMigrationFile>();
        var blockers = new List<string>();
        var warnings = new List<string> { "Provider capacity or quota is not exposed by the storage interface; destination capacity was not measured." };
        var totalFiles = 0L;
        var totalBytes = 0L;
        var skip = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await attachments.ListStorageMigrationCandidatesAsync(sourceName, providers.DefaultProvider, skip, PageSize, cancellationToken).ConfigureAwait(false);
            if (page.Count == 0) break;
            foreach (var attachment in page)
            {
                totalFiles++;
                totalBytes = checked(totalBytes + attachment.Size);
                var itemBlockers = new List<string>();
                if (attachment.Size <= 0 || attachment.Size > MaximumFileBytes) itemBlockers.Add("file_size_unsupported");
                try
                {
                    await using var content = await source.OpenReadAsync(attachment.StorageKey, cancellationToken).ConfigureAwait(false);
                    if (content is null) itemBlockers.Add("source_file_missing");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    itemBlockers.Add("source_unavailable");
                }
                if (itemBlockers.Count > 0 && blockers.Count < ReportFileLimit)
                    blockers.AddRange(itemBlockers.Select(code => $"{attachment.Id}:{code}"));
                if (files.Count < ReportFileLimit)
                    files.Add(new StorageMigrationFile(attachment.Id, attachment.FileName, attachment.Size, attachment.Status.ToString(), itemBlockers));
            }
            skip += page.Count;
            if (page.Count < PageSize) break;
        }

        var destinationReachable = false;
        // LocalAttachmentStorage accepts lower-case alphanumeric segments only.
        var probeKey = $"migrationpreflight/{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))}";
        byte[] probe = [0x6b, 0x6e, 0x6f, 0x63, 0x6b, 0x2d, 0x74, 0x77, 0x69, 0x63, 0x65];
        try
        {
            await destination.PutAsync(probeKey, new MemoryStream(probe, writable: false), probe.Length, "application/octet-stream", cancellationToken).ConfigureAwait(false);
            await using var read = await destination.OpenReadAsync(probeKey, cancellationToken).ConfigureAwait(false);
            if (read is null) throw new InvalidOperationException("Destination probe could not be read back.");
            var copied = new byte[probe.Length];
            var readBytes = await read.ReadAtLeastAsync(copied, copied.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
            destinationReachable = readBytes == probe.Length && probe.AsSpan().SequenceEqual(copied);
            if (!destinationReachable) blockers.Add("destination_probe_mismatch");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            blockers.Add("destination_unavailable");
        }
        finally
        {
            try { await destination.DeleteAsync(probeKey, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException) { blockers.Add("destination_probe_cleanup_failed"); }
        }

        if (totalFiles > files.Count) warnings.Add($"Only the first {files.Count} file records are included in this report; {totalFiles - files.Count} additional files were checked for source readability.");
        return new StorageMigrationDryRunResult(sourceName, destinationName, totalFiles, totalBytes, files, totalFiles > files.Count,
            destinationReachable, "unknown", blockers.Distinct(StringComparer.Ordinal).ToList(), warnings);
    }

    private static string NormalizeProvider(string? provider)
    {
        var normalized = provider?.Trim();
        if (string.Equals(normalized, "Local", StringComparison.OrdinalIgnoreCase)) return "Local";
        if (string.Equals(normalized, "S3", StringComparison.OrdinalIgnoreCase)) return "S3";
        if (string.Equals(normalized, "AzureBlob", StringComparison.OrdinalIgnoreCase)) return "AzureBlob";
        throw new RequestValidationException("provider", "provider_invalid");
    }
}
