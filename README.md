# IL2LLVM

![Logo](Logo.svg)

IL2LLVM translates a managed assembly built with this repository's CoreLib into a native object file through LLVM. It is not a .NET runtime, NativeAOT frontend, or a general-purpose replacement for the .NET SDK.

<img alt="QQ_1789867398271" src="https://github.com/user-attachments/assets/541d775a-f146-4a9f-a00a-24d826574926" />  
<img alt="image (3)" src="https://github.com/user-attachments/assets/4031dbc3-0b7b-470e-8807-6ff9aa4ca4fa" />  
<img alt="新建项目" src="https://github.com/user-attachments/assets/79fcbf6f-8310-4cbc-927c-9045caa576ff" />  

> Running on Mac OS, Linux, Windows, ESP32-S3(WAVESHARE ESP32-S3-Touch-LCD-2, Screen: 320x240 LT7789, Touch: CST816)  

## Project purpose

The purpose of this project is to support any processor architecture for which LLVM can emit an object file. IL2LLVM does not contain x86, ARM, Windows, Linux, or kernel-specific translation logic. The project supplies the LLVM target triple and the native host supplies the ABI-dependent entry point, exception transfer, and linker configuration.

The managed runtime is deliberately small. A user-mode host only needs a small ISO C library surface:

- `malloc`
- `free`
- `memcpy`
- `memset`
- `abort`
- `printf` or an equivalent text output function
- `wprintf` or an equivalent UTF-16 output function when character output is used

The following symbols are the runtime boundary implemented by the host. They are not platform APIs and can be implemented for the target processor and environment:

- `setjmp`
- `longjmp`
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

Platform-specific operations remain external. Methods marked with `[DllImport("*")]` are native symbols. The final host must provide every imported symbol that the managed program reaches. Examples include allocation, deallocation, block memory operations, non-local exception transfer, abort, console output, and wall-clock time. GC, exception frame tracking, and green-thread synchronization are implemented in `CoreLib`.

### Native arguments and callbacks

Unlike CLR P/Invoke, IL2LLVM does not marshal managed `string` or array arguments into native character or element pointers. A `[DllImport("*")]` signature must describe the actual native ABI; passing a `string` or `T[]` directly passes a managed object reference, not its contents. Use the `ByReference<T>` implicit conversions in `CoreLib` to pass a pointer to the first element instead: `string` converts to `ByReference<char>` (UTF-16 characters), and `T[]` converts to `ByReference<T>`. For example, the `Console.WriteLine(ByReference<char>)` import accepts a string through that conversion. Match the native character width, provide a length when needed, and note that empty strings or arrays convert to a null reference; arrays do not acquire a terminator automatically. Keep the underlying managed data alive for the duration of the native call.

Do not pass a managed `Delegate` object or its raw function pointer directly as an unmanaged callback. Delegate invocation supplies the bound target (`this`) as a leading argument, but a native caller does not supply that argument automatically; even static-method delegate thunks use this internal calling shape. The resulting signature mismatch is unsafe. Use a callback with a matching unmanaged function-pointer signature (such as a suitable static `delegate* unmanaged<...>` entry point), or write an explicit native/managed trampoline that passes the target context and manages its lifetime. Native callbacks must also respect the single-native-thread restriction described below.

The built-in collector uses GC descriptors emitted by IL2LLVM and registers static fields as roots. It is not a replacement for the host allocator: `Marshal.AllocHGlobal` and `FreeHGlobal` import `malloc` and `free`, while new managed allocations are cleared through `Unsafe.InitBlock`. Block copies use `Unsafe.CopyBlock`; these methods import `memset` and `memcpy` respectively.

`System.Threading.Monitor.Enter` and `Exit` are implemented entirely in `CoreLib`. A managed side table records the lock object, owning green thread, and recursion count. Contending green threads yield until the owner releases the object; recursive entry by the owner is supported, and an invalid `Exit` throws `SynchronizationLockException`. No native monitor hook or atomic instruction is needed while all managed execution remains on one native thread.

