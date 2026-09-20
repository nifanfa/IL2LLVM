# IL2LLVM

![Logo](Logo.svg)

IL2LLVM translates a managed assembly built with this repository's CoreLib into a native object file through LLVM. It is not a .NET runtime, NativeAOT frontend, or a general-purpose replacement for the .NET SDK.

<img width="640" height="318" alt="QQ_1789867398271" src="https://github.com/user-attachments/assets/541d775a-f146-4a9f-a00a-24d826574926" />

## Project purpose

The purpose of this project is to support any processor architecture for which LLVM can emit an object file. IL2LLVM does not contain x86, ARM, Windows, Linux, or kernel-specific translation logic. The project supplies the LLVM target triple and the native host supplies the ABI-dependent entry point, exception transfer, and linker configuration.

The managed runtime is deliberately small. A user-mode host only needs a small ISO C library surface:

- `calloc`
- `free`
- `memcpy`
- `memset`
- `abort`
- `printf` or an equivalent text output function
- `wprintf` or an equivalent UTF-16 output function when character output is used

The following symbols are the runtime boundary implemented by the host. They are not platform APIs and can be implemented for the target processor and environment:

- `setjmp`
- `longjmp`
- `Enter`
- `Exit`
- `GetCurrentTimeMilliseconds`

The current `setjmp` entry has an additional stack-pointer argument so the generated exception machinery can restore the managed stack state. It therefore requires a target-specific implementation even though `setjmp` and `longjmp` have standard C counterparts. Console output symbols such as `System_Console_WriteLine_Int32` are optional host conveniences, not requirements of the translator.

Projects below the repository root build without the framework class library. `Directory.Build.targets` imports `CoreLib/CoreLib.cs` as a shared source file, so the compiled input assembly contains the runtime types used by the translator.

```
C# project + CoreLib
        |
        v
 managed assembly
        |
        v
     IL2LLVM
        |
        v
 native object file + host runtime
        |
        v
 executable or kernel module
```

## Repository layout

| Path | Purpose |
| --- | --- |
| `IL2LLVM/` | The IL-to-LLVM translator. |
| `CoreLib/` | Shared custom CoreLib compiled into managed input assemblies. |
| `ConsoleAppExample/` | Managed test program, including language-feature and GC validation. |
| `apphost/` | Minimal C entry point and host implementations for the console test. |
| `LinuxKernelModuleExample/` | Linux x86-64 kernel-module host, linker inputs, and Kbuild Makefile. |

## Build requirements

- .NET 10 SDK
- Vendored NuGet packages under `packages/`; `nuget.config` restores them to `obj/packages`
- A native linker and host runtime appropriate for the output target
- For `LinuxKernelModuleExample`: GCC, make, and Linux headers matching the kernel that will load the module

Build the translator:

```powershell
dotnet restore IL2LLVM\IL2LLVM.csproj
dotnet build IL2LLVM\IL2LLVM.csproj --no-restore
```

## IL2LLVM invocation

IL2LLVM requires exactly three arguments:

```text
IL2LLVM <input-assembly> <output-file> <target>[;<code-model>[;<cpu>[;<features>]]]
```

`target` is an LLVM target triple. The optional code model is one of `default`, `tiny`, `small`, `kernel`, `medium`, or `large`. CPU defaults to `generic`; target features use LLVM's comma-separated `+feature,-feature` syntax.

Examples:

```powershell
IL2LLVM\bin\Debug\net10.0\IL2LLVM.exe `
  ConsoleAppExample\bin\Debug\net10.0\ConsoleAppExample.dll `
  ConsoleAppExample\bin\Debug\net10.0\ConsoleAppExample.obj `
  x86_64-pc-windows-msvc

IL2LLVM\bin\Debug\net10.0\IL2LLVM.exe `
  LinuxKernelModuleExample\bin\Debug\net10.0\LinuxKernelModuleExample.dll `
  LinuxKernelModuleExample\bin\Debug\net10.0\LinuxKernelModuleExample.obj `
  "x86_64-unknown-linux-gnu;kernel"
```

