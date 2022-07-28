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

        
        [MenuItem("ILVirtualMachine/Clear Inject", false, 21)]
        public static void ClearInject()
        {
            var assetsPath = Application.dataPath;
            var assetsUri = new System.Uri(assetsPath);
            var files = Directory.GetFiles(assetsPath, "ILVirtualMachine.cs", SearchOption.AllDirectories);
            foreach (var file in files)
            { 
                if (file.EndsWith("ILVirtualMachine.cs"))
                {
                    // reimport to force compile
			        var relativeUri = assetsUri.MakeRelativeUri(new System.Uri(file));
			        var relativePath = System.Uri.UnescapeDataString(relativeUri.ToString());
                    AssetDatabase.ImportAsset(relativePath, ImportAssetOptions.ForceUpdate);
                    AssetDatabase.Refresh();
                    break;
                }
            }
        }

        [MenuItem("ILVirtualMachine/Clear Hotfix", false, 22)]
        public static void ClearHotfix()
        {
            ILVmRunner.ClearHotfix();
        }
        

        [MenuItem("ILVirtualMachine/RunTests", false, 41)]
        public static void RunTests()
        {
            using (var assemblyHandle = new AssemblyHandle())
            { 
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
                        var vmRet = vm.Execute(methodTypeDef, parameters);
                        Logger.Error("#ILVM_Test# {0} \treflection ret: {1} \tvm ret: {2} \tsucc: {3}", method.DeclaringType, rfRet, vmRet, (string)rfRet == (string)vmRet ? "<color=green>succ</color>" :  "<color=red>failed</color>");
                    }
                }
                catch (Exception e)
                {
                    Logger.Error("ILVmEditor: exception: {0}", e);
                }
            }
        }
    }

}