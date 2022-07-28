using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using GenericParameterAttributes = System.Reflection.GenericParameterAttributes;

namespace ILVM
{
    public class ILVirtualMachine
    {

        public class VMAddrPool
        {
            public enum AddrType
            {
                Invalid = 0, 
                VmVar = 1, 
                VmStack = 2, 
                ArrayElem = 3, 
            }

            public class AddressHandle
            {
                public ILVirtualMachine vm;
                public int virtualAddrValue = -1;
                public AddrType addrType = AddrType.Invalid;

                public AddressHandle() { }

                public void SetObj(object obj)
                {
                    if (addrType == AddrType.VmVar)
                        SetVmVar(obj);
                    else if (addrType == AddrType.VmStack)
                        SetVmStack(obj);
                    else if (addrType == AddrType.ArrayElem)
                        SetArrayElem(obj);
                    else
                        throw new Exception("VMAddressHanel: SetObj: cannot set " + addrType.ToString());
                }

                public object GetObj()
                {
                    if (addrType == AddrType.VmVar)
                        return GetVmVar();
                    else if (addrType == AddrType.VmStack)
                        return GetVmStack();
                    else if (addrType == AddrType.ArrayElem)
                        return GetArrayElem();
                    else
                        throw new Exception("VMAddressHanel: SetObj: cannot set " + addrType.ToString());
                }

                #region addrHandle for vm variables
                public int vmVarIdx = -1;
                private object GetVmVar()
                {
                    var obj = vm.machineVar[vmVarIdx];
                    return obj;
                }

                private void SetVmVar(object obj)
                {
                    vm.machineVar[vmVarIdx] = obj;
                }
                #endregion

                #region addrHandle for vm stack
                public object vmStackVal = null;

                private object GetVmStack()
                {
                    return vmStackVal;
                }

                private void SetVmStack(object obj)
                {
                    vmStackVal = obj;
                }
                #endregion

                #region addrHandle for array elements
                public KeyValuePair<Array, int> arrayKey;

                private object GetArrayElem()
                {
                    var obj = arrayKey.Key.GetValue(arrayKey.Value);
                    return obj;
                }

                private void SetArrayElem(object obj)
                {
                    arrayKey.Key.SetValue(obj, arrayKey.Value);
                }

                #endregion
            }

            #region all addrHandle
            private int virtualAddrValueIndexer = -1;
            private int GetNewVirtualAddrValueIndex()
            {
                virtualAddrValueIndexer = virtualAddrValueIndexer + 1;
                return virtualAddrValueIndexer;
            }

            private Dictionary<int, AddressHandle> allAddrHandle = new Dictionary<int, AddressHandle>();
            public void ClearAllAddrHandle()
            {
                allAddrHandle.Clear();
            }

            public AddressHandle GetAddrHandle(int virtualAddr)
            {
                AddressHandle addrHandle;
                if (allAddrHandle.TryGetValue(virtualAddr, out addrHandle))
                {
                    return addrHandle;
                }
                return null;
            }

            public void SetAddrHandle(AddressHandle addrHandle)
            {
                addrHandle.virtualAddrValue = GetNewVirtualAddrValueIndex();
                allAddrHandle.Add(addrHandle.virtualAddrValue, addrHandle);
            }
            #endregion

            
            #region addrHandle for vm variables
            private Dictionary<int, AddressHandle> vmVarIdx2AddrHandle = new Dictionary<int, AddressHandle>();

            public AddressHandle GetVmVarAddrHandle(ILVirtualMachine vm, int idx)
            {
                AddressHandle addrHandle;
                vmVarIdx2AddrHandle.TryGetValue(idx, out addrHandle);
                if (addrHandle == null)
                    addrHandle = CreateVmVarAddrHandle(vm, idx);
                return addrHandle;
            }

