namespace Audorix.KnockTwice.Contracts.V1.Admin;

/// <summary>Installation metadata; requires the <c>installation.read</c> permission.</summary>
public sealed record InstallationResponse(Guid InstallationId, string DefaultLanguage, DateTimeOffset InitializedAtUtc);
