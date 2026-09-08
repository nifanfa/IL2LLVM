global using LLVMSharp.Interop;
global using Mono.Cecil;
global using Mono.Cecil.Cil;
global using Mono.Collections.Generic;
if (args.Length != 3)
    throw new ArgumentException("Expected an input file, output file, and target triple.");

new Translator().Translate(args);
