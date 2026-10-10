using System.Security.Cryptography;

namespace UrlShortener.Services;

public static class CodeGenerator
{
    private const string Alphabet =
        "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public static string Generate(int length = 7) =>
        string.Create(length, 0, static (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        });
}