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
    public class ILVmRunner
    {
        private static AssemblyHandle currentAssembly;

        public static void Hotfix()
        {
            if (currentAssembly != null)
                currentAssembly.Dispose();
            currentAssembly = null;
            
            var debugPath = Application.dataPath.Replace("/Assets", "/Library/ILVM/Assembly-CSharp.dll");
            var assemblyHandle = new AssemblyHandle(debugPath);
            try
            {
                if (assemblyHandle.IsInjected())
                {
                    Logger.Error("ILVmRunner: cannot hotfix injected assembly!");
                    assemblyHandle.Dispose();
                    assemblyHandle = null;
                    return;
                }
                ILVmManager.LoadMethodIdFromFile(assemblyHandle);
                ILVmManager.ClearMethodInfo();
                ILVM.VMAddr.Clear();

                var timer = new DebugTimer();
                timer.Start("Print MethodId");
                ILVmManager.DumpAllMethodId();
                timer.Stop();

                timer.Start("Load Method");
                var methodNeedFix = new List<MethodDefinition>();
                var moudle = assemblyHandle.GetAssembly().MainModule;
                foreach (var type in moudle.GetTypes())
                {
                    if (!type.HasMethods)
                        continue;

                    foreach (var method in type.Methods)
                    {
                        if (IsNeedFix(method))
                            methodNeedFix.Add(method);
                    }
                }
                timer.Stop();

                if (methodNeedFix.Count <= 0)
                {
                    Logger.Error("ILVmRunner: no method need fix");
                    assemblyHandle.Dispose();
                    assemblyHandle = null;
                    return;
                }

                //timer.Start("Print Method");
                //foreach (var method in methodNeedFix)
                //{
                //    Logger.Log("method: {0}", method.DeclaringType, method.Body);
                //    var methodBody = method.Body;
                //    foreach (var il in methodBody.Instructions)
                //    {
                //        Logger.Log("il: {0}", il.ToString());
                //    }
                //}
                //timer.Stop();

                timer.Start("Hotfix Method");
                foreach (var method in methodNeedFix)
                {
                    ILVmManager.SetMethodInfo(method);
                    Logger.Log("ILVmRunner: hotfix method: {0}", method);
                }
                timer.Stop();
            }
            catch (Exception e)
            {
                if (assemblyHandle != null)
                    assemblyHandle.Dispose();
                assemblyHandle = null;
            }

            currentAssembly = assemblyHandle;
        }

        public static void ClearHotfix()
        {
            ILVmManager.ClearMethodInfo();
            ILVM.VMAddr.Clear();
            if (currentAssembly != null)
                currentAssembly.Dispose();
            currentAssembly = null;
        }


        private static bool IsNeedFix(MethodDefinition method)
        {
            if (!method.HasCustomAttributes)
                return false;

            foreach (var attr in method.CustomAttributes)
            {
                if (attr.Constructor.DeclaringType.Name == "HotfixAttribute")
                    return true;
            }
            return false;
        }
    }

}