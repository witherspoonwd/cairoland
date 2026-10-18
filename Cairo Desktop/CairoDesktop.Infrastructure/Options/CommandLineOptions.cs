namespace CairoDesktop.Infrastructure.Options
{
    public class CommandLineOptions
    {
        public bool Restart { get; set; }

        public bool Tour { get; set; }

        public bool Shell { get; set; }

        public bool NoShell { get; set; }

        /// <summary>Set when Shell Launcher v2 starts Cairo (see Enable-CairoShellLauncher.ps1).</summary>
        public bool ShellLauncher { get; set; }
    }
}
