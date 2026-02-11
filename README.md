# IL2LLVM
![Logo](Logo.svg)

This project aims to build a compiler framework that translates Microsoft .NET Intermediate Language (IL) code into LLVM Intermediate Representation (IR). By leveraging LLVM’s modular backend infrastructure, the system will enable IL-based applications to be compiled and optimized for multiple architectures, including x86, x64, ARM, RISC-V, and others.

The core objectives are:

* IL Parsing and Analysis: Implement a robust front-end that reads and interprets .NET IL instructions.

* IR Generation: Map IL constructs (types, methods, control flow, exceptions) into equivalent LLVM IR instructions.

* Cross-Architecture Support: Utilize LLVM’s backend toolchain to generate optimized machine code for diverse targets.

* Extensibility: Provide a modular design so new IL features or LLVM optimizations can be integrated easily.

* Interoperability: Ensure compatibility with existing .NET assemblies while enabling native performance through LLVM.

Ultimately, this project bridges the gap between the .NET ecosystem and LLVM’s powerful optimization and code generation capabilities, delivering a portable and high-performance compilation pipeline across all major architectures.

* Still work in progress!
