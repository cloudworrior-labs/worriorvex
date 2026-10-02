namespace WorriorVex.Domain;

/// <summary>Thrown when an operation would break a domain rule.</summary>
public sealed class DomainException(string message) : Exception(message);
