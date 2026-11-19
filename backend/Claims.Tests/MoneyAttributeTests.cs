using Claims.Api.Controllers;

namespace Claims.Tests;

public class MoneyAttributeTests
{
    [Theory]
    [InlineData("0", false, true)]
    [InlineData("0", true, false)]
    [InlineData("12.34", true, true)]
    [InlineData("12.345", false, false)]
    [InlineData("-1", false, false)]
    [InlineData("9999999999999999.99", false, true)]
    [InlineData("10000000000000000", false, false)] // would overflow numeric(18,2)
    public void Checks_amount(string amount, bool positive, bool valid) =>
        Assert.Equal(valid, new MoneyAttribute { Positive = positive }.IsValid(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Missing_value_is_left_to_Required() => Assert.True(new MoneyAttribute().IsValid(null));
}
