using System;

internal static class Program
{
    // DO NOT run this program directly. Build it, run IL2LLVM, and then run apphost!
    private static void Main()
    {
        Console.WriteLine("Hello, World!");
        LanguageFeatureValidation.Run();
        GarbageCollectionValidation.Run();
    }
}
