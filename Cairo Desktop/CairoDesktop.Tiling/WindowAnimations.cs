using CairoDesktop.Interop;
using ManagedShell.Common.Logging;

namespace CairoDesktop.Tiling
{
    /// <summary>
    /// Turns off Windows' window open/close animations while tiling runs, so tag switches (SW_HIDE / SW_SHOW) happen
    /// instantly. The change is session-only (not written to the user profile), and the original value is kept in
    /// the journal so every exit and crash path restores it.
    /// </summary>
    public sealed class WindowAnimations
    {
        private readonly HiddenWindowJournal _journal;
        private bool _suppressed;

        public WindowAnimations(HiddenWindowJournal journal)
        {
            _journal = journal;
        }

        public void Suppress()
        {
            if (_suppressed)
            {
                return;
            }

            if (!TryGet(out int current))
            {
                ShellLogger.Warning("WindowAnimations: Unable to read the window animation setting");
                return;
            }

            if (!_journal.State.AnimationSaved)
            {
                _journal.SaveAnimation(current);
            }

            if (current != 0)
            {
                Set(0);
            }

            _suppressed = true;
        }

        public void Restore()
        {
            if (!_suppressed)
            {
                return;
            }

            _suppressed = false;
            _journal.RestoreAnimation();
        }

        internal static bool TryGet(out int minAnimate)
        {
            User32.ANIMATIONINFO info = User32.ANIMATIONINFO.Create();
            bool ok = User32.SystemParametersInfo(User32.SPI_GETANIMATION, info.cbSize, ref info, 0);
            minAnimate = info.iMinAnimate;
            return ok;
        }

        internal static void Set(int minAnimate)
        {
            User32.ANIMATIONINFO info = User32.ANIMATIONINFO.Create();
            info.iMinAnimate = minAnimate;

            // SPIF_SENDCHANGE without SPIF_UPDATEINIFILE: applies now, but a logoff resets it
            if (!User32.SystemParametersInfo(User32.SPI_SETANIMATION, info.cbSize, ref info, User32.SPIF_SENDCHANGE))
            {
                ShellLogger.Warning($"WindowAnimations: Unable to set window animations to {minAnimate}");
            }
        }
    }
}