### Cooperative green threads

`System.Threading.Thread` implements stackful green threads on one native execution thread. A context switch saves the active stack with `setjmp` and `memcpy`, snapshots its precise GC roots, restores another saved stack, and resumes it with `longjmp`. The collector remains precise and non-moving; suspended threads are marked from their root snapshots rather than by conservatively scanning copied stack bytes.

Scheduling is cooperative, not parallel. `Thread.Yield`, `Thread.Sleep`, and `Thread.Join` are explicit scheduling points. IL2LLVM also inserts `Thread.Yield(true)` at IL loop back edges so a managed loop cannot permanently starve other green threads. The generated-call path uses only a static integer counter until the scheduling interval expires; explicit `Thread.Yield()` calls pass the default `false` value and attempt to switch immediately.

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

Managed monitors do not make the runtime safe for concurrent native execution. They rely on the cooperative scheduler's single native execution thread: monitor table updates briefly suppress context switches, while code holding a monitor may otherwise yield or sleep. A contender for the same object waits, but green threads using unrelated monitor objects can continue running.

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

`ESP32S3LVGLExample` uses the touch display for two LVGL screens: brightness
controls and device information. Tap **About** or **Brightness** at the bottom
to switch between them; the brightness setting remains unchanged when switching.
Regenerate `ESP32S3LVGLExample/ESP32S3LVGLExample.S` with the
`Build ESP32S3LVGLExample(Xtensa)` launch profile before building the Arduino sketch.

The two layouts live in `ESP32S3LVGLExample/BrightnessPage.xaml` and
`ESP32S3LVGLExample/AboutPage.xaml`. The example includes `*.xaml` as
`AdditionalFiles` for `LVGLXAMLGenerator`, a Roslyn incremental source
generator. The compiler generates and compiles a `.xaml.g.cs` for each page;
new pages need no per-page project edits. Generated sources appear under the
analyzer's generated files in Visual Studio, rather than beside the XAML.
This is a small LVGL-specific XAML subset, not WPF XAML: `<Screen>` declares
`Class` and `Method` and contains `Object`, `Label`, `Button`, `Checkbox`,
`Switch`, `Bar`, `Slider`, `Arc`, `Dropdown`, `Roller`, `TextArea`, or `Table`.
`Table` also supports `Column` and `Cell` child elements. Nesting sets the LVGL parent;
`Name` gives a widget a name for `RelativeTo`/`EventData`, and
`Field="true"` exposes it as a static field in the partial class.
Sizes, alignment, padding, text, ranges, values, flags, and callbacks use
the attributes shown in the example pages. The generated method takes
`(LVObject screen, LVObject navigationTarget)`; `On` names an existing
`[UnmanagedCallersOnly]` static callback, `Filter` names a supported LVGL event,
and `EventData` passes a named widget's handle or `navigationTarget.Handle`.
Unsupported controls or attributes fail generation instead of being ignored.
The pages specify the local `LVGLXAMLGenerator/LVGLPage.xsd` directly with
`xsi:noNamespaceSchemaLocation`; no LVGL XML namespace or IDE-specific schema
selection is needed. The `xmlns:xsi` value is an XML identifier, not a network
request. Open `.xaml` with Visual Studio's XML editor rather than its WPF
designer for XSD completions.

## Scope and limitations

- IL2LLVM translates methods with bodies in the input assembly. It does not link arbitrary .NET framework assemblies.
- Unsupported IL or unresolved managed methods stop translation with an error; they are not silently replaced by runtime stubs.
- CoreLib green threads share one native execution thread. Concurrent or asynchronously injected managed execution remains unsupported, including callbacks entered from native timer or worker threads.
- There is no automatic executable or module linker step in the MSBuild targets. Object generation and native linking are separate steps.
- Linux kernel code must not rely on the C standard library. The kernel example provides its own implementations for the external symbols it uses.
- Native runtime code is target-specific by design; CoreLib and IL2LLVM do not select runtime layouts or exception buffers from the target triple.
