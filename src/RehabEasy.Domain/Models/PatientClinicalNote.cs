namespace RehabEasy.Domain.Models;

public sealed class PatientClinicalNote
{
    public string PatientId { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; init; }
}
