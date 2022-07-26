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

        // public static void TryRun()
        // {
        //     var timer = new DebugTimer();
        //     timer.Start("Load Assembly");
        //     var assemblyHandle = new AssemblyHandle();
        //     var assembly = assemblyHandle.GetAssembly();
        //     if (assembly == null)
        //         return;
        //     timer.Stop();

        //     timer.Start("Load Method");
        //     var methodNeedFix = new List<MethodDefinition>();
        //     var moudle = assembly.MainModule;
        //     foreach (var type in moudle.GetTypes())
        //     {
        //         if (!type.HasMethods)
        //             continue;

        //         foreach (var method in type.Methods)
        //         {
        //             if (IsNeedFix(method))
        //                 methodNeedFix.Add(method);
        //         }
        //     }
        //     timer.Stop();

        //     if (methodNeedFix.Count <= 0)
        //     {
        //         UnityEngine.Debug.LogFormat("ILVmEditor: no method need fix");
        //         return;
        //     }

        //     timer.Start("Print Method");
        //     foreach (var method in methodNeedFix)
        //     {
        //         UnityEngine.Debug.LogFormat("method: {0}", method.DeclaringType, method.Body);
        //         var methodBody = method.Body;
        //         foreach (var il in methodBody.Instructions)
        //         {
        //             UnityEngine.Debug.LogFormat("il: {0}", il.ToString());
        //         }
        //     }
        //     timer.Stop();

        // }


        // private static bool IsNeedFix(MethodDefinition method)
        // {
        //     if (!method.HasCustomAttributes)
        //         return false;

        //     foreach (var attr in method.CustomAttributes)
        //     {
		// 		if (attr.Constructor.DeclaringType.Name.StartsWith("PatchAttribute"))
		// 			return true;
        //     }
        //     return false;
        // }


        [MenuItem("ILVirtualMachine/RunTests", false, 1)]
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


        [MenuItem("ILVirtualMachine/TryInject", false, 2)]
        public static void TestInject()
        {
            ILVmInjector.Inject();
        }
        
    }

}