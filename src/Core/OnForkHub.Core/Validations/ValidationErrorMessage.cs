namespace OnForkHub.Core.Validations;

public sealed class ValidationErrorMessage(string message, string field = "")
{
    public string Field { get; } = field ?? string.Empty;

    public string Message { get; } = message ?? string.Empty;

    public DateTime Timestamp { get; } = DateTime.UtcNow;

    public override string ToString()
    {
        return JsonSerializer.Serialize(
            new
            {
                Field,
                Message,
                Timestamp,
            }
        );
    }
}
