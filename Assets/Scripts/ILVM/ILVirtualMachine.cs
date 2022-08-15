using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using GenericParameterAttributes = System.Reflection.GenericParameterAttributes;
using UnityEngine.Assertions;

namespace ILVM
{

    public class VMStack : IEnumerable
    {
        private List<object> innerList = new List<object>(32);

        public int Count
        {
            get { return innerList.Count; }
        }

        public void Push(object val)
        {
            innerList.Add(val);
        }

        public object Pop()
        {
            var val = innerList[innerList.Count - 1];
            innerList.RemoveAt(innerList.Count - 1);
            return val;
        }

        public object Peek()
        {
            var val = innerList[innerList.Count - 1];
            return val;
        }

        public object GetValue(int index)
        {
            return innerList[index];
        }

        public void SetValue(int index, object val)
        {
            innerList[index] = val;
        }

        public void Clear()
        {
            innerList.Clear();
        }

        public IEnumerator GetEnumerator()
        {
            return innerList.GetEnumerator();
        }


        private System.Text.StringBuilder sb = new StringBuilder();
        public override string ToString()
        {
            sb.Length = 0;
            sb.AppendFormat("Stack({0}): ", innerList.Count);
            foreach (var obj in innerList)
            {
                sb.Append(obj != null ? obj.ToString() : "null");
                sb.Append(";");
            }
            return sb.ToString();
        }
    }

    public class VMPool
    {
        private static List<ILVirtualMachine> vmPoolIdle = new List<ILVirtualMachine>();
        private static List<ILVirtualMachine> vmPoolUsing = new List<ILVirtualMachine>();

        public static ILVirtualMachine GetFromPool()
        {
            ILVirtualMachine vm;
            if (vmPoolIdle.Count > 0)
            {
                var lastIdx = vmPoolIdle.Count - 1;
                vm = vmPoolIdle[lastIdx];
                vmPoolIdle.RemoveAt(lastIdx);
            }
            else
                vm = new ILVirtualMachine();

            vmPoolUsing.Add(vm);
            return vm;
        }

        public static void BackToPool(ILVirtualMachine vm)
        {
            var idx = vmPoolUsing.IndexOf(vm);
            if (idx < 0)
                return;

            var lastIdx = vmPoolUsing.Count - 1;
            vmPoolUsing[idx] = vmPoolUsing[lastIdx];
            vmPoolUsing.RemoveAt(lastIdx);

            vmPoolIdle.Add(vm);
        }
    }

    public class ILVirtualMachine
    {
        private MethodDefinition methodDef;
        private MethodInfo methodInfo;
        private const int kArgSize = 128;
        private object[] arguments = null;

        private VMStack machineStack = new VMStack();
        private object[] machineVar = new object[kArgSize];
        private Type[] machineVarType = new Type[kArgSize];

        private Dictionary<int, int> offset2idx = new Dictionary<int, int>();
        private Dictionary<string, Type> genericMap = new Dictionary<string, Type>();

        public ILVirtualMachine() {}

        /// <summary>
        /// execute method ils, note that args[0] must be the obj own this method
        /// </summary>
        /// <returns></returns>
        public object Execute(MethodDefinition mtd, object[] args, MethodInfo mti = null)
        {
            var ilLst = mtd.Body.Instructions;

            // Print all IL
            var sb = new System.Text.StringBuilder();
            sb.AppendFormat("============================ execute new: {0}.{1} \t{2}", (args != null && args[0] != null) ? args[0].GetType().ToString() : "null", mtd, ilLst.Count);
            sb.AppendLine();
            foreach (var il in ilLst)
            {
                sb.AppendFormat("IL: {0}", il.ToString());
                sb.AppendLine();
            }
            Logger.Log(sb.ToString());

            methodDef = mtd;
            methodInfo = mti;
            machineStack.Clear();
            InitGenericMap();
            InitArgument(args);
            InitLocalVar();

            // save offset
            offset2idx.Clear();
            for (var idx = 0; idx != ilLst.Count; ++idx)
            {
                var il = ilLst[idx];
                offset2idx[il.Offset] = idx;
            }

            // execute
            var succ = true;
            SetEBP(0);
            while (true)
            {
                var ebp = GetEBP();
                var il = ilLst[ebp];
                SetEBP(ebp + 1);

                Logger.Log("Exe IL: {0}", il);
                if (!ExecuteIL(il))
                {
                    Logger.Error("ILRunner: execute il failed, {0}", il);
                    succ = false;
                    break;
                }
                Logger.Log("{0}", machineStack.ToString());

                if (il.Next == null)
                    break;
            }

            object ret = null;
            if (methodDef.ReturnType.FullName != "System.Void" && succ && machineStack.Count > 0)
                ret = machineStack.Pop();

            Logger.Log("============================ execute finished: {0}.{1} \t{2}", (args != null && args[0] != null) ? args[0].GetType().ToString() : "null", mtd, succ);
            Reset();

            return ret;
        }

        
        public void Reset()
        {
            methodDef = null;
            methodInfo = null;
            arguments = null;
            machineStack.Clear();
            machineVar = new object[kArgSize];
            machineVarType = new Type[kArgSize];
            genericMap.Clear();

            offset2idx.Clear();
            SetEBP(0);

            // TODO: may need clear VMAddr this vm used?
        }

        private int ebp = 0;
        private void SetEBP(int idx)
        {
            ebp = idx;
        }
        private int GetEBP()
        {
            return ebp;
        }

        private void InitArgument(object[] args)
        {
            arguments = args;
            if (methodDef.IsStatic)
            {
                for (var i = 0; i <= arguments.Length - 2; ++i)
                    arguments[i] = arguments[i + 1];
            }
        }

        private void AddGenericCache(TypeReference typeRef, Type typeInfo)
        {
            if (typeRef.IsGenericParameter)
            {
                if (!genericMap.ContainsKey(typeRef.Name))
                    genericMap.Add(typeRef.Name, typeInfo);
            }

            if (typeRef.IsGenericInstance)
            {
                var genericRef = typeRef as GenericInstanceType;
                for (var i = 0; i != genericRef.GenericArguments.Count; ++i)
                {
                    AddGenericCache(genericRef.GenericArguments[i], typeInfo.GetGenericArguments()[i]);
                }
            }
        }

        private void InitGenericMap()
        {
            genericMap.Clear();
            if (methodInfo != null && methodDef.ContainsGenericParameter)
            {
                // return type
                AddGenericCache(methodDef.ReturnType, methodInfo.ReturnType);

                // parameters
                for (var i = 0; i != methodDef.Parameters.Count; ++i)
                {
                    var param = methodDef.Parameters[i];
                    AddGenericCache(param.ParameterType, methodInfo.GetParameters()[i].ParameterType);
                }
            }
        }

        private void InitLocalVar()
        {
            var varLst = methodDef.Body.Variables;
            for (var i = 0; i != varLst.Count; ++i)
            {
                var varTypeRef = varLst[i].VariableType;
                Type varTypeInfo;
                if (varTypeRef.IsGenericParameter)
                    varTypeInfo = genericMap[varTypeRef.Name];
                else
                    varTypeInfo = GetTypeInfoFromTypeReference(varTypeRef);

                var varObj = varTypeInfo.IsValueType ? Activator.CreateInstance(varTypeInfo) : null;
                machineVar[i] = varObj;
                machineVarType[i] = varTypeInfo;
            }
        }