            private AddressHandle CreateVmVarAddrHandle(ILVirtualMachine vm, int idx)
            {
                var addrHandle = new AddressHandle();
                addrHandle.vm = vm;
                addrHandle.addrType = AddrType.VmVar;
                addrHandle.vmVarIdx = idx;
                SetAddrHandle(addrHandle);
                vmVarIdx2AddrHandle.Add(idx, addrHandle);
                return addrHandle;
            }
            #endregion

            
            #region addrHandle for vm stack
            public AddressHandle GetVmStackAddrHandle(ILVirtualMachine vm, object val)
            {
                AddressHandle addrHandle = CreateVmStackAddrHandle(vm, val);
                return addrHandle;
            }

            private AddressHandle CreateVmStackAddrHandle(ILVirtualMachine vm, object val)
            {
                var addrHandle = new AddressHandle();
                addrHandle.vm = vm;
                addrHandle.addrType = AddrType.VmStack;
                addrHandle.vmStackVal = val;
                SetAddrHandle(addrHandle);
                return addrHandle;
            }
            #endregion

            
            #region addrHandle for array elements
            private Dictionary<KeyValuePair<Array, int>, AddressHandle> array2AddrHandle = new Dictionary<KeyValuePair<Array, int>, AddressHandle>();
            public AddressHandle GetArrayElemAddrHandle(ILVirtualMachine vm, Array arr, int idx)
            {
                AddressHandle addrHandle;
                var key = new KeyValuePair<Array, int>(arr, idx);
                array2AddrHandle.TryGetValue(key, out addrHandle);
                if (addrHandle == null)
                    addrHandle = CreateArrayElemAddrHandle(vm, arr, idx);
                return addrHandle;
            }
            
            private AddressHandle CreateArrayElemAddrHandle(ILVirtualMachine vm, Array arr, int idx)
            {
                var addrHandle = new AddressHandle();
                addrHandle.vm = vm;
                addrHandle.addrType = AddrType.ArrayElem;
                addrHandle.arrayKey = new KeyValuePair<Array, int>(arr, idx);
                SetAddrHandle(addrHandle);
                array2AddrHandle.Add(addrHandle.arrayKey, addrHandle);
                return addrHandle;
            }
            #endregion
        }

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

        private const int kArgSize = 32;
        private object[] arguments = null;

        private VMStack machineStack = new VMStack();
        private object[] machineVar = new object[kArgSize];
        private Type[] machineVarType = new Type[kArgSize];

        private VMAddrPool addrPool = new VMAddrPool();

        private Dictionary<int, int> offset2idx = new Dictionary<int, int>();

        public ILVirtualMachine() {}

        /// <summary>
        /// execute method ils, note that args[0] must be the obj own this method
        /// </summary>
        /// <returns></returns>
        public object Execute(MethodDefinition methodDef, object[] args)
        {
            var ilLst = methodDef.Body.Instructions;

            // Print all IL
            var sb = new System.Text.StringBuilder();
            sb.AppendFormat("============================ execute new: {0} \t{1}", args != null ? args[0].GetType().ToString() : "null", ilLst.Count);
            sb.AppendLine();
            foreach (var il in ilLst)
            {
                sb.AppendFormat("IL: {0}", il.ToString());
                sb.AppendLine();
            }
            Logger.Log(sb.ToString());

            arguments = args;
            machineStack.Clear();
            addrPool.ClearAllAddrHandle();
            InitLocalVar(methodDef);

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
            if (succ && machineStack.Count > 0)
                ret = machineStack.Pop();
            arguments = null;
            machineStack.Clear();
            offset2idx.Clear();
            addrPool.ClearAllAddrHandle();
            return ret;
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

