using System;
using System.Security.Cryptography;

namespace NopeNote.Windows.Helpers;

/// <summary>PBKDF2-SHA256 password hashing for the optional app lock.</summary>
internal static class PasswordHasher
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    internal static (string Salt, string Hash) Create(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return (Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    internal static bool Verify(string password, string saltText, string hashText)
    {
        try
        {
            byte[] salt = Convert.FromBase64String(saltText);
            byte[] expected = Convert.FromBase64String(hashText);
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
                password, salt, Iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
