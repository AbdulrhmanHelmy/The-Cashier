using System;
using System.Linq;
using AlKhayal.Application.Sales;
using Xunit;

namespace AlKhayal.UnitTests
{
    public sealed class SalesPricingTests
    {
        [Theory]
        [InlineData(2000000,3,1,6000000)]
        [InlineData(33333,3,1,100000)]
        [InlineData(100000,1,3,33300)]
        [InlineData(100050,1,1,100100)]
        [InlineData(0,12,1,0)]
        public void Selected_unit_price_rounds_once_to_piastres(long price,long numerator,long denominator,long expected)
        { Assert.Equal(expected,SalesPricing.SelectedUnitPrice(price,numerator,denominator)); }

        [Fact]
        public void Invalid_conversions_and_overflow_are_rejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(()=>SalesPricing.SelectedUnitPrice(-1,1,1));
            Assert.Throws<ArgumentOutOfRangeException>(()=>SalesPricing.SelectedUnitPrice(100,0,1));
            Assert.Throws<ArgumentOutOfRangeException>(()=>SalesPricing.SelectedUnitPrice(100,1,0));
            Assert.Throws<OverflowException>(()=>SalesPricing.SelectedUnitPrice(long.MaxValue,2,1));
        }

        [Theory]
        [InlineData(1,300000)] [InlineData(11,300000)] [InlineData(12,275000)]
        [InlineData(13,275000)] [InlineData(24,275000)] [InlineData(25,275000)]
        public void Quantity_pricing_applies_largest_qualifying_pack_rate_to_the_whole_line(int quantity,long expected)
        {
            var tiers=new[]{new SalePriceBreak{UnitId=2,Numerator=12,Denominator=1,SellPriceScaled=3300000}};
            Assert.Equal(expected,SalesPricing.AutomaticBaseUnitPrice(300000,quantity,tiers));
        }

        [Fact]
        public void Multiple_thresholds_choose_the_largest_qualifying_conversion_independent_of_order()
        {
            var tiers=new[]{new SalePriceBreak{UnitId=9,Numerator=24,Denominator=1,SellPriceScaled=6000000},new SalePriceBreak{UnitId=2,Numerator=12,Denominator=1,SellPriceScaled=3300000}};
            Assert.Equal(250000,SalesPricing.AutomaticBaseUnitPrice(300000,25,tiers));
            Assert.Equal(275000,SalesPricing.AutomaticBaseUnitPrice(300000,13,tiers.Reverse()));
        }

        [Theory]
        [InlineData(3,1000000)]
        [InlineData(4,1333300)]
        [InlineData(6,2000000)]
        public void Fractional_pack_rate_rounds_the_line_once(int quantity,long expected)
        {
            var tiers=new[]{new SalePriceBreak{UnitId=2,Numerator=3,Denominator=1,SellPriceScaled=1000000}};
            Assert.Equal(333300,SalesPricing.AutomaticBaseUnitPrice(400000,quantity,tiers));
            Assert.Equal(expected,SalesPricing.AutomaticBaseLineGross(400000,quantity,tiers));
        }
    }
}
