using Xunit;
using NarsApi.Infrastructure;

namespace NarsApi.Tests;

public class PasswordValidatorTests
{
    [Fact]
    public void ValidPassword_ReturnsNull()
    {
        Assert.Null(PasswordValidator.Validate("Str0ng!Pass"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("Ab1!")]
    public void TooShort_ReturnsError(string password)
    {
        Assert.Equal("Password must be at least 8 characters.", PasswordValidator.Validate(password));
    }

    [Fact]
    public void NoUppercase_ReturnsError()
    {
        Assert.Equal("Password must contain at least one uppercase letter.",
            PasswordValidator.Validate("nouppercase1!"));
    }

    [Fact]
    public void NoDigit_ReturnsError()
    {
        Assert.Equal("Password must contain at least one digit.",
            PasswordValidator.Validate("NoDigitHere!"));
    }

    [Fact]
    public void NoSpecialChar_ReturnsError()
    {
        Assert.Equal("Password must contain at least one special character.",
            PasswordValidator.Validate("NoSpecial1"));
    }

    [Theory]
    [InlineData("Aa1!aaaaa")]       // exactly 9 chars
    [InlineData("Test1234!")]       // typical valid password
    [InlineData("P@ssw0rdLong")]    // with @ symbol
    public void EdgeCases_ValidPasswords(string password)
    {
        Assert.Null(PasswordValidator.Validate(password));
    }

    [Fact]
    public void Exactly8Chars_Valid()
    {
        Assert.Null(PasswordValidator.Validate("Aa1!xxxx"));
    }

    [Fact]
    public void SevenChars_ReturnsError()
    {
        Assert.Equal("Password must be at least 8 characters.",
            PasswordValidator.Validate("Aa1!xxx"));
    }

    [Theory]
    [InlineData("P@ssw0rd")]
    [InlineData("Passw0rd!")]
    [InlineData("Abc123!@")]
    [InlineData("Xyz789#!")]
    [InlineData("Qwerty!1")]
    public void CommonPassword_ReturnsError(string password)
    {
        Assert.Equal("Password is too common. Choose a more complex password.",
            PasswordValidator.Validate(password));
    }

    [Fact]
    public void NullPassword_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => PasswordValidator.Validate(null!));
    }

    [Fact]
    public void WhitespacePassword_ReturnsError()
    {
        Assert.NotNull(PasswordValidator.Validate("   "));
    }

    [Fact]
    public void VeryLongPassword_Valid()
    {
        var longPwd = "Aa1!" + new string('x', 200);
        Assert.Null(PasswordValidator.Validate(longPwd));
    }
}