The second argument is the native object-file output path. IL2LLVM always asks
LLVM to emit one relocatable object; archive creation and final native linking
are separate build steps. The output uses static relocation. Globals created by
the translator for managed static fields, GC descriptors, field data, and
compiler-generated helpers use internal linkage. Managed entry points and
`[DllImport("*")]` imports remain external symbols for the host linker.

The Windows build uses the repository's `IL2LLVM/native/win-x64/libLLVM.dll`.
It is built from LLVM 21.1.8 with the experimental Xtensa backend enabled, in
addition to the regular LLVM targets.

### Calling convention

IL2LLVM emits all generated calls using the C `cdecl` calling convention. This applies to calls across the managed/native boundary as well as calls to host runtime symbols. The host runtime must therefore expose matching `cdecl` entry points; IL2LLVM does not automatically select or adapt platform-specific calling conventions.

The Visual Studio launch profiles in `IL2LLVM/Properties/launchSettings.json` provide the same commands for the console x86/x64 objects and the Linux x86-64 kernel object.

## Custom runtime boundary

`CoreLib` is source compiled into each managed program. It provides the managed definitions used by generated code, including object layout, arrays, strings, exceptions, collections, delegates, tasks, and GC metadata.

Platform-specific operations remain external. Methods marked with `[DllImport("*")]` are native symbols. The final host must provide every imported symbol that the managed program reaches. Examples include allocation, deallocation, non-local exception transfer, abort, console output, and synchronization. GC and exception frame tracking are implemented in `CoreLib`.

The built-in collector uses GC descriptors emitted by IL2LLVM and registers static fields as roots. It is not a replacement for the host allocator: the current runtime imports `calloc` and `free`.

`System.Threading.Monitor.Enter` and `Exit` are currently host hooks. The supplied hosts contain placeholders, not a complete synchronization implementation.

### Cooperative green threads

`System.Threading.Thread` implements stackful green threads on one native execution thread. A context switch saves the active stack with `setjmp` and `memcpy`, snapshots its precise GC roots, restores another saved stack, and resumes it with `longjmp`. The collector remains precise and non-moving; suspended threads are marked from their root snapshots rather than by conservatively scanning copied stack bytes.

Scheduling is cooperative, not parallel. `Thread.Yield`, `Thread.Sleep`, and `Thread.Join` are explicit scheduling points. IL2LLVM also inserts an automatic yield check at IL loop back edges so a managed loop cannot permanently starve other green threads. The hot check uses only a static integer counter and does not create a GC frame; it enters the object-aware scheduler only when the scheduling interval expires.

`Task` support uses the same cooperative execution model and does not imply a native thread pool. A native timer, signal handler, interrupt handler, or worker thread must not call into managed code concurrently or inject a managed callback at an arbitrary instruction. Native event sources must queue work for the single native execution thread, which can then complete a task or resume a green thread at a normal managed scheduling boundary.

### Minimal `TaskCompletionSource<T>` example

Use `TaskCompletionSource<T>` when a C event source will produce a value later.

```csharp
using System;
using System.Runtime;
using System.Threading.Tasks;

internal static class Program
{
    static TaskCompletionSource<int> pending;

    static Task<int> ReadAsync()
    {
        pending = new TaskCompletionSource<int>();
        return pending.Task;
    }

    static async Task RunAsync()
    {
        int value = await ReadAsync();
        Console.WriteLine(value);
    }

    [RuntimeExport("OnValue")]
    static void OnValue(int value)
    {
        TaskCompletionSource<int> current = pending;
        pending = null;
        current?.SetResult(value);
    }

    // This is the managed entry point called by native code.
    static void Main()
    {
        _ = RunAsync();
    }
}
```

The native side can call the exported method when its event is ready:

```c
// These symbols are generated/provided by the managed object.
extern void managed_Main(void);
extern void OnValue(int value);

static void process_event(void)
{
    OnValue(42);
}

int main(void)
{
    managed_Main();  // RunAsync reaches await and returns here.
    process_event(); // Completes the TaskCompletionSource.
    return 0;
}
```

