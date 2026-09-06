using System.Runtime.InteropServices;

namespace System
{
    public class Object
    {
        private IntPtr m_pMethodTable;
        private unsafe GCDesc* m_pGCDesc;

        public Object() { }

        public virtual bool Equals(object other) => this == other;
        public static bool ReferenceEquals(object left, object right) => left == right;
        public static bool Equals(object left, object right) => left == null ? right == null : left.Equals(right);
        public virtual int GetHashCode() => 1;
        public virtual string ToString() => GetType().FullName;
        public Type GetType() => Type.GetTypeFromHandle(default);
    }

    public unsafe struct GCDesc
    {
        public IntPtr TotalSlotCount;
        public IntPtr BaseSize;
        public IntPtr FixedReferenceCount;
        public IntPtr ArrayLengthOffset;
        public IntPtr ArrayElementSize;
        public IntPtr ArrayElementReferenceCount;
        public fixed ushort FixedReferenceOffsets[1];
    }

    public struct Void { }
    public struct Boolean { }
    public struct Char { }
    public struct SByte { }
    public struct Byte { }
    public struct Int16 { }
    public struct UInt16 { }
    public struct Int32
    {
        public const int MinValue = -2147483648;
        public const int MaxValue = 2147483647;
    }
    public struct UInt32 { }
    public struct Int64 { }
    public struct UInt64 { }
    public struct IntPtr { }
    public struct UIntPtr { }
    public struct Single { }
    public struct Double { }

    public abstract class ValueType : Object { }
    public abstract class Enum : ValueType { }
    public struct Nullable<T> where T : struct
    {
        private bool _hasValue;
        private T _value;
        public Nullable(T value) { _value = value; _hasValue = true; }
        public bool HasValue => _hasValue;
        public T Value => _hasValue ? _value : throw new InvalidOperationException();
        public T GetValueOrDefault() => _value;
        public T GetValueOrDefault(T defaultValue) => _hasValue ? _value : defaultValue;
        public static implicit operator Nullable<T>(T value) => new Nullable<T>(value);
        public static explicit operator T(Nullable<T> value) => value.Value;
    }
    public abstract class Array
    {
        public int Length;
        private int _rank;
        private int _length0;
        private int _length1;
        private int _length2;

        public virtual int Rank => _rank;
        public virtual int GetLength(int dimension) => dimension == 0 ? _length0 : dimension == 1 ? _length1 : _length2;
        public virtual int GetLowerBound(int dimension) => 0;
        public virtual int GetUpperBound(int dimension) => GetLength(dimension) - 1;
    }

    public sealed class ArrayEnumerator<T> : Object, System.Collections.Generic.IEnumerator<T>
    {
        private T[] _array;
        private int _index = -1;

        public ArrayEnumerator(T[] array) { _array = array; }
        public T Current => _array[_index];
        object System.Collections.IEnumerator.Current => Current;
        public bool MoveNext() => ++_index < _array.Length;
        public void Reset() { _index = -1; }
        public void Dispose() { }
    }

    public sealed class String : Object
    {
        public int Length;
        private char[] _chars;

        public String() { }
        public String(char[] value)
        {
            _chars = value;
            Length = value == null ? 0 : value.Length;
        }

        public char this[int index] => _chars[index];
        public override string ToString() => this;
        public override bool Equals(object other) => other is string value && Equals(this, value);
        public override int GetHashCode() => (int)Length;
        public static bool Equals(string left, string right)
        {
            if (ReferenceEquals(left, null) || ReferenceEquals(right, null))
                return ReferenceEquals(left, right);
            if (left.Length != right.Length)
                return false;
            for (int index = 0; index < left.Length; index++)
                if (left[index] != right[index])
                    return false;
            return true;
        }
        public static bool operator ==(string left, string right) => Equals(left, right);
        public static bool operator !=(string left, string right) => !Equals(left, right);
        public static string Concat(string left, string right)
        {
            if (ReferenceEquals(left, null))
                return right;
            if (ReferenceEquals(right, null))
                return left;
            char[] value = new char[left.Length + right.Length];
            for (int index = 0; index < left.Length; index++)
                value[index] = left[index];
            for (int index = 0; index < right.Length; index++)
                value[left.Length + index] = right[index];
            return new string(value);
        }
    }

    public class Exception : Object
    {
        public string Message;
        public Exception() { }
        public Exception(string message) { Message = message; }
    }

    public class NotSupportedException : Exception
    {
        public NotSupportedException() { }
        public NotSupportedException(string message) : base(message) { }
    }

    public class ArgumentException : Exception
    {
        public ArgumentException() { }
        public ArgumentException(string message) : base(message) { }
    }

