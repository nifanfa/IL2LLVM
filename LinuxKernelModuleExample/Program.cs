using System;

internal static class Program
{
    // DO NOT run this program directly. Build it, run IL2LLVM, and then run make to build the kernel module!
    private static void Main()
    {
        Console.WriteLine("Hello, World!");
        Console.WriteLine("Hello, World!(u8)"u8);
        LanguageFeatureValidation.Run();
        GarbageCollectionValidation.Run();
    }
}
