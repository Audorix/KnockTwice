namespace Audorix.ChatX.Domain;

/// <summary>
/// Raised when a domain invariant is violated. <see cref="Code"/> is a stable,
/// machine-readable identifier that the API maps to its error envelope.
/// </summary>
public sealed class DomainRuleException : Exception
{
    public DomainRuleException(string code, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    public DomainRuleException()
        : this("domain_rule_violation", "A domain rule was violated.")
    {
    }

    public DomainRuleException(string message)
        : this("domain_rule_violation", message)
    {
    }

    public DomainRuleException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = "domain_rule_violation";
    }

    public string Code { get; }
}