    public class ArgumentNullException : ArgumentException
    {
        public ArgumentNullException() { }
        public ArgumentNullException(string message) : base(message) { }
    }

    public class OperationCanceledException : Exception
    {
        public OperationCanceledException() { }
        public OperationCanceledException(string message) : base(message) { }
    }

    public class IndexOutOfRangeException : Exception
    {
        public IndexOutOfRangeException() { }
        public IndexOutOfRangeException(string message) : base(message) { }
    }

    public class InvalidProgramException : Exception
    {
        public InvalidProgramException() { }
        public InvalidProgramException(string message) : base(message) { }
    }

    public class OverflowException : Exception
    {
        public OverflowException() { }
        public OverflowException(string message) : base(message) { }
    }

    public class InvalidCastException : Exception
    {
        public InvalidCastException() { }
        public InvalidCastException(string message) : base(message) { }
    }

    public class NullReferenceException : Exception
    {
        public NullReferenceException() { }
        public NullReferenceException(string message) : base(message) { }
    }

    public class InvalidOperationException : Exception
    {
        public InvalidOperationException() { }
        public InvalidOperationException(string message) : base(message) { }
    }

    public interface IDisposable
    {
        void Dispose();
    }

    public delegate void Action();
    public delegate void Action<T>(T arg);
    public delegate TResult Func<TResult>();
    public delegate TResult Func<T, TResult>(T arg);
    public delegate TResult Func<T1, T2, TResult>(T1 arg1, T2 arg2);

    public class Delegate : Object
    {
        private IntPtr _function;
        private object _target;
        public static Delegate Combine(Delegate left, Delegate right) => right ?? left;
        public static Delegate Remove(Delegate source, Delegate value) => source;
    }
    public class MulticastDelegate : Delegate { }

    public sealed class Type : Object
    {
        public string Name;
        public string Namespace;
        public string FullName;

        internal Type(string name, string @namespace, string fullName)
        {
            Name = name;
            Namespace = @namespace;
            FullName = fullName;
        }

        public static Type GetTypeFromHandle(RuntimeTypeHandle handle) => handle.Type;
    }

    public struct RuntimeTypeHandle
    {
        internal Type Type;
    }

    public class Attribute { }
    public enum AttributeTargets { }
    public sealed class AttributeUsageAttribute : Attribute
    {
        public AttributeUsageAttribute(AttributeTargets validOn) { }
        public bool AllowMultiple { get; set; }
        public bool Inherited { get; set; }
    }

    public sealed class ParamArrayAttribute : Attribute { }
    public sealed class Console
    {
        [DllImport("*")]
        public static extern void Write([MarshalAs(UnmanagedType.LPWStr)] string value);
        [DllImport("*")]
        public static extern void WriteLine([MarshalAs(UnmanagedType.LPWStr)] string value);
        [DllImport("*")]
        public static extern void WriteLine(int value);
        [DllImport("*")]
        public static extern void WriteLine(IntPtr value);
    }
}

namespace System.Runtime.InteropServices
{
    public enum UnmanagedType
    {
        LPWStr = 21
    }

    public sealed class DllImportAttribute : Attribute
    {
        public DllImportAttribute(string dllName) { }
    }

    public sealed class MarshalAsAttribute : Attribute
    {
        public MarshalAsAttribute(UnmanagedType unmanagedType)
        {
            Value = unmanagedType;
        }

        public UnmanagedType Value { get; }
    }
}

namespace System.Runtime.CompilerServices
{
    public sealed class CompilerGeneratedAttribute : Attribute { }
    public sealed class IsExternalInit { }
    public sealed class IsVolatile { }
    public sealed class MethodImplAttribute : Attribute
    {
        public MethodImplAttribute(MethodImplOptions options) { }
    }
    public enum MethodImplOptions
    {
        NoInlining = 8,
        AggressiveInlining = 256
    }

