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

        /// <summary>
        /// Above a half while the avatar is asleep on its feet: the nap of
        /// net.wirenook.locomotion writes it from an animation, on every
        /// client, and it is in no expression parameter list. An avatar's
        /// animator declares it as a float to read it (closed eyes, say)
        /// and finds it at nought wherever nothing naps.
        /// </summary>
        public const string Asleep = "WN/Asleep";

        /// <summary>
        /// The wearer has switched themself invisible, from the menu: a
        /// synced bool. The switch itself (net.wirenook.adventure-tools)
        /// stops every renderer drawing for everyone else. A feature with
        /// something that shows and is not a renderer (a light) reads this
        /// and puts it out, for everyone but the wearer.
        /// </summary>
        public const string Unseen = "WN/Unseen";

        /// <summary>
        /// Above a half on a client where the avatar is not to be drawn at
        /// all. A feature that decides who sees the wearer viewer by viewer
        /// writes it from an animation, on every client for itself; the
        /// invisible switch obeys it. In no expression parameter list.
        /// </summary>
        public const string Hidden = "WN/Hidden";

        /// <summary>
        /// Above a half on a client where the wearer is drawn as a spectre:
        /// seen by people who are let behind the scenes, and by the wearer,
        /// while everyone else sees nothing. Written like <see cref="Hidden"/>.
        /// </summary>
        public const string Spectral = "WN/Spectral";

        /// <summary>
        /// Above a half on a client where the wearer is not to be heard:
        /// their humanoid head bone, which VRChat puts the voice and the
        /// nameplate at, has been moved out of earshot there. Written like
        /// <see cref="Hidden"/>.
        /// </summary>
        public const string Muted = "WN/Muted";
    }
}
