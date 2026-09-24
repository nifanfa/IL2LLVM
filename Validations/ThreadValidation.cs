using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

internal static class ThreadValidation
{
    private static volatile int s_firstReady;
    private static volatile int s_secondReady;
    private static volatile int s_rootSurvived;
    private static volatile int s_automaticWaiterReady;
    private static volatile int s_automaticRelease;
    private static volatile int s_monitorOwnerInside;
    private static volatile int s_monitorContenderStarted;
    private static volatile int s_monitorContenderEntered;
    private static volatile int s_monitorFailure;

    internal static void Run()
    {
        ValidateYieldAndPreciseRoots();
        ValidateAutomaticYield();
        ValidateSleep();
        ValidateMonitor();
        ValidateUnsafeMemory();
        Console.WriteLine("Thread validation passed.");
    }

    private static void ValidateYieldAndPreciseRoots()
    {
        s_firstReady = 0;
        s_secondReady = 0;
        s_rootSurvived = 0;

        Thread collector = new Thread(() =>
        {
            while (s_firstReady == 0)
                Thread.Yield();
            GC.Collect();
            s_secondReady = 1;
        });
        Thread owner = new Thread(() =>
        {
            string stackRoot = new string(new[] { 'r', 'o', 'o', 't' });
            s_firstReady = 1;
            while (s_secondReady == 0)
                Thread.Yield();
            if (stackRoot == "root")
                s_rootSurvived = 1;
        });

        collector.Start();
        owner.Start();
        owner.Join();
        collector.Join();
        if (s_rootSurvived == 0)
            throw new Exception("Thread validation failed: precise suspended root.");
    }

    private static void ValidateAutomaticYield()
    {
        s_automaticWaiterReady = 0;
        s_automaticRelease = 0;
        Thread release = new Thread(() => s_automaticRelease = 1);
        Thread waiter = new Thread(() =>
        {
            s_automaticWaiterReady = 1;
            while (s_automaticRelease == 0)
            {
            }
        });

        release.Start();
        waiter.Start();
        waiter.Join();
        release.Join();
        if (s_automaticWaiterReady == 0 || s_automaticRelease == 0)
            throw new Exception("Thread validation failed: automatic yield.");
    }

    private static void ValidateSleep()
    {
        int completed = 0;
        Thread sleeper = new Thread(() =>
        {
            Thread.Sleep(1);
            completed = 1;
        });
        sleeper.Start();
        sleeper.Join();
        if (completed != 1)
            throw new Exception("Thread validation failed: sleep.");
    }

    private static void ValidateMonitor()
    {
        object sync = new object();
        s_monitorOwnerInside = 0;
        s_monitorContenderStarted = 0;
        s_monitorContenderEntered = 0;
        s_monitorFailure = 0;

        Thread contender = new Thread(() =>
        {
            s_monitorContenderStarted = 1;
            lock (sync)
            {
                if (s_monitorOwnerInside != 0)
                    s_monitorFailure = 1;
                s_monitorContenderEntered = 1;
            }
        });
        Thread owner = new Thread(() =>
        {
            lock (sync)
            {
                s_monitorOwnerInside = 1;
                lock (sync)
                {
                    contender.Start();
                }

                while (s_monitorContenderStarted == 0)
                    Thread.Yield();
                Thread.Sleep(1);
                if (s_monitorContenderEntered != 0)
                    s_monitorFailure = 1;
                s_monitorOwnerInside = 0;
            }
        });

        owner.Start();
        owner.Join();
        contender.Join();
        if (s_monitorFailure != 0 || s_monitorContenderEntered == 0)
            throw new Exception("Thread validation failed: monitor ownership or recursion.");

        object exceptionSync = new object();
        try
        {
            lock (exceptionSync)
                throw new InvalidOperationException("Monitor release validation.");
        }
        catch (InvalidOperationException)
        {
        }

        lock (exceptionSync)
        {
        }

        bool invalidExitCaught = false;
        try
        {
            Monitor.Exit(exceptionSync);
        }
        catch (SynchronizationLockException)
        {
            invalidExitCaught = true;
        }
        if (!invalidExitCaught)
            throw new Exception("Thread validation failed: monitor ownership check.");
    }

    private static unsafe void ValidateUnsafeMemory()
    {
        const uint length = 16;
        byte* source = stackalloc byte[(int)length];
        IntPtr allocation = Marshal.AllocHGlobal((nint)length);
        if (allocation == IntPtr.Zero)
            throw new Exception("Thread validation failed: unmanaged allocation.");

        byte* destination = (byte*)(void*)allocation;
        Unsafe.InitBlock(source, 0x5a, length);
        source[7] = 0xa5;
        Unsafe.InitBlock(destination, 0, length);
        Unsafe.CopyBlock(destination, source, length);
        for (int index = 0; index < length; index++)
        {
            byte expected = index == 7 ? (byte)0xa5 : (byte)0x5a;
            if (destination[index] != expected)
                throw new Exception("Thread validation failed: unsafe memory block operation.");
        }
        Marshal.FreeHGlobal(allocation);
    }
}
