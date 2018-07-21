using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;

namespace ReadWriteSynchronizer
{
    public static class ReadWriteSynchronizer
    {
        public static void CheckMatch<TDelegate>(MethodInfo writeMethod, Expression<TDelegate> readMethod,
            Func<PropertyInfo, bool> writeMethodUsagesFilter = null)
        {
            var assemblyName = new AssemblyName {Name = Guid.NewGuid().ToString("N")};
            var moduleBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(assemblyName,
                AssemblyBuilderAccess.Run).DefineDynamicModule(assemblyName.Name);
            var typeBuilder = moduleBuilder.DefineType("T" + Guid.NewGuid().ToString("N"), TypeAttributes.NotPublic,
                null, new Type[] { });
            var @delegate = readMethod.Compile();
            var info = (MethodInfo) @delegate.GetType().GetProperty("Method").GetValue(@delegate);
            var methodBuilder = typeBuilder.DefineMethod(
                info.Name,
                info.Attributes,
                info.ReturnType,
                info.GetParameters().Select(_ => _.ParameterType).ToArray());
            readMethod.CompileToMethod(methodBuilder);
            var readMethodInfo = typeBuilder.CreateType().GetMethod(info.Name);
            CheckMatch(writeMethod, readMethodInfo, writeMethodUsagesFilter);
        }