The program prints `42`. `managed_Main()` starts `RunAsync()` and returns when it reaches `await`; `OnValue(42)` then calls `SetResult`, which resumes the `await`. In a real host, `process_event` would be an event-loop step. The callback must run on the same managed/event-loop thread; an interrupt or native worker must queue the event instead of calling managed code directly. A source should be completed only once.

The `Monitor.Enter` and `Monitor.Exit` host hooks do not make the runtime safe for concurrent native execution. Within the green-thread scheduler they also suppress context switches while a monitor region is active.

## Console host

Build the managed console input first:

```powershell
dotnet build ConsoleAppExample\ConsoleAppExample.csproj
```

Generate an object with one of the console launch profiles or with the command above. Link that object with `apphost/apphost.c`, `apphost/Runtime.c`, and a native toolchain for the selected target. `apphost` calls `managed_Main` directly; it does not start `dotnet` or load a CLR.

For a Linux x86-64 user-mode executable, select `Build ConsoleAppExample(Linux x86_64)` or generate the object with `x86_64-unknown-linux-gnu`, then run:

```sh
cd apphost
make
./ConsoleAppExample
```

The Makefile links the existing object with `Runtime.c`. It does not build the managed project or run IL2LLVM.

## Linux kernel module

`LinuxKernelModuleExample` is an x86-64 example. It calls `managed_Main` from the module init function and supplies the runtime boundary in `Runtime.c` and `runtime_jump_x86_64.S`.

Generate the managed object using the `kernel` code model:

```powershell
dotnet build LinuxKernelModuleExample\LinuxKernelModuleExample.csproj
dotnet IL2LLVM\bin\Debug\net10.0\IL2LLVM.dll `
  LinuxKernelModuleExample\bin\Debug\net10.0\LinuxKernelModuleExample.dll `
  LinuxKernelModuleExample\bin\Debug\net10.0\LinuxKernelModuleExample.obj `
  "x86_64-unknown-linux-gnu;kernel"
```

Then, in the Linux environment that has headers for the target kernel:

```sh
cd LinuxKernelModuleExample
make KDIRS=/lib/modules/$(uname -r)/build
sudo insmod my_module.ko
dmesg | tail -n 30
sudo rmmod my_module
```

The `kernel` code model is required because modules are loaded in the high kernel address range. It emits signed 32-bit and other kernel-supported relocations instead of `R_X86_64_32` or GOT-relative relocations that the Linux 5.4 module loader rejects.

This example is not portable to another architecture without a matching native host, exception-transfer implementation, target triple, and code-model choice. It also must be built against headers compatible with the kernel that loads it.

## ESP32-S3

`ESP32S3Example` contains an Arduino sketch and its native runtime boundary.
The `Build ESP32S3Example(Xtensa)` launch profile emits
`ESP32S3Example/ESP32S3Example.S`. Arduino can compile the assembly source when
the Xtensa assembler is configured with `-Wa,--text-section-literals`.
Other output paths continue to produce one relocatable object; IL2LLVM does not
create archives.

Run `ESP32S3Example/ConfigureArduinoXtensa.bat` once to locate the installed
ESP32 Arduino core and create or update its `platform.local.txt` with
`-Wa,--text-section-literals`. The script preserves the core's existing
assembly flags and does not modify `platform.txt`. Restart Arduino IDE after
running the script so it reloads the platform configuration.

The launch profile enables LLVM's `+windowed` Xtensa feature so generated code
uses the same windowed ABI as the ESP32 Arduino toolchain.

## Scope and limitations

- IL2LLVM translates methods with bodies in the input assembly. It does not link arbitrary .NET framework assemblies.
- Unsupported IL or unresolved managed methods stop translation with an error; they are not silently replaced by runtime stubs.
- CoreLib green threads share one native execution thread. Concurrent or asynchronously injected managed execution remains unsupported, including callbacks entered from native timer or worker threads.
- There is no automatic executable or module linker step in the MSBuild targets. Object generation and native linking are separate steps.
- Linux kernel code must not rely on the C standard library. The kernel example provides its own implementations for the external symbols it uses.
- Native runtime code is target-specific by design; CoreLib and IL2LLVM do not select runtime layouts or exception buffers from the target triple.