        private void InitLocalVar(MethodDefinition methodDef)
        {
            var varLst = methodDef.Body.Variables;
            for (var i = 0; i != varLst.Count; ++i)
            {
                var varDef = varLst[i];
                machineVar[i] = null;
                machineVarType[i] = GetTypeInfoFromTypeReference(varDef.VariableType);
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
                    return ExecuteLoadArg((int)il.Operand);

                //case Code.Ldflda:
                case Code.Ldfld:
                    return ExecuteLdfld(il.Operand as FieldDefinition);
                //case Code.Ldsflda:
                case Code.Ldsfld:
                    return ExecuteLdsfld(il.Operand as FieldDefinition);
                case Code.Stfld:
                    return ExecuteStfld(il.Operand as FieldDefinition);
                case Code.Stsfld:
                    return ExecuteStsfld(il.Operand as FieldDefinition);

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

                case Code.Call:
                    return ExecuteCall(il);
                case Code.Callvirt:
                    return ExecuteCallvirt(il);

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
            //var val = machineStack.Pop();
            //var addrHandle = addrPool.GetVmStackAddrHandle(this, val);
            //machineStack.Push(addrHandle);
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
            object[] parameters = null;
            var paramCnt = methodRef.Parameters.Count;
            if (paramCnt > 0)
            {
                parameters = new object[paramCnt];
                for (var i = 0; i != paramCnt; ++i)
                {
                    parameters[paramCnt - i - 1] = machineStack.Pop();
                }
            }

            var constructorInfo = GetConstructorFromMethodReference(methodRef, parameters);
            var ret = constructorInfo.Invoke(parameters);
            machineStack.Push(ret);
            return true;
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
            var typeDef = il.Operand as TypeDefinition;
            var typeInfo = GetTypeByName(typeDef.FullName);
            var addrHandle = machineStack.Pop() as VMAddrPool.AddressHandle;
            var obj = addrHandle.GetObj();
            if (typeInfo.IsValueType)
                obj = Activator.CreateInstance(typeInfo);
            else
                obj = null;
            addrHandle.SetObj(obj);
            machineStack.Push(obj);
            return true;
        }

        //private object InitObjInner(object inst, Type typeInfo)
        //{
        //    foreach (var field in typeInfo.GetFields())
        //    {
        //        var fieldType = field.FieldType;
        //        if (fieldType.IsPrimitive)
        //        {
        //            if (fieldType == typeof(Boolean))
        //                field.SetValue(inst, default(Boolean));
        //            else if (fieldType == typeof(Byte))
        //                field.SetValue(inst, default(Byte));
        //            else if (fieldType == typeof(SByte))
        //                field.SetValue(inst, default(SByte));
        //            else if (fieldType == typeof(Int16))
        //                field.SetValue(inst, default(Int16));
        //            else if (fieldType == typeof(UInt16))
        //                field.SetValue(inst, default(UInt16));
        //            else if (fieldType == typeof(Int32))
        //                field.SetValue(inst, default(Int32));
        //            else if (fieldType == typeof(UInt32))
        //                field.SetValue(inst, default(UInt32));
        //            else if (fieldType == typeof(Int64))
        //                field.SetValue(inst, default(Int64));
        //            else if (fieldType == typeof(UInt64))
        //                field.SetValue(inst, default(UInt64));
        //            else if (fieldType == typeof(IntPtr))
        //                field.SetValue(inst, default(IntPtr));
        //            else if (fieldType == typeof(UIntPtr))
        //                field.SetValue(inst, default(UIntPtr));
        //            else if (fieldType == typeof(Char))
        //                field.SetValue(inst, default(Char));
        //            else if (fieldType == typeof(Single))
        //                field.SetValue(inst, default(Single));
        //            else if (fieldType == typeof(Double))
        //                field.SetValue(inst, default(Double));
        //            continue;
        //        }

        //        if (!fieldType.IsValueType)
        //        {
        //            field.SetValue(inst, null);
        //            continue;
        //        }

        //        var fieldObj = field.GetValue(inst);
        //        field.SetValue(inst, InitObjInner(fieldObj, fieldType));
        //    }
        //    return inst;
        //}

        private bool ExecuteIsinst(Instruction il)
        {
            var obj = machineStack.Pop();
            var typeRef = il.Operand as TypeReference;
            var typeInfo = GetTypeInfoFromTypeReference(typeRef);
            if (obj.GetType() == typeInfo)
                machineStack.Push(obj);
            else
                machineStack.Push(null);
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
            var addrHandle = addrPool.GetArrayElemAddrHandle(this, arr, idx);
            machineStack.Push(addrHandle);
            return true;
        }

        private bool ExecuteLdtoken(Instruction il)
        {
            if (il.Operand is TypeDefinition)
            {
                var typeDef = il.Operand as TypeDefinition;
                var typeInfo = GetTypeByName(typeDef.FullName);
                machineStack.Push(typeInfo.TypeHandle);
            }
            else
            {
                Logger.Error("ExecuteLdtoken: not support: {0}", il.Operand);
                return false;
            }
            return true;
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
            var curType = objVar.GetType();
            var tarType = machineVarType[index];
            object result = objVar;
            if (!tarType.IsAssignableFrom(curType))
            {
                // hack here, handle store int to bool type
                if (tarType == typeof(Boolean))
                {
                    result = (Int32)Convert.ChangeType(objVar, typeof(Int32)) != 0;
                }
                else
                {
                    result = Convert.ChangeType(objVar, tarType);
                }
            }

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
            var addrHandle = addrPool.GetVmVarAddrHandle(this, index);
            machineStack.Push(addrHandle);
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

        private bool ExecuteLdfld(FieldDefinition fieldDef)
        {
            var obj = machineStack.Pop();
            var typeInfo = obj.GetType();
            var fieldInfo = typeInfo.GetField(fieldDef.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            var val = fieldInfo.GetValue(obj);
            machineStack.Push(val);
            return true;
        }

        private bool ExecuteLdsfld(FieldDefinition fieldDef)
        {
            var classType = GetTypeByName(fieldDef.DeclaringType.FullName);
            var fieldInfo = classType.GetField(fieldDef.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            var val = fieldInfo.GetValue(null);
            machineStack.Push(val);
            return true;
        }

        private bool ExecuteStfld(FieldDefinition fieldDef)
        {
            var val = machineStack.Pop();
            var obj = machineStack.Pop();

            // may ref by pointer
            var addrHandle = obj as VMAddrPool.AddressHandle;
            if (addrHandle != null)
                obj = addrHandle.GetObj();

            var typeInfo = obj.GetType();
            var fieldInfo = typeInfo.GetField(fieldDef.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            fieldInfo.SetValue(obj, val);

            if (addrHandle != null)
                addrHandle.SetObj(obj);

            return true;
        }

        private bool ExecuteStsfld(FieldDefinition fieldDef)
        {
            var classType = GetTypeByName(fieldDef.DeclaringType.FullName);
            var fieldInfo = classType.GetField(fieldDef.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            var val = machineStack.Pop();
            fieldInfo.SetValue(null, val);
            return true;
        }

        private int GetPriorityOfNumber(object num)
        {
            if (num is Byte)
                return 1;
            if (num is Boolean)
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
            if (val is Byte)
                result = ((Byte)Convert.ChangeType(a, typeof(Byte)) != (Byte)Convert.ChangeType(b, typeof(Byte)));
            else if (val is Int16)
                result = ((Int16)Convert.ChangeType(a, typeof(Int16)) != (Int16)Convert.ChangeType(b, typeof(Int16)));
            else if (val is Int32)
                result = ((Int32)Convert.ChangeType(a, typeof(Int32)) != (Int32)Convert.ChangeType(b, typeof(Int32)));
            else if (val is Int64)
                result = ((Int64)Convert.ChangeType(a, typeof(Int64)) != (Int64)Convert.ChangeType(b, typeof(Int64)));
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

            if (result)
                return ExecuteBr(offset);

            return true;
        }

        private bool ExecuteBrtrue(int offset)
        {
            var val = machineStack.Pop();
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
            return true;
        }

        private bool ExecuteBrfalse(int offset)
        {
            var val = machineStack.Pop();
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

        private object InternalCall(Instruction il, MethodInfo methodInfo, object instance, object[] parameters, bool virtualCall)
        {
            if (virtualCall)
                return methodInfo.Invoke(instance, parameters);

            var instType = instance?.GetType();
            if (instType == null || instType == methodInfo.DeclaringType || typeof(System.Type).IsAssignableFrom(instType))
                return methodInfo.Invoke(instance, parameters);

            // 这里比较麻烦，OpCode 里，Call 直接调用对应的函数地址，Callvirt 会进行多态地调用（即调用 instance 实际类型对应的函数；
            // 但实现上，我们无法使用 DynamicMethod（版本低），Delegate 的方式也无效（https://stackoverflow.com/questions/4357729/use-reflection-to-invoke-an-overridden-base-method）；
            // 故 hack 处理，将此类函数，也进行虚拟机解释执行；
            // 以避免在解释执行的函数中调用其基类函数，结果多态调用到自己触发死循环；
            var methodRef = il.Operand as MethodReference;
            var methodDef = methodRef.Resolve();
            var paramCnt = 1 + (parameters != null ? parameters.Length : 0);
            var paramLst = new object[paramCnt];
            paramLst[0] = instance;
            for (var i = 1; i != paramCnt; ++i)
            {
                paramLst[i] = parameters[i - 1];
            }
            var innerVm = new ILVirtualMachine();
            return innerVm.Execute(methodDef, paramLst);
        }

        private bool ExecuteCall(Instruction il, bool virtualCall = false)
        {
            var methodRef = il.Operand as MethodReference;
            var methodDef = methodRef.Resolve();

            // handle property method
            if (methodDef.IsSetter || methodDef.IsGetter)
                return ExecuteCallProp(il);

            object[] parameters = null;
            VMAddrPool.AddressHandle[] paramAddrHandles = null;
            if (methodRef.HasParameters)
            {
                parameters = new object[methodRef.Parameters.Count];
                paramAddrHandles = new VMAddrPool.AddressHandle[methodRef.Parameters.Count];
                for (var i = 0; i != methodRef.Parameters.Count; ++i)
                {
                    // cannot know type right now, may runtime bound
                    //var paramType = GetTypeByName(methodRef.Parameters[i].ParameterType.FullName);
                    //var paramVal = Convert.ChangeType(machineStack.Pop(), paramType);
                    var paramVal = machineStack.Pop();
                    if (paramVal is VMAddrPool.AddressHandle)
                    {
                        var paramAddrHandle = paramVal as VMAddrPool.AddressHandle;
                        paramAddrHandles[i] = paramAddrHandle;
                        paramVal = paramAddrHandle.GetObj();
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
            VMAddrPool.AddressHandle instAddrHandle = null;
            if (!methodInfo.IsStatic)
                instance = machineStack.Pop();
            if (instance is VMAddrPool.AddressHandle)
            {
                instAddrHandle = instance as VMAddrPool.AddressHandle;
                instance = instAddrHandle.GetObj();
            }

            object result;
            try
            {
                if (methodInfo.IsGenericMethodDefinition)
                {
                    var genericParams = new Type[parameters.Length];
                    for (int i = 0, j = 0; i != methodInfo.GetParameters().Length; ++i)
                    {
                        if (methodInfo.GetParameters()[i].ParameterType.IsGenericType)
                        {
                            genericParams[j] = parameters[i].GetType();
                            j += 1;
                        }
                    }
                    methodInfo = methodInfo.MakeGenericMethod(genericParams);
                }
                result = InternalCall(il, methodInfo, instance, parameters, virtualCall);
            }
            catch (Exception e)
            {
                Logger.Error("ILRunner: Call: invoke method failed: {0}, \n{1}", methodRef.FullName, e);
                return false;
            }

            if (methodRef.ReturnType.FullName != "System.Void")
                machineStack.Push(result);

            // addr handles
            if (instAddrHandle != null)
                instAddrHandle.SetObj(instance);
            if (paramAddrHandles != null)
            {
                for (var i = 0; i != paramAddrHandles.Length; ++i)
                {
                    var paramAddrHandle = paramAddrHandles[i];
                    if (paramAddrHandle == null)
                        continue;
                    paramAddrHandle.SetObj(parameters[i]);
                }
            }

            return true;
        }

        private bool ExecuteCallvirt(Instruction il)
        {
            return ExecuteCall(il, true);
        }

        private bool ExecuteCallProp(Instruction il)
        {
            var methodRef = il.Operand as MethodReference;
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
            VMAddrPool.AddressHandle[] propAddrHandles = null;
            var idxParams = propInfo.GetIndexParameters();
            if (idxParams.Length > 0)
            {
                propIdx = new object[idxParams.Length];
                propAddrHandles = new VMAddrPool.AddressHandle[idxParams.Length];
                for (var i = 0; i != idxParams.Length; ++i)
                {
                    var idxVal = machineStack.Pop();
                    if (idxVal is VMAddrPool.AddressHandle)
                    {
                        var paramAddrHandle = idxVal as VMAddrPool.AddressHandle;
                        propAddrHandles[i] = paramAddrHandle;
                        idxVal = paramAddrHandle.GetObj();
                    }
                    propIdx[i] = idxVal;
                }
            }

            object instance = null;
            VMAddrPool.AddressHandle instAddrHandle = null;
            if (!methodDef.IsStatic)
                instance = machineStack.Pop();
            if (instance is VMAddrPool.AddressHandle)
            {
                instAddrHandle = instance as VMAddrPool.AddressHandle;
                instance = instAddrHandle.GetObj();
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
            if (instAddrHandle != null)
                instAddrHandle.SetObj(instance);
            if (propAddrHandles != null)
            {
                for (var i = 0; i != propAddrHandles.Length; ++i)
                {
                    var paramAddrHandle = propAddrHandles[i];
                    if (paramAddrHandle == null)
                        continue;
                    paramAddrHandle.SetObj(propIdx[i]);
                }
            }

            return true;
        }

        private FieldInfo typeRefArgumentsField = null;

        private Type GetTypeInfoFromTypeReference(TypeReference typeRef)
        {
            var typeDef = typeRef.Resolve();
            var typeInfo = GetTypeByName(typeDef.FullName);
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

            var allMethods = classInfo.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            foreach (var method in allMethods)
            {
                if (method.Name != methodDef.Name)
                    continue;

                if (method.GetParameters().Length != methodDef.Parameters.Count)
                    continue;

                var match = true;
                for (var i = 0; i != method.GetParameters().Length; ++i)
                {
                    var paramInfo = method.GetParameters()[i];
                    var paramObj = parameters[i];
                    if (!IsParameterMatch(paramInfo.ParameterType, paramObj.GetType()))
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    methodInfo = method;
                    break;
                }
            }

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
            
            var allConstructors = classInfo.GetConstructors();
            foreach (var constructor in allConstructors)
            {
                if (constructor.GetParameters().Length != methodDef.Parameters.Count)
                    continue;

                var match = true;
                for (var i = 0; i != constructor.GetParameters().Length; ++i)
                {
                    var paramInfo = constructor.GetParameters()[i];
                    var paramObj = parameters[i];
                    if (!IsParameterMatch(paramInfo.ParameterType, paramObj.GetType()))
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    constructorInfo = constructor;
                    break;
                }
            }
            return constructorInfo;
        }


        private bool IsParameterMatch(Type needType, Type checkType)
        {
            if (!needType.IsGenericParameter)
                return needType.IsAssignableFrom(checkType);

            // exp. "where T : BaseClassA, BaseClassB"
            var baseTypeConstraints = needType.GetGenericParameterConstraints();
            foreach (var baseType in baseTypeConstraints)
            {
                if (!baseType.IsAssignableFrom(checkType))
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
                if (checkType.IsValueType)
                    return false;
            }
            if ((specialConstraints & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
            {
                if (Nullable.GetUnderlyingType(checkType) != null)
                    return false;
            }

            if ((specialConstraints & GenericParameterAttributes.DefaultConstructorConstraint) != 0)
            {
                var constructors = checkType.GetConstructors();
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