        private bool ExecuteIL(Instruction il)
        {
            switch (il.OpCode.Code)
            {
                case Code.Nop:
                    return ExecuteNop();
                case Code.Ret:
                    return ExecuteRet();
                case Code.Dup:
                    return ExecuteDup();
                case Code.Box:
                    return ExecuteBox();
                case Code.Unbox:
                case Code.Unbox_Any:
                    return ExecuteUnbox(il.Operand as TypeReference);
                case Code.Pop: 
                    return ExecutePop();
                case Code.Throw: 
                    return ExecuteThrow();

                case Code.Newobj:
                    return ExecuteNewobj(il.Operand as MethodReference);
                case Code.Newarr:
                    var typeRef = il.Operand as TypeReference;
                    var typeInfo = GetTypeInfoFromTypeReference(typeRef);
                    return ExecuteNewarr(typeInfo);
                //case Code.Initblk:
                case Code.Initobj: 
                    return ExecuteInitobj(il);
                case Code.Isinst: 
                    return ExecuteIsinst(il);
                case Code.Ldobj: 
                    return ExecuteLdobj(il);
                case Code.Stobj: 
                    return ExecuteStobj(il);

                case Code.Ldelem_I:
                case Code.Ldelem_I1:
                case Code.Ldelem_I2:
                case Code.Ldelem_I4:
                case Code.Ldelem_I8:
                case Code.Ldelem_R4:
                case Code.Ldelem_R8:
                case Code.Ldelem_U1:
                case Code.Ldelem_U2:
                case Code.Ldelem_U4:
                case Code.Ldelem_Ref:
                case Code.Ldelem_Any:
                    return ExecuteLoadElem();
                case Code.Ldelema:
                    return ExecuteLoadElemAddr();
                case Code.Ldtoken: 
                    return ExecuteLdtoken(il);
                case Code.Stelem_I:
                case Code.Stelem_I1:
                case Code.Stelem_I2:
                case Code.Stelem_I4:
                case Code.Stelem_I8:
                case Code.Stelem_R4:
                case Code.Stelem_R8:
                case Code.Stelem_Ref:
                case Code.Stelem_Any:
                    return ExecuteStoreElem();

                case Code.Ldnull:
                    return ExecuteLoad(null);
                case Code.Ldc_I4_0:
                    return ExecuteLoad(0);
                case Code.Ldc_I4_1:
                    return ExecuteLoad(1);
                case Code.Ldc_I4_2:
                    return ExecuteLoad(2);
                case Code.Ldc_I4_3:
                    return ExecuteLoad(3);
                case Code.Ldc_I4_4:
                    return ExecuteLoad(4);
                case Code.Ldc_I4_5:
                    return ExecuteLoad(5);
                case Code.Ldc_I4_6:
                    return ExecuteLoad(6);
                case Code.Ldc_I4_7:
                    return ExecuteLoad(7);
                case Code.Ldc_I4_8:
                    return ExecuteLoad(8);
                case Code.Ldc_I4_M1:
                    return ExecuteLoad(-1);
                case Code.Ldc_I4_S:
                    return ExecuteLoad(Convert.ChangeType(il.Operand, typeof(Int16)));
                case Code.Ldc_I4:
                    return ExecuteLoad(Convert.ChangeType(il.Operand, typeof(Int32)));
                case Code.Ldc_I8:
                    return ExecuteLoad(Convert.ChangeType(il.Operand, typeof(Int64)));
                case Code.Ldc_R4:
                    return ExecuteLoad(Convert.ChangeType(il.Operand, typeof(Single)));
                case Code.Ldc_R8:
                    return ExecuteLoad(Convert.ChangeType(il.Operand, typeof(Double)));
                case Code.Ldstr:
                    return ExecuteLoad(il.Operand);

                case Code.Ldloc_0:
                    return ExecuteLoadLocal(0);
                case Code.Ldloc_1:
                    return ExecuteLoadLocal(1);
                case Code.Ldloc_2:
                    return ExecuteLoadLocal(2);
                case Code.Ldloc_3:
                    return ExecuteLoadLocal(3);
                case Code.Ldloc_S:
                case Code.Ldloc:
                    int ldIdx;
                    if (il.Operand is VariableDefinition)
                        ldIdx = (il.Operand as VariableDefinition).Index;
                    else
                        ldIdx = (int)il.Operand;
                    return ExecuteLoadLocal(ldIdx);
                case Code.Ldloca_S: 
                case Code.Ldloca: 
                    int ldaIdx;
                    if (il.Operand is VariableDefinition)
                        ldaIdx = (il.Operand as VariableDefinition).Index;
                    else
                        ldaIdx = (int)il.Operand;
                    return ExecuteLoadLocalAddr(ldaIdx);
                case Code.Stloc_0:
                    return ExecuteStoreLocal(0);
                case Code.Stloc_1:
                    return ExecuteStoreLocal(1);
                case Code.Stloc_2:
                    return ExecuteStoreLocal(2);
                case Code.Stloc_3:
                    return ExecuteStoreLocal(3);
                case Code.Stloc_S:
                case Code.Stloc:
                    int stIdx;
                    if (il.Operand is VariableDefinition)
                        stIdx = (il.Operand as VariableDefinition).Index;
                    else
                        stIdx = (int)il.Operand;
                    return ExecuteStoreLocal(stIdx);

                case Code.Ldarg_0:
                    return ExecuteLoadArg(0);
                case Code.Ldarg_1:
                    return ExecuteLoadArg(1);
                case Code.Ldarg_2:
                    return ExecuteLoadArg(2);
                case Code.Ldarg_3:
                    return ExecuteLoadArg(3);
                case Code.Ldarg_S:
                case Code.Ldarg:
                    int ldargIdx;
                    if (il.Operand is ParameterDefinition)
                        ldargIdx = (il.Operand as ParameterDefinition).Index;
                    else
                        ldargIdx = (int)il.Operand;
                    return ExecuteLoadArg(ldargIdx);
                case Code.Ldarga:
                case Code.Ldarga_S:
                    int ldargaIdx;
                    if (il.Operand is ParameterDefinition)
                        ldargaIdx = (il.Operand as ParameterDefinition).Index;
                    else
                        ldargaIdx = (int)il.Operand;
                    return ExecuteLoadArgAddr(ldargaIdx);

                case Code.Ldfld:
                    var ldfldFieldDef = il.Operand as FieldDefinition;
                    if (ldfldFieldDef == null)
                        ldfldFieldDef = (il.Operand as FieldReference)?.Resolve();
                    Assert.IsNotNull(ldfldFieldDef, string.Format("Ldfld: invald il: {0} \t{1}", il.ToString(), il.Operand.GetType()));
                    return ExecuteLdfld(ldfldFieldDef);
                case Code.Ldflda:
                    var ldfldaFieldDef = il.Operand as FieldDefinition;
                    if (ldfldaFieldDef == null)
                        ldfldaFieldDef = (il.Operand as FieldReference)?.Resolve();
                    Assert.IsNotNull(ldfldaFieldDef, string.Format("Ldfld: invald il: {0} \t{1}", il.ToString(), il.Operand.GetType()));
                    return ExecuteLdflda(ldfldaFieldDef);
                case Code.Ldsfld:
                    var ldsfldFieldDef = il.Operand as FieldDefinition;
                    if (ldsfldFieldDef == null)
                        ldsfldFieldDef = (il.Operand as FieldReference)?.Resolve();
                    Assert.IsNotNull(ldsfldFieldDef, string.Format("Ldsfld: invald il: {0} \t{1}", il.ToString(), il.Operand.GetType()));
                    return ExecuteLdsfld(ldsfldFieldDef);
                case Code.Ldsflda:
                    var ldsfldaFieldDef = il.Operand as FieldDefinition;
                    if (ldsfldaFieldDef == null)
                        ldsfldaFieldDef = (il.Operand as FieldReference)?.Resolve();
                    Assert.IsNotNull(ldsfldaFieldDef, string.Format("Ldsfld: invald il: {0} \t{1}", il.ToString(), il.Operand.GetType()));
                    return ExecuteLdsflda(ldsfldaFieldDef);
                case Code.Stfld:
                    var stfldFieldDef = il.Operand as FieldDefinition;
                    if (stfldFieldDef == null)
                        stfldFieldDef = (il.Operand as FieldReference)?.Resolve();
                    Assert.IsNotNull(stfldFieldDef, string.Format("Stfld: invald il: {0} \t{1}", il.ToString(), il.Operand.GetType()));
                    return ExecuteStfld(stfldFieldDef);
                case Code.Stsfld:
                    var stsfldFieldDef = il.Operand as FieldDefinition;
                    if (stsfldFieldDef == null)
                        stsfldFieldDef = (il.Operand as FieldReference)?.Resolve();
                    Assert.IsNotNull(stsfldFieldDef, string.Format("Stsfld: invald il: {0} \t{1}", il.ToString(), il.Operand.GetType()));
                    return ExecuteStsfld(stsfldFieldDef);

                case Code.Ldind_I:
                case Code.Ldind_I1:
                case Code.Ldind_I2:
                case Code.Ldind_I4:
                case Code.Ldind_I8:
                case Code.Ldind_R4:
                case Code.Ldind_R8:
                case Code.Ldind_U1:
                case Code.Ldind_U2:
                case Code.Ldind_U4:
                case Code.Ldind_Ref:
                    return ExecuteLdind();
                case Code.Stind_I:
                case Code.Stind_I1:
                case Code.Stind_I2:
                case Code.Stind_I4:
                case Code.Stind_I8:
                case Code.Stind_R4:
                case Code.Stind_R8:
                case Code.Stind_Ref: 
                    return ExecuteStind();

                case Code.Add:
                case Code.Add_Ovf:
                case Code.Add_Ovf_Un:
                    return ExecuteAdd();
                case Code.Sub:
                case Code.Sub_Ovf:
                case Code.Sub_Ovf_Un:
                    return ExecuteSub();
                case Code.Mul:
                case Code.Mul_Ovf:
                case Code.Mul_Ovf_Un:
                    return ExecuteMul();
                case Code.Div:
                case Code.Div_Un:
                    return ExecuteDiv();
                case Code.Neg:
                    return ExecuteNeg();
                case Code.Shl:
                    return ExecuteShl();
                case Code.Shr:
                    return ExecuteShr();
                case Code.Shr_Un:
                    return ExecuteShrUn();

                case Code.And:
                    return ExecuteAnd();
                case Code.Or:
                    return ExecuteOr();
                case Code.Xor:
                    return ExecuteXor();
                case Code.Not:
                    return ExecuteNot();

                case Code.Conv_I1:
                case Code.Conv_Ovf_I1:
                case Code.Conv_Ovf_I1_Un:
                case Code.Conv_U1:
                case Code.Conv_Ovf_U1_Un:
                    return ExecuteConv(typeof(Byte));
                case Code.Conv_I2:
                case Code.Conv_Ovf_I2:
                case Code.Conv_Ovf_I2_Un:
                case Code.Conv_U2:
                case Code.Conv_Ovf_U2_Un:
                    return ExecuteConv(typeof(Int16));
                case Code.Conv_I4:
                case Code.Conv_Ovf_I4:
                case Code.Conv_Ovf_I4_Un:
                case Code.Conv_U4:
                case Code.Conv_Ovf_U4_Un:
                    return ExecuteConv(typeof(Int32));
                case Code.Conv_I8:
                case Code.Conv_Ovf_I8:
                case Code.Conv_Ovf_I8_Un:
                case Code.Conv_U8:
                case Code.Conv_Ovf_U8_Un:
                    return ExecuteConv(typeof(Int64));
                //case Code.Conv_I:
                //case Code.Conv_Ovf_I:
                //case Code.Conv_Ovf_I_Un:
                //case Code.Conv_U:
                //case Code.Conv_Ovf_U_Un:
                //    machineStack.Push((nint)machineStack.Pop());
                //    return true;
                case Code.Conv_R_Un:
                    return ExecuteConvR();
                case Code.Conv_R4:
                    return ExecuteConvR4();
                case Code.Conv_R8:
                    return ExecuteConvR8();

                case Code.Beq:
                case Code.Beq_S:
                    return ExecuteBeq((il.Operand as Instruction).Offset);
                case Code.Bne_Un:
                case Code.Bne_Un_S:
                    return ExecuteBne((il.Operand as Instruction).Offset);
                case Code.Bge:
                case Code.Bge_S:
                case Code.Bge_Un:
                case Code.Bge_Un_S:
                    return ExecuteBge((il.Operand as Instruction).Offset);
                case Code.Bgt:
                case Code.Bgt_S:
                case Code.Bgt_Un:
                case Code.Bgt_Un_S:
                    return ExecuteBgt((il.Operand as Instruction).Offset);
                case Code.Ble:
                case Code.Ble_S:
                case Code.Ble_Un:
                case Code.Ble_Un_S:
                    return ExecuteBle((il.Operand as Instruction).Offset);
                case Code.Blt:
                case Code.Blt_S:
                case Code.Blt_Un:
                case Code.Blt_Un_S:
                    return ExecuteBlt((il.Operand as Instruction).Offset);
                case Code.Brtrue:
                case Code.Brtrue_S:
                    return ExecuteBrtrue((il.Operand as Instruction).Offset);
                case Code.Brfalse:
                case Code.Brfalse_S:
                    return ExecuteBrfalse((il.Operand as Instruction).Offset);
                case Code.Br:
                case Code.Br_S:
                    return ExecuteBr((il.Operand as Instruction).Offset);
                case Code.Leave: 
                case Code.Leave_S: 
                    return ExecuteLeave((il.Operand as Instruction).Offset);

                case Code.Ceq:
                    return ExecuteCeq();
                case Code.Cgt:
                case Code.Cgt_Un:
                    return ExecuteCgt();
                case Code.Clt:
                case Code.Clt_Un:
                    return ExecuteClt();

                case Code.Constrained: 
                    return ExecuteConstrained(il.Operand as TypeDefinition);
                case Code.Call:
                    return ExecuteCall(il.Operand as MethodReference);
                case Code.Callvirt:
                    return ExecuteCallvirt(il.Operand as MethodReference);

                default:
                    return ExecuteFailed(il);
            }
        }

        private bool ExecuteNop()
        {
            return true;
        }

        private bool ExecuteRet()
        {
            // return value already save in stack
            return true;
        }

        private bool ExecuteDup()
        {
            machineStack.Push(machineStack.Peek());
            return true;
        }

        private bool ExecuteBox()
        {
            // value in stack already boxed
            return true;
        }

        private bool ExecuteUnbox(TypeReference typeRef)
        {
            return true;
        }

        private bool ExecutePop()
        {
            machineStack.Pop();
            return true;
        }

        private bool ExecuteThrow()
        {
            var e = machineStack.Pop() as System.Exception;
            throw e;
        }

        private bool ExecuteNewobj(MethodReference methodRef)
        {
            return ExecuteCallConstructor(methodRef, false);
        }

        private bool ExecuteNewarr(Type elemType)
        {
            var len = (int)machineStack.Pop();
            var arr = Array.CreateInstance(elemType, len);
            machineStack.Push(arr);
            return true;
        }

