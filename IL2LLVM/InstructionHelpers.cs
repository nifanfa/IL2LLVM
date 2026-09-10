sealed class InstructionHelpers(Translator translator) : TranslationComponent(translator)
{
    internal new LLVMValueRef ConvertValue(LLVMBuilderRef builder, LLVMValueRef value, LLVMTypeRef target, bool signed = true)
    {
        var source = value.TypeOf;
        if (source.Equals(target)) return value;
        if (source.Kind == LLVMTypeKind.LLVMIntegerTypeKind && target.Kind == LLVMTypeKind.LLVMIntegerTypeKind)
        {
            if (source.IntWidth < target.IntWidth)
                return signed ? builder.BuildSExt(value, target) : builder.BuildZExt(value, target);
            return builder.BuildTrunc(value, target);
        }
        if (source.Kind == LLVMTypeKind.LLVMIntegerTypeKind && target.Kind is LLVMTypeKind.LLVMFloatTypeKind or LLVMTypeKind.LLVMDoubleTypeKind)
            return signed ? builder.BuildSIToFP(value, target) : builder.BuildUIToFP(value, target);
        if (source.Kind is LLVMTypeKind.LLVMFloatTypeKind or LLVMTypeKind.LLVMDoubleTypeKind &&
            target.Kind == LLVMTypeKind.LLVMIntegerTypeKind)
            return signed ? builder.BuildFPToSI(value, target) : builder.BuildFPToUI(value, target);
        if (source.Kind == LLVMTypeKind.LLVMFloatTypeKind && target.Kind == LLVMTypeKind.LLVMDoubleTypeKind)
            return builder.BuildFPExt(value, target);
        if (source.Kind == LLVMTypeKind.LLVMDoubleTypeKind && target.Kind == LLVMTypeKind.LLVMFloatTypeKind)
            return builder.BuildFPTrunc(value, target);
        if (source.Kind == LLVMTypeKind.LLVMIntegerTypeKind && target.Kind == LLVMTypeKind.LLVMPointerTypeKind)
            return builder.BuildIntToPtr(value, target);
        if (source.Kind == LLVMTypeKind.LLVMPointerTypeKind && target.Kind == LLVMTypeKind.LLVMIntegerTypeKind)
            return builder.BuildPtrToInt(value, target);
        if (source.Kind == LLVMTypeKind.LLVMPointerTypeKind && target.Kind == LLVMTypeKind.LLVMPointerTypeKind)
            return builder.BuildPointerCast(value, target);
        return value;
    }

    internal new LLVMValueRef PromoteSmallIntegerLoad(LLVMBuilderRef builder, LLVMValueRef value, TypeReference type)
    {
        while (type is RequiredModifierType or OptionalModifierType or PinnedType)
        {
            type = type switch
            {
                RequiredModifierType requiredModifier => requiredModifier.ElementType,
                OptionalModifierType optionalModifier => optionalModifier.ElementType,
                PinnedType pinned => pinned.ElementType,
                _ => type
            };
        }

        type = GetEnumUnderlyingType(type) ?? type;
        return type.MetadataType switch
        {
            MetadataType.SByte or MetadataType.Int16 => ConvertValue(builder, value, int32Type),
            MetadataType.Boolean or MetadataType.Byte or MetadataType.Char or MetadataType.UInt16 =>
                ConvertValue(builder, value, int32Type, false),
            _ => value
        };
    }

    internal new (LLVMValueRef Left, LLVMValueRef Right) NormalizeBinaryOperands(LLVMBuilderRef builder, LLVMValueRef left, LLVMValueRef right)
    {
        if (left.TypeOf.Equals(right.TypeOf))
            return (left, right);

        var leftType = left.TypeOf;
        var rightType = right.TypeOf;
        if (leftType.Kind == LLVMTypeKind.LLVMIntegerTypeKind && rightType.Kind == LLVMTypeKind.LLVMIntegerTypeKind)
        {
            var target = leftType.IntWidth >= rightType.IntWidth ? leftType : rightType;
            return (ConvertValue(builder, left, target), ConvertValue(builder, right, target));
        }

        if (leftType.Kind is LLVMTypeKind.LLVMFloatTypeKind or LLVMTypeKind.LLVMDoubleTypeKind ||
            rightType.Kind is LLVMTypeKind.LLVMFloatTypeKind or LLVMTypeKind.LLVMDoubleTypeKind)
        {
            var target = leftType.Kind == LLVMTypeKind.LLVMDoubleTypeKind || rightType.Kind == LLVMTypeKind.LLVMDoubleTypeKind
                ? doubleType
                : floatType;
            return (ConvertValue(builder, left, target), ConvertValue(builder, right, target));
        }

        return (left, right);
    }

    internal new bool IsFloatingValue(LLVMValueRef value)
    {
        return value.TypeOf.Kind is LLVMTypeKind.LLVMFloatTypeKind or LLVMTypeKind.LLVMDoubleTypeKind;
    }

    internal new LLVMValueRef BuildComparison(LLVMBuilderRef builder, Code code, LLVMValueRef left, LLVMValueRef right)
    {
        var leftType = left.TypeOf;
        var rightType = right.TypeOf;
        if (!leftType.Equals(rightType))
        {
            if (leftType.Kind is LLVMTypeKind.LLVMFloatTypeKind or LLVMTypeKind.LLVMDoubleTypeKind ||
                rightType.Kind is LLVMTypeKind.LLVMFloatTypeKind or LLVMTypeKind.LLVMDoubleTypeKind)
            {
                var target = leftType.Kind == LLVMTypeKind.LLVMDoubleTypeKind || rightType.Kind == LLVMTypeKind.LLVMDoubleTypeKind
                    ? doubleType
                    : floatType;
                left = ConvertValue(builder, left, target);
                right = ConvertValue(builder, right, target);
            }
            else if (leftType.Kind == LLVMTypeKind.LLVMPointerTypeKind)
                right = ConvertValue(builder, right, leftType);
            else if (rightType.Kind == LLVMTypeKind.LLVMPointerTypeKind)
                left = ConvertValue(builder, left, rightType);
            else if (leftType.Kind == LLVMTypeKind.LLVMIntegerTypeKind && rightType.Kind == LLVMTypeKind.LLVMIntegerTypeKind)
            {
                var target = leftType.IntWidth >= rightType.IntWidth ? leftType : rightType;
                left = ConvertValue(builder, left, target);
                right = ConvertValue(builder, right, target);
            }
        }

        if (left.TypeOf.Kind is LLVMTypeKind.LLVMFloatTypeKind or LLVMTypeKind.LLVMDoubleTypeKind)
        {
            var predicate = code switch
            {
                Code.Ceq or Code.Beq or Code.Beq_S => LLVMRealPredicate.LLVMRealOEQ,
                Code.Cgt or Code.Bgt or Code.Bgt_S => LLVMRealPredicate.LLVMRealOGT,
                Code.Cgt_Un or Code.Bgt_Un or Code.Bgt_Un_S => LLVMRealPredicate.LLVMRealUGT,
                Code.Clt or Code.Blt or Code.Blt_S => LLVMRealPredicate.LLVMRealOLT,
                Code.Clt_Un or Code.Blt_Un or Code.Blt_Un_S => LLVMRealPredicate.LLVMRealULT,
                Code.Bge or Code.Bge_S => LLVMRealPredicate.LLVMRealOGE,
                Code.Bge_Un or Code.Bge_Un_S => LLVMRealPredicate.LLVMRealUGE,
                Code.Ble or Code.Ble_S => LLVMRealPredicate.LLVMRealOLE,
                Code.Ble_Un or Code.Ble_Un_S => LLVMRealPredicate.LLVMRealULE,
                Code.Bne_Un or Code.Bne_Un_S => LLVMRealPredicate.LLVMRealUNE,
                _ => LLVMRealPredicate.LLVMRealOEQ
            };
            return builder.BuildFCmp(predicate, left, right);
        }

        var intPredicate = code switch
        {
            Code.Ceq or Code.Beq or Code.Beq_S => LLVMIntPredicate.LLVMIntEQ,
            Code.Cgt or Code.Bgt or Code.Bgt_S => LLVMIntPredicate.LLVMIntSGT,
            Code.Cgt_Un or Code.Bgt_Un or Code.Bgt_Un_S => LLVMIntPredicate.LLVMIntUGT,
            Code.Clt or Code.Blt or Code.Blt_S => LLVMIntPredicate.LLVMIntSLT,
            Code.Clt_Un or Code.Blt_Un or Code.Blt_Un_S => LLVMIntPredicate.LLVMIntULT,
            Code.Bge or Code.Bge_S => LLVMIntPredicate.LLVMIntSGE,
            Code.Bge_Un or Code.Bge_Un_S => LLVMIntPredicate.LLVMIntUGE,
            Code.Ble or Code.Ble_S => LLVMIntPredicate.LLVMIntSLE,
            Code.Ble_Un or Code.Ble_Un_S => LLVMIntPredicate.LLVMIntULE,
            Code.Bne_Un or Code.Bne_Un_S => LLVMIntPredicate.LLVMIntNE,
            _ => LLVMIntPredicate.LLVMIntEQ
        };
        return builder.BuildICmp(intPredicate, left, right);
    }

    internal new string GetLabelName(Instruction instr) => $"IL_{instr.Offset.ToString("x2").PadLeft(4, '0').ToUpper()}";

    internal new int GetMetadataTypeSize(MetadataType type) => type switch
    {
        MetadataType.Boolean => 1,
        MetadataType.SByte => 1,
        MetadataType.Byte => 1,
        MetadataType.Char => 2,
        MetadataType.Int16 => 2,
        MetadataType.UInt16 => 2,
        MetadataType.Int32 => 4,
        MetadataType.UInt32 => 4,
        MetadataType.Int64 => 8,
        MetadataType.UInt64 => 8,
        MetadataType.Single => 4,
        MetadataType.Double => 8,
        MetadataType.IntPtr => pointerSize,
        MetadataType.UIntPtr => pointerSize,
        MetadataType.Pointer => pointerSize,
        MetadataType.ByReference => pointerSize,
        MetadataType.Array => pointerSize,
        MetadataType.Class => pointerSize,
        MetadataType.Object => pointerSize,
        MetadataType.String => pointerSize,
        MetadataType.ValueType => pointerSize,
        _ => pointerSize
    };

    internal new LLVMTypeRef GetLLVMTypeRefFromMetadataType(MetadataType type) => type switch
    {
        MetadataType.Void => voidType,
        MetadataType.Boolean => int8Type,
        MetadataType.SByte => int8Type,
        MetadataType.Byte => int8Type,
        MetadataType.Char => int16Type,
        MetadataType.Int16 => int16Type,
        MetadataType.UInt16 => int16Type,
        MetadataType.Int32 => int32Type,
        MetadataType.UInt32 => int32Type,
        MetadataType.Int64 => int64Type,
        MetadataType.UInt64 => int64Type,
        MetadataType.Single => floatType,
        MetadataType.Double => doubleType,
        MetadataType.IntPtr => sizeType,
        MetadataType.UIntPtr => sizeType,
        MetadataType.Pointer => LLVMTypeRef.CreatePointer(int8Type, 0),
        MetadataType.ByReference => LLVMTypeRef.CreatePointer(int8Type, 0),
        MetadataType.Array => LLVMTypeRef.CreatePointer(int8Type, 0),
        MetadataType.Class => LLVMTypeRef.CreatePointer(int8Type, 0),
        MetadataType.Object => LLVMTypeRef.CreatePointer(int8Type, 0),
        MetadataType.String => LLVMTypeRef.CreatePointer(int8Type, 0),
        MetadataType.ValueType => LLVMTypeRef.CreatePointer(int8Type, 0),
        _ => LLVMTypeRef.CreatePointer(int8Type, 0)
    };

    internal new int GetMethodParameterCount(MethodReference method)
    {
        int count = method.Parameters.Count;
        if (method.HasThis) count++;
        return count;
    }

}
