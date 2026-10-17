using System.Security.Cryptography;

namespace LinkShortener.Api.Application;

public sealed class LinkCodeGenerator
{
    // Exclude I, L, O and 1 to reduce ambiguity when reading a code aloud (D-03).
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 7;

    public string Generate() => RandomNumberGenerator.GetString(Alphabet, CodeLength);
}
