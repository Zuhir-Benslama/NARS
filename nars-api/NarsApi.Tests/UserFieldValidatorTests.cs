using NarsApi.Infrastructure;
using Xunit;

namespace NarsApi.Tests;

public class UserFieldValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateEmail_Empty_ReturnsNull(string? email)
    {
        Assert.Null(UserFieldValidator.ValidateEmail(email));
    }

    [Fact]
    public void ValidateEmail_Valid_ReturnsNull()
    {
        Assert.Null(UserFieldValidator.ValidateEmail("user@example.com"));
    }

    [Fact]
    public void ValidateEmail_TooLong_ReturnsLengthError()
    {
        var longEmail = "a@" + new string('x', UserFieldValidator.MaxEmailLength) + ".com";
        Assert.Equal($"Email must be at most {UserFieldValidator.MaxEmailLength} characters.",
            UserFieldValidator.ValidateEmail(longEmail));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("user@")]
    [InlineData("@example.com")]
    public void ValidateEmail_Invalid_ReturnsInvalidError(string email)
    {
        Assert.Equal("Email is not a valid email address.",
            UserFieldValidator.ValidateEmail(email));
    }

    [Fact]
    public void ValidateMaxLength_Null_ReturnsNull()
    {
        Assert.Null(UserFieldValidator.ValidateMaxLength(null, 10, "Name"));
    }

    [Fact]
    public void ValidateMaxLength_WithinLimit_ReturnsNull()
    {
        Assert.Null(UserFieldValidator.ValidateMaxLength("short", 10, "Name"));
    }

    [Fact]
    public void ValidateMaxLength_AtLimit_ReturnsNull()
    {
        Assert.Null(UserFieldValidator.ValidateMaxLength(new string('x', 10), 10, "Name"));
    }

    [Fact]
    public void ValidateMaxLength_ExceedsLimit_ReturnsError()
    {
        Assert.Equal("Name must be at most 5 characters.",
            UserFieldValidator.ValidateMaxLength(new string('x', 6), 5, "Name"));
    }
}