        public static void CheckMatch(MethodInfo writeMethod, MethodInfo readMethod,
            Func<PropertyInfo, bool> writeMethodUsagesFilter = null)
        {
            var entityType = writeMethod.GetParameters()[0].ParameterType;
            var propertysByGetMethod = entityType.GetProperties().ToDictionary(_ => {
                MemberInfo method = _.GetGetMethod();
                return method;
            });
            var propertysBySetMethod = entityType.GetProperties().ToDictionary(_ => {
                MemberInfo method = _.GetSetMethod();
                return method;
            });
            var readMethodUsages = UsageResolver.ResolveUsages(readMethod, () => new Type[] { })
                .Select(_ => {
                    propertysByGetMethod.TryGetValue(_.ResolvedMember, out var value);
                    return value;
                })
                .Where(_ => _ != null)
                .ToList();
            var writeMethodUsages = UsageResolver.ResolveUsages(writeMethod, () => new Type[] { }).Select(_ => {
                    propertysBySetMethod.TryGetValue(_.ResolvedMember, out var value);
                    return value;
                })
                .Where(_ => _ != null)
                .ToList();
            {
                var list = (writeMethodUsagesFilter != null
                        ? writeMethodUsages.Where(writeMethodUsagesFilter)
                        : writeMethodUsages)
                    .Except(readMethodUsages).ToList();
                if (list.Count > 0)
                {
                    var s = string.Join(@"
", list.Select(_ => $"{_.Name} = e.{_.Name},"));
                    throw new ApplicationException($@"В Read методе отсутствуют вызовы следующих свойств:
{s}
");
                }
            }
            {
                var list = readMethodUsages.Except(writeMethodUsages).ToList();
                if (list.Count > 0)
                {
                    var s = string.Join(@"
", list.Select(_ => $"entity.{_.Name} = data.{_.Name};"));
                    throw new ApplicationException($@"В Write методе отсутствуют вызовы следующих свойств:
{s}
");
                }
            }
        }
    }

    public class Usage
    {
        public MethodBase CurrentMethod { get; }
        public MemberInfo ResolvedMember { get; }

        public Usage(MethodBase currentMethod, MemberInfo resolvedMember)
        {
            CurrentMethod = currentMethod;
            ResolvedMember = resolvedMember;
        }
    }

    /// <summary>
    /// http://www.codeproject.com/KB/cs/sdilreader.aspx
    /// </summary>
    public static class UsageResolver
    {
        public static IEnumerable<Usage> ResolveUsages(MethodBase methodInfo, Func<Type[]> genericMethodArgumentsFunc)
        {
            var methodBody = methodInfo.GetMethodBody();
            if (methodBody == null) yield break;
            var ilAsByteArray = methodBody.GetILAsByteArray();
            var position = 0;
            while (position < ilAsByteArray.Length)
            {
                OpCode opCode;
                ushort value = ilAsByteArray[position++];
                if (value == 0xfe)
                {
                    value = ilAsByteArray[position++];
                    opCode = multiByteOpCodes[value];
                }
                else
                    opCode = singleByteOpCodes[value];
                switch (opCode.OperandType)
                {
                    case OperandType.InlineBrTarget:
                        ReadInt32(ilAsByteArray, ref position);
                        break;
                    case OperandType.InlineField:
                        ReadInt32(ilAsByteArray, ref position);
                        break;
                    case OperandType.InlineMethod:
                        var metadataToken = ReadInt32(ilAsByteArray, ref position);
                        if (methodInfo.DeclaringType != null)
                            yield return new Usage(
                                methodInfo,
                                methodInfo.Module.ResolveMember(
                                    metadataToken,
                                    methodInfo.DeclaringType.GetGenericArguments(),
                                    genericMethodArgumentsFunc()));
                        break;
                    case OperandType.InlineSig:
                        ReadInt32(ilAsByteArray, ref position);
                        break;
                    case OperandType.InlineTok:
                        ReadInt32(ilAsByteArray, ref position);
                        break;
                    case OperandType.InlineType:
                        ReadInt32(ilAsByteArray, ref position);
                        break;
                    case OperandType.InlineI:
                        ReadInt32(ilAsByteArray, ref position);
                        break;
                    case OperandType.InlineI8:
                        ReadInt64(ref position);
                        break;
                    case OperandType.InlineNone:
                        break;
                    case OperandType.InlineR:
                        ReadDouble(ref position);
                        break;
                    case OperandType.InlineString:
                        ReadInt32(ilAsByteArray, ref position);
                        break;
                    case OperandType.InlineSwitch:
                        var count = ReadInt32(ilAsByteArray, ref position);
                        for (var i = 0; i < count; i++) ReadInt32(ilAsByteArray, ref position);
                        break;
                    case OperandType.InlineVar:
                        ReadUInt16(ref position);
                        break;
                    case OperandType.ShortInlineBrTarget:
                        ReadSByte(ref position);
                        break;
                    case OperandType.ShortInlineI:
                        ReadSByte(ref position);
                        break;
                    case OperandType.ShortInlineR:
                        ReadSingle(ref position);
                        break;
                    case OperandType.ShortInlineVar:
                        ReadByte(ref position);
                        break;
                    default:
                        throw new InvalidOperationException();
                }
            }
        }

        static UsageResolver()
        {
            singleByteOpCodes = new OpCode[0x100];
            multiByteOpCodes = new OpCode[0x100];
            foreach (var fieldInfo in typeof(OpCodes).GetFields())
            {
                if (fieldInfo.FieldType == typeof(OpCode))
                {
                    var opCode = (OpCode)fieldInfo.GetValue(null);
                    var value = unchecked((ushort)opCode.Value);
                    if (value < 0x100)
                    {
                        singleByteOpCodes[value] = opCode;
                    }
                    else
                    {
                        if ((value & 0xff00) != 0xfe00)
                        {
                            throw new ApplicationException("Invalid OpCode.");
                        }
                        multiByteOpCodes[value & 0xff] = opCode;
                    }
                }
            }
        }

        private static readonly OpCode[] multiByteOpCodes;
        private static readonly OpCode[] singleByteOpCodes;

        private static void ReadUInt16(ref int position)
        {
            position += 2;
        }

        private static int ReadInt32(byte[] bytes, ref int position)
        {
            return bytes[position++] | bytes[position++] << 8 | bytes[position++] << 0x10 | bytes[position++] << 0x18;
        }

        private static void ReadInt64(ref int position)
        {
            position += 8;
        }

        private static void ReadDouble(ref int position)
        {
            position += 8;
        }

        private static void ReadSByte(ref int position)
        {
            position++;
        }

        private static void ReadByte(ref int position)
        {
            position++;
        }

        private static void ReadSingle(ref int position)
        {
            position += 4;
        }
    }
}