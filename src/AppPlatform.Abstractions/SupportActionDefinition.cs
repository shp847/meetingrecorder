namespace AppPlatform.Abstractions;

public sealed record SupportActionDefinition(
    string Id,
    string Title,
    string Description,
    string ButtonLabel,
    string StatusMessage);
