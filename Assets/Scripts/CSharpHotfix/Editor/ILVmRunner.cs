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
    public class DebugTimer
    {
        private bool finished = true;
        private string timerName = "";
        private System.Diagnostics.Stopwatch stopWatch;

        public DebugTimer()
        {
            stopWatch = System.Diagnostics.Stopwatch.StartNew();
        }

        ~DebugTimer()
        {
            if (finished)
                return;
            
            UnityEngine.Debug.LogErrorFormat("Timer: {0} \t{1}ms \t(quit unexpectly)", timerName, stopWatch.ElapsedMilliseconds);
            stopWatch.Stop();
        }

        public void Start(string name)
        {
            if (!finished)
                Stop();
            finished = false;
            timerName = name;
            stopWatch.Reset();
        }

        public void Stop()
        {
            if (stopWatch == null)
                return;

            finished = true;
            UnityEngine.Debug.LogErrorFormat("Timer: {0} \t{1}ms", timerName, stopWatch.ElapsedMilliseconds);
            stopWatch.Stop();
        }
    }


    public class AssemblyHandle : IDisposable
    {
        private AssemblyDefinition assemblyDef;

        public AssemblyHandle()
        {
            Init();
        }

        private void Init()
        {
            var path = Application.dataPath.Replace("/Assets", "/Library/ScriptAssemblies/Assembly-CSharp.dll");
            
            // read assembly
            AssemblyDefinition assembly = null;
            var readSymbols = true;
            try
            {
                // try read with symbols
                assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadSymbols = true });
            }
            catch
            {
                // read with symbols failed, just don't read them
                UnityEngine.Debug.LogWarningFormat("ILRunner: read assembly with symbol failed: {0}", path);
                try
                {
                    readSymbols = false;
                    assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadSymbols = false });
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogErrorFormat("ILRunner: read assembly failed: {0}", e);
                }
            }
            if (assembly == null)
                return;

            // init resolver search path
            var initResolverSucc = true;
            var resolver = assembly.MainModule.AssemblyResolver as BaseAssemblyResolver;
            
            foreach (var searchPath in
                (from asm in AppDomain.CurrentDomain.GetAssemblies()
                    select System.IO.Path.GetDirectoryName(asm.ManifestModule.FullyQualifiedName)).Distinct())
            {
                try
                {
                    //UnityEngine.Debug.LogError("searchPath:" + searchPath);
                    resolver.AddSearchDirectory(searchPath);
                }
                catch (Exception e) 
                { 
                    initResolverSucc = false;
                    UnityEngine.Debug.LogErrorFormat("ILRunner: init resolver failed: {0}", e);
                }
            }
            if (!initResolverSucc)
                return;

            assemblyDef = assembly;
        }

        public void Dispose()
        {
            if (assemblyDef == null)
                return;
            
            // clear symbol reader, incase lock file on windows
            if (assemblyDef.MainModule.SymbolReader != null)
            {
                assemblyDef.MainModule.SymbolReader.Dispose();
            }
            assemblyDef.Dispose();
            assemblyDef = null;
        }

        public AssemblyDefinition GetAssembly()
        {
            return assemblyDef;
        }
    }


    // [InitializeOnLoad]
    public static class ILRunner 
    {

        public static void TryRun()
        {
            var timer = new DebugTimer();
            timer.Start("Load Assembly");
            var assemblyHandle = new AssemblyHandle();
            var assembly = assemblyHandle.GetAssembly();
            if (assembly == null)
                return;
            timer.Stop();

            timer.Start("Load Method");
            var methodNeedFix = new List<MethodDefinition>();
            var moudle = assembly.MainModule;
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
                UnityEngine.Debug.LogFormat("ILRunner: no method need fix");
                return;
            }

            timer.Start("Print Method");
            foreach (var method in methodNeedFix)
            {
                UnityEngine.Debug.LogFormat("method: {0}", method.DeclaringType, method.Body);
                var methodBody = method.Body;
                foreach (var il in methodBody.Instructions)
                {
                    UnityEngine.Debug.LogFormat("il: {0}", il.ToString());
                }
            }
            timer.Stop();

        }


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
                    UnityEngine.Debug.LogErrorFormat("#ILVM_Test# {0} \treflection ret: {1} \tvm ret: {2} \tsucc: {3}", method.DeclaringType, rfRet, vmRet, (string)rfRet == (string)vmRet ? "<color=green>succ</color>" :  "<color=red>failed</color>");
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogErrorFormat("ILRunner: exception: {0}", e);
            }
            assemblyHandle.Dispose();
        }


        [MenuItem("ILVirtualMachine/TryInject", false, 2)]
        public static void TestInject()
        {
            //
        }


        private static bool IsNeedFix(MethodDefinition method)
        {
            if (!method.HasCustomAttributes)
                return false;

            foreach (var attr in method.CustomAttributes)
            {
				if (attr.Constructor.DeclaringType.Name.StartsWith("PatchAttribute"))
					return true;
            }
            return false;
        }

        private static AssemblyDefinition GetAssemblyDefinition()
        {
            var path = Application.dataPath.Replace("/Assets", "/Library/ScriptAssemblies/Assembly-CSharp.dll");
            
            // read assembly
            AssemblyDefinition assembly = null;
            var readSymbols = true;
            try
            {
                // try read with symbols
                assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadSymbols = true });
            }
            catch
            {
                // read with symbols failed, just don't read them
                UnityEngine.Debug.LogWarningFormat("ILRunner: read assembly with symbol failed: {0}", path);
                try
                {
                    readSymbols = false;
                    assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadSymbols = false });
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogErrorFormat("ILRunner: read assembly failed: {0}", e);
                }
            }
            if (assembly == null)
                return null;

            // init resolver search path
            var initResolverSucc = true;
            var resolver = assembly.MainModule.AssemblyResolver as BaseAssemblyResolver;
            
            foreach (var searchPath in
                (from asm in AppDomain.CurrentDomain.GetAssemblies()
                    select System.IO.Path.GetDirectoryName(asm.ManifestModule.FullyQualifiedName)).Distinct())
            {
                try
                {
                    //UnityEngine.Debug.LogError("searchPath:" + searchPath);
                    resolver.AddSearchDirectory(searchPath);
                }
                catch (Exception e) 
                { 
                    initResolverSucc = false;
                    UnityEngine.Debug.LogErrorFormat("ILRunner: init resolver failed: {0}", e);
                }
            }
            if (!initResolverSucc)
                return null;

            return assembly;
        }

        
    }

}