namespace Audorix.KnockTwice.Contracts.V1.Notifications;

/// <param name="Enabled">Whether the installation sends Web Push.</param>
/// <param name="PublicKey">The VAPID public key (base64url) to pass to <c>PushManager.subscribe</c> as <c>applicationServerKey</c>; null when disabled.</param>
public sealed record PushConfigurationResponse(bool Enabled, string? PublicKey);

/// <summary>A browser's push subscription, as <c>PushSubscription.toJSON()</c> returns it.</summary>
public sealed record PushSubscriptionRequest(string? Endpoint, PushSubscriptionKeys? Keys);

public sealed record PushSubscriptionKeys(string? P256dh, string? Auth);

public sealed record PushUnsubscribeRequest(string? Endpoint);