    public sealed class ExtensionAttribute : Attribute { }
    public class StateMachineAttribute : Attribute
    {
        public StateMachineAttribute(Type type) { }
    }
    public sealed class AsyncStateMachineAttribute : StateMachineAttribute
    {
        public AsyncStateMachineAttribute(Type type) : base(type) { }
    }
    public sealed class AsyncMethodBuilderAttribute : Attribute
    {
        public AsyncMethodBuilderAttribute(Type type) { }
    }
    public sealed class IteratorStateMachineAttribute : StateMachineAttribute
    {
        public IteratorStateMachineAttribute(Type type) : base(type) { }
    }
    public interface IAsyncStateMachine
    {
        void MoveNext();
        void SetStateMachine(IAsyncStateMachine stateMachine);
    }
    public struct AsyncTaskMethodBuilder
    {
        private System.Threading.Tasks.Task _task;
        public static AsyncTaskMethodBuilder Create() => new AsyncTaskMethodBuilder { _task = new System.Threading.Tasks.Task() };
        public System.Threading.Tasks.Task Task => _task;
        public void SetStateMachine(IAsyncStateMachine stateMachine) { }
        public void SetResult() { _task?.SetResult(); }
        public void SetException(System.Exception exception) { _task?.SetException(exception); }
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine => stateMachine.MoveNext();
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
            => awaiter.UnsafeOnCompleted(stateMachine.MoveNext);
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
            => awaiter.OnCompleted(stateMachine.MoveNext);
    }
    public struct AsyncTaskMethodBuilder<TResult>
    {
        private System.Threading.Tasks.Task<TResult> _task;
        public static AsyncTaskMethodBuilder<TResult> Create() => new AsyncTaskMethodBuilder<TResult> { _task = new System.Threading.Tasks.Task<TResult>() };
        public System.Threading.Tasks.Task<TResult> Task => _task;
        public void SetStateMachine(IAsyncStateMachine stateMachine) { }
        public void SetResult(TResult result) { _task?.SetResult(result); }
        public void SetException(System.Exception exception) { _task?.SetException(exception); }
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine => stateMachine.MoveNext();
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
            => awaiter.UnsafeOnCompleted(stateMachine.MoveNext);
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
            => awaiter.OnCompleted(stateMachine.MoveNext);
    }
    public interface INotifyCompletion
    {
        void OnCompleted(Action continuation);
    }
    public interface ICriticalNotifyCompletion : INotifyCompletion
    {
        void UnsafeOnCompleted(Action continuation);
    }
}

namespace System.Runtime
{
    public sealed class RuntimeExportAttribute : Attribute
    {
        public RuntimeExportAttribute(string name) { }
    }
}

namespace System.Reflection
{
    public sealed class DefaultMemberAttribute : Attribute
    {
        public DefaultMemberAttribute(string memberName) { }
    }
}

namespace System.Collections
{
    public interface IEnumerator
    {
        bool MoveNext();
        object Current { get; }
        void Reset();
    }

    public interface IEnumerable
    {
        IEnumerator GetEnumerator();
    }
}

namespace System.Collections.Generic
{
    public interface IEnumerator<out T> : IDisposable, IEnumerator
    {
        new T Current { get; }
    }

    public interface IEnumerable<out T> : IEnumerable
    {
        new IEnumerator<T> GetEnumerator();
    }

    public class List<T> : Object, IEnumerable<T>
    {
        private T[] _items;
        private int _count;

        public List()
        {
            _items = new T[4];
        }

        public int Count => _count;
        public T this[int index]
        {
            get => _items[index];
            set => _items[index] = value;
        }

        public void Add(T value)
        {
            if (_count == _items.Length)
            {
                T[] expanded = new T[_items.Length * 2];
                for (int index = 0; index < _count; index++)
                    expanded[index] = _items[index];
                _items = expanded;
            }
            _items[_count++] = value;
        }

        public IEnumerator<T> GetEnumerator() => new Enumerator(this);
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class Enumerator : Object, IEnumerator<T>
        {
            private readonly List<T> _list;
            private int _index = -1;
            public Enumerator(List<T> list) { _list = list; }
            public T Current => _list[_index];
            object System.Collections.IEnumerator.Current => Current;
            public bool MoveNext() => ++_index < _list.Count;
            public void Reset() { _index = -1; }
            public void Dispose() { }
        }
    }
}

namespace System.Linq
{
    using System.Collections.Generic;

    public static class Enumerable
    {
        public static System.Collections.Generic.IEnumerable<TResult> Select<TSource, TResult>(this System.Collections.Generic.IEnumerable<TSource> source, Func<TSource, TResult> selector)
        {
            List<TResult> result = new List<TResult>();
            IEnumerator<TSource> iterator = source.GetEnumerator();
            while (iterator.MoveNext())
                result.Add(selector(iterator.Current));
            return result;
        }

        public static System.Collections.Generic.IEnumerable<TSource> Where<TSource>(this System.Collections.Generic.IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            List<TSource> result = new List<TSource>();
            IEnumerator<TSource> iterator = source.GetEnumerator();
            while (iterator.MoveNext())
                if (predicate(iterator.Current))
                    result.Add(iterator.Current);
            return result;
        }

