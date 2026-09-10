sealed class Types(Translator translator) : TranslationComponent(translator)
{
    internal bool TryTranslateTypeInstruction(LLVMBuilderRef builder, LLVMBuilderRef entryBuilder, LLVMValueRef function,
        Instruction instruction, MethodReference method, System.Collections.Generic.Stack<LLVMValueRef> stack,
        HashSet<LLVMBasicBlockRef> terminatedBlocks, Func<LLVMValueRef, TypeReference, LLVMValueRef> buildRuntimeTypeMatch,
        Action<LLVMValueRef, TypeReference> trackType, Action<int, LLVMValueRef, TypeReference> storeTemporaryRoot,
        Action synchronizeEvaluationStackRoots, LLVMTypeRef exceptionThrowType, LLVMValueRef exceptionThrowFunction)
    {
        var pointerType = LLVMTypeRef.CreatePointer(int8Type, 0);
        switch (instruction.OpCode.Code)
        {
            case Code.Castclass:
            case Code.Isinst:
                {
                    var targetType = SubstituteGenericParameter((TypeReference)instruction.Operand, method);
                    var value = stack.Count == 0 ? LLVMValueRef.CreateConstNull(pointerType) : stack.Pop();
                    value = ConvertValue(builder, value, pointerType);
                    var nullBlock = context.AppendBasicBlock(function, $"cast.null.{nextVirtualDispatchId++}");
                    var checkBlock = context.AppendBasicBlock(function, $"cast.check.{nextVirtualDispatchId++}");
                    var matchBlock = context.AppendBasicBlock(function, $"cast.match.{nextVirtualDispatchId++}");
                    var failBlock = context.AppendBasicBlock(function, $"cast.fail.{nextVirtualDispatchId++}");
                    var continuation = context.AppendBasicBlock(function, $"cast.cont.{nextVirtualDispatchId++}");
                    var sourceBlock = builder.InsertBlock;
                    builder.BuildCondBr(builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, value,
                        LLVMValueRef.CreateConstNull(pointerType)), nullBlock, checkBlock);
                    terminatedBlocks.Add(sourceBlock);

                    builder.PositionAtEnd(nullBlock);
                    var nullResult = LLVMValueRef.CreateConstNull(pointerType);
                    builder.BuildBr(continuation);

                    builder.PositionAtEnd(checkBlock);
                    builder.BuildCondBr(buildRuntimeTypeMatch(value, targetType), matchBlock, failBlock);
                    terminatedBlocks.Add(checkBlock);

                    builder.PositionAtEnd(matchBlock);
                    builder.BuildBr(continuation);

                    builder.PositionAtEnd(failBlock);
                    if (instruction.OpCode.Code == Code.Castclass)
                    {
                        var exception = BuildAllocation(builder, GetObjectSize(coreLib.InvalidCastException));
                        InitializeRuntimeType(builder, exception, coreLib.InvalidCastException);
                        builder.BuildCall2(exceptionThrowType, exceptionThrowFunction, [exception]);
                        builder.BuildUnreachable();
                    }
                    else
                        builder.BuildBr(continuation);
                    terminatedBlocks.Add(failBlock);

                    builder.PositionAtEnd(continuation);
                    var result = builder.BuildPhi(pointerType, "cast.result");
                    if (instruction.OpCode.Code == Code.Castclass)
                        result.AddIncoming([nullResult, value], [nullBlock, matchBlock], 2);
                    else
                        result.AddIncoming([nullResult, value, nullResult], [nullBlock, matchBlock, failBlock], 3);
                    stack.Push(result);
                    trackType(result, targetType);
                    return true;
                }
            case Code.Ldtoken:
                if (instruction.Operand is TypeReference tokenType)
                {
                    tokenType = SubstituteGenericParameter(tokenType, method);
                    var typeObject = GetRuntimeTypeObject(tokenType);
                    stack.Push(typeObject);
                    trackType(typeObject, coreLib.Type);
                }
                else if (instruction.Operand is FieldReference field)
                {
                    var handle = GetRuntimeFieldHandle(builder, entryBuilder, field);
                    stack.Push(handle);
                    trackType(handle, coreLib.RuntimeFieldHandle);
                }
                else
                    stack.Push(LLVMValueRef.CreateConstNull(pointerType));
                return true;
            case Code.Box:
                {
                    var value = stack.Pop();
                    var valueType = SubstituteGenericParameter((TypeReference)instruction.Operand, method);
                    storeTemporaryRoot(0, value, valueType);
                    synchronizeEvaluationStackRoots();
                    if (TryGetNullableElementType(valueType, out var nullableElementType))
                    {
                        var nullableDefinition = valueType.Resolve() ??
                            throw new NotSupportedException($"Nullable type is not defined: {valueType.FullName}");
                        var hasValueField = coreLib.GetNullableHasValueField(nullableDefinition);
                        var valueField = coreLib.GetNullableValueField(nullableDefinition);
                        var hasValue = builder.BuildLoad2(GetLLVMTypeRef(hasValueField.FieldType),
                            GetFieldAddress(builder, value, hasValueField, valueType));
                        var valueBlock = context.AppendBasicBlock(function, $"nullable.box.value.{nextVirtualDispatchId++}");
                        var nullBlock = context.AppendBasicBlock(function, $"nullable.box.null.{nextVirtualDispatchId++}");
                        var continuation = context.AppendBasicBlock(function, $"nullable.box.cont.{nextVirtualDispatchId++}");
                        var sourceBlock = builder.InsertBlock;
                        builder.BuildCondBr(builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, hasValue,
                            LLVMValueRef.CreateConstNull(hasValue.TypeOf)), valueBlock, nullBlock);
                        terminatedBlocks.Add(sourceBlock);

                        builder.PositionAtEnd(valueBlock);
                        var nullableValueAddress = GetFieldAddress(builder, value, valueField, valueType);
                        var nullableValue = IsValueType(nullableElementType)
                            ? nullableValueAddress
                            : builder.BuildLoad2(GetLLVMTypeRef(nullableElementType), nullableValueAddress);
                        var nullableBox = BuildBoxedValue(builder, nullableValue, nullableElementType);
                        builder.BuildBr(continuation);

                        builder.PositionAtEnd(nullBlock);
                        var nullValue = LLVMValueRef.CreateConstNull(pointerType);
                        builder.BuildBr(continuation);

                        builder.PositionAtEnd(continuation);
                        var result = builder.BuildPhi(pointerType, "nullable.box");
                        result.AddIncoming([nullableBox, nullValue], [valueBlock, nullBlock], 2);
                        stack.Push(result);
                        trackType(result, nullableElementType);
                        return true;
                    }
                    if (IsManagedReferenceType(valueType))
                    {
                        var reference = ConvertValue(builder, value, pointerType);
                        stack.Push(reference);
                        trackType(reference, valueType);
                        return true;
                    }
                    var box = BuildBoxedValue(builder, value, valueType);
                    stack.Push(box);
                    trackType(box, valueType);
                    return true;
                }
            case Code.Unbox:
            case Code.Unbox_Any:
                {
                    var value = stack.Pop();
                    var valueType = SubstituteGenericParameter((TypeReference)instruction.Operand, method);
                    var boxedValue = !IsManagedReferenceType(valueType) ? GetBoxedValueAddress(builder, value, valueType) : value;
                    LLVMValueRef result;
                    if (instruction.OpCode.Code == Code.Unbox_Any && valueType.MetadataType is MetadataType.Boolean or
                        MetadataType.SByte or MetadataType.Byte or MetadataType.Char or MetadataType.Int16 or
                        MetadataType.UInt16 or MetadataType.Int32 or MetadataType.UInt32 or MetadataType.Int64 or
                        MetadataType.UInt64 or MetadataType.IntPtr or MetadataType.UIntPtr or MetadataType.Single or MetadataType.Double)
                        result = builder.BuildLoad2(GetLLVMTypeRef(valueType), boxedValue);
                    else if (instruction.OpCode.Code == Code.Unbox_Any && GetEnumUnderlyingType(valueType) is not null)
                        result = ConvertValue(builder, builder.BuildLoad2(int64Type, boxedValue), GetLLVMTypeRef(valueType));
                    else if (instruction.OpCode.Code == Code.Unbox_Any && !IsValueType(valueType))
                        result = ConvertValue(builder, value, GetLLVMTypeRef(valueType));
                    else
                        result = boxedValue;
                    stack.Push(result);
                    trackType(result, valueType);
                    return true;
                }
            default:
                return false;
        }
    }
}
