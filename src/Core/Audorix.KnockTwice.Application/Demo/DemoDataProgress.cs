namespace Audorix.KnockTwice.Application.Demo;

/// <summary>The parts of the demo data, in loading order (progress per part, owner request 2026-11-25).</summary>
public static class DemoDataSteps
{
    public const string People = "people";
    public const string Companies = "companies";
    public const string Contacts = "contacts";

    /// <summary>Groups and direct conversations.</summary>
    public const string Chats = "chats";

    public const string Channels = "channels";
    public const string Messages = "messages";

    /// <summary>Saved messages, reminders and scheduled messages (mentions come with the messages).</summary>
    public const string Activity = "activity";

    public const string Support = "support";
    public const string Photos = "photos";
}

/// <summary>One part of the demo data and how many items it has.</summary>
public sealed record DemoDataStep(string Key, int Total);

/// <summary>Receives how many items of a part have been processed (added or found already there).</summary>
public interface IDemoDataProgress
{
    void Report(string part, int done);
}
