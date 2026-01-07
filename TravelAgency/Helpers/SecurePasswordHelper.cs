using System;
using System.Security.Cryptography;
using System.Text;

namespace TravelAgency.Helpers
{
    public static class SecurePasswordHelper
    {
        public static string Hash(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Password cannot be empty.");

            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(password);
                var hash = sha.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
        }

        public static bool Verify(string password, string hashedPassword)
        {
            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hashedPassword))
                return false;

            var hashOfInput = Hash(password);

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(hashOfInput),
                Encoding.UTF8.GetBytes(hashedPassword)
            );
        }
    }
}
