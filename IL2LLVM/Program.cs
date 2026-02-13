using LLVMSharp.Interop;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using System.Diagnostics;

string fileName = "../../../../ConsoleApp1/bin/Debug/net10.0/ConsoleApp1.dll";

// 初始化 LLVM
LLVM.InitializeAllTargetInfos();
LLVM.InitializeAllTargets();
LLVM.InitializeAllTargetMCs();
LLVM.InitializeAllAsmParsers();
LLVM.InitializeAllAsmPrinters();

var context = LLVMContextRef.Create();
var module = context.CreateModuleWithName(Path.GetFileNameWithoutExtension(fileName));

// 设置目标三元组
module.Target = "i386-pc-windows-msvc";

// 创建目标机器
var target = LLVMTargetRef.GetTargetFromTriple(module.Target);
var machine = target.CreateTargetMachine(module.Target, "generic", "", LLVMCodeGenOptLevel.LLVMCodeGenLevelDefault,
                                         LLVMRelocMode.LLVMRelocDefault, LLVMCodeModel.LLVMCodeModelDefault);

int pointerSize = (int)machine.CreateTargetDataLayout().ABISizeOfType(LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0));
LLVMTypeRef sizeType = LLVMTypeRef.CreateIntPtr(machine.CreateTargetDataLayout());

Dictionary<string, Tuple<LLVMValueRef, LLVMTypeRef, MethodReference, Collection<Instruction>?>> moduleMethods = new();
Dictionary<RuntimeMethod, Tuple<LLVMValueRef, LLVMTypeRef>> runtimeMethods = new();
Dictionary<string, Tuple<LLVMValueRef, LLVMTypeRef>> staticFields = new();

{
    var funcType = LLVMTypeRef.CreateFunction(LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0), [sizeType]);
    var funcValue = module.AddFunction(RuntimeMethod.Newobj.ToString(), funcType);
    runtimeMethods.Add(RuntimeMethod.Newobj, new(funcValue, funcType));
}

{
    var funcType = LLVMTypeRef.CreateFunction(LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0), [sizeType, sizeType]);
    var funcValue = module.AddFunction(RuntimeMethod.Newarr.ToString(), funcType);
    runtimeMethods.Add(RuntimeMethod.Newarr, new(funcValue, funcType));
}

