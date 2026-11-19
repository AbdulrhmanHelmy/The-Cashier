using AlKhayal.UI;
using Xunit;

namespace AlKhayal.UnitTests
{
    public sealed class WebViewKeyboardPolicyTests
    {
        [Theory]
        [InlineData(0x74u, false)]
        [InlineData(0x74u, true)]
        [InlineData(0x52u, true)]
        public void Refresh_key_down_is_blocked(uint virtualKey, bool controlPressed)
        {
            Assert.True(WebViewKeyboardPolicy.ShouldBlockRefresh(virtualKey, controlPressed, true));
        }

        [Theory]
        [InlineData(0x52u, false)]
        [InlineData(0x43u, true)]
        [InlineData(0x56u, true)]
        [InlineData(0x7Bu, false)]
        [InlineData(0x25u, false)]
        [InlineData(0x41u, false)]
        public void Non_refresh_key_down_is_not_blocked(uint virtualKey, bool controlPressed)
        {
            Assert.False(WebViewKeyboardPolicy.ShouldBlockRefresh(virtualKey, controlPressed, true));
        }

        [Theory]
        [InlineData(0x74u, false)]
        [InlineData(0x52u, true)]
        public void Key_up_is_not_blocked(uint virtualKey, bool controlPressed)
        {
            Assert.False(WebViewKeyboardPolicy.ShouldBlockRefresh(virtualKey, controlPressed, false));
        }
    }
}