        public static TSource[] ToArray<TSource>(this System.Collections.Generic.IEnumerable<TSource> source)
        {
            List<TSource> result = new List<TSource>();
            IEnumerator<TSource> iterator = source.GetEnumerator();
            while (iterator.MoveNext())
                result.Add(iterator.Current);
            TSource[] values = new TSource[result.Count];
            for (int index = 0; index < result.Count; index++)
                values[index] = result[index];
            return values;
        }

        public static int Count<TSource>(this System.Collections.Generic.IEnumerable<TSource> source)
        {
            int count = 0;
            IEnumerator<TSource> iterator = source.GetEnumerator();
            while (iterator.MoveNext())
                count++;
            return count;
        }

        public static TSource First<TSource>(this System.Collections.Generic.IEnumerable<TSource> source)
        {
            IEnumerator<TSource> iterator = source.GetEnumerator();
            if (iterator.MoveNext())
                return iterator.Current;
            throw new InvalidOperationException();
        }
    }
}

namespace System.Threading
{
    public static class Monitor
    {
        public static void Enter(object value) { }
        public static void Exit(object value) { }
    }
}

namespace System.Threading.Tasks
{
    public class Task : Object
    {
        private bool _completed;
        private Exception _exception;
        private Action _continuation;
        public bool IsCompleted => _completed;
        public TaskAwaiter GetAwaiter() => new TaskAwaiter(this);
        public ConfiguredTaskAwaitable ConfigureAwait(bool continueOnCapturedContext) => new ConfiguredTaskAwaitable(this);
        public void SetResult() { _completed = true; _continuation?.Invoke(); }
        public void SetException(Exception exception) { _exception = exception; _completed = true; _continuation?.Invoke(); }
        internal void OnCompleted(Action continuation)
        {
            if (_completed) continuation();
            else _continuation += continuation;
        }
        internal void GetResult()
        {
            if (_exception != null) throw _exception;
        }
        public static Task FromResult() { Task task = new Task(); task.SetResult(); return task; }
        public static Task<TResult> FromResult<TResult>(TResult result) { Task<TResult> task = new Task<TResult>(); task.SetResult(result); return task; }
    }

    public class Task<TResult> : Task
    {
        private TResult _result;
        public new TaskAwaiter<TResult> GetAwaiter() => new TaskAwaiter<TResult>(this);
        public new ConfiguredTaskAwaitable<TResult> ConfigureAwait(bool continueOnCapturedContext) => new ConfiguredTaskAwaitable<TResult>(this);
        public void SetResult(TResult result) { _result = result; base.SetResult(); }
        public TResult GetResult() { base.GetResult(); return _result; }
        public static Task<TResult> FromResult(TResult result) { Task<TResult> task = new Task<TResult>(); task.SetResult(result); return task; }
    }

    public class TaskCompletionSource : Object
    {
        private readonly Task _task = new Task();
        public Task Task => _task;
        public void SetResult() => _task.SetResult();
        public void SetException(Exception exception) => _task.SetException(exception);
    }

    public class TaskCompletionSource<TResult> : Object
    {
        private readonly Task<TResult> _task = new Task<TResult>();
        public Task<TResult> Task => _task;
        public void SetResult(TResult result) => _task.SetResult(result);
        public void SetException(Exception exception) => _task.SetException(exception);
    }

    public struct TaskAwaiter : System.Runtime.CompilerServices.ICriticalNotifyCompletion
    {
        private readonly Task _task;
        public TaskAwaiter(Task task) { _task = task; }
        public bool IsCompleted => _task.IsCompleted;
        public void GetResult() => _task.GetResult();
        public void OnCompleted(Action continuation) => _task.OnCompleted(continuation);
        public void UnsafeOnCompleted(Action continuation) => _task.OnCompleted(continuation);
    }

    public struct TaskAwaiter<TResult> : System.Runtime.CompilerServices.ICriticalNotifyCompletion
    {
        private readonly Task<TResult> _task;
        public TaskAwaiter(Task<TResult> task) { _task = task; }
        public bool IsCompleted => _task.IsCompleted;
        public TResult GetResult() => _task.GetResult();
        public void OnCompleted(Action continuation) => _task.OnCompleted(continuation);
        public void UnsafeOnCompleted(Action continuation) => _task.OnCompleted(continuation);
    }

    public struct ConfiguredTaskAwaitable
    {
        private readonly Task _task;
        public ConfiguredTaskAwaitable(Task task) { _task = task; }
        public TaskAwaiter GetAwaiter() => _task.GetAwaiter();
    }

    public struct ConfiguredTaskAwaitable<TResult>
    {
        private readonly Task<TResult> _task;
        public ConfiguredTaskAwaitable(Task<TResult> task) { _task = task; }
        public TaskAwaiter<TResult> GetAwaiter() => _task.GetAwaiter();
    }
}
