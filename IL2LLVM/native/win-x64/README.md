# Custom libLLVM for Windows x64

`libLLVM.dll` is built from the upstream LLVM `llvmorg-21.1.8` tag (commit
`2078da43e25a4623cab2d0d60decddf709aaea28`) with Microsoft Visual C++ in
Release mode.

The relevant CMake options are:

```text
LLVM_BUILD_LLVM_C_DYLIB=ON
LLVM_TARGETS_TO_BUILD=all
LLVM_EXPERIMENTAL_TARGETS_TO_BUILD=Xtensa
```

The native build has four integration changes:

- `llvm/tools/llvm-shlib/libllvm.cpp` registers the experimental Xtensa target,
  target info, target MC, assembly parser, and assembly printer when the DLL is
  loaded. LLVMSharp's generated `InitializeAll*` methods only register stable
  targets in LLVM 21.
- `llvm/lib/Target/TargetMachineC.cpp` maps PIC relocation requests to static
  relocation only for Xtensa triples because the LLVM 21 Xtensa backend does
  not implement PIC relocations. Other targets retain the requested relocation
  model.
- `llvm/lib/Target/Xtensa/MCTargetDesc/XtensaTargetStreamer.cpp` switches the
  object streamer to the aligned literal section before emitting constant-pool
  entries. For assembly output, it switches to the function's text section
  before `.literal_position`; together with GNU assembler's
  `--text-section-literals`, this keeps `L32R` literals before and within range
  of their uses.
- `llvm/lib/Target/Xtensa/MCTargetDesc/XtensaMCAsmInfo.cpp` disables Dwarf CFI
  directives because the ESP32 Xtensa GNU assembler rejects them for this
  target.

The checked-in DLL is packed with UPX 5.2.1 using `upx --best --lzma` to keep
the binary below GitHub's per-file size limit. UPX decompression happens in
memory when Windows loads the DLL and does not change its exported C API.

The binary is distributed under the Apache License 2.0 with LLVM Exceptions;
see `LICENSE.TXT` in this directory.
