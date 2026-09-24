using System.Runtime.InteropServices;

namespace System.Threading
{
    public sealed unsafe partial class Thread
    {
        static partial void SpillRegisterWindows() => SpillRegisterWindowsNative();

        [DllImport("*", EntryPoint = "xthal_window_spill")]
        private static extern void SpillRegisterWindowsNative();
    }
}
