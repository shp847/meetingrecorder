namespace AppPlatform.Abstractions;

public sealed record AboutContentDefinition(
    string ProductName,
    string Version,
    string ProductDescription,
    string AuthorName,
    string AuthorEmail,
    string SupportDescription,
    string ReleaseDescription,
    string LegalNotice);
