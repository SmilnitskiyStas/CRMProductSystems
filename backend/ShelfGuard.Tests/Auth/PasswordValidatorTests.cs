using ShelfGuard.Application.Common;
using Xunit;

namespace ShelfGuard.Tests.Auth;

/// <summary>TASK-329 — shared password policy.</summary>
public sealed class PasswordValidatorTests
{
    [Theory]
    [InlineData("")]
    [InlineData("short1")]
    [InlineData("elevenchar1")]
    public void Rejects_passwords_shorter_than_12_chars(string password)
    {
        var error = PasswordValidator.Validate(password);
        Assert.Equal("Password must be at least 12 characters.", error);
    }

    [Fact]
    public void Rejects_password_without_letters()
    {
        var error = PasswordValidator.Validate("123456789012345");
        Assert.Equal("Password must contain at least one letter.", error);
    }

    [Fact]
    public void Rejects_password_without_digits()
    {
        var error = PasswordValidator.Validate("abcdefghijklmnop");
        Assert.Equal("Password must contain at least one digit.", error);
    }

    [Theory]
    [InlineData("qwerty123456")]
    [InlineData("QWERTY123456")] // case-insensitive
    [InlineData("password1234")]
    [InlineData("Password1234")]
    [InlineData("admin1234567")]
    [InlineData("1q2w3e4r5t6y")]
    public void Rejects_common_passwords(string password)
    {
        var error = PasswordValidator.Validate(password);
        Assert.Equal("This password is too common. Choose a more unique password.", error);
    }

    [Theory]
    [InlineData("stanislav2026x", "stanislav@example.com")]
    [InlineData("xxStAnIsLaVxx1", "stanislav@example.com")] // case-insensitive
    public void Rejects_password_containing_email_local_part(string password, string email)
    {
        var error = PasswordValidator.Validate(password, email);
        Assert.Equal("Password must not contain your email address.", error);
    }

    [Theory]
    [InlineData("correct-horse-battery-7", null)]
    [InlineData("Zhyto2026!Polyana", "someone@example.com")]
    [InlineData("SecurePass123", "admin@acme.com")] // 13 chars, letters + digits
    public void Accepts_valid_passwords(string password, string? email)
    {
        Assert.Null(PasswordValidator.Validate(password, email));
    }

    [Fact]
    public void Short_email_local_part_is_not_matched_inside_password()
    {
        // local part "ab" (< 3 chars) must not cause false rejections
        Assert.Null(PasswordValidator.Validate("grab-the-apple-42", "ab@example.com"));
    }
}
