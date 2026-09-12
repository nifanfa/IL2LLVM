global using LLVMSharp.Interop;
global using Mono.Cecil;
global using Mono.Cecil.Cil;
global using Mono.Collections.Generic;

using System.Reflection;

var buildTimestamp = typeof(Program).Assembly
    .GetCustomAttributes<AssemblyMetadataAttribute>()
    .Single(attribute => attribute.Key == "BuildTimestamp")
    .Value;

Console.WriteLine($"IL2LLVM version {buildTimestamp}");
Console.WriteLine("Managed IL to LLVM object-file translator.");
Console.WriteLine("Copyright (c) nifanfa (nifanfa@nifanfa.com)");
Console.WriteLine("Translates assemblies built with the custom CoreLib into native object files.");

if (args.Length != 3)
    throw new ArgumentException("Expected an input file, output file, and target triple.");

new Translator().Translate(args);
