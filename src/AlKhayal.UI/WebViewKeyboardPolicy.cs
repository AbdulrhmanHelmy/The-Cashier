namespace AlKhayal.UI
{
    internal static class WebViewKeyboardPolicy
    {
        private const uint F5 = 0x74;
        private const uint R = 0x52;

        internal static bool ShouldBlockRefresh(uint virtualKey, bool controlPressed, bool isKeyDown)
        {
            return isKeyDown && (virtualKey == F5 || (controlPressed && virtualKey == R));
        }
    }
}
