namespace PoOlympic
{
    /// <summary>
    /// The clock of the attract (demo) mode's idle wait and of the broadcast camera's shot dwell: it adds real frame time
    /// but ignores any frame longer than <see cref="MaxFrameSeconds"/> (a scene load, a tab in the background, a GC or
    /// shader-compile hitch), so a stall never counts as idle time or as time a shot was on screen.
    /// </summary>
    public sealed class IdleClock
    {
        /// <summary>Frames longer than this (s) are skipped.</summary>
        public const float MaxFrameSeconds = 0.25f;

        /// <summary>Seconds counted since the last <see cref="Reset"/>.</summary>
        public float Seconds { get; private set; }

        public void Reset() => Seconds = 0f;

        /// <summary>Adds one frame's time (skipped when it is not positive or longer than MaxFrameSeconds).</summary>
        public float Advance(float frameSeconds)
        {
            Seconds += Step(frameSeconds);
            return Seconds;
        }

        /// <summary>The part of a frame's time a dwell clock may count: the frame itself, or 0 for a long / bad frame.</summary>
        public static float Step(float frameSeconds) =>
            frameSeconds > 0f && frameSeconds <= MaxFrameSeconds && !float.IsNaN(frameSeconds) ? frameSeconds : 0f;
    }
}
