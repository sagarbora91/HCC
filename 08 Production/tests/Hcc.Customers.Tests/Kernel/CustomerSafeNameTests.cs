using Hcc.Application.Customers;

namespace Hcc.Customers.Tests.Kernel;

/// <summary>af2-fix: a customer name written into audit text never carries a phone number, however it is spaced.</summary>
public class CustomerSafeNameTests
{
    [Theory]
    [InlineData("9876543210", "***")]
    [InlineData("98765 43210", "***")]
    [InlineData("+91-98765-43210", "***")]
    [InlineData("Ravi (+91) 98765 43210", "Ravi ***")]
    [InlineData("98 76 54 32 Ravi", "*** Ravi")]
    [InlineData("A1234B5678", "A***B***")]
    public void A_name_with_eight_or_more_digits_is_masked(string name, string expected) =>
        Assert.Equal(expected, CustomerCommands.SafeName(name));

    [Theory]
    [InlineData("Ananya Rao")]
    [InlineData("अनन्या राव")]
    [InlineData("Room 101 guest")]
    [InlineData("Flat 12-B, 3rd floor")]
    [InlineData("1234 567")]
    public void Ordinary_names_and_names_with_fewer_than_eight_digits_are_unchanged(string name) =>
        Assert.Equal(name, CustomerCommands.SafeName(name));

    [Fact]
    public void Null_is_empty() => Assert.Equal("", CustomerCommands.SafeName(null));
}
