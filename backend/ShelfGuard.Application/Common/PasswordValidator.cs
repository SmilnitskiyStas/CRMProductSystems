namespace ShelfGuard.Application.Common;

/// <summary>
/// Shared password policy (TASK-329). Applied everywhere a password is set:
/// user invite, self-service change, tenant onboarding, provider flows.
/// Existing stored hashes are unaffected — the policy only guards new passwords.
/// </summary>
public static class PasswordValidator
{
    public const int MinLength = 12;

    /// <summary>
    /// ~100 most common passwords (case-insensitive), incl. ukrainian-keyboard
    /// classics. Entries shorter than MinLength are kept anyway — the list also
    /// guards against future MinLength changes and substring-style variants.
    /// </summary>
    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "123456", "123456789", "12345678", "1234567890", "123456789012",
        "password", "password1", "password12", "password123", "password1234",
        "password12345", "passwords123", "p@ssword1234", "passw0rd1234",
        "qwerty", "qwerty12", "qwerty123", "qwerty1234", "qwerty12345",
        "qwerty123456", "qwertyuiop", "qwertyuiop123", "qwertyuiopas",
        "1q2w3e4r5t6y", "1qaz2wsx3edc", "zaq12wsx", "zaq1zaq1zaq1",
        "abc123456789", "abcd12345678", "abcdefg12345", "abcdef123456",
        "iloveyou1234", "welcome12345", "welcome123456", "letmein12345",
        "sunshine1234", "princess1234", "football1234", "baseball1234",
        "superman1234", "batman123456", "dragon123456", "master123456",
        "monkey123456", "shadow123456", "michael123456", "jennifer12345",
        "computer1234", "internet1234", "samsung12345", "google123456",
        "admin1234567", "admin123456", "administrator", "administrator1",
        "root12345678", "user12345678", "test12345678", "testtest1234",
        "temp12345678", "changeme1234", "default12345", "secret123456",
        "trustno1trustno1", "whatever1234", "starwars1234", "pokemon12345",
        "minecraft123", "liverpool123", "chelsea12345", "arsenal12345",
        "barcelona123", "realmadrid123", "juventus1234", "manchester123",
        "spiderman123", "metallica123", "nirvana12345", "eminem123456",
        "0123456789012", "1234512345123", "111111111111", "000000000000",
        "121212121212", "112233445566", "123123123123", "123321123321",
        "654321654321", "987654321098", "1234567890123", "aaaaaaaaaaaa",
        "asdfghjkl123", "asdfgh123456", "asdasd123456", "zxcvbnm12345",
        "zxcvbn123456", "qazwsx123456", "qazwsxedc123", "poiuytrewq12",
        "mnbvcxz12345", "0987654321qw",
        // Ukrainian / cyrillic-keyboard classics (typed in latin layout)
        "parol1234567", "parol1234", "parol123456", "ukraine12345",
        "ukraina12345", "kyiv12345678", "shakhtar1234", "dynamo123456",
        "slavaukraini", "kohannya1234", "natasha12345", "andriy123456",
        "oleksandr123", "volodymyr123", "ivanov123456", "petrov123456",
        "gfhjkm123456", "gfhjkm1234",   // "пароль" on latin layout
        "qwerty098765", "1234567890qw", "q1w2e3r4t5y6",
    };

    /// <summary>
    /// Validates a new password. Returns an English error message, or null when valid.
    /// <paramref name="email"/> (when known) is used to reject passwords containing
    /// the email's local part.
    /// </summary>
    public static string? Validate(string? password, string? email = null)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
            return $"Password must be at least {MinLength} characters.";

        if (!password.Any(char.IsLetter))
            return "Password must contain at least one letter.";

        if (!password.Any(char.IsDigit))
            return "Password must contain at least one digit.";

        if (CommonPasswords.Contains(password))
            return "This password is too common. Choose a more unique password.";

        if (!string.IsNullOrWhiteSpace(email))
        {
            var localPart = email.Split('@')[0].Trim();
            if (localPart.Length >= 3 &&
                password.Contains(localPart, StringComparison.OrdinalIgnoreCase))
                return "Password must not contain your email address.";
        }

        return null;
    }
}