        private bool ExecuteInitobj(Instruction il)
        {
            TypeDefinition typeDef;
            Type typeInfo;
            if (il.Operand is TypeReference)
            {
                typeInfo = GetTypeInfoFromTypeReference(il.Operand as TypeReference);
                typeDef = (il.Operand as TypeReference).Resolve();
            }
            else
            {
                typeDef = il.Operand as TypeDefinition;
                typeInfo = GetTypeByName(typeDef.FullName);
            }

            var addr = machineStack.Pop() as VMAddr;
            var obj = addr.GetObj();
            if (typeInfo.IsValueType)
                obj = Activator.CreateInstance(typeInfo);
            else
                obj = null;
            addr.SetObj(obj);
            return true;
        }

        private bool ExecuteIsinst(Instruction il)
        {
            var obj = machineStack.Pop();
            var typeRef = il.Operand as TypeReference;
            var typeInfo = GetTypeInfoFromTypeReference(typeRef);
            if (typeInfo.IsAssignableFrom(obj.GetType()))
                machineStack.Push(obj);
            else
                machineStack.Push(null);
            return true;
        }

        private bool ExecuteLdobj(Instruction il)
        {
            var addr = machineStack.Pop() as VMAddr;
            var obj = addr.GetObj();
            machineStack.Push(obj);
            return true;
        }

        private bool ExecuteStobj(Instruction il)
        {
            var obj = machineStack.Pop();
            var addr = machineStack.Pop() as VMAddr;
            addr.SetObj(obj);
            return true;
        }

        private bool ExecuteLoadElem()
        {
            var idx = (int)machineStack.Pop();
            var arr = machineStack.Pop() as Array;
            machineStack.Push(arr.GetValue(idx));
            return true;
        }

        private bool ExecuteLoadElemAddr()
        {
            var idx = (int)machineStack.Pop();
            var arr = machineStack.Pop() as Array;
            var addr = VMAddrForArray.Create(new VMAddrForArray.VMAddrForArrayData(arr, idx));
            machineStack.Push(addr);
            return true;
        }
        
        private bool ExecuteLdtoken(Instruction il)
        {
            if (il.Operand is TypeReference)
            {
                var typeRef = il.Operand as TypeReference;
                var typeInfo = GetTypeInfoFromTypeReference(typeRef);
                machineStack.Push(typeInfo.TypeHandle);
                return true;
            }

            if (il.Operand is TypeDefinition)
            {
                var typeDef = il.Operand as TypeDefinition;
                var typeInfo = GetTypeByName(typeDef.FullName);
                machineStack.Push(typeInfo.TypeHandle);
                return true;
            }

            Logger.Error("ExecuteLdtoken: not support: {0}", il.Operand);
            return false;
        }

        private bool ExecuteStoreElem()
        {
            var val = machineStack.Pop();
            var idx = (int)machineStack.Pop();
            var arr = machineStack.Pop() as Array;
            arr.SetValue(val, idx);
            return true;
        }

        private bool ExecuteLoad(object value)
        {
            machineStack.Push(value);
            return true;
        }
        
        private bool ExecuteStoreLocal(int index)
        {
            var objVar = machineStack.Pop();
            var addr = objVar as VMAddr;
            if (addr != null)
                objVar = addr.GetObj();

            var curType = objVar.GetType();
            var tarType = machineVarType[index];
            object result = objVar;
            if (!tarType.IsAssignableFrom(curType))
            {
                if (tarType == typeof(Boolean)) // hack here, handle store int to bool type
                {
                    result = (Int32)Convert.ChangeType(objVar, typeof(Int32)) != 0;
                }
                else if (tarType.IsEnum)        // handle store int to enum type
                {
                    result = Enum.ToObject(tarType, objVar);
                }
                else
                {
                    result = Convert.ChangeType(objVar, tarType);
                }
            }

            if (addr != null)
                addr.SetObj(result);
            machineVar[index] = result;
            return true;
        }

        private bool ExecuteLoadLocal(int index)
        {
            machineStack.Push(machineVar[index]);
            return true;
        }

        private bool ExecuteLoadLocalAddr(int index)
        {
            var addr = VMAddr.Create(machineVar[index]);
            machineVar[index] = addr;
            machineStack.Push(addr);
            return true;
        }

        private bool ExecuteLoadArg(int index)
        {
            if (arguments == null)
                machineStack.Push(null);
            else
                machineStack.Push(arguments[index]);
            return true;
        }

        private bool ExecuteLoadArgAddr(int index)
        {
            var addr = VMAddr.Create(arguments[index]);
            arguments[index] = addr;
            machineStack.Push(addr);
            return true;
        }

        private bool ExecuteLdfld(FieldDefinition fieldDef)
        {
            var obj = machineStack.Pop();
            var addr = obj as VMAddr;
            if (addr != null)
                obj = addr.GetObj();

            var typeInfo = obj.GetType();
            var bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            FieldInfo fieldInfo = null;
            while (typeInfo != null)
            {
                fieldInfo = typeInfo.GetField(fieldDef.Name, bindingFlags);
                if (fieldInfo != null)
                    break;
                typeInfo = typeInfo.BaseType;
            }
            Assert.IsNotNull(fieldInfo, string.Format("fieldInfo {0} not found in {1}", fieldDef.Name, obj.GetType()));

            var val = fieldInfo.GetValue(obj);
            machineStack.Push(val);
            return true;
        }

        private bool ExecuteLdflda(FieldDefinition fieldDef)
        {
            var obj = machineStack.Pop();
            var addr = obj as VMAddr;
            if (addr != null)
                obj = addr.GetObj();

            var typeInfo = obj.GetType();
            var bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            FieldInfo fieldInfo = null;
            while (typeInfo != null)
            {
                fieldInfo = typeInfo.GetField(fieldDef.Name, bindingFlags);
                if (fieldInfo != null)
                    break;
                typeInfo = typeInfo.BaseType;
            }
            Assert.IsNotNull(fieldInfo, string.Format("fieldInfo {0} not found in {1}", fieldDef.Name, obj.GetType()));

            var fieldAddr = VMAddrForFieldInfo.Create(new VMAddrForFieldInfo.VMAddrForFieldInfoData(fieldInfo, obj));
            machineStack.Push(fieldAddr);
            return true;
        }

        private bool ExecuteLdsfld(FieldDefinition fieldDef)
        {
            var typeInfo = GetTypeByName(fieldDef.DeclaringType.FullName);
            var bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            FieldInfo fieldInfo = null;
            while (typeInfo != null)
            {
                fieldInfo = typeInfo.GetField(fieldDef.Name, bindingFlags);
                if (fieldInfo != null)
                    break;
                typeInfo = typeInfo.BaseType;
            }
            Assert.IsNotNull(fieldInfo, string.Format("fieldInfo {0} not found in {1}", fieldDef.Name, fieldDef.DeclaringType.FullName));

            var val = fieldInfo.GetValue(null);
            machineStack.Push(val);
            return true;
        }

        private bool ExecuteLdsflda(FieldDefinition fieldDef)
        {
            var typeInfo = GetTypeByName(fieldDef.DeclaringType.FullName);
            var bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            FieldInfo fieldInfo = null;
            while (typeInfo != null)
            {
                fieldInfo = typeInfo.GetField(fieldDef.Name, bindingFlags);
                if (fieldInfo != null)
                    break;
                typeInfo = typeInfo.BaseType;
            }
            Assert.IsNotNull(fieldInfo, string.Format("fieldInfo {0} not found in {1}", fieldDef.Name, fieldDef.DeclaringType.FullName));

            var fieldAddr = VMAddrForFieldInfo.Create(new VMAddrForFieldInfo.VMAddrForFieldInfoData(fieldInfo, null));
            machineStack.Push(fieldAddr);
            return true;
        }

        private bool ExecuteStfld(FieldDefinition fieldDef)
        {
            var val = machineStack.Pop();
            var obj = machineStack.Pop();

            // may ref by pointer
            var addr = obj as VMAddr;
            if (addr != null)
                obj = addr.GetObj();

            var typeInfo = obj.GetType();
            var fieldInfo = typeInfo.GetField(fieldDef.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);

            // special handle for boolean
            if (fieldInfo.FieldType == typeof(System.Boolean) && val.GetType() == typeof(System.Int32))
                val = (System.Int32)val == 1;
            fieldInfo.SetValue(obj, val);

            if (addr != null)
                addr.SetObj(obj);

            return true;
        }

        private bool ExecuteStsfld(FieldDefinition fieldDef)
        {
            var classType = GetTypeByName(fieldDef.DeclaringType.FullName);
            var fieldInfo = classType.GetField(fieldDef.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            var val = machineStack.Pop();
            fieldInfo.SetValue(null, val);
            // special handle for boolean
            if (fieldInfo.FieldType == typeof(System.Boolean) && val.GetType() == typeof(System.Int32))
                val = (System.Int32)val == 1;
            fieldInfo.SetValue(null, val);
            return true;
        }

        private bool ExecuteLdind()
        {
            var addr = machineStack.Pop() as VMAddr;
            Assert.IsNotNull(addr);
            var val = addr.GetObj();
            machineStack.Push(val);
            return false;
        }

        private bool ExecuteStind()
        {
            var val = machineStack.Pop();
            var addr = machineStack.Pop() as VMAddr;
            Assert.IsNotNull(addr);
            addr.SetObj(val);
            return true;
        }
        
        private int GetPriorityOfNumber(object num)
        {
            if (num is Boolean)
                return 1;
            if (num is Byte || num is SByte)
                return 2;
            if (num is Int16 || num is UInt16)
                return 3;
            if (num is Int32 || num is UInt32)
                return 4;
            if (num is Int64 || num is UInt64)
                return 5;
            if (num is Single)
                return 6;
            if (num is Double)
                return 7;

            return -1;
        }

        private bool ExecuteAdd()
        {
            var b = machineStack.Pop();
            var a = machineStack.Pop();

            var aPriority = GetPriorityOfNumber(a);
            var bPriority = GetPriorityOfNumber(b);
            var val = aPriority > bPriority ? a : b;
            if (val is Byte)
                machineStack.Push((Byte)Convert.ChangeType(a, typeof(Byte)) + (Byte)Convert.ChangeType(b, typeof(Byte)));
            else if (val is Int16)
                machineStack.Push((Int16)Convert.ChangeType(a, typeof(Int16)) + (Int16)Convert.ChangeType(b, typeof(Int16)));
            else if (val is Int32)
                machineStack.Push((Int32)Convert.ChangeType(a, typeof(Int32)) + (Int32)Convert.ChangeType(b, typeof(Int32)));
            else if (val is Int64)
                machineStack.Push((Int64)Convert.ChangeType(a, typeof(Int64)) + (Int64)Convert.ChangeType(b, typeof(Int64)));
            else if (val is UInt16)
                machineStack.Push((UInt16)Convert.ChangeType(a, typeof(UInt16)) + (UInt16)Convert.ChangeType(b, typeof(UInt16)));
            else if (val is UInt32)
                machineStack.Push((UInt32)Convert.ChangeType(a, typeof(UInt32)) + (UInt32)Convert.ChangeType(b, typeof(UInt32)));
            else if (val is UInt64)
                machineStack.Push((UInt64)Convert.ChangeType(a, typeof(UInt64)) + (UInt64)Convert.ChangeType(b, typeof(UInt64)));
            else if (val is Single)
                machineStack.Push((Single)Convert.ChangeType(a, typeof(Single)) + (Single)Convert.ChangeType(b, typeof(Single)));
            else if (val is Double)
                machineStack.Push((Double)Convert.ChangeType(a, typeof(Double)) + (Double)Convert.ChangeType(b, typeof(Double)));

            return true;
        }

        private bool ExecuteSub()
        {
            var b = machineStack.Pop();
            var a = machineStack.Pop();

            var aPriority = GetPriorityOfNumber(a);
            var bPriority = GetPriorityOfNumber(b);
            var val = aPriority > bPriority ? a : b;
            if (val is Byte)
                machineStack.Push((Byte)Convert.ChangeType(a, typeof(Byte)) - (Byte)Convert.ChangeType(b, typeof(Byte)));
            else if (val is Int16)
                machineStack.Push((Int16)Convert.ChangeType(a, typeof(Int16)) - (Int16)Convert.ChangeType(b, typeof(Int16)));
            else if (val is Int32)
                machineStack.Push((Int32)Convert.ChangeType(a, typeof(Int32)) - (Int32)Convert.ChangeType(b, typeof(Int32)));
            else if (val is Int64)
                machineStack.Push((Int64)Convert.ChangeType(a, typeof(Int64)) - (Int64)Convert.ChangeType(b, typeof(Int64)));
            else if (val is UInt16)
                machineStack.Push((UInt16)Convert.ChangeType(a, typeof(UInt16)) - (UInt16)Convert.ChangeType(b, typeof(UInt16)));
            else if (val is UInt32)
                machineStack.Push((UInt32)Convert.ChangeType(a, typeof(UInt32)) - (UInt32)Convert.ChangeType(b, typeof(UInt32)));
            else if (val is UInt64)
                machineStack.Push((UInt64)Convert.ChangeType(a, typeof(UInt64)) - (UInt64)Convert.ChangeType(b, typeof(UInt64)));
            else if (val is Single)
                machineStack.Push((Single)Convert.ChangeType(a, typeof(Single)) - (Single)Convert.ChangeType(b, typeof(Single)));
            else if (val is Double)
                machineStack.Push((Double)Convert.ChangeType(a, typeof(Double)) - (Double)Convert.ChangeType(b, typeof(Double)));

            return true;
        }

        private bool ExecuteMul()
        {
            var b = machineStack.Pop();
            var a = machineStack.Pop();

            var aPriority = GetPriorityOfNumber(a);
            var bPriority = GetPriorityOfNumber(b);
            var val = aPriority > bPriority ? a : b;
            if (val is Byte)
                machineStack.Push((Byte)Convert.ChangeType(a, typeof(Byte)) * (Byte)Convert.ChangeType(b, typeof(Byte)));
            else if (val is Int16)
                machineStack.Push((Int16)Convert.ChangeType(a, typeof(Int16)) * (Int16)Convert.ChangeType(b, typeof(Int16)));
            else if (val is Int32)
                machineStack.Push((Int32)Convert.ChangeType(a, typeof(Int32)) * (Int32)Convert.ChangeType(b, typeof(Int32)));
            else if (val is Int64)
                machineStack.Push((Int64)Convert.ChangeType(a, typeof(Int64)) * (Int64)Convert.ChangeType(b, typeof(Int64)));
            else if (val is UInt16)
                machineStack.Push((UInt16)Convert.ChangeType(a, typeof(UInt16)) * (UInt16)Convert.ChangeType(b, typeof(UInt16)));
            else if (val is UInt32)
                machineStack.Push((UInt32)Convert.ChangeType(a, typeof(UInt32)) * (UInt32)Convert.ChangeType(b, typeof(UInt32)));
            else if (val is UInt64)
                machineStack.Push((UInt64)Convert.ChangeType(a, typeof(UInt64)) * (UInt64)Convert.ChangeType(b, typeof(UInt64)));
            else if (val is Single)
                machineStack.Push((Single)Convert.ChangeType(a, typeof(Single)) * (Single)Convert.ChangeType(b, typeof(Single)));
            else if (val is Double)
                machineStack.Push((Double)Convert.ChangeType(a, typeof(Double)) * (Double)Convert.ChangeType(b, typeof(Double)));

            return true;
        }

        private bool ExecuteDiv()
        {
            var b = machineStack.Pop();
            var a = machineStack.Pop();

            var aPriority = GetPriorityOfNumber(a);
            var bPriority = GetPriorityOfNumber(b);
            var val = aPriority > bPriority ? a : b;
            if (val is Byte)
                machineStack.Push((Byte)Convert.ChangeType(a, typeof(Byte)) / (Byte)Convert.ChangeType(b, typeof(Byte)));
            else if (val is Int16)
                machineStack.Push((Int16)Convert.ChangeType(a, typeof(Int16)) / (Int16)Convert.ChangeType(b, typeof(Int16)));
            else if (val is Int32)
                machineStack.Push((Int32)Convert.ChangeType(a, typeof(Int32)) / (Int32)Convert.ChangeType(b, typeof(Int32)));
            else if (val is Int64)
                machineStack.Push((Int64)Convert.ChangeType(a, typeof(Int64)) / (Int64)Convert.ChangeType(b, typeof(Int64)));
            else if (val is UInt16)
                machineStack.Push((UInt16)Convert.ChangeType(a, typeof(UInt16)) / (UInt16)Convert.ChangeType(b, typeof(UInt16)));
            else if (val is UInt32)
                machineStack.Push((UInt32)Convert.ChangeType(a, typeof(UInt32)) / (UInt32)Convert.ChangeType(b, typeof(UInt32)));
            else if (val is UInt64)
                machineStack.Push((UInt64)Convert.ChangeType(a, typeof(UInt64)) / (UInt64)Convert.ChangeType(b, typeof(UInt64)));
            else if (val is Single)
                machineStack.Push((Single)Convert.ChangeType(a, typeof(Single)) / (Single)Convert.ChangeType(b, typeof(Single)));
            else if (val is Double)
                machineStack.Push((Double)Convert.ChangeType(a, typeof(Double)) / (Double)Convert.ChangeType(b, typeof(Double)));

            return true;
        }

        private bool ExecuteNeg()
        {
            var val = machineStack.Pop();
            if (val is Byte)
                machineStack.Push(-(Byte)val);
            else if (val is Int16)
                machineStack.Push(-(Int16)val);
            else if (val is Int32)
                machineStack.Push(-(Int32)val);
            else if (val is Int64)
                machineStack.Push(-(Int64)val);
            else if (val is Single)
                machineStack.Push(-(Single)val);
            else if (val is Double)
                machineStack.Push(-(Double)val);
            return true;
        }

        private bool ExecuteShl()
        {
            var shl = (Int32)machineStack.Pop();
            var val = machineStack.Pop();
            if (val is Int64)
            {
                var tmp = (Int64)val;
                tmp = tmp << shl;
                machineStack.Push(tmp);
            }
            else
            {
                var tmp = (Int32)Convert.ChangeType(val, typeof(Int32));
                tmp = tmp << shl;
                machineStack.Push(tmp);
            }
            return true;
        }

        private bool ExecuteShr()
        {
            var shl = (Int32)machineStack.Pop();
            var val = machineStack.Pop();
            if (val is Int64)
            {
                var tmp = (Int64)val;
                tmp = tmp >> shl;
                machineStack.Push(tmp);
            }
            else
            {
                var tmp = (Int32)Convert.ChangeType(val, typeof(Int32));
                tmp = tmp >> shl;
                machineStack.Push(tmp);
            }
            return true;
        }

        private bool ExecuteShrUn()
        {
            var shl = (Int32)machineStack.Pop();
            var val = machineStack.Pop();
            if (val is UInt64)
            {
                var tmp = (UInt64)val;
                tmp = tmp >> shl;
                machineStack.Push(tmp);
            }
            else
            {
                var tmp = (UInt32)Convert.ChangeType(val, typeof(UInt32));
                tmp = tmp >> shl;
                machineStack.Push(tmp);
            }
            return true;
        }

        private bool ExecuteAnd()
        {
            var b = machineStack.Pop();
            var a = machineStack.Pop();
            
            var aPriority = GetPriorityOfNumber(a);
            var bPriority = GetPriorityOfNumber(b);
            var val = aPriority > bPriority ? a : b;
            object result;
            if (val is Byte)
                result = ((Byte)Convert.ChangeType(a, typeof(Byte)) & (Byte)Convert.ChangeType(b, typeof(Byte)));
            if (val is Boolean)
                result = ((Boolean)Convert.ChangeType(a, typeof(Boolean)) & (Boolean)Convert.ChangeType(b, typeof(Boolean)));
            else if (val is Int16)
                result = ((Int16)Convert.ChangeType(a, typeof(Int16)) & (Int16)Convert.ChangeType(b, typeof(Int16)));
            else if (val is Int32)
                result = ((Int32)Convert.ChangeType(a, typeof(Int32)) & (Int32)Convert.ChangeType(b, typeof(Int32)));
            else if (val is Int64)
                result = ((Int64)Convert.ChangeType(a, typeof(Int64)) & (Int64)Convert.ChangeType(b, typeof(Int64)));
            else if (val is UInt16)
                result = ((UInt16)Convert.ChangeType(a, typeof(UInt16)) & (UInt16)Convert.ChangeType(b, typeof(UInt16)));
            else if (val is UInt32)
                result = ((UInt32)Convert.ChangeType(a, typeof(UInt32)) & (UInt32)Convert.ChangeType(b, typeof(UInt32)));
            else if (val is UInt64)
                result = ((UInt64)Convert.ChangeType(a, typeof(UInt64)) & (UInt64)Convert.ChangeType(b, typeof(UInt64)));
            else
                return false;

            machineStack.Push(result);
            return true;
        }

        private bool ExecuteOr()
        {
            var b = machineStack.Pop();
            var a = machineStack.Pop();
            
            var aPriority = GetPriorityOfNumber(a);
            var bPriority = GetPriorityOfNumber(b);
            var val = aPriority > bPriority ? a : b;
            object result;
            if (val is Byte)
                result = ((Byte)Convert.ChangeType(a, typeof(Byte)) | (Byte)Convert.ChangeType(b, typeof(Byte)));
            if (val is Boolean)
                result = ((Boolean)Convert.ChangeType(a, typeof(Boolean)) | (Boolean)Convert.ChangeType(b, typeof(Boolean)));
            else if (val is Int16)
                result = ((Int16)Convert.ChangeType(a, typeof(Int16)) | (Int16)Convert.ChangeType(b, typeof(Int16)));
            else if (val is Int32)
                result = ((Int32)Convert.ChangeType(a, typeof(Int32)) | (Int32)Convert.ChangeType(b, typeof(Int32)));
            else if (val is Int64)
                result = ((Int64)Convert.ChangeType(a, typeof(Int64)) | (Int64)Convert.ChangeType(b, typeof(Int64)));
            else if (val is UInt16)
                result = ((UInt16)Convert.ChangeType(a, typeof(UInt16)) | (UInt16)Convert.ChangeType(b, typeof(UInt16)));
            else if (val is UInt32)
                result = ((UInt32)Convert.ChangeType(a, typeof(UInt32)) | (UInt32)Convert.ChangeType(b, typeof(UInt32)));
            else if (val is UInt64)
                result = ((UInt64)Convert.ChangeType(a, typeof(UInt64)) | (UInt64)Convert.ChangeType(b, typeof(UInt64)));
            else
                return false;

            machineStack.Push(result);
            return true;
        }

        private bool ExecuteXor()
        {
            var b = machineStack.Pop();
            var a = machineStack.Pop();
            
            var aPriority = GetPriorityOfNumber(a);
            var bPriority = GetPriorityOfNumber(b);
            var val = aPriority > bPriority ? a : b;
            object result;
            if (val is Byte)
                result = ((Byte)Convert.ChangeType(a, typeof(Byte)) ^ (Byte)Convert.ChangeType(b, typeof(Byte)));
            if (val is Boolean)
                result = ((Boolean)Convert.ChangeType(a, typeof(Boolean)) ^ (Boolean)Convert.ChangeType(b, typeof(Boolean)));
            else if (val is Int16)
                result = ((Int16)Convert.ChangeType(a, typeof(Int16)) ^ (Int16)Convert.ChangeType(b, typeof(Int16)));
            else if (val is Int32)
                result = ((Int32)Convert.ChangeType(a, typeof(Int32)) ^ (Int32)Convert.ChangeType(b, typeof(Int32)));
            else if (val is Int64)
                result = ((Int64)Convert.ChangeType(a, typeof(Int64)) ^ (Int64)Convert.ChangeType(b, typeof(Int64)));
            else if (val is UInt16)
                result = ((UInt16)Convert.ChangeType(a, typeof(UInt16)) ^ (UInt16)Convert.ChangeType(b, typeof(UInt16)));
            else if (val is UInt32)
                result = ((UInt32)Convert.ChangeType(a, typeof(UInt32)) ^ (UInt32)Convert.ChangeType(b, typeof(UInt32)));
            else if (val is UInt64)
                result = ((UInt64)Convert.ChangeType(a, typeof(UInt64)) ^ (UInt64)Convert.ChangeType(b, typeof(UInt64)));
            else
                return false;

            machineStack.Push(result);
            return true;
        }

        private bool ExecuteNot()
        {
            var val = (Boolean)machineStack.Pop();
            machineStack.Push(!val);
            return true;
        }

        private bool ExecuteConv(Type type)
        {
            var val = machineStack.Pop();
            val = Convert.ChangeType(val, type);
            machineStack.Push(val);
            return true;
        }
        
        private bool ExecuteConvR()
        {
            var val = machineStack.Pop();
            val = Convert.ChangeType(val, typeof(UInt32));
            machineStack.Push((float)val);
            return true;
        }

        private bool ExecuteConvR4()
        {
            var val = machineStack.Pop();
            val = Convert.ChangeType(val, typeof(float));
            machineStack.Push(val);
            return true;
        }

        private bool ExecuteConvR8()
        {
            var val = machineStack.Pop();
            val = Convert.ChangeType(val, typeof(double));
            val = Convert.ChangeType(val, typeof(float));
            machineStack.Push(val);
            return true;
        }
        
        private bool ExecuteBeq(int offset)
        {
            var b = machineStack.Pop();
            var a = machineStack.Pop();
            
            var aPriority = GetPriorityOfNumber(a);
            var bPriority = GetPriorityOfNumber(b);
            var val = aPriority > bPriority ? a : b;
            var result = false;
            if (val is Boolean)
                result = (Boolean)a == (Boolean)b;
            else if (val is Byte)
                result = ((Byte)Convert.ChangeType(a, typeof(Byte)) == (Byte)Convert.ChangeType(b, typeof(Byte)));
            else if (val is Int16)
                result = ((Int16)Convert.ChangeType(a, typeof(Int16)) == (Int16)Convert.ChangeType(b, typeof(Int16)));
            else if (val is Int32)
                result = ((Int32)Convert.ChangeType(a, typeof(Int32)) == (Int32)Convert.ChangeType(b, typeof(Int32)));
            else if (val is Int64)
                result = ((Int64)Convert.ChangeType(a, typeof(Int64)) == (Int64)Convert.ChangeType(b, typeof(Int64)));
            else if (val is SByte)
                result = ((SByte)Convert.ChangeType(a, typeof(SByte)) == (SByte)Convert.ChangeType(b, typeof(SByte)));
            else if (val is UInt16)
                result = ((UInt16)Convert.ChangeType(a, typeof(UInt16)) == (UInt16)Convert.ChangeType(b, typeof(UInt16)));
            else if (val is UInt32)
                result = ((UInt32)Convert.ChangeType(a, typeof(UInt32)) == (UInt32)Convert.ChangeType(b, typeof(UInt32)));
            else if (val is UInt64)
                result = ((UInt64)Convert.ChangeType(a, typeof(UInt64)) == (UInt64)Convert.ChangeType(b, typeof(UInt64)));
            else if (val is Single)
                result = ((Single)Convert.ChangeType(a, typeof(Single)) == (Single)Convert.ChangeType(b, typeof(Single)));
            else if (val is Double)
                result = ((Double)Convert.ChangeType(a, typeof(Double)) == (Double)Convert.ChangeType(b, typeof(Double)));
            else if (val is Enum)
                result = (Int32)a == (Int32)b;
            else
                return false;

            if (result)
                return ExecuteBr(offset);

            return true;
        }

        private bool ExecuteBne(int offset)
        {
            var b = machineStack.Pop();
            var a = machineStack.Pop();
            
            var aPriority = GetPriorityOfNumber(a);
            var bPriority = GetPriorityOfNumber(b);
            var val = aPriority > bPriority ? a : b;
            var result = false;
            if (val is Boolean)
                result = (Boolean)a != (Boolean)b;
            else if (val is Byte)
                result = ((Byte)Convert.ChangeType(a, typeof(Byte)) != (Byte)Convert.ChangeType(b, typeof(Byte)));
            else if (val is Int16)
                result = ((Int16)Convert.ChangeType(a, typeof(Int16)) != (Int16)Convert.ChangeType(b, typeof(Int16)));
            else if (val is Int32)
                result = ((Int32)Convert.ChangeType(a, typeof(Int32)) != (Int32)Convert.ChangeType(b, typeof(Int32)));
            else if (val is Int64)
                result = ((Int64)Convert.ChangeType(a, typeof(Int64)) != (Int64)Convert.ChangeType(b, typeof(Int64)));
            else if (val is SByte)
                result = ((SByte)Convert.ChangeType(a, typeof(SByte)) != (SByte)Convert.ChangeType(b, typeof(SByte)));
            else if (val is UInt16)
                result = ((UInt16)Convert.ChangeType(a, typeof(UInt16)) != (UInt16)Convert.ChangeType(b, typeof(UInt16)));
            else if (val is UInt32)
                result = ((UInt32)Convert.ChangeType(a, typeof(UInt32)) != (UInt32)Convert.ChangeType(b, typeof(UInt32)));
            else if (val is UInt64)
                result = ((UInt64)Convert.ChangeType(a, typeof(UInt64)) != (UInt64)Convert.ChangeType(b, typeof(UInt64)));
            else if (val is Single)
                result = ((Single)Convert.ChangeType(a, typeof(Single)) != (Single)Convert.ChangeType(b, typeof(Single)));
            else if (val is Double)
                result = ((Double)Convert.ChangeType(a, typeof(Double)) != (Double)Convert.ChangeType(b, typeof(Double)));
            else if (val is Enum)
                result = (Int32)a != (Int32)b;
            else
                return false;

            if (result)
                return ExecuteBr(offset);

            return true;
        }

        private bool ExecuteBge(int offset)
        {
            var b = machineStack.Pop();
            var a = machineStack.Pop();

            var aPriority = GetPriorityOfNumber(a);
            var bPriority = GetPriorityOfNumber(b);
            var val = aPriority > bPriority ? a : b;
            var result = false;
            if (val is Byte)
                result = ((Byte)Convert.ChangeType(a, typeof(Byte)) >= (Byte)Convert.ChangeType(b, typeof(Byte)));
            else if (val is Int16)
                result = ((Int16)Convert.ChangeType(a, typeof(Int16)) >= (Int16)Convert.ChangeType(b, typeof(Int16)));
            else if (val is Int32)
                result = ((Int32)Convert.ChangeType(a, typeof(Int32)) >= (Int32)Convert.ChangeType(b, typeof(Int32)));
            else if (val is Int64)
                result = ((Int64)Convert.ChangeType(a, typeof(Int64)) >= (Int64)Convert.ChangeType(b, typeof(Int64)));
            else if (val is SByte)
                result = ((SByte)Convert.ChangeType(a, typeof(SByte)) >= (SByte)Convert.ChangeType(b, typeof(SByte)));
            else if (val is UInt16)
                result = ((UInt16)Convert.ChangeType(a, typeof(UInt16)) >= (UInt16)Convert.ChangeType(b, typeof(UInt16)));
            else if (val is UInt32)
                result = ((UInt32)Convert.ChangeType(a, typeof(UInt32)) >= (UInt32)Convert.ChangeType(b, typeof(UInt32)));
            else if (val is UInt64)
                result = ((UInt64)Convert.ChangeType(a, typeof(UInt64)) >= (UInt64)Convert.ChangeType(b, typeof(UInt64)));
            else if (val is Single)
                result = ((Single)Convert.ChangeType(a, typeof(Single)) >= (Single)Convert.ChangeType(b, typeof(Single)));
            else if (val is Double)
                result = ((Double)Convert.ChangeType(a, typeof(Double)) >= (Double)Convert.ChangeType(b, typeof(Double)));
            else if (val is Enum)
                result = (Int32)a >= (Int32)b;
            else
                return false;

            if (result)
                return ExecuteBr(offset);

            return true;
        }

        private bool ExecuteBgt(int offset)
        {
            var b = machineStack.Pop();
            var a = machineStack.Pop();

            var aPriority = GetPriorityOfNumber(a);
            var bPriority = GetPriorityOfNumber(b);
            var val = aPriority > bPriority ? a : b;
            var result = false;
            if (val is Byte)
                result = ((Byte)Convert.ChangeType(a, typeof(Byte)) > (Byte)Convert.ChangeType(b, typeof(Byte)));
            else if (val is Int16)
                result = ((Int16)Convert.ChangeType(a, typeof(Int16)) > (Int16)Convert.ChangeType(b, typeof(Int16)));
            else if (val is Int32)
                result = ((Int32)Convert.ChangeType(a, typeof(Int32)) > (Int32)Convert.ChangeType(b, typeof(Int32)));
            else if (val is Int64)
                result = ((Int64)Convert.ChangeType(a, typeof(Int64)) > (Int64)Convert.ChangeType(b, typeof(Int64)));
            else if (val is SByte)
                result = ((SByte)Convert.ChangeType(a, typeof(SByte)) > (SByte)Convert.ChangeType(b, typeof(SByte)));
            else if (val is UInt16)
                result = ((UInt16)Convert.ChangeType(a, typeof(UInt16)) > (UInt16)Convert.ChangeType(b, typeof(UInt16)));
            else if (val is UInt32)
                result = ((UInt32)Convert.ChangeType(a, typeof(UInt32)) > (UInt32)Convert.ChangeType(b, typeof(UInt32)));
            else if (val is UInt64)
                result = ((UInt64)Convert.ChangeType(a, typeof(UInt64)) > (UInt64)Convert.ChangeType(b, typeof(UInt64)));
            else if (val is Single)
                result = ((Single)Convert.ChangeType(a, typeof(Single)) > (Single)Convert.ChangeType(b, typeof(Single)));
            else if (val is Double)
                result = ((Double)Convert.ChangeType(a, typeof(Double)) > (Double)Convert.ChangeType(b, typeof(Double)));
            else if (val is Enum)
                result = (Int32)a > (Int32)b;
            else
                return false;

            if (result)
                return ExecuteBr(offset);

            return true;
        }

        private bool ExecuteBle(int offset)
        {
            var b = machineStack.Pop();
            var a = machineStack.Pop();

            var aPriority = GetPriorityOfNumber(a);
            var bPriority = GetPriorityOfNumber(b);
            var val = aPriority > bPriority ? a : b;
            var result = false;
            if (val is Byte)
                result = ((Byte)Convert.ChangeType(a, typeof(Byte)) <= (Byte)Convert.ChangeType(b, typeof(Byte)));
            else if (val is Int16)
                result = ((Int16)Convert.ChangeType(a, typeof(Int16)) <= (Int16)Convert.ChangeType(b, typeof(Int16)));
            else if (val is Int32)
                result = ((Int32)Convert.ChangeType(a, typeof(Int32)) <= (Int32)Convert.ChangeType(b, typeof(Int32)));
            else if (val is Int64)
                result = ((Int64)Convert.ChangeType(a, typeof(Int64)) <= (Int64)Convert.ChangeType(b, typeof(Int64)));
            else if (val is SByte)
                result = ((SByte)Convert.ChangeType(a, typeof(SByte)) <= (SByte)Convert.ChangeType(b, typeof(SByte)));
            else if (val is UInt16)
                result = ((UInt16)Convert.ChangeType(a, typeof(UInt16)) <= (UInt16)Convert.ChangeType(b, typeof(UInt16)));
            else if (val is UInt32)
                result = ((UInt32)Convert.ChangeType(a, typeof(UInt32)) <= (UInt32)Convert.ChangeType(b, typeof(UInt32)));
            else if (val is UInt64)
                result = ((UInt64)Convert.ChangeType(a, typeof(UInt64)) <= (UInt64)Convert.ChangeType(b, typeof(UInt64)));
            else if (val is Single)
                result = ((Single)Convert.ChangeType(a, typeof(Single)) <= (Single)Convert.ChangeType(b, typeof(Single)));
            else if (val is Double)
                result = ((Double)Convert.ChangeType(a, typeof(Double)) <= (Double)Convert.ChangeType(b, typeof(Double)));
            else if (val is Enum)
                result = (Int32)a <= (Int32)b;
            else
                return false;

            if (result)
                return ExecuteBr(offset);

            return true;
        }

        private bool ExecuteBlt(int offset)
        {
            var b = machineStack.Pop();
            var a = machineStack.Pop();

            var aPriority = GetPriorityOfNumber(a);
            var bPriority = GetPriorityOfNumber(b);
            var val = aPriority > bPriority ? a : b;
            var result = false;
            if (val is Byte)
                result = ((Byte)Convert.ChangeType(a, typeof(Byte)) < (Byte)Convert.ChangeType(b, typeof(Byte)));
            else if (val is Int16)
                result = ((Int16)Convert.ChangeType(a, typeof(Int16)) < (Int16)Convert.ChangeType(b, typeof(Int16)));
            else if (val is Int32)
                result = ((Int32)Convert.ChangeType(a, typeof(Int32)) < (Int32)Convert.ChangeType(b, typeof(Int32)));
            else if (val is Int64)
                result = ((Int64)Convert.ChangeType(a, typeof(Int64)) < (Int64)Convert.ChangeType(b, typeof(Int64)));
            else if (val is SByte)
                result = ((SByte)Convert.ChangeType(a, typeof(SByte)) < (SByte)Convert.ChangeType(b, typeof(SByte)));
            else if (val is UInt16)
                result = ((UInt16)Convert.ChangeType(a, typeof(UInt16)) < (UInt16)Convert.ChangeType(b, typeof(UInt16)));
            else if (val is UInt32)
                result = ((UInt32)Convert.ChangeType(a, typeof(UInt32)) < (UInt32)Convert.ChangeType(b, typeof(UInt32)));
            else if (val is UInt64)
                result = ((UInt64)Convert.ChangeType(a, typeof(UInt64)) < (UInt64)Convert.ChangeType(b, typeof(UInt64)));
            else if (val is Single)
                result = ((Single)Convert.ChangeType(a, typeof(Single)) < (Single)Convert.ChangeType(b, typeof(Single)));
            else if (val is Double)
                result = ((Double)Convert.ChangeType(a, typeof(Double)) < (Double)Convert.ChangeType(b, typeof(Double)));
            else if (val is Enum)
                result = (Int32)a < (Int32)b;
            else
                return false;

            if (result)
                return ExecuteBr(offset);

            return true;
        }
        
        private bool ExecuteBrtrue(int offset)
        {
            var val = machineStack.Pop();
            if (val == null)
                return true;

            if (val is Boolean)
            {
                if ((Boolean)val)
                    return ExecuteBr(offset);
            }
            else if (val is Byte || val is Int16 || val is UInt16 || val is Int32 || val is UInt32 || val is Int64 || val is UInt64)
            {
                if ((UInt64)Convert.ChangeType(val, typeof(UInt64)) != 0)
                    return ExecuteBr(offset);
            }
            else if (val is Single || val is Double)
            {
                if ((Double)val != 0)
                    return ExecuteBr(offset);
            }
            else if (val is Enum)
            {
                var intVal = (int)val;
                if (intVal != 0)
                    return ExecuteBr(offset);
            }
            else if (!val.GetType().IsValueType)
            {
                return ExecuteBr(offset);
            }
            else
            { 
                Logger.Error("ILRunner: brtrue: invalid type: {0}", val.GetType());
                return false;
            }
            return true;
        }

        private bool ExecuteBrfalse(int offset)
        {
            var val = machineStack.Pop();
            if (val == null)
                return ExecuteBr(offset);

            if (val is Boolean)
            {
                if (!(Boolean)val)
                    return ExecuteBr(offset);
            }
            else if (val is Byte || val is Int16 || val is UInt16 || val is Int32 || val is UInt32 || val is Int64 || val is UInt64)
            {
                if ((UInt64)Convert.ChangeType(val, typeof(UInt64)) == 0)
                    return ExecuteBr(offset);
            }
            else if (val is Single || val is Double)
            {
                if ((Double)val == 0)
                    return ExecuteBr(offset);
            }
            else if (val is Enum)
            { 
                var intVal = (int)val;
                if (intVal == 0)
                    return ExecuteBr(offset);
            }
            else if (!val.GetType().IsValueType)
            {
                return true;
            }
            else
            {
                Logger.Error("ILRunner: brfalse: invalid type: {0}", val.GetType());
                return false;
            }
            return true;
        }


        private bool ExecuteBr(int offset)
        {
            var idx = offset2idx[offset];
            SetEBP(idx);
            return true;
        }

        private bool ExecuteLeave(int offset)
        {
            var idx = offset2idx[offset];
            SetEBP(idx);
            return true;
        }

        private bool ExecuteCeq()
        {
            var b = machineStack.Pop();
            var a = machineStack.Pop();
            
            var aPriority = GetPriorityOfNumber(a);
            var bPriority = GetPriorityOfNumber(b);
            var val = aPriority > bPriority ? a : b;
            var result = false;
            if (val is Byte)
                result = ((Byte)Convert.ChangeType(a, typeof(Byte)) == (Byte)Convert.ChangeType(b, typeof(Byte)));
            else if (val is Int16)
                result = ((Int16)Convert.ChangeType(a, typeof(Int16)) == (Int16)Convert.ChangeType(b, typeof(Int16)));
            else if (val is Int32)
                result = ((Int32)Convert.ChangeType(a, typeof(Int32)) == (Int32)Convert.ChangeType(b, typeof(Int32)));
            else if (val is Int64)
                result = ((Int64)Convert.ChangeType(a, typeof(Int64)) == (Int64)Convert.ChangeType(b, typeof(Int64)));
            else if (val is UInt16)
                result = ((UInt16)Convert.ChangeType(a, typeof(UInt16)) == (UInt16)Convert.ChangeType(b, typeof(UInt16)));
            else if (val is UInt32)
                result = ((UInt32)Convert.ChangeType(a, typeof(UInt32)) == (UInt32)Convert.ChangeType(b, typeof(UInt32)));
            else if (val is UInt64)
                result = ((UInt64)Convert.ChangeType(a, typeof(UInt64)) == (UInt64)Convert.ChangeType(b, typeof(UInt64)));
            else if (val is Single)
                result = ((Single)Convert.ChangeType(a, typeof(Single)) == (Single)Convert.ChangeType(b, typeof(Single)));
            else if (val is Double)
                result = ((Double)Convert.ChangeType(a, typeof(Double)) == (Double)Convert.ChangeType(b, typeof(Double)));
            else
                return false;

            machineStack.Push(result ? 1 : 0);
            return true;
        }

        private bool ExecuteCgt()
        {
            var b = machineStack.Pop();
            var a = machineStack.Pop();

            var aPriority = GetPriorityOfNumber(a);
            var bPriority = GetPriorityOfNumber(b);
            var val = aPriority > bPriority ? a : b;
            var result = false;
            if (val is Byte)
                result = ((Byte)Convert.ChangeType(a, typeof(Byte)) > (Byte)Convert.ChangeType(b, typeof(Byte)));
            else if (val is Int16)
                result = ((Int16)Convert.ChangeType(a, typeof(Int16)) > (Int16)Convert.ChangeType(b, typeof(Int16)));
            else if (val is Int32)
                result = ((Int32)Convert.ChangeType(a, typeof(Int32)) > (Int32)Convert.ChangeType(b, typeof(Int32)));
            else if (val is Int64)
                result = ((Int64)Convert.ChangeType(a, typeof(Int64)) > (Int64)Convert.ChangeType(b, typeof(Int64)));
            else if (val is UInt16)
                result = ((UInt16)Convert.ChangeType(a, typeof(UInt16)) > (UInt16)Convert.ChangeType(b, typeof(UInt16)));
            else if (val is UInt32)
                result = ((UInt32)Convert.ChangeType(a, typeof(UInt32)) > (UInt32)Convert.ChangeType(b, typeof(UInt32)));
            else if (val is UInt64)
                result = ((UInt64)Convert.ChangeType(a, typeof(UInt64)) > (UInt64)Convert.ChangeType(b, typeof(UInt64)));
            else if (val is Single)
                result = ((Single)Convert.ChangeType(a, typeof(Single)) > (Single)Convert.ChangeType(b, typeof(Single)));
            else if (val is Double)
                result = ((Double)Convert.ChangeType(a, typeof(Double)) > (Double)Convert.ChangeType(b, typeof(Double)));
            else
                return false;

            machineStack.Push(result ? 1 : 0);
            return true;
        }

        private bool ExecuteClt()
        {
            var b = machineStack.Pop();
            var a = machineStack.Pop();

            var aPriority = GetPriorityOfNumber(a);
            var bPriority = GetPriorityOfNumber(b);
            var val = aPriority > bPriority ? a : b;
            var result = false;
            if (val is Byte)
                result = ((Byte)Convert.ChangeType(a, typeof(Byte)) < (Byte)Convert.ChangeType(b, typeof(Byte)));
            else if (val is Int16)
                result = ((Int16)Convert.ChangeType(a, typeof(Int16)) < (Int16)Convert.ChangeType(b, typeof(Int16)));
            else if (val is Int32)
                result = ((Int32)Convert.ChangeType(a, typeof(Int32)) < (Int32)Convert.ChangeType(b, typeof(Int32)));
            else if (val is Int64)
                result = ((Int64)Convert.ChangeType(a, typeof(Int64)) < (Int64)Convert.ChangeType(b, typeof(Int64)));
            else if (val is UInt16)
                result = ((UInt16)Convert.ChangeType(a, typeof(UInt16)) < (UInt16)Convert.ChangeType(b, typeof(UInt16)));
            else if (val is UInt32)
                result = ((UInt32)Convert.ChangeType(a, typeof(UInt32)) < (UInt32)Convert.ChangeType(b, typeof(UInt32)));
            else if (val is UInt64)
                result = ((UInt64)Convert.ChangeType(a, typeof(UInt64)) < (UInt64)Convert.ChangeType(b, typeof(UInt64)));
            else if (val is Single)
                result = ((Single)Convert.ChangeType(a, typeof(Single)) < (Single)Convert.ChangeType(b, typeof(Single)));
            else if (val is Double)
                result = ((Double)Convert.ChangeType(a, typeof(Double)) < (Double)Convert.ChangeType(b, typeof(Double)));
            else
                return false;

            machineStack.Push(result ? 1 : 0);
            return true;
        }

        private bool ExecuteConstrained(TypeDefinition typeDef)
        { 
            // currently do nothing.
            return true;
        }


        private object InternalCall(MethodReference methodRef, MethodInfo methodInfo, object instance, object[] parameters, bool virtualCall)
        {
            // parameters type cast
            var paramInfoLst = methodInfo.GetParameters();
            for (var i = 0; i != paramInfoLst.Length; ++i)
            {
                var paramInfo = paramInfoLst[i];
                var paramObj = parameters[i];
                if (!paramInfo.ParameterType.IsPrimitive || paramObj == null)
                    continue;
                if (paramInfo.ParameterType == typeof(Boolean))
                    parameters[i] = (Boolean)Convert.ChangeType(paramObj, typeof(Boolean)); 
                else if (paramInfo.ParameterType == typeof(SByte))
                    parameters[i] = (SByte)Convert.ChangeType(paramObj, typeof(SByte)); 
                else if (paramInfo.ParameterType == typeof(Byte))
                    parameters[i] = (Byte)Convert.ChangeType(paramObj, typeof(Byte)); 
                else if (paramInfo.ParameterType == typeof(Int16))
                    parameters[i] = (Int16)Convert.ChangeType(paramObj, typeof(Int16)); 
                else if (paramInfo.ParameterType == typeof(UInt16))
                    parameters[i] = (UInt16)Convert.ChangeType(paramObj, typeof(UInt16)); 
                else if (paramInfo.ParameterType == typeof(Int32))
                    parameters[i] = (Int32)Convert.ChangeType(paramObj, typeof(Int32)); 
                else if (paramInfo.ParameterType == typeof(UInt32))
                    parameters[i] = (UInt32)Convert.ChangeType(paramObj, typeof(UInt32)); 
                else if (paramInfo.ParameterType == typeof(Int64))
                    parameters[i] = (Int64)Convert.ChangeType(paramObj, typeof(Int64)); 
                else if (paramInfo.ParameterType == typeof(UInt64))
                    parameters[i] = (UInt64)Convert.ChangeType(paramObj, typeof(UInt64)); 
                else if (paramInfo.ParameterType == typeof(Single))
                    parameters[i] = (Single)Convert.ChangeType(paramObj, typeof(Single)); 
                else if (paramInfo.ParameterType == typeof(Double))
                    parameters[i] = (Double)Convert.ChangeType(paramObj, typeof(Double)); 
            }

            var forceExecuteByVM = false;
            foreach (var customAttr in methodInfo.GetCustomAttributes(false))
            {
                if (customAttr is ILVM.VMExecuteAttribute)
                {
                    forceExecuteByVM = true;
                    break;
                }
            }

            if (!forceExecuteByVM)
            {
                if (virtualCall)
                   return methodInfo.Invoke(instance, parameters);

                var instType = instance?.GetType();
                if (instType == null || instType == methodInfo.DeclaringType || typeof(System.Type).IsAssignableFrom(instType))
                    return methodInfo.Invoke(instance, parameters);
            }

            // 这里比较麻烦，OpCode 里，Call 直接调用对应的函数地址，Callvirt 会进行多态地调用（即调用 instance 实际类型对应的函数；
            // 但实现上，我们无法使用 DynamicMethod（版本低），Delegate 的方式也无效（https://stackoverflow.com/questions/4357729/use-reflection-to-invoke-an-overridden-base-method）；
            // 故 hack 处理，将此类函数，也进行虚拟机解释执行；
            // 以避免在解释执行的函数中调用其基类函数，结果多态调用到自己触发死循环；
            var methodDef = methodRef.Resolve();
            var paramCnt = 1 + (parameters != null ? parameters.Length : 0);
            var paramLst = new object[paramCnt];
            paramLst[0] = instance;
            for (var i = 1; i != paramCnt; ++i)
            {
                var paramVal = parameters[i - 1];
                var paramDef = methodDef.Parameters[i - 1];
                if (paramDef.ParameterType.IsByReference)
                {
                    var paramAddr = VMAddr.Create(paramVal);
                    paramVal = paramAddr;
                }
                paramLst[i] = paramVal;
            }

            var innerVm = VMPool.GetFromPool();
            var ret = innerVm.Execute(methodDef, paramLst, methodInfo);
            VMPool.BackToPool(innerVm);

            // handle 'Ref' parameters
            for (var i = 1; i != paramCnt; ++i)
            {
                var paramVal = parameters[i - 1];
                var paramAddr = paramVal as VMAddr;
                if (paramAddr == null)
                    continue;
                paramAddr.SetObj(paramLst[i]);
            }
            return ret;
        }

        private bool ExecuteCall(MethodReference methodRef, bool virtualCall = false)
        {
            var methodDef = methodRef.Resolve();

            // handle property method
            if (methodDef.IsSetter || methodDef.IsGetter)
                return ExecuteCallProp(methodRef);

            // handle constructor
            if (methodDef.IsConstructor)
                return ExecuteCallConstructor(methodRef, true);

            object[] parameters = null;
            VMAddr[] paramAddrs = null;
            if (methodRef.HasParameters)
            {
                parameters = new object[methodRef.Parameters.Count];
                paramAddrs = new VMAddr[methodRef.Parameters.Count];
                for (var i = 0; i != methodRef.Parameters.Count; ++i)
                {
                    var paramVal = machineStack.Pop();
                    if (paramVal is VMAddr)
                    {
                        var addr = paramVal as VMAddr;
                        paramAddrs[i] = addr;
                        paramVal = addr.GetObj();
                    }
                    parameters[methodRef.Parameters.Count - i - 1] = paramVal;
                }
            }

            var methodInfo = GetMethodInfoFromMethodReference(methodRef, parameters);
            if (methodInfo == null)
            {
                Logger.Error("ILRunner: Call: cannot find method: {0}", methodRef.FullName);
                return false;
            }

            object instance = null;
            VMAddr instAddr = null;
            if (!methodInfo.IsStatic)
                instance = machineStack.Pop();
            if (instance is VMAddr)
            {
                instAddr = instance as VMAddr;
                instance = instAddr.GetObj();
            }

            object result;
            try
            {
                if (methodInfo.IsGenericMethodDefinition)
                {
                    // trait generic type from methodRef
                    var genericParams = new Type[methodInfo.GetGenericArguments().Length];
                    var genericMethodRef = methodRef as GenericInstanceMethod;
                    for (var i = 0; i != genericMethodRef.GenericArguments.Count; ++i)
                    {
                        var genericTypeRef = genericMethodRef.GenericArguments[i];
                        var genericTypeInfo = GetTypeInfoFromTypeReference(genericTypeRef);
                        genericParams[i] = genericTypeInfo;
                    }
                    methodInfo = methodInfo.MakeGenericMethod(genericParams);
                }
                result = InternalCall(methodRef, methodInfo, instance, parameters, virtualCall);
            }
            catch (Exception e)
            {
                Logger.Error("ILRunner: Call: invoke method failed: {0}, \n{1}", methodRef.FullName, e);
                return false;
            }

            if (methodRef.ReturnType.FullName != "System.Void")
                machineStack.Push(result);

            // addr handles
            if (instAddr != null)
                instAddr.SetObj(instance);
            if (paramAddrs != null)
            {
                for (var i = 0; i != paramAddrs.Length; ++i)
                {
                    var paramAddr = paramAddrs[i];
                    if (paramAddr == null)
                        continue;
                    paramAddr.SetObj(parameters[paramAddrs.Length - i - 1]);
                }
            }

            return true;
        }

        private bool ExecuteCallvirt(MethodReference methodRef)
        {
            return ExecuteCall(methodRef, true);
        }

        private bool ExecuteCallProp(MethodReference methodRef)
        {
            var methodDef = methodRef.Resolve();
            var propInfo = GetPropInfoFromMethodReference(methodRef);
            if (propInfo == null)
            {
                Logger.Error("ILRunner: CallProp: cannot find prop: {0}", methodRef.FullName);
                return false;
            }

            var isSet = methodDef.IsSetter;
            object setVal = null;
            if (isSet)
            {
                setVal = machineStack.Pop();
            }

            object[] propIdx = null;
            VMAddr[] propAddrs = null;
            var idxParams = propInfo.GetIndexParameters();
            if (idxParams.Length > 0)
            {
                propIdx = new object[idxParams.Length];
                propAddrs = new VMAddr[idxParams.Length];
                for (var i = 0; i != idxParams.Length; ++i)
                {
                    var idxVal = machineStack.Pop();
                    if (idxVal is VMAddr)
                    {
                        var propAddr = idxVal as VMAddr;
                        propAddrs[i] = propAddr;
                        idxVal = propAddr.GetObj();
                    }
                    propIdx[i] = idxVal;
                }
            }

            object instance = null;
            VMAddr instAddr = null;
            if (!methodDef.IsStatic)
                instance = machineStack.Pop();
            if (instance is VMAddr)
            {
                instAddr = instance as VMAddr;
                instance = instAddr.GetObj();
            }

            if (isSet)
            {
                propInfo.SetValue(instance, Convert.ChangeType(setVal, propInfo.PropertyType), propIdx);
            }
            else
            {
                var getVal = propInfo.GetValue(instance, propIdx);
                machineStack.Push(getVal);
            }

            // addr handles
            if (instAddr != null)
                instAddr.SetObj(instance);
            if (propAddrs != null)
            {
                for (var i = 0; i != propAddrs.Length; ++i)
                {
                    var propAddr = propAddrs[i];
                    if (propAddr == null)
                        continue;
                    propAddr.SetObj(propIdx[propAddrs.Length - i - 1]);
                }
            }

            return true;
        }

        private bool ExecuteCallConstructor(MethodReference methodRef, bool hasAddrOnStack)
        {
            object[] parameters = null;
            VMAddr[] paramAddrs = null;
            if (methodRef.HasParameters)
            {
                parameters = new object[methodRef.Parameters.Count];
                paramAddrs = new VMAddr[methodRef.Parameters.Count];
                for (var i = 0; i != methodRef.Parameters.Count; ++i)
                {
                    var paramVal = machineStack.Pop();
                    if (paramVal is VMAddr)
                    {
                        var addr = paramVal as VMAddr;
                        paramAddrs[i] = addr;
                        paramVal = addr.GetObj();
                    }
                    parameters[methodRef.Parameters.Count - i - 1] = paramVal;
                }
            }

            VMAddr instAddr = null;
            if (hasAddrOnStack)
                instAddr = machineStack.Pop() as VMAddr;

            // invoke
            var constructorInfo = GetConstructorFromMethodReference(methodRef, parameters);
            var ret = constructorInfo.Invoke(parameters);
            machineStack.Push(ret);
            
            // addr handles
            if (instAddr != null)
                instAddr.SetObj(ret);
            if (paramAddrs != null)
            {
                for (var i = 0; i != paramAddrs.Length; ++i)
                {
                    var paramAddr = paramAddrs[i];
                    if (paramAddr == null)
                        continue;
                    paramAddr.SetObj(parameters[paramAddrs.Length - i - 1]);
                }
            }

            return true;
        }

        private Type GetTypeInfoFromTypeReference(TypeReference typeRef)
        {
            if (typeRef.IsGenericParameter && genericMap.ContainsKey(typeRef.Name))
                return genericMap[typeRef.Name];
            var typeDef = typeRef.Resolve();
            var typeName = typeDef.FullName;
            
            if (typeRef.IsPointer)
                typeName = typeName + "*";
            if (typeRef.IsArray)
                typeName = typeName + "[]";
            var typeInfo = GetTypeByName(typeName);
            if (typeInfo == null || !typeInfo.IsGenericType)
                return typeInfo;

            var genericTypeRef = typeRef as GenericInstanceType;
            var parameterTypes = new Type[genericTypeRef.GenericArguments.Count];
            for (var i = 0; i != genericTypeRef.GenericArguments.Count; ++i)
            {
                var genericArg = genericTypeRef.GenericArguments[i];
                var genericType = GetTypeInfoFromTypeReference(genericArg);
                parameterTypes[i] = genericType;
            }
            typeInfo = typeInfo.MakeGenericType(parameterTypes);
            return typeInfo;
        }

        private MethodInfo GetMethodInfoFromMethodReference(MethodReference methodRef, object[] parameters)
        {
            var classRef = methodRef.DeclaringType;
            var classInfo = GetTypeInfoFromTypeReference(classRef);
            if (classInfo == null)
                return null;

            MethodInfo methodInfo = null;
            var methodDef = methodRef.Resolve();
            if (methodDef.IsConstructor)
                return null;

            var allMethods = new List<MethodInfo>(classInfo.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static));
            var allMatched = new List<MethodInfo>();

            // check name & param cnt first
            foreach (var method in allMethods)
            {
                if (method.Name != methodDef.Name)
                    continue;
                if (method.GetParameters().Length != methodDef.Parameters.Count)
                    continue;
                allMatched.Add(method);
            }
            if (allMatched.Count == 0)
                return null;
            if (allMatched.Count == 1)
                return allMatched[0];
            
            // check param type then
            allMethods = allMatched;
            allMatched = new List<MethodInfo>();
            foreach (var method in allMethods)
            {
                var matched = true;
                for (var i = 0; i != method.GetParameters().Length; ++i)
                {
                    var paramInfo = method.GetParameters()[i];
                    var paramObj = parameters[i];
                    var paramRef = methodDef.Parameters[i].ParameterType;
                    if (!IsParameterMatch(paramInfo.ParameterType, paramObj?.GetType(), paramRef))
                    {
                        matched = false;
                        break;
                    }
                }
                if (matched)
                    allMatched.Add(method);
            }
            if (allMatched.Count == 0)
                return null;
            if (allMatched.Count == 1)
                return allMatched[0];
            
            // check param type name
            allMethods = allMatched;
            allMatched = new List<MethodInfo>();
            foreach (var method in allMethods)
            {
                var matched = true;
                for (var i = 0; i != method.GetParameters().Length; ++i)
                {
                    var paramInfo = method.GetParameters()[i];
                    var paramObj = parameters[i];
                    var paramRef = methodDef.Parameters[i].ParameterType;
                    if (paramRef.FullName != paramInfo.ParameterType.FullName)
                    {
                        matched = false;
                        break;
                    }
                }
                if (matched)
                    allMatched.Add(method);
            }
            if (allMatched.Count == 1)
                return allMatched[0];
            if (allMatched.Count == 0)
            {
                allMatched = allMethods;
                Logger.Error("ILVM: has multi method can match {0}", methodRef);
            }

            // fallback
            allMatched.Sort((a, b) =>
            {
                var paramCnt = a.GetParameters().Length;
                for (var i = 0; i != paramCnt; ++i)
                {
                    var aParam = a.GetParameters()[i];
                    var bParam = b.GetParameters()[i];
                    if (aParam.ParameterType == bParam.ParameterType)
                        continue;

                    if (aParam.ParameterType.IsAssignableFrom(bParam.ParameterType))
                        return 1;
                    return -1;
                }
                return 0;
            });
            methodInfo = allMatched[0];
            return methodInfo;
        }

