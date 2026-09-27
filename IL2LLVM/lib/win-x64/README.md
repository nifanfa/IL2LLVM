# Custom libLLVM for Windows x64

`libLLVM.dll` is built from the [`IL2LLVM-21.1.8` branch of
`nifanfa/llvm-project`](https://github.com/nifanfa/llvm-project) at commit
`1a9515e3a898a661115547a8eacd1c571be4e5f6` with Microsoft Visual C++ in
Release mode. The branch is based on upstream LLVM `llvmorg-21.1.8` (commit
`2078da43e25a4623cab2d0d60decddf709aaea28`).

The relevant CMake options are:

```text
LLVM_BUILD_LLVM_C_DYLIB=ON
LLVM_TARGETS_TO_BUILD=all
LLVM_EXPERIMENTAL_TARGETS_TO_BUILD=Xtensa
```

The checked-in DLL is packed with UPX 5.2.1 using `upx --best --lzma` to keep
the binary below GitHub's per-file size limit. UPX decompression happens in
memory when Windows loads the DLL and does not change its exported C API.

The binary is distributed under the Apache License 2.0 with LLVM Exceptions;
see `LICENSE.TXT` in this directory.
