using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.IO;
using System;
using System.Text;
using System.Diagnostics;
using UnityEngine;
using UnityEditor;
using Mono.Cecil;

namespace ILVM
{

    // [InitializeOnLoad]
    public static class ILVmEditor 
    {
        [MenuItem("ILVirtualMachine/Inject", false, 1)]
        public static void TryInject()
        {
            if (EditorApplication.isCompiling || Application.isPlaying)
            {
                Logger.Error("ILVMEditor: inject: cannot inject while compiling or playing");
                return;
            }
            EditorUtility.DisplayProgressBar("ILVM Inject", "injecting...", 0);
            try
            {
                ILVmInjector.Inject();
            }
            catch(Exception e)
            {
                Logger.Error("ILVMEditor: inject: {0}", e.ToString());
            }
            EditorUtility.ClearProgressBar();
        }
        

        [MenuItem("ILVirtualMachine/Hotfix", false, 2)]
        public static void TryHotfix()
        {
            ILVmRunner.Hotfix();
        }

        [MenuItem("ILVirtualMachine/Clear Hotfix", false, 3)]
        public static void ClearHotfix()
        {
            ILVmRunner.ClearHotfix();
        }

        [MenuItem("ILVirtualMachine/TestCode", false, 41)]
        public static unsafe void TestCode()
        {
            //var testTnst = (ILVMTest.Test_InvokeBaseMethod)Activator.CreateInstance(typeof(ILVMTest.Test_InvokeBaseMethod));
            var testInst = new ILVMTest.Test_InvokeBaseMethod();
            var baseInst = (ILVMTest.Inner_Test_InvokeBaseMethodBase)testInst;
            //var baseInst = new ILVMTest.Inner_Test_InvokeBaseMethodBase();

            var methodInfoBase = typeof(ILVMTest.Inner_Test_InvokeBaseMethodBase).GetMethod("Func");
            var methodInfoTest = typeof(ILVMTest.Test_InvokeBaseMethod).GetMethod("Func");

            var methodBasePtr = methodInfoBase.MethodHandle.GetFunctionPointer();
            var methodTestPtr = methodInfoTest.MethodHandle.GetFunctionPointer();

            var methodBaseNew = (Func<string>)Activator.CreateInstance(typeof(Func<string>), baseInst, methodBasePtr);
            var methodTestNew = (Func<string>)Activator.CreateInstance(typeof(Func<string>), testInst, methodTestPtr);

            var delegateBase = Delegate.CreateDelegate(typeof(Func<string>), baseInst, methodInfoBase, false);
            var delegateTest = Delegate.CreateDelegate(typeof(Func<string>), testInst, methodInfoTest, false);

            Logger.Error("Ret Base: {0}", (string)methodInfoBase.Invoke(baseInst, null));
            Logger.Error("Ret Test: {0}", (string)methodInfoTest.Invoke(testInst, null));
            
            Logger.Error("Ret Base New: {0} \t{1}", methodBaseNew(), methodBasePtr);
            Logger.Error("Ret Test New: {0} \t{1}", methodTestNew(), methodTestPtr);
            
            Logger.Error("Ret Base Delegate: {0}", delegateBase.DynamicInvoke(null));
            Logger.Error("Ret Test Delegate: {0}", delegateTest.DynamicInvoke(null));
        }
        
        
        
        [MenuItem("ILVirtualMachine/RunTests", false, 21)]
        public static void TestVM()
        {
            var assemblyHandle = new AssemblyHandle();
            var assembly = assemblyHandle.GetAssembly();
            if (assembly == null)
                return;

            var methodToTest = new List<MethodInfo>();
            foreach (var dotnetAssembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in dotnetAssembly.GetTypes())
                {
                    if (type.Namespace != "ILVMTest")
                        continue;
                    if (!type.Name.StartsWith("Test_"))
                        continue;
                    var method = type.GetMethod("Func", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                    if (method == null)
                        continue;
                    methodToTest.Add(method);
                }
            }

            try
            {
                var vm = new ILVirtualMachine();
                foreach (var method in methodToTest)
                {
                    var rfClsInst = Activator.CreateInstance(method.DeclaringType);
                    var rfRet = method.Invoke(rfClsInst, null);

                    var classTypeDef = assembly.MainModule.GetType(method.DeclaringType.FullName);
                    var methodTypeDef = classTypeDef.Methods.First(m => m.Name == "Func");

                    var vmClsInst = Activator.CreateInstance(method.DeclaringType);
                    var parameters = new object[] { vmClsInst };
                    var vmRet = vm.Execute(methodTypeDef.Body.Instructions, parameters);
                    Logger.Error("#ILVM_Test# {0} \treflection ret: {1} \tvm ret: {2} \tsucc: {3}", method.DeclaringType, rfRet, vmRet, (string)rfRet == (string)vmRet ? "<color=green>succ</color>" :  "<color=red>failed</color>");
                }
            }
            catch (Exception e)
            {
                Logger.Error("ILVmEditor: exception: {0}", e);
            }
            assemblyHandle.Dispose();
        }
    }

}