        private ConstructorInfo GetConstructorFromMethodReference(MethodReference methodRef, object[] parameters)
        {
            var classRef = methodRef.DeclaringType;
            var classInfo = GetTypeInfoFromTypeReference(classRef);
            if (classInfo == null)
                return null;

            ConstructorInfo constructorInfo = null;
            var methodDef = methodRef.Resolve();
            if (!methodDef.IsConstructor)
                return null;
            
            var allConstructors = new List<ConstructorInfo>(classInfo.GetConstructors());
            var allMatched = new List<ConstructorInfo>();

            // check name & param cnt first
            foreach (var constructor in allConstructors)
            {
                if (constructor.GetParameters().Length != methodDef.Parameters.Count)
                    continue;
                allMatched.Add(constructor);
            }
            if (allMatched.Count == 0)
                return null;
            if (allMatched.Count == 1)
                return allMatched[0];

            // check param type then
            allConstructors = allMatched;
            allMatched = new List<ConstructorInfo>();
            foreach (var constructor in allConstructors)
            {
                if (constructor.GetParameters().Length != methodDef.Parameters.Count)
                    continue;

                var matched = true;
                for (var i = 0; i != constructor.GetParameters().Length; ++i)
                {
                    var paramInfo = constructor.GetParameters()[i];
                    var paramObj = parameters[i];
                    var paramRef = methodRef.Parameters[i].ParameterType;
                    if (!IsParameterMatch(paramInfo.ParameterType, paramObj?.GetType(), paramRef))
                    {
                        matched = false;
                        break;
                    }
                }
                if (matched)
                    allMatched.Add(constructor);
            }
            if (allMatched.Count == 0)
                return null;
            if (allMatched.Count == 1)
                return allMatched[0];

            // check param type name
            allConstructors = allMatched;
            allMatched = new List<ConstructorInfo>();
            foreach (var constructor in allConstructors)
            {
                var matched = true;
                for (var i = 0; i != constructor.GetParameters().Length; ++i)
                {
                    var paramInfo = constructor.GetParameters()[i];
                    var paramObj = parameters[i];
                    var paramRef = methodRef.Parameters[i].ParameterType;
                    if (paramRef.FullName != paramInfo.ParameterType.FullName)
                    {
                        matched = false;
                        break;
                    }
                }
                if (matched)
                    allMatched.Add(constructor);
            }
            if (allMatched.Count == 1)
                return allMatched[0];
            if (allMatched.Count == 0)
            {
                allMatched = allConstructors;
                Logger.Error("ILVM: has multi method can match {0}", methodRef);
            }
            
            // fallback
            allMatched.Sort((a, b) =>
            {
                var paramCnt = a.GetParameters().Length;
                for (var i = 0; i != paramCnt; ++i)
                {
                    var aParam = a.GetParameters()[i];
                    var bParam = b.GetParameters()[i];
                    if (aParam.ParameterType == bParam.ParameterType)
                        continue;

                    if (aParam.ParameterType.IsAssignableFrom(bParam.ParameterType))
                        return 1;
                    return -1;
                }
                return 0;
            });
            constructorInfo = allMatched[0];
            return constructorInfo;
        }


        private bool IsParameterMatch(Type needType, Type objType, TypeReference typeRef)
        {
            if (typeRef.IsGenericInstance)
            {
                var genericTypeRef = typeRef as GenericInstanceType;
                for (var i = 0; i != genericTypeRef.GenericArguments.Count; ++i)
                {
                    var needTypeGenericParam = needType.GetGenericArguments()[i];
                    var objTypeGenericParam = objType.GetGenericArguments()[i];
                    var typeRefGenericParam = genericTypeRef.GenericArguments[i];
                    if (!IsParameterMatch(needTypeGenericParam, objTypeGenericParam, typeRefGenericParam))
                        return false;
                }
                return needType.IsAssignableFrom(objType);
            }

            // 非 generic，先检查反射信息和 typeRef 是否匹配
            if (typeRef is ByReferenceType)
                typeRef = (typeRef as ByReferenceType).ElementType;
            if (!typeRef.IsGenericParameter)
            {
                var defType = GetTypeInfoFromTypeReference(typeRef);
                return needType.IsAssignableFrom(defType);
            }

            // generic，检查反射信息和 objType 是否匹配
            if (objType == null)
                return !needType.IsValueType;

            if (!needType.IsGenericParameter)
                return needType.IsAssignableFrom(objType);

            // exp. "where T : BaseClassA, BaseClassB"
            var baseTypeConstraints = needType.GetGenericParameterConstraints();
            foreach (var baseType in baseTypeConstraints)
            {
                if (!baseType.IsAssignableFrom(objType))
                    return false;
            }

            // exp. "where T: new(), Nullable"
            var specialConstraints = needType.GenericParameterAttributes & GenericParameterAttributes.SpecialConstraintMask;
            if (specialConstraints == GenericParameterAttributes.None)
            {
                return true;
            }

            if ((specialConstraints & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
            {
                if (objType.IsValueType)
                    return false;
            }
            if ((specialConstraints & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
            {
                if (Nullable.GetUnderlyingType(objType) != null)
                    return false;
            }

            if ((specialConstraints & GenericParameterAttributes.DefaultConstructorConstraint) != 0)
            {
                var constructors = objType.GetConstructors();
                var found = false;
                foreach (var c in constructors)
                {
                    if (c.GetParameters().Length == 0)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                    return false;
            }

            return true;
        }

        private PropertyInfo GetPropInfoFromMethodReference(MethodReference methodRef)
        {
            var classRef = methodRef.DeclaringType;
            var classInfo = GetTypeInfoFromTypeReference(classRef);
            if (classInfo == null)
                return null;

            var propName = methodRef.Name.Substring("set_".Length);
            var propInfo = classInfo.GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            return propInfo;
        }

        private Type GetTypeByName(string typeName)
        {
            if (genericMap.ContainsKey(typeName))
                return genericMap[typeName];

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var typeInfo = assembly.GetType(typeName);
                if (typeInfo != null)
                    return typeInfo;
            }
            return null;
        }

        private bool ExecuteFailed(Instruction il)
        {
            Logger.Error("ILRunner: this opcode not supported yet: {0}", il.ToString());
            return false;
        }
    }
}