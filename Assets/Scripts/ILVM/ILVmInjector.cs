using UnityEngine;
using System;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace ILVM
{
    [Serializable]
    public class ILVmInjectConfig
    {
        public List<string> injectClass = new List<string>();
        public List<string> nameSpaceFilter = new List<string>();
        public List<string> classNameFilter = new List<string>();
        public List<string> methodNameFilter = new List<string>();
    }

    public class ILVmInjector
    {
        private static HashSet<Type> injectClass = new HashSet<Type>();
        private static HashSet<string> filterNamespace = new HashSet<string>();
        private static HashSet<string> filterClass = new HashSet<string>();
        private static HashSet<string> filterMethod = new HashSet<string>();
        public static void LoadInjectConfig()
        {
            injectClass.Clear();
            filterNamespace.Clear();
            filterClass.Clear();
            filterMethod.Clear();

            string jsonData = null;
            var files = Directory.GetFiles(Application.dataPath, "ILVmInjectConfig.json", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                if (file.EndsWith("ILVmInjectConfig.json"))
                {
                    jsonData = File.ReadAllText(Application.dataPath + "/Scripts/ILVM/Editor/ILVmInjectConfig.json");
                    break;
                }
            }
            var filter = JsonUtility.FromJson<ILVmInjectConfig>(jsonData);
            
            foreach (var name in filter.injectClass)
            {
                var type = GetTypeInAssembly(name);
                if (type != null)
                    injectClass.Add(type);
            }
            foreach (var ns in filter.nameSpaceFilter)
            {
                filterNamespace.Add(ns);
            }
            foreach (var name in filter.classNameFilter)
            {
                filterClass.Add(name);
            }
            foreach (var name in filter.methodNameFilter)
            {
                filterMethod.Add(name);
            }
        }

        private static bool FilterClass(Type type)
        {
            if (filterNamespace.Contains(type.Namespace))
                return false;
            foreach (var ns in filterNamespace)
            {
                if (type.Namespace != null && type.Namespace.StartsWith(ns + "."))
                    return false;
            }

            if (filterClass.Contains(type.Name))
                return false;

            if (type.Name.Contains("<"))
                return false;

            return true;
        }


        private static bool FilterMethod(MethodDefinition method)
        {
            if (!method.IsDefinition || !method.HasBody)
                return false;

            var methodFullName = method.DeclaringType.FullName + method.Name;
            if (filterMethod.Contains(methodFullName))
                return false;

            if (method.IsAbstract || method.Name == "op_Equality" || method.Name == "op_Inequality")
                return false;

            if (method.IsUnmanaged)
                return false;

            return true;
        }

        private static Type GetTypeInAssembly(string typeName)
        {
            var assembly = Assembly.Load("Assembly-CSharp");
            var type = assembly.GetType(typeName);
            return type;
        }

        public static IEnumerable<Type> GetTypeToInject()
        {
            LoadInjectConfig();
            var allTypes = injectClass.Count > 0 ? injectClass.ToArray() : Assembly.Load("Assembly-CSharp").GetTypes();
            return (from type in allTypes
                    where FilterClass(type) 
                    select type);
        }

        public static bool IsInjected()
        {
            using (var assemblyHandle = new AssemblyHandle())
            {
                return assemblyHandle.IsInjected();
            }
        }

        public static void Inject()
        {
            using (var assemblyHandle = new AssemblyHandle())
            {
                if (assemblyHandle.IsInjected())
                {
                    Logger.Error("ILVmInjector: assembly has already injected!");
                    return;
                }
                ILVmManager.ClearMethodId();

                var timer = new DebugTimer();
                timer.Start("Get Type To Inject");
                var type2Inject = new HashSet<string>();
                foreach (var typeInfo in GetTypeToInject())
                {
                    type2Inject.Add(typeInfo.FullName);
                }
                timer.Stop();

                timer.Start("Get Method To Inject");
                var method2Inject = new List<MethodDefinition>();
                foreach (var typeDef in assemblyHandle.GetAssembly().MainModule.GetTypes())
                {
                    if (!type2Inject.Contains(typeDef.FullName))
                        continue;

                    foreach (var methodDef in typeDef.Methods)
                    {
                        if (!FilterMethod(methodDef))
                            continue;
                        method2Inject.Add(methodDef);
                    }
                }
                timer.Stop();

                timer.Start("Inject Method");
                var succ = true;
                try
                {
                    for (var i = 0; i != method2Inject.Count; ++i)
                    {
                        var methodDef = method2Inject[i];
                        InjectMethod(i, methodDef, assemblyHandle);
                    }

                    // mark as injected
                    assemblyHandle.SetIsInjected();

                    // modify assembly
                    assemblyHandle.Write();

                    // save method id
                    for (var i = 0; i != method2Inject.Count; ++i)
                    {
                        var methodDef = method2Inject[i];
                        ILVmManager.AddMethodId(i, methodDef);
                    }
                }
                catch (Exception e)
                {
                    succ = false;
                    Logger.Error("ILVmInjector: inject failed: \n{0}", e.ToString());
                }
                timer.Stop();
                if (!succ)
                    return;

                timer.Start("Copy Assembly");
                assemblyHandle.Dispose();       // release before copy
                var copySucc = true;
                try
                {
                    var assemblyPath = ILVmManager.GetAssemblyPath();
                    var assemblyHotfixPath = ILVmManager.GetAssemblyHotfixPath(); ;
                    var assemblyPDBPath = assemblyPath.Replace(".dll", ".pdb");
                    var assemblyHotfixPDBPath = assemblyHotfixPath.Replace(".dll", ".pdb");
                    Logger.Log("assemblyPath: {0} \t{1}", assemblyPath, assemblyPDBPath);
                    Logger.Log("assemblyHotfixPath: {0} \t{1}", assemblyHotfixPath, assemblyHotfixPDBPath);

                    System.IO.File.Copy(assemblyHotfixPath, assemblyPath, true);
                    System.IO.File.Copy(assemblyHotfixPDBPath, assemblyPDBPath, true);
                }
                catch (Exception e)
                {
                    copySucc = false;
                    Logger.Error("ILVmInjector: override assembly failed: {0}", e);
                }
                timer.Stop();
                if (!copySucc)
                    return;

                timer.Start("Save MethodId");
                ILVmManager.SaveMethodIdToFile();
                timer.Stop();

                timer.Start("Print MethodId");
                ILVmManager.DumpAllMethodId();
                timer.Stop();

                Logger.Error("ILVmInjector: inject assembly succ");
            }
        }

        private static void InjectMethod(int methodId, MethodDefinition method, AssemblyHandle assemblyHandle)
        {
            var assembly = assemblyHandle.GetAssembly();
            var body = method.Body;
            var originIL = body.Instructions;
            var ilProcessor = body.GetILProcessor();
            var insertPoint = originIL[0];
            var endPoint = originIL[originIL.Count - 1];
            var ilList = new List<Instruction>();
            ilList.Add(Instruction.Create(OpCodes.Ldc_I4, methodId));
            ilList.Add(Instruction.Create(OpCodes.Call, assemblyHandle.MR_HasMethodInfo));
            ilList.Add(Instruction.Create(OpCodes.Brfalse, insertPoint));
            InjectMethodArgument(methodId, method, ilList, assemblyHandle);
            InjectMethodCall(methodId, method, ilList, assemblyHandle);

            // goto the end
            ilList.Add(Instruction.Create(OpCodes.Br, endPoint));

            // inject il
            for (var i = ilList.Count - 1; i >= 0; --i)
                ilProcessor.InsertBefore(originIL[0], ilList[i]);

            Logger.Log("InjectMethod: {0}", method.FullName);
        }

        private static void InjectMethodArgument(int methodId, MethodDefinition method, List<Instruction> ilList, AssemblyHandle assemblyHandle)
        {
            var assembly = assemblyHandle.GetAssembly();
            var shift = 2;  // extra: methodId, instance

            //object[] arr = new object[argumentCount + shift]
            var argumentCount = method.Parameters.Count;
            ilList.Add(Instruction.Create(OpCodes.Ldc_I4, argumentCount + shift));
            ilList.Add(Instruction.Create(OpCodes.Newarr, assemblyHandle.TR_SystemObject));

            // save arr, needed by 'Ref' logic
            if (method.Parameters.Any(param => param.ParameterType.IsByReference))
            {
                method.Body.Variables.Add(new VariableDefinition(assemblyHandle.TR_SystemObjectArr));
                ilList.Add(Instruction.Create(OpCodes.Dup));
                ilList.Add(GetStlocInstruction(method.Body.Variables.Count - 1, method));
            }

            // methodId
            ilList.Add(Instruction.Create(OpCodes.Dup));
            ilList.Add(Instruction.Create(OpCodes.Ldc_I4, 0));
            ilList.Add(Instruction.Create(OpCodes.Ldc_I4, methodId));
            ilList.Add(Instruction.Create(OpCodes.Box, assemblyHandle.TR_SystemInt));
            ilList.Add(Instruction.Create(OpCodes.Stelem_Ref));

            // instance
            ilList.Add(Instruction.Create(OpCodes.Dup));
            ilList.Add(Instruction.Create(OpCodes.Ldc_I4, 1));
            if (method.IsStatic)
                ilList.Add(Instruction.Create(OpCodes.Ldnull));
            else
                ilList.Add(Instruction.Create(OpCodes.Ldarg_0));
            ilList.Add(Instruction.Create(OpCodes.Stelem_Ref));

            // arguments
            for (int i = 0; i < argumentCount; ++i)
            {
                var parameter = method.Parameters[i];

                // value = argument[i]
                ilList.Add(Instruction.Create(OpCodes.Dup));
                ilList.Add(Instruction.Create(OpCodes.Ldc_I4, i + shift));
                ilList.Add(Instruction.Create(OpCodes.Ldarg, parameter));

                // box
                TryBoxMethodArgument(parameter, ilList, assemblyHandle);

                // arr[i] = value;
                ilList.Add(Instruction.Create(OpCodes.Stelem_Ref));
            }

            // don't pop it, we will need it when call method wrapper
            // ilList.Add(Instruction.Create(OpCodes.Pop));
        }

        private static void TryBoxMethodArgument(ParameterDefinition param, List<Instruction> ilList, AssemblyHandle assemblyHandle)
        {
            var paramType = param.ParameterType;
            if (paramType.IsValueType)
            {
                ilList.Add(Instruction.Create(OpCodes.Box, paramType));
            }
            else if (paramType.IsGenericParameter)
            {
                ilList.Add(Instruction.Create(OpCodes.Box, assemblyHandle.GetAssembly().MainModule.ImportReference(paramType)));
            }
            else if (paramType is ByReferenceType)
            {
                //var byRefType = paramType as ByReferenceType;
                //if (byRefType.IsByReference)
                //    ilList.Add(Instruction.Create(OpCodes.Ldind_Ref));

                // now we wrap it by VMAddr
                var elemType = (paramType as ByReferenceType).GetElementType();
                ilList.Add(Instruction.Create(OpCodes.Ldobj, elemType));
                ilList.Add(Instruction.Create(OpCodes.Box, elemType));
                ilList.Add(Instruction.Create(OpCodes.Call, assemblyHandle.MR_VMAddrCtor.Resolve()));
            }
        }

        private static void TryUnboxMethodArgument(TypeReference typeRef, List<Instruction> ilList, AssemblyHandle assemblyHandle)
        {
            var typeName = typeRef.FullName;
            OpCode op;
            if (typeName == "System.Boolean")
                op = OpCodes.Ldind_U1;
            else if (typeName == "System.Int16")
                op = OpCodes.Ldind_I2;
            else if (typeName == "System.UInt16")
                op = OpCodes.Ldind_U2;
            else if (typeName == "System.Int32")
                op = OpCodes.Ldind_I4;
            else if (typeName == "System.UInt32")
                op = OpCodes.Ldind_U4;
            else if (typeName == "System.Int64")
                op = OpCodes.Ldind_I8;
            else if (typeName == "System.UInt64")
                op = OpCodes.Ldind_I8;
            else if (typeName == "System.Single")
                op = OpCodes.Ldind_R4;
            else if (typeName == "System.Double")
                op = OpCodes.Ldind_R8;
            else
                op = OpCodes.Nop;

            // primitive types
            if (op != OpCodes.Nop)
            {
                ilList.Add(Instruction.Create(OpCodes.Unbox, typeRef));
                ilList.Add(Instruction.Create(op));
            }
            // other types like custom struct
            else
            {
                ilList.Add(Instruction.Create(OpCodes.Unbox_Any, typeRef));
            }
        }

        private static void InjectMethodCall(int methodId, MethodDefinition method, List<Instruction> ilList, AssemblyHandle assemblyHandle)
        {
            var hasReturnVal = method.ReturnType.FullName != "System.Void";
            if (hasReturnVal)
                ilList.Add(Instruction.Create(OpCodes.Call, assemblyHandle.MR_MethodReturnObjectWrapper));
            else
                ilList.Add(Instruction.Create(OpCodes.Call, assemblyHandle.MR_MethodReturnVoidWrapper));

            // try unbox return value
            if (method.ReturnType.IsValueType)
            {
                var returnType = assemblyHandle.GetAssembly().MainModule.ImportReference(method.ReturnType);
                TryUnboxMethodArgument(returnType, ilList, assemblyHandle);
            }

            // set argument val back, to implent 'Ref' logic
            if (method.Parameters.Any(param => param.ParameterType.IsByReference))
            {
                var shift = 2;  // extra: methodId, instance
                for (var i = 0; i != method.Parameters.Count; ++i)
                {
                    var param = method.Parameters[i];
                    if (!param.ParameterType.IsByReference)
                        continue;
                    var elemType = (param.ParameterType as ByReferenceType).GetElementType();

                    // load ref
                    ilList.Add(GetLdargInstruction(i, method));

                    // load param arr
                    ilList.Add(GetLdlocInstruction(method.Body.Variables.Count - 1, method));
                    ilList.Add(GetLdcInstruction(i + shift));
                    ilList.Add(Instruction.Create(OpCodes.Ldelem_Ref));
                    ilList.Add(Instruction.Create(OpCodes.Call, assemblyHandle.MR_VMAddrGetObj));
                    TryUnboxMethodArgument(elemType, ilList, assemblyHandle);

                    // set ref
                    ilList.Add(Instruction.Create(OpCodes.Stobj, elemType));
                }
            }
        }

        private static Instruction GetLdcInstruction(int idx)
        {
            Instruction il = null;
            if (idx <= 8)
            {
                switch (idx)
                {
                    case 0:
                        il = Instruction.Create(OpCodes.Ldc_I4_0);
                        break;
                    case 1:
                        il = Instruction.Create(OpCodes.Ldc_I4_1);
                        break;
                    case 2:
                        il = Instruction.Create(OpCodes.Ldc_I4_2);
                        break;
                    case 3:
                        il = Instruction.Create(OpCodes.Ldc_I4_3);
                        break;
                    case 4:
                        il = Instruction.Create(OpCodes.Ldc_I4_4);
                        break;
                    case 5:
                        il = Instruction.Create(OpCodes.Ldc_I4_5);
                        break;
                    case 6:
                        il = Instruction.Create(OpCodes.Ldc_I4_6);
                        break;
                    case 7:
                        il = Instruction.Create(OpCodes.Ldc_I4_7);
                        break;
                    case 8:
                        il = Instruction.Create(OpCodes.Ldc_I4_8);
                        break;
                }
            }
            else if (idx < 128)
            {
                il = Instruction.Create(OpCodes.Ldc_I4_S, idx);
            }
            else
            {
                il = Instruction.Create(OpCodes.Ldc_I4, idx);
            }
            return il;
        }

        private static Instruction GetStlocInstruction(int idx, MethodDefinition methodDef)
        {
            Instruction il = null;
            if (idx < 4)
            {
                switch (idx)
                {
                    case 0:
                        il = Instruction.Create(OpCodes.Stloc_0);
                        break;
                    case 1:
                        il = Instruction.Create(OpCodes.Stloc_1);
                        break;
                    case 2:
                        il = Instruction.Create(OpCodes.Stloc_2);
                        break;
                    case 3:
                        il = Instruction.Create(OpCodes.Stloc_3);
                        break;
                }
            }
            else if (idx < 256)
            {
                var val = methodDef.Body.Variables[idx];
                il = Instruction.Create(OpCodes.Stloc_S, val);
            }
            else
            {
                var val = methodDef.Body.Variables[idx];
                il = Instruction.Create(OpCodes.Stloc, val);
            }
            return il;
        }

        private static Instruction GetLdlocInstruction(int idx, MethodDefinition methodDef)
        {
            Instruction il = null;
            if (idx < 4)
            {
                switch (idx)
                {
                    case 0:
                        il = Instruction.Create(OpCodes.Ldloc_0);
                        break;
                    case 1:
                        il = Instruction.Create(OpCodes.Ldloc_1);
                        break;
                    case 2:
                        il = Instruction.Create(OpCodes.Ldloc_2);
                        break;
                    case 3:
                        il = Instruction.Create(OpCodes.Ldloc_3);
                        break;
                }
            }
            else if (idx < 256)
            {
                var val = methodDef.Body.Variables[idx];
                il = Instruction.Create(OpCodes.Ldloc_S, val);
            }
            else
            {
                var val = methodDef.Body.Variables[idx];
                il = Instruction.Create(OpCodes.Ldloc, val);
            }
            return il;
        }

        private static Instruction GetLdargInstruction(int idx, MethodDefinition methodDef)
        {
            Instruction il = null;
            if (idx < 4)
            {
                switch (idx)
                {
                    case 0:
                        il = Instruction.Create(OpCodes.Ldarg_0);
                        break;
                    case 1:
                        il = Instruction.Create(OpCodes.Ldarg_1);
                        break;
                    case 2:
                        il = Instruction.Create(OpCodes.Ldarg_2);
                        break;
                    case 3:
                        il = Instruction.Create(OpCodes.Ldarg_3);
                        break;
                }
            }
            else if (idx < 256)
            {
                var arg = methodDef.Parameters[idx];
                il = Instruction.Create(OpCodes.Ldarg_S, arg);
            }
            else
            {
                var arg = methodDef.Parameters[idx];
                il = Instruction.Create(OpCodes.Ldarg, arg);
            }
            return il;
        }
    }
}
