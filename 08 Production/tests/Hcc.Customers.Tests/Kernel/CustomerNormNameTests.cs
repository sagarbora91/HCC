using Hcc.Domain.Customers;

namespace Hcc.Customers.Tests.Kernel;

/// <summary>AF-CRM (AF-DEC-4): Unicode-aware name normalisation; ASCII names must normalise exactly as before.</summary>
public class CustomerNormNameTests
{
    // Expected strings are what the pre-change a-z/0-9 rule produced for each input.
    public static TheoryData<string?, string> AsciiTable => new()
    {
        { "Asha Rao", "asha rao" },
        { "Asha Rao (fictional)", "asha rao" },
        { "ASHA  RAO", "asha rao" },
        { "Vikram Shah (FICTIONAL)", "vikram shah" },
        { "O'Brien-Smith", "o brien smith" },
        { "Dr. A. P. J. Kalam", "dr a p j kalam" },
        { "  leading space", "leading space" },
        { "trailing!!!", "trailing" },
        { "Room 101 Guest", "room 101 guest" },
        { "A_B", "a b" },
        { "", "" },
        { null, "" },
        { "(fictional)", "" },
        { "Mr. & Mrs. Patil", "mr mrs patil" },
        { "R2-D2", "r2 d2" },
        { "x@y.com", "x y com" },
        { "Tab\tName", "tab name" },
        { "Anil  Kumar (fictional) Jr", "anil kumar jr" },
        { "###", "" },
        { "123", "123" },
    };

    [Theory]
    [MemberData(nameof(AsciiTable))]
    public void Ascii_NormName_is_unchanged(string? name, string expected) => Assert.Equal(expected, CustomerRules.NormName(name));

    [Fact]
    public void Devanagari_names_keep_their_letters_and_matras()
    {
        Assert.Equal("राम पाटील", CustomerRules.NormName("राम  पाटील (fictional)"));
        Assert.NotEqual(CustomerRules.NormName("राम पाटील"), CustomerRules.NormName("सीता जोशी"));
    }

    [Fact]
    public void Symbol_only_names_never_share_a_customer_key()
    {
        var a = CustomerRules.KeyOf(null, "###");
        var b = CustomerRules.KeyOf(null, "@@@");
        Assert.StartsWith("n:#", a, StringComparison.Ordinal);
        Assert.NotEqual(a, b);
        Assert.Equal("n:asha rao", CustomerRules.KeyOf(null, "Asha Rao"));
        Assert.Equal("9820011111", CustomerRules.KeyOf("+91 98200 11111", "Asha Rao"));
    }

    [Fact]
    public void Search_uses_the_name_branch_for_any_script_and_digits_otherwise()
    {
        var ram = new Customer("n:x", "राम पाटील", string.Empty, null, [], [], [], 0, 0, 0, null, null, null, [], 0, []);
        var sita = new Customer("n:y", "सीता जोशी", string.Empty, null, [], [], [], 0, 0, 0, null, null, null, [], 0, []);
        Assert.Equal(["n:x"], CustomerRules.Search([ram, sita], "राम").Select(c => c.Key));
        Assert.Equal(["n:y"], CustomerRules.Search([ram, sita], "जोशी").Select(c => c.Key));
        Assert.Empty(CustomerRules.Search([ram, sita], "12"));
    }
}
