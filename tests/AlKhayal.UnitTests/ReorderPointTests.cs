using AlKhayal.Application.Alerts;
using AlKhayal.Application.Catalog;
using Xunit;

namespace AlKhayal.UnitTests
{
    public sealed class ReorderPointTests
    {
        [Theory]
        [InlineData("0",0L)]
        [InlineData("10",10000000L)]
        [InlineData("0.000001",1L)]
        [InlineData("9223372036854.775807",long.MaxValue)]
        public void Quantities_use_exact_scale_six(string input,long expected)
        { long scaled;Assert.True(CatalogService.TryScaleReorderPoint(input,out scaled));Assert.Equal(expected,scaled); }
        [Theory]
        [InlineData(null)][InlineData("")][InlineData("-1")][InlineData("1.0000001")]
        [InlineData("1e3")][InlineData("9223372036854.775808")][InlineData("NaN")]
        public void Invalid_reorder_quantities_are_rejected(string input)
        { long scaled;Assert.False(CatalogService.TryScaleReorderPoint(input,out scaled)); }
        [Fact]
        public void Disabled_zero_does_not_alert_and_equal_threshold_does()
        {
            Assert.False(AlertService.IsLowStock(0,0));
            Assert.True(AlertService.IsLowStock(10000000,10000000));
            Assert.True(AlertService.IsLowStock(0,10000000));
            Assert.False(AlertService.IsLowStock(11000000,10000000));
        }
    }
}
