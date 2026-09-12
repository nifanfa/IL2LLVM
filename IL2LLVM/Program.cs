global using LLVMSharp.Interop;
global using Mono.Cecil;
global using Mono.Cecil.Cil;
global using Mono.Collections.Generic;

Console.WriteLine("IL2LLVM");
Console.WriteLine("Managed IL to LLVM object-file translator.");
Console.WriteLine($"Build started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
Console.WriteLine("Copyright (c) nifanfa (nifanfa@nifanfa.com)");
Console.WriteLine("Translates assemblies built with the custom CoreLib into native object files.");

if (args.Length != 3)
    throw new ArgumentException("Expected an input file, output file, and target triple.");

new Translator().Translate(args);
