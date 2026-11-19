using System;
using AlKhayal.UI;
using Xunit;

namespace AlKhayal.UnitTests
{
    public sealed class DesktopPdfOpenerTests
    {
        [Theory]
        [InlineData("")]
        [InlineData("not base64")]
        [InlineData("dGV4dA==")]
        public void Rejects_invalid_pdf_before_creating_a_preview(string input)
        {
            Assert.Throws<InvalidOperationException>(() => new DesktopPdfOpener().open(input));
        }
    }
}
