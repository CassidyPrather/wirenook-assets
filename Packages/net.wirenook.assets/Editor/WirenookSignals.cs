namespace Wirenook.Assets.Editor
{
    /// <summary>
    /// Animator parameters that mean the same thing in every wirenook
    /// package, so that a drop-in feature and the avatar it lands on can
    /// agree without knowing each other. A feature declares and syncs the
    /// ones it raises; an avatar's own animator may read them, and loses
    /// nothing where no feature raises them: an undeclared parameter rests
    /// at false.
    /// </summary>
    public static class WirenookSignals
    {
        /// <summary>
        /// The wearer has said they are away, from the menu. An avatar
        /// should treat it exactly as it treats VRChat's own <c>AFK</c>,
        /// which only a headset's proximity sensor, an open system menu or
        /// the End key can set.
        /// </summary>
        public const string Away = "WN/Away";

        /// <summary>VRChat's own flag for a wearer who has stepped away. It syncs it itself.</summary>
        public const string Afk = "AFK";
    }
}
