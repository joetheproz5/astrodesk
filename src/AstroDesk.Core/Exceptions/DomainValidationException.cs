namespace AstroDesk.Core.Exceptions;

public sealed class DomainValidationException : InvalidOperationException
{
    public DomainValidationException(string message)
        : base(message)
    {
    }
}
