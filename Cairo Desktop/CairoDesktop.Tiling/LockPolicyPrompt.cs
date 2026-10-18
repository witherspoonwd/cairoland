using CairoDesktop.Common;
using CairoDesktop.Common.Localization;

namespace CairoDesktop.Tiling
{
    /// <summary>
    /// Tells the user when Win+L couldn't be taken over, and offers the one-time elevated fix.
    /// </summary>
    public static class LockPolicyPrompt
    {
        private static bool _isOpen;

        public static void Show(WorkstationLockManager lockManager)
        {
            if (_isOpen)
            {
                return;
            }

            _isOpen = true;
            CairoMessage.ShowOkCancel(DisplayString.sLockFix_Message,
                DisplayString.sLockFix_Title,
                CairoMessageImage.Warning,
                DisplayString.sLockFix_Fix,
                DisplayString.sLockFix_NotNow,
                async result =>
                {
                    _isOpen = false;
                    if (result != true)
                    {
                        return;
                    }

                    if (await lockManager.FixPolicyAccessAsync())
                    {
                        CairoMessage.Show(DisplayString.sLockFix_Fixed, DisplayString.sLockFix_Title, CairoMessageImage.Information);
                    }
                    else
                    {
                        CairoMessage.Show(DisplayString.sLockFix_Failed, DisplayString.sLockFix_Title, CairoMessageImage.Error);
                    }
                });
        }
    }
}
