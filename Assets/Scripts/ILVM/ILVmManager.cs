using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

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
            stopWatch.Start();
        }

        public void Stop()
        {
            if (stopWatch == null)
                return;

            finished = true;
            stopWatch.Stop();
            UnityEngine.Debug.LogErrorFormat("Timer: {0} \t{1}ms", timerName, stopWatch.ElapsedMilliseconds);
        }
    }

    public class Logger
    {
        public static void Log(string message, params object[] args)
        {
            UnityEngine.Debug.LogErrorFormat("#ILVM# " + message, args);
        }

        public static void Error(string message, params object[] args)
        {
            UnityEngine.Debug.LogErrorFormat("#ILVM_ERROR# " + message, args);
        }
    }
    

    public class AssemblyHandle : IDisposable
    {
        private AssemblyDefinition assemblyDef;
        private bool assemblyReadSymbols = false;

        public AssemblyHandle()
        {
            Init();
        }

        private void Init()
        {            
            var path = ILVmManager.GetAssemblyPath();

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
                Logger.Log("AssemblyHandle: read assembly with symbol failed: {0}", path);
                try
                {
                    readSymbols = false;
                    assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadSymbols = false });
                }
                catch (Exception e)
                {
                    Logger.Error("AssemblyHandle: read assembly failed: {0}", e);
                }
            }
            if (assembly == null)
                return;
            assemblyReadSymbols = readSymbols;

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
                    Logger.Error("AssemblyHandle: init resolver failed: {0}", e);
                }
            }
            if (!initResolverSucc)
                return;

            assemblyDef = assembly;
            ImportReference();
        }

        public void Dispose()
        {
            ClearReference();
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

        public void Write()
        {
            if (assemblyDef == null)
                return;
            
            assemblyDef.Write(ILVmManager.GetAssemblyHotfixPath(), new WriterParameters { WriteSymbols = assemblyReadSymbols });
        }

        private const string injectedFlag = "ILVMInjectedFlag";
        public bool IsInjected()
        {
            if (assemblyDef == null)
                return false;

            var injected = assemblyDef.MainModule.Types.Any(t => t.Name == injectedFlag);
            return injected;
        }

        public void SetIsInjected()
        {
            if (assemblyDef == null)
                return;

            var objType = assemblyDef.MainModule.ImportReference(typeof(System.Object));
            var flagType = new TypeDefinition("ILVM", injectedFlag, Mono.Cecil.TypeAttributes.Class | Mono.Cecil.TypeAttributes.Public, objType);
            assemblyDef.MainModule.Types.Add(flagType);
        }
        
        private void ImportReference()
        {
            var mgrType = assemblyDef.MainModule.GetType("ILVM.ILVmManager");
            foreach (MethodDefinition method in mgrType.Methods)
            {
                if (method.Name == "HasMethodInfo")
                    mr_HasMethodInfo = assemblyDef.MainModule.ImportReference(method);
                else if (method.Name == "MethodReturnVoidWrapper")
                    mr_MethodReturnVoidWrapper = assemblyDef.MainModule.ImportReference(method);
                else if (method.Name == "MethodReturnObjectWrapper")
                    mr_MethodReturnObjectWrapper = assemblyDef.MainModule.ImportReference(method);
            }

            var objType = typeof(System.Object);
            tr_SystemObject = assemblyDef.MainModule.ImportReference(objType);
            
            var intType = typeof(System.Int32);
            tr_SystemInt = assemblyDef.MainModule.ImportReference(intType);
        }
        
        private void ClearReference()
        {
            mr_HasMethodInfo = null;
            mr_MethodReturnVoidWrapper = null;
            mr_MethodReturnObjectWrapper = null;

            tr_SystemObject = null;
            tr_SystemInt = null;
        }

        public MethodReference MR_HasMethodInfo
        {
            get { return mr_HasMethodInfo; }
        }
        private MethodReference mr_HasMethodInfo;

        public MethodReference MR_MethodReturnVoidWrapper
        {
            get { return mr_MethodReturnVoidWrapper; }
        }
        private MethodReference mr_MethodReturnVoidWrapper;
        
        public MethodReference MR_MethodReturnObjectWrapper
        {
            get { return mr_MethodReturnObjectWrapper; }
        }
        private MethodReference mr_MethodReturnObjectWrapper;
        
        public TypeReference TR_SystemObject
        {
            get { return tr_SystemObject; }
        }
        private TypeReference tr_SystemObject;
        
        public TypeReference TR_SystemInt
        {
            get { return tr_SystemInt; }
        }
        private TypeReference tr_SystemInt;
    }


    public class ILVmManager
    {
        public static string GetAssemblyPath()
        {
            var path = Application.dataPath.Replace("/Assets", "/Library/ScriptAssemblies/Assembly-CSharp.dll");
            return path;
        }

        public static string GetAssemblyHotfixPath()
        {
            var path = Application.dataPath.Replace("/Assets", "/Library/ScriptAssemblies/Assembly-CSharp.dll");
            path = path.Replace("Assembly-CSharp.dll", "Assembly-CSharp.hotfix.dll");
            return path;
        }

        private static Dictionary<int, MethodDefinition> methodId2methodDef = new Dictionary<int, MethodDefinition>(8196);
        private static Dictionary<MethodDefinition, int> methodDef2methodId = new Dictionary<MethodDefinition, int>(8196);
        public static void ClearMethodId()
        {
            methodId2methodDef.Clear();
            methodDef2methodId.Clear();
        }

        public static void AddMethodId(int methodId, MethodDefinition methodDef)
        {
            methodId2methodDef.Add(methodId, methodDef);
            methodDef2methodId.Add(methodDef, methodId);
        }

        public static void DumpAllMethodId()
        {
            Logger.Log("ILVmManager: DumpAllMethodId: {0}", methodId2methodDef.Count);
            foreach (var kvp in methodId2methodDef)
            {
                Logger.Log("{0}: \t{1}", kvp.Key, kvp.Value);
            }
        }

        public static MethodDefinition GetMethodDefById(int methodId)
        {
            MethodDefinition methodDef;
            methodId2methodDef.TryGetValue(methodId, out methodDef);
            return methodDef;
        }

        public static int GetMethodIdByDef(MethodDefinition methodDef)
        {
            int methodId;
            methodDef2methodId.TryGetValue(methodDef, out methodId);
            return methodId;
        }


        public class MethodInfoWrap
        {
            private MethodInfo methodInfo;
        }

        private static Dictionary<int, MethodInfoWrap> methodInfos = new Dictionary<int, MethodInfoWrap>();

        public static void ClearMethodInfo()
        {
            methodInfos.Clear();
        }

        public static void SetMethodInfo(MethodDefinition methodDef)
        {
            //
        }

        public static bool HasMethodInfo(int methodId)
        {
            return false;
        }

        public static void MethodReturnVoidWrapper(object[] objList)
        {
            //var methodId = (System.Int32) objList[0];
            //var methodInfo = GetMethodInfo(methodId);
            //Assert.IsNotNull(methodInfo);

            //var offset = methodInfo.paramOffset;
            //var len = objList.Length - offset;
            //var param = new object[len];
            //for (var i = 0; i != len; ++i)
            //    param[i] = objList[i + offset];

            //var instance = objList[1];
            //methodInfo.methodInfo.Invoke(instance, param);
        }

        public static object MethodReturnObjectWrapper(object[] objList)
        {
            //var methodId = (System.Int32) objList[0];
            //var methodInfo = GetMethodInfo(methodId);
            //Assert.IsNotNull(methodInfo);
            
            //var offset = methodInfo.paramOffset;
            //var len = objList.Length - offset;
            //var param = new object[len];
            //for (var i = 0; i != len; ++i)
            //    param[i] = objList[i + offset];

            //var instance = objList[1];
            //return methodInfo.methodInfo.Invoke(instance, param);

            return null;
        }
    }
}