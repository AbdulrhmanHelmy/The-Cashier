using System;
using System.Collections.Generic;
using System.Linq;
using AlKhayal.Application.Finance;

namespace AlKhayal.Application.Sales
{
    public static class SalesPricing
    {
        // Catalog variant prices are per base unit. Final sale inputs are piastres.
        public static long SelectedUnitPrice(long basePriceScaled, long numerator, long denominator)
        {
            if (basePriceScaled < 0 || numerator <= 0 || denominator <= 0) throw new ArgumentOutOfRangeException();
            return checked((long)decimal.Round((decimal)basePriceScaled * numerator / denominator /
                MoneyPolicy.PiastreInInternalUnits, 0, MidpointRounding.AwayFromZero) * MoneyPolicy.PiastreInInternalUnits);
        }

        public static long AutomaticBaseUnitPrice(long basePriceScaled, decimal quantity, IEnumerable<SalePriceBreak> priceBreaks)
        {
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            var qualifying = QualifyingBreak(quantity, priceBreaks);
            if (qualifying == null) return SelectedUnitPrice(basePriceScaled, 1, 1);
            var selectedPrice = qualifying.SellPriceScaled.HasValue
                ? SelectedUnitPrice(qualifying.SellPriceScaled.Value, 1, 1)
                : SelectedUnitPrice(basePriceScaled, qualifying.Numerator, qualifying.Denominator);
            return SelectedUnitPrice(selectedPrice, qualifying.Denominator, qualifying.Numerator);
        }

        public static long AutomaticBaseLineGross(long basePriceScaled, decimal quantity, IEnumerable<SalePriceBreak> priceBreaks)
        {
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            var qualifying = QualifyingBreak(quantity, priceBreaks);
            if (qualifying == null) return RoundLine(quantity * SelectedUnitPrice(basePriceScaled, 1, 1));
            var selectedPrice = qualifying.SellPriceScaled.HasValue
                ? SelectedUnitPrice(qualifying.SellPriceScaled.Value, 1, 1)
                : SelectedUnitPrice(basePriceScaled, qualifying.Numerator, qualifying.Denominator);
            return RoundLine(quantity * selectedPrice * qualifying.Denominator / qualifying.Numerator);
        }

        private static long RoundLine(decimal internalAmount)
        {
            return checked(decimal.ToInt64(decimal.Round(internalAmount / MoneyPolicy.PiastreInInternalUnits, 0,
                MidpointRounding.AwayFromZero)) * MoneyPolicy.PiastreInInternalUnits);
        }

        private static SalePriceBreak QualifyingBreak(decimal quantity, IEnumerable<SalePriceBreak> priceBreaks)
        {
            return (priceBreaks ?? Enumerable.Empty<SalePriceBreak>())
                .Where(x => x != null && x.Numerator > x.Denominator && x.Denominator > 0 &&
                    (decimal)x.Numerator / x.Denominator <= quantity)
                .OrderByDescending(x => (decimal)x.Numerator / x.Denominator)
                .ThenBy(x => x.UnitId)
                .FirstOrDefault();
        }
    }

    public sealed class SalePriceBreak
    {
        public long UnitId { get; set; }
        public long Numerator { get; set; }
        public long Denominator { get; set; }
        public long? SellPriceScaled { get; set; }
    }
}
