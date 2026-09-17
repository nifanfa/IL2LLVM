sealed class Types(Translator translator) : TranslationComponent(translator)
{
    internal bool TryTranslateTypeInstruction(LLVMBuilderRef builder, LLVMBuilderRef entryBuilder, LLVMValueRef function,
        Instruction instruction, MethodReference method, Stack<LLVMValueRef> stack,
        HashSet<LLVMBasicBlockRef> terminatedBlocks, Func<LLVMValueRef, TypeReference, LLVMValueRef> buildRuntimeTypeMatch,
        Action<LLVMValueRef, TypeReference> trackType, Action<int, LLVMValueRef, TypeReference> storeTemporaryRoot,
        Action synchronizeEvaluationStackRoots, LLVMTypeRef exceptionThrowType, LLVMValueRef exceptionThrowFunction,
        Func<LLVMTypeRef, LLVMValueRef> buildEntryAlloca)
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
                    var handleStorage = buildEntryAlloca(LLVMTypeRef.CreateArray(int8Type,
                        (uint)Math.Max(1, GetTypeSize(coreLib.RuntimeTypeHandle))));
                    var handle = builder.BuildBitCast(handleStorage, pointerType);
                    StoreField(builder, handle, coreLib.RuntimeTypeHandleTypeField,
                        GetRuntimeTypeObject(tokenType));
                    stack.Push(handle);
                    trackType(handle, coreLib.RuntimeTypeHandle);
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
                        trackType(result, coreLib.Object);
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
                    trackType(box, coreLib.Object);
                    return true;
                }
            case Code.Unbox:
            case Code.Unbox_Any:
                {
                    var value = stack.Pop();
                    var valueType = SubstituteGenericParameter((TypeReference)instruction.Operand, method);
                    if (instruction.OpCode.Code == Code.Unbox_Any &&
                        TryGetNullableElementType(valueType, out var nullableElementType))
                    {
                        var nullableDefinition = valueType.Resolve() ??
                            throw new NotSupportedException($"Nullable type is not defined: {valueType.FullName}");
                        var storage = buildEntryAlloca(LLVMTypeRef.CreateArray(int8Type,
                            (uint)Math.Max(1, GetTypeSize(valueType))));
                        var nullable = builder.BuildBitCast(storage, pointerType);
                        FillMemory(builder, nullable, LLVMValueRef.CreateConstNull(int8Type),
                            LLVMValueRef.CreateConstInt(sizeType, (ulong)Math.Max(1, GetTypeSize(valueType)), false));

                        var valueBlock = context.AppendBasicBlock(function, $"nullable.unbox.value.{nextVirtualDispatchId++}");
                        var continuation = context.AppendBasicBlock(function, $"nullable.unbox.cont.{nextVirtualDispatchId++}");
                        var sourceBlock = builder.InsertBlock;
                        builder.BuildCondBr(builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, value,
                            LLVMValueRef.CreateConstNull(pointerType)), valueBlock, continuation);
                        terminatedBlocks.Add(sourceBlock);

                        builder.PositionAtEnd(valueBlock);
                        var valueField = coreLib.GetNullableValueField(nullableDefinition);
                        CopyValue(builder, GetFieldAddress(builder, nullable, valueField, valueType),
                            GetBoxedValueAddress(builder, value, nullableElementType), GetTypeSize(nullableElementType));
                        var hasValueField = coreLib.GetNullableHasValueField(nullableDefinition);
                        builder.BuildStore(LLVMValueRef.CreateConstInt(GetLLVMTypeRef(hasValueField.FieldType), 1, false),
                            GetFieldAddress(builder, nullable, hasValueField, valueType));
                        builder.BuildBr(continuation);

                        builder.PositionAtEnd(continuation);
                        stack.Push(nullable);
                        trackType(nullable, valueType);
                        return true;
                    }
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
                    if (instruction.OpCode.Code == Code.Unbox_Any)
                        result = PromoteSmallIntegerLoad(builder, result, valueType);
                    stack.Push(result);
                    trackType(result, valueType);
                    return true;
                }
            case Code.Mkrefany:
                {
                    var valueType = SubstituteGenericParameter((TypeReference)instruction.Operand, method);
                    var valueAddress = ConvertValue(builder, stack.Pop(), pointerType);

                    // TypedReference is a regular value type in CoreLib.  Keep the
                    // referenced address and runtime type handle in its fields so
                    // the following refanyval/refanytype instructions can recover
                    // the same information without a special LLVM type.
                    var typedReferenceStorage = buildEntryAlloca(LLVMTypeRef.CreateArray(
                        int8Type, (uint)Math.Max(1, GetTypeSize(coreLib.TypedReference))));
                    var typedReference = builder.BuildBitCast(typedReferenceStorage, pointerType);
                    StoreField(builder, typedReference, coreLib.TypedReferenceValueField, valueAddress);

                    var typeHandleStorage = buildEntryAlloca(LLVMTypeRef.CreateArray(
                        int8Type, (uint)Math.Max(1, GetTypeSize(coreLib.RuntimeTypeHandle))));
                    var typeHandle = builder.BuildBitCast(typeHandleStorage, pointerType);
                    StoreField(builder, typeHandle, coreLib.RuntimeTypeHandleTypeField,
                        GetRuntimeTypeObject(valueType));
                    StoreField(builder, typedReference, coreLib.TypedReferenceTypeField, typeHandle);

                    stack.Push(typedReference);
                    trackType(typedReference, coreLib.TypedReference);
                    return true;
                }
            case Code.Refanyval:
                {
                    var typedReference = stack.Pop();
                    var valueType = SubstituteGenericParameter((TypeReference)instruction.Operand, method);
                    var typeHandleAddress = GetFieldAddress(builder, typedReference,
                        coreLib.TypedReferenceTypeField);
                    var actualType = builder.BuildLoad2(pointerType, typeHandleAddress);
                    var expectedType = ConvertValue(builder, GetRuntimeTypeObject(valueType), pointerType);
                    var sourceBlock = builder.InsertBlock;
                    var matchBlock = context.AppendBasicBlock(function, $"refanyval.match.{nextVirtualDispatchId++}");
                    var failBlock = context.AppendBasicBlock(function, $"refanyval.fail.{nextVirtualDispatchId++}");
                    builder.BuildCondBr(builder.BuildICmp(LLVMIntPredicate.LLVMIntEQ, actualType, expectedType),
                        matchBlock, failBlock);
                    terminatedBlocks.Add(sourceBlock);

                    builder.PositionAtEnd(failBlock);
                    var exception = BuildAllocation(builder, GetObjectSize(coreLib.InvalidCastException));
                    InitializeRuntimeType(builder, exception, coreLib.InvalidCastException);
                    builder.BuildCall2(exceptionThrowType, exceptionThrowFunction, [exception]);
                    builder.BuildUnreachable();
                    terminatedBlocks.Add(failBlock);

                    builder.PositionAtEnd(matchBlock);
                    var valueAddress = ConvertValue(builder,
                        builder.BuildLoad2(GetLLVMTypeRef(coreLib.TypedReferenceValueField.FieldType),
                            GetFieldAddress(builder, typedReference, coreLib.TypedReferenceValueField)), pointerType);
                    stack.Push(valueAddress);
                    trackType(valueAddress, new ByReferenceType(valueType));
                    return true;
                }
            case Code.Refanytype:
                {
                    var typedReference = stack.Pop();
                    var typeHandle = GetFieldAddress(builder, typedReference, coreLib.TypedReferenceTypeField);
                    stack.Push(typeHandle);
                    trackType(typeHandle, coreLib.RuntimeTypeHandle);
                    return true;
                }
            default:
                return false;
        }
    }
}