{
    // 加载程序集
    var assembly = AssemblyDefinition.ReadAssembly(fileName);

    // 遍历所有类型和方法
    foreach (TypeDefinition type in assembly.MainModule.Types)
    {
        var fields = type.Fields;
        if (fields.Any())
        {
            foreach (var field in fields)
            {
                if (field.IsStatic)
                {
                    string fieldName = GetFriendlyFieldName(field);
                    var fieldType = GetLLVMTypeRefFromMetadataType(field.DeclaringType.MetadataType);
                    var fieldValue = module.AddGlobal(LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0), fieldName);
                    fieldValue.Initializer = LLVMValueRef.CreateConstNull(LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0));

                    staticFields.Add(fieldName, new(fieldValue, fieldType));
                }
            }
        }

        foreach (MethodDefinition method in type.Methods)
        {
            if (!method.HasBody) continue;
            RegisterMethodFunction(module, method, method.Body.Instructions);

            foreach (var instr in method.Body.Instructions)
            {
                switch (instr.OpCode.Code)
                {
                    case Code.Call:
                    case Code.Calli:
                    case Code.Callvirt:
                    case Code.Newobj:
                        MethodReference targetMethod = (MethodReference)instr.Operand;
                        var resolved = targetMethod.Resolve();
                        // Shallow method resolve
                        // Methods from other modules are considered external methods.
                        RegisterMethodFunction(module, targetMethod, resolved.Module != assembly.MainModule ? new() : resolved.Body.Instructions);
                        break;
                }
            }
        }
    }

    foreach (var method in moduleMethods)
    {
        if (method.Value.Item4.Any())
        {
            Console.WriteLine($"Method: {method.Value.Item3}, FriendlyMethodName: {GetFriendlyMethodName(method.Value.Item3)}");
            var entry = method.Value.Item1.AppendBasicBlock(GetLabelName(method.Value.Item4.First()));
            var builder = context.CreateBuilder();
            builder.PositionAtEnd(entry);
            {
                Stack<LLVMValueRef> stack = new();
                Dictionary<int, Tuple<LLVMValueRef, LLVMTypeRef>> local = new();
                SortedDictionary<int, LLVMBasicBlockRef> label = new()
                {
                    { method.Value.Item4.First().Offset, entry }
                };

                // Scan for branches
                foreach (var instr in method.Value.Item4)
                {
                    switch (instr.OpCode.Code)
                    {
                        case Code.Beq:
                        case Code.Beq_S:
                        case Code.Bge:
                        case Code.Bge_S:
                        case Code.Bge_Un:
                        case Code.Bge_Un_S:
                        case Code.Bgt:
                        case Code.Bgt_S:
                        case Code.Bgt_Un:
                        case Code.Bgt_Un_S:
                        case Code.Ble:
                        case Code.Ble_S:
                        case Code.Ble_Un:
                        case Code.Ble_Un_S:
                        case Code.Blt:
                        case Code.Blt_S:
                        case Code.Blt_Un:
                        case Code.Blt_Un_S:
                        case Code.Bne_Un:
                        case Code.Bne_Un_S:
                        case Code.Br:
                        case Code.Br_S:
                        case Code.Brfalse:
                        case Code.Brfalse_S:
                        case Code.Brtrue:
                        case Code.Brtrue_S:
                            {
                                var branchStart = (Instruction)instr.Operand;
                                var next = instr.Next;
                                if (!label.ContainsKey(branchStart.Offset))
                                {
                                    label.TryAdd(branchStart.Offset, method.Value.Item1.AppendBasicBlock(GetLabelName(branchStart)));
                                }
                                if (!label.ContainsKey(next.Offset))
                                {
                                    label.TryAdd(next.Offset, method.Value.Item1.AppendBasicBlock(GetLabelName(next))); // fallthrough
                                }
                                break;
                            }
                    }
                }

                // 遍历 IL 指令
                foreach (var instr in method.Value.Item4)
                {
                    Console.WriteLine($"{GetLabelName(instr)}\t\t{instr.OpCode}\t{instr.Operand}");
                    if (label.ContainsKey(instr.Offset))
                    {
                        var curr = label[instr.Offset];
                        if (curr != builder.InsertBlock)
                        {
                            builder.BuildBr(curr); // Fallthrough
                            builder.PositionAtEnd(curr);
                        }
                    }
                    switch (instr.OpCode.Code)
                    {
                        case Code.Nop:
                            break;
                        case Code.Newarr:
                            {
                                TypeReference type = (TypeReference)instr.Operand;
                                var size = LLVMValueRef.CreateConstInt(sizeType, (ulong)GetMetadataTypeSize(type.MetadataType));
                                var count = stack.Pop();
                                var function = runtimeMethods[RuntimeMethod.Newarr];
                                var ptr = builder.BuildCall2(function.Item2, function.Item1, [count, size]);
                                stack.Push(ptr);
                            }
                            break;
                        case Code.Call:
                        case Code.Callvirt:
                        case Code.Newobj:
                            {
                                MethodReference targetMethod = (MethodReference)instr.Operand;
                                LLVMValueRef ptr = default;

                                if (instr.OpCode.Code == Code.Newobj)
                                {
                                    var function = runtimeMethods[RuntimeMethod.Newobj];
                                    int size = targetMethod.DeclaringType.Resolve().Fields.Sum(v => GetMetadataTypeSize(v.FieldType.MetadataType));
                                    ptr = builder.BuildCall2(function.Item2, function.Item1, [LLVMValueRef.CreateConstInt(sizeType, (ulong)size)]);
                                    stack.Push(ptr); // push "this" for constructor call(reorder is needed!)
                                }

                                var m = moduleMethods[GetFriendlyMethodName(targetMethod)];

                                var targetFuncCreated = m.Item2;
                                var targetFunc = m.Item1;
                                LLVMValueRef[] targetArgs = Enumerable.Range(0, GetMethodParameterCount(targetMethod))
                                    .Select(i => stack.Pop())
                                    .Reverse()
                                    .ToArray();
                                if (ptr != default)
                                {
                                    // reorder "this"
                                    targetArgs = [.. (LLVMValueRef[])[ptr], .. targetArgs[0..^1]];
                                    stack.Push(ptr); // newobj leaves the object on the stack
                                }

                                var result = builder.BuildCall2(targetFuncCreated, targetFunc, targetArgs);
                                if (targetMethod.ReturnType.MetadataType != MetadataType.Void)
                                {
                                    stack.Push(result);
                                }
                            }
                            break;
                        case Code.Ret:
                            if (method.Value.Item3.ReturnType.MetadataType != MetadataType.Void)
                            {
                                builder.BuildRet(stack.Pop());
                            }
                            else
                            {
                                builder.BuildRetVoid();
                            }
                            break;
                        case Code.Stfld:
                            {
                                FieldDefinition field = (FieldDefinition)instr.Operand;
                                int offsetValue = field.DeclaringType.Fields
                                    .TakeWhile(v => v.MetadataToken != field.MetadataToken)
                                    .Sum(v => GetMetadataTypeSize(v.FieldType.MetadataType));
                                var offset = LLVMValueRef.CreateConstInt(sizeType, (ulong)offsetValue);

                                var val = stack.Pop();
                                var obj = stack.Pop();

                                var gep = builder.BuildGEP2(LLVMTypeRef.Int8, obj, [offset]);
                                builder.BuildStore(val, gep);
                            }
                            break;
                        case Code.Stloc_0:
                        case Code.Stloc_1:
                        case Code.Stloc_2:
                        case Code.Stloc_3:
                        case Code.Stloc_S:
                            {
                                int offset = instr.OpCode.Code switch
                                {
                                    Code.Stloc_0 => 0,
                                    Code.Stloc_1 => 1,
                                    Code.Stloc_2 => 2,
                                    Code.Stloc_3 => 3,
                                    Code.Stloc_S => ((VariableDefinition)instr.Operand).Index,
                                };

                                var val = stack.Pop();

                                var alloc = local.ContainsKey(offset) ? local[offset] : new(builder.BuildAlloca(val.TypeOf), val.TypeOf);
                                builder.BuildStore(val, alloc.Item1);
                                local.TryAdd(offset, alloc);
                            }
                            break;
                        case Code.Stelem_I:
                        case Code.Stelem_I1:
                        case Code.Stelem_I2:
                        case Code.Stelem_I4:
                            {
                                var value = stack.Pop();
                                var index = stack.Pop();
                                var array = stack.Pop();
                                LLVMTypeRef type = instr.OpCode.Code switch
                                {
                                    Code.Stelem_I => sizeType,
                                    Code.Stelem_I1 => LLVMTypeRef.Int8,
                                    Code.Stelem_I2 => LLVMTypeRef.Int16,
                                    Code.Stelem_I4 => LLVMTypeRef.Int32
                                };
                                builder.BuildStore(value, builder.BuildGEP2(type, array, [index]));
                            }
                            break;
                        case Code.Stsfld:
                            {
                                FieldReference field = (FieldReference)instr.Operand;

                                var ptr = staticFields[GetFriendlyFieldName(field)];
                                var value = stack.Pop();
                                builder.BuildStore(value, ptr.Item1);
                            }
                            break;
                        case Code.Ldsfld:
                            {
                                FieldReference field = (FieldReference)instr.Operand;
                                var ptr = staticFields[GetFriendlyFieldName(field)];
                                var value = builder.BuildLoad2(ptr.Item2, ptr.Item1);
                                stack.Push(value);
                            }
                            break;
                        case Code.Ldstr:
                            {
                                var str = builder.BuildGlobalStringPtr((string)instr.Operand);
                                stack.Push(str);
                            }
                            break;
                        case Code.Ldfld:
                            {
                                FieldDefinition field = (FieldDefinition)instr.Operand;
                                int offsetValue = field.DeclaringType.Fields
                                    .TakeWhile(v => v.MetadataToken != field.MetadataToken)
                                    .Sum(v => GetMetadataTypeSize(v.FieldType.MetadataType));
                                var offset = LLVMValueRef.CreateConstInt(sizeType, (ulong)offsetValue);

                                var obj = stack.Pop();
                                var gep = builder.BuildGEP2(LLVMTypeRef.Int8, obj, [offset]);
                                stack.Push(builder.BuildLoad2(GetLLVMTypeRefFromMetadataType(field.FieldType.MetadataType), gep));
                            }
                            break;
                        case Code.Ldarg_0:
                        case Code.Ldarg_1:
                        case Code.Ldarg_2:
                        case Code.Ldarg_3:
                            {
                                var param = method.Value.Item1.GetParam(instr.OpCode.Code switch
                                {
                                    Code.Ldarg_0 => 0,
                                    Code.Ldarg_1 => 1,
                                    Code.Ldarg_2 => 2,
                                    Code.Ldarg_3 => 3
                                });
                                stack.Push(param);
                            }
                            break;
                        case Code.Ldelem_I1:
                        case Code.Ldelem_U1:
                        case Code.Ldelem_I2:
                        case Code.Ldelem_U2:
                        case Code.Ldelem_I4:
                        case Code.Ldelem_U4:
                            {
                                var index = stack.Pop();
                                var array = stack.Pop();
                                var type = instr.OpCode.Code switch
                                {
                                    Code.Ldelem_I1 => LLVMTypeRef.Int8,
                                    Code.Ldelem_U1 => LLVMTypeRef.Int8,
                                    Code.Ldelem_I2 => LLVMTypeRef.Int16,
                                    Code.Ldelem_U2 => LLVMTypeRef.Int16,
                                    Code.Ldelem_I4 => LLVMTypeRef.Int32,
                                    Code.Ldelem_U4 => LLVMTypeRef.Int32
                                };
                                var gep = builder.BuildGEP2(type, array, [index]);
                                stack.Push(builder.BuildLoad2(type, gep));
                            }
                            break;
                        case Code.Ldloc_0:
                        case Code.Ldloc_1:
                        case Code.Ldloc_2:
                        case Code.Ldloc_3:
                        case Code.Ldloc_S:
                            {
                                int offset = instr.OpCode.Code switch
                                {
                                    Code.Ldloc_0 => 0,
                                    Code.Ldloc_1 => 1,
                                    Code.Ldloc_2 => 2,
                                    Code.Ldloc_3 => 3,
                                    Code.Ldloc_S => ((VariableDefinition)instr.Operand).Index,
                                };
                                var load = builder.BuildLoad2(local[offset].Item2, local[offset].Item1);
                                stack.Push(load);
                            }
                            break;
                        case Code.Ldc_I4_0:
                        case Code.Ldc_I4_1:
                        case Code.Ldc_I4_2:
                        case Code.Ldc_I4_3:
                        case Code.Ldc_I4_4:
                        case Code.Ldc_I4_5:
                        case Code.Ldc_I4_6:
                        case Code.Ldc_I4_7:
                        case Code.Ldc_I4_8:
                        case Code.Ldc_I4:
                        case Code.Ldc_I4_S:
                            {
                                var value = LLVMValueRef.CreateConstInt(sizeType, (ulong)(instr.OpCode.Code switch
                                {
                                    Code.Ldc_I4_0 => 0,
                                    Code.Ldc_I4_1 => 1,
                                    Code.Ldc_I4_2 => 2,
                                    Code.Ldc_I4_3 => 3,
                                    Code.Ldc_I4_4 => 4,
                                    Code.Ldc_I4_5 => 5,
                                    Code.Ldc_I4_6 => 6,
                                    Code.Ldc_I4_7 => 7,
                                    Code.Ldc_I4_8 => 8,
                                    Code.Ldc_I4 => (int)instr.Operand,
                                    Code.Ldc_I4_S => (sbyte)instr.Operand
                                }));
                                stack.Push(value);
                            }
                            break;
                        case Code.Ldnull:
                            {
                                var nullptr = LLVMValueRef.CreateConstNull(LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0));
                                stack.Push(nullptr);
                            }
                            break;
                        case Code.Beq:
                        case Code.Beq_S:
                        case Code.Bge:
                        case Code.Bge_S:
                        case Code.Bge_Un:
                        case Code.Bge_Un_S:
                        case Code.Bgt:
                        case Code.Bgt_S:
                        case Code.Bgt_Un:
                        case Code.Bgt_Un_S:
                        case Code.Ble:
                        case Code.Ble_S:
                        case Code.Ble_Un:
                        case Code.Ble_Un_S:
                        case Code.Blt:
                        case Code.Blt_S:
                        case Code.Blt_Un:
                        case Code.Blt_Un_S:
                        case Code.Bne_Un:
                        case Code.Bne_Un_S:
                        case Code.Br:
                        case Code.Br_S:
                        case Code.Brfalse:
                        case Code.Brfalse_S:
                        case Code.Brtrue:
                        case Code.Brtrue_S:
                            {
                                Console.WriteLine();

                                var branchStart = (Instruction)instr.Operand;

                                switch (instr.OpCode.Code)
                                {
                                    case Code.Brfalse:
                                    case Code.Brfalse_S:
                                    case Code.Brtrue:
                                    case Code.Brtrue_S:
                                        {
                                            var value = stack.Pop();

                                            var fallthrough = label[instr.Next.Offset];
                                            var branch = label[branchStart.Offset]; // Label added above

                                            bool isTrueBranch = instr.OpCode.Code switch
                                            {
                                                Code.Brfalse => false,
                                                Code.Brfalse_S => false,
                                                Code.Brtrue => true,
                                                Code.Brtrue_S => true
                                            };

                                            builder.BuildCondBr(value,
                                                isTrueBranch ? branch : fallthrough,
                                                isTrueBranch ? fallthrough : branch
                                            );

                                            builder.PositionAtEnd(fallthrough);
                                        }
                                        break;

                                    case Code.Br:
                                    case Code.Br_S:
                                        {
                                            var fallthrough = label[instr.Next.Offset];
                                            var branch = label[branchStart.Offset]; // Label added above

                                            builder.BuildBr(branch);
                                            builder.PositionAtEnd(fallthrough);
                                        }
                                        break;

                                    default:
                                        NotImplemented(instr);
                                        break;
                                }
                            }
                            break;
                        case Code.Ceq:
                        case Code.Cgt:
                        case Code.Cgt_Un:
                        case Code.Clt:
                        case Code.Clt_Un:
                            {
                                var pred = instr.OpCode.Code switch
                                {
                                    Code.Ceq => LLVMIntPredicate.LLVMIntEQ,
                                    Code.Cgt => LLVMIntPredicate.LLVMIntSGT,
                                    Code.Cgt_Un => LLVMIntPredicate.LLVMIntUGT,
                                    Code.Clt => LLVMIntPredicate.LLVMIntSLT,
                                    Code.Clt_Un => LLVMIntPredicate.LLVMIntULT
                                };

                                var val2 = stack.Pop();
                                var val1 = stack.Pop();
                                var cond = builder.BuildICmp(pred, val1, val2);
                                var result = builder.BuildSelect(cond, LLVMValueRef.CreateConstInt(sizeType, 1), LLVMValueRef.CreateConstInt(sizeType, 0));

                                stack.Push(result);
                            }
                            break;
                        case Code.Sub:
                            {
                                var val2 = stack.Pop();
                                var val1 = stack.Pop();
                                var result = builder.BuildSub(val1, val2);
                                stack.Push(result);
                            }
                            break;
                        case Code.Add:
                            {
                                var val2 = stack.Pop();
                                var val1 = stack.Pop();
                                var result = builder.BuildAdd(val1, val2);
                                stack.Push(result);
                            }
                            break;
                        case Code.Mul:
                            {
                                var val2 = stack.Pop();
                                var val1 = stack.Pop();
                                var result = builder.BuildMul(val1, val2);
                                stack.Push(result);
                            }
                            break;
                        case Code.Div:
                            {
                                var val2 = stack.Pop();
                                var val1 = stack.Pop();
                                var result = builder.BuildSDiv(val1, val2);
                                stack.Push(result);
                            }
                            break;
                        case Code.Dup:
                            {
                                var value = stack.Pop();
                                stack.Push(value);
                                stack.Push(value);
                            }
                            break;
                        default:
                            NotImplemented(instr);
                            break;
                    }
                }
            }
        }
    }

    string GetLabelName(Instruction instr) => $"IL_{instr.Offset.ToString("x2").PadLeft(4, '0').ToUpper()}";

    int GetMetadataTypeSize(MetadataType type) => type switch
    {
        MetadataType.Boolean => 1,
        MetadataType.SByte => 1,
        MetadataType.Byte => 1,
        MetadataType.Char => 2,
        MetadataType.Int16 => 2,
        MetadataType.UInt16 => 2,
        MetadataType.Int32 => 4,
        MetadataType.UInt32 => 4,
        MetadataType.Single => 4,
        MetadataType.Array => pointerSize,
        MetadataType.Class => pointerSize,
        MetadataType.String => pointerSize,
        MetadataType.ValueType => pointerSize,
        _ => throw new NotImplementedException()
    };

    LLVMTypeRef GetLLVMTypeRefFromMetadataType(MetadataType type) => type switch
    {
        MetadataType.Void => LLVMTypeRef.Void,
        MetadataType.Boolean => LLVMTypeRef.Int8,
        MetadataType.SByte => LLVMTypeRef.Int8,
        MetadataType.Byte => LLVMTypeRef.Int8,
        MetadataType.Char => LLVMTypeRef.Int16,
        MetadataType.Int16 => LLVMTypeRef.Int16,
        MetadataType.UInt16 => LLVMTypeRef.Int16,
        MetadataType.Int32 => LLVMTypeRef.Int32,
        MetadataType.UInt32 => LLVMTypeRef.Int32,
        MetadataType.Single => LLVMTypeRef.Float,
        MetadataType.Array => LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0),
        MetadataType.Class => LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0),
        MetadataType.String => LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0),
        MetadataType.ValueType => LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0),
        _ => throw new NotImplementedException()
    };

    void NotImplemented(Instruction instr)
    {
        if (Debugger.IsAttached)
        {
            Debugger.Break();
        }
        else throw new NotImplementedException();
    }

    int GetMethodParameterCount(MethodReference method)
    {
        int count = method.Parameters.Count;
        if (method.HasThis) count++;
        return count;
    }

    LLVMTypeRef CreateLLVMFunction(LLVMModuleRef module, MethodReference method)
    {
        List<LLVMTypeRef> paramTypes = new List<LLVMTypeRef>();
        if (method.HasThis)
        {
            // "this" will be a parameter
            paramTypes.Add(LLVMTypeRef.CreatePointer(LLVMTypeRef.Int8, 0));
        }
        foreach (var p in method.Parameters)
        {
            paramTypes.Add(GetLLVMTypeRefFromMetadataType(p.ParameterType.MetadataType));
        }
        LLVMTypeRef returnType = GetLLVMTypeRefFromMetadataType(method.ReturnType.MetadataType);
        var func = LLVMTypeRef.CreateFunction(returnType, paramTypes.ToArray());
        return func;
    }

    string GetFriendlyMethodName(MethodReference method, TypeReference? methodDeclareType = null)
    {
        const string member_access_operator = ".";
        const string separator = "_";
        TypeReference declareType = methodDeclareType ?? method.DeclaringType;

        List<string> names = new List<string>();
        if (declareType.Namespace != string.Empty) names.Add(declareType.Namespace.Replace(member_access_operator, separator));
        if (declareType.Name != string.Empty) names.Add(declareType.Name.Replace(member_access_operator, separator));
        if (method.Name != string.Empty) names.Add(method.Name.Replace(member_access_operator, separator));
        method.Parameters.ToList().ForEach(p => names.Add(p.ParameterType.MetadataType.ToString()));
        string friendlyMethodName = string.Join(separator, names);
        return friendlyMethodName;
    }

    string GetFriendlyFieldName(FieldReference field)
    {
        const string member_access_operator = ".";
        const string separator = "_";
        TypeReference declareType = field.DeclaringType;

        List<string> names = new List<string>();
        if (declareType.Namespace != string.Empty) names.Add(declareType.Namespace.Replace(member_access_operator, separator));
        if (declareType.Name != string.Empty) names.Add(declareType.Name.Replace(member_access_operator, separator));
        if (field.Name != string.Empty) names.Add(field.Name.Replace(member_access_operator, separator));
        string friendlyFieldName = string.Join(separator, names);
        return friendlyFieldName;
    }

    void RegisterMethodFunction(LLVMModuleRef module, MethodReference method, Collection<Instruction>? instructions)
    {
        TypeReference declareType = method.DeclaringType;

        var funcType = CreateLLVMFunction(module, method);
        var funcValue = module.AddFunction(GetFriendlyMethodName(method, declareType), funcType);
        moduleMethods.TryAdd(GetFriendlyMethodName(method, declareType), new(funcValue, funcType, method, instructions));
    }
}

module.Dump();

// 输出 obj 文件
machine.EmitToFile(module, $"{Path.GetFileNameWithoutExtension(fileName)}.obj", LLVMCodeGenFileType.LLVMObjectFile);

enum RuntimeMethod
{
    Newobj,
    Newarr
}