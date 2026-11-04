using Hcc.Domain.Customers;

namespace Hcc.Customers.Tests.Kernel;

/// <summary>AF-CRM (AF-DEC-3): the opaque customer wire key.</summary>
public class CustomerKeyTokenTests
{
    private static readonly byte[] SecretA = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private static readonly byte[] SecretB = Enumerable.Range(2, 32).Select(i => (byte)i).ToArray();

    [Fact]
    public void Token_is_deterministic_and_25_characters()
    {
        var t = CustomerKeyToken.Make(SecretA, "9820011111");
        Assert.Equal(t, CustomerKeyToken.Make(SecretA, "9820011111"));
        Assert.Equal(25, t.Length);
        Assert.StartsWith("ck_", t, StringComparison.Ordinal);
        Assert.True(CustomerKeyToken.IsToken(t));
        Assert.NotEqual(t, CustomerKeyToken.Make(SecretA, "9820011112"));
    }

    [Fact]
    public void Token_differs_per_secret_and_hides_the_key()
    {
        var a = CustomerKeyToken.Make(SecretA, "9820011111");
        Assert.NotEqual(a, CustomerKeyToken.Make(SecretB, "9820011111"));
        Assert.DoesNotContain("9820011111", a, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\d{10}", a);
        Assert.Equal(25, CustomerKeyToken.Make(SecretA, "n:राम पाटील").Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("9820011111")]
    [InlineData("n:asha rao")]
    [InlineData("ck_short")]
    [InlineData("ck_AAAAAAAAAAAAAAAAAAAA!A")]
    public void Non_tokens_are_not_tokens(string? value) => Assert.False(CustomerKeyToken.IsToken(value));
}
