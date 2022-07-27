using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using UnityEngine.Assertions;

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
            
            Logger.Error("Timer: {0} \t{1}ms \t(quit unexpectly)", timerName, stopWatch.ElapsedMilliseconds);
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
            Logger.Error("Timer: {0} \t{1}ms", timerName, stopWatch.ElapsedMilliseconds);
        }
    }

    public class Logger
    {
        public static void Log(string message, params object[] args)
        {
            if (args == null || args.Length <= 0)
                UnityEngine.Debug.LogError("#ILVM# " + message);
            else
                UnityEngine.Debug.LogErrorFormat("#ILVM# " + message, args);
        }

        public static void Error(string message, params object[] args)
        {
            if (args == null || args.Length <= 0)
                UnityEngine.Debug.LogError("#ILVM_ERROR# " + message);
            else
                UnityEngine.Debug.LogErrorFormat("#ILVM_ERROR# " + message, args);
        }
    }
    

    public class AssemblyHandle : IDisposable
    {
        private AssemblyDefinition assemblyDef;
        private bool assemblyReadSymbols = false;

        public AssemblyHandle(string overridePath = null)
        {
            Init(overridePath);
        }

        private void Init(string overridePath = null)
        {            
            var path = ILVmManager.GetAssemblyPath();
            if (!string.IsNullOrEmpty(overridePath))
                path = overridePath;

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
                Logger.Log("AssemblyHandle: Warning: read assembly with symbol failed: {0}", path);
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

            var filePath = GetMethodIdFilePath();
            if (File.Exists(filePath))
                File.Delete(filePath);
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

        public static string GetMethodIdFilePath()
        {
            var dirPath = Application.dataPath.Replace("/Assets", "/Library/ILVM/");
            if (!Directory.Exists(dirPath))
                Directory.CreateDirectory(dirPath);
            var path = dirPath + "methodId.txt";
            return path;
        }

        public static void SaveMethodIdToFile()
        {
            var filePath = GetMethodIdFilePath();
            var fileInfo = new FileInfo(filePath);
            using (StreamWriter sw = fileInfo.CreateText())
            {
                var list = methodId2methodDef.ToList();
                list.Sort((a, b) => a.Key.CompareTo(b.Key));
                foreach (var kv in list)
                {
                    var signature = MethodDefToString(kv.Value);
                    sw.WriteLine(string.Format("{0} {1}", kv.Key, signature));
                }
            }	
        }

        public static void LoadMethodIdFromFile(AssemblyHandle assemblyHandle)
        {
            methodId2methodDef.Clear();
            methodDef2methodId.Clear();
            typeCache.Clear();
            
            var filePath = GetMethodIdFilePath();
            if (!File.Exists(filePath))
            {
                Logger.Error("ILVmManager: methodId file not exists! {0}", filePath);
                return;
            }

            var fileInfo = new FileInfo(filePath);
            using (StreamReader sr = fileInfo.OpenText())
            {
                var str = "";
                while ((str = sr.ReadLine()) != null)
                {
                    var strLst = str.Split(' ');
                    var methodId = Int32.Parse(strLst[0]);
                    var signature = strLst[1];
                    var methodDef = MethodDefFromString(signature, assemblyHandle);
                    methodId2methodDef.Add(methodId, methodDef);
                    methodDef2methodId.Add(methodDef, methodId);
                }
            }	
            typeCache.Clear();
        }

        private static System.Text.StringBuilder methodSignatureSb = new System.Text.StringBuilder();

        private static string MethodDefToString(MethodDefinition methodDef)
        {
            methodSignatureSb.Length = 0;

            // type
            methodSignatureSb.Append(methodDef.DeclaringType.FullName);
            methodSignatureSb.Append(";");

            // method name
            methodSignatureSb.Append(methodDef.Name);
            methodSignatureSb.Append(";");

            // parameters
            foreach (var param in methodDef.Parameters)
            {
                methodSignatureSb.Append(param.ParameterType.FullName);
                methodSignatureSb.Append(";");
            }

            return methodSignatureSb.ToString();
        }

        private static MethodDefinition MethodDefFromString(string methodSignature, AssemblyHandle assemblyHandle)
        {
            var strDataLst = methodSignature.Split(';');
            var typeName = strDataLst[0];
            var typeDef = GetTypeDefByName(typeName, assemblyHandle);

            var paramNames = new string[strDataLst.Length - 3];     // the last string is empty
            for (var i = 2; i < strDataLst.Length - 1; ++i)
            {
                paramNames[i - 2] = strDataLst[i];
            }

            var methodName = strDataLst[1];
            MethodDefinition methodDef = null;
            foreach (var method in typeDef.Methods)
            {
                if (method.Name != methodName)
                    continue;
                if (method.Parameters.Count != paramNames.Length)
                    continue;

                var matched = true;
                for (var i = 0; i != paramNames.Length; ++i)
                {
                    if (method.Parameters[i].ParameterType.FullName != paramNames[i])
                    {
                        matched = false;
                        break;
                    }
                }
                if (matched)
                {
                    methodDef = method;
                    break;
                }
            }

            return methodDef;
        }

        private static Dictionary<string, TypeDefinition> typeCache = new Dictionary<string, TypeDefinition>(1024);
        private static TypeDefinition GetTypeDefByName(string typeName, AssemblyHandle assemblyHandle)
        {
            TypeDefinition typeDef;
            if (typeCache.TryGetValue(typeName, out typeDef) && typeDef != null)
                return typeDef;

            typeDef = assemblyHandle.GetAssembly().MainModule.GetType(typeName);
            if (typeDef == null)
            {
                Logger.Error("ILVmManager: GetTypeDefByName: faild: {0}", typeName);
                return null;
            }

            typeCache.Add(typeName, typeDef);
            return typeDef;
        }

        public static MethodDefinition GetMethodDefById(int methodId)
        {
            MethodDefinition methodDef;
            if (!methodId2methodDef.TryGetValue(methodId, out methodDef))
                return null;
            return methodDef;
        }

        public static int GetMethodIdByDef(MethodDefinition methodDef)
        {
            int methodId;
            if (!methodDef2methodId.TryGetValue(methodDef, out methodId))
                return -1;
            return methodId;
        }


        public struct MethodInfoWrap
        {
            public MethodDefinition methodDef;
            public bool Invalid
            {
                get { return methodDef == null; }
            }
        }

        private static Dictionary<int, MethodInfoWrap> methodInfos = new Dictionary<int, MethodInfoWrap>();

        public static void ClearMethodInfo()
        {
            methodInfos.Clear();
        }

        public static void SetMethodInfo(MethodDefinition methodDef)
        {
            var methodId = GetMethodIdByDef(methodDef);
            if (methodId < 0)
            {
                Logger.Error("ILVmManager: SetMethodInfo failed: {0}", methodDef);
                return;
            }

            var wrap = new MethodInfoWrap();
            wrap.methodDef = methodDef;
            methodInfos.Add(methodId, wrap);
        }

        public static MethodInfoWrap GetMethodInfo(int methodId)
        {
            MethodInfoWrap wrap;
            methodInfos.TryGetValue(methodId, out wrap);
            return wrap;
        }

        public static bool HasMethodInfo(int methodId)
        {
            return methodInfos.ContainsKey(methodId);
        }

        public static ILVirtualMachine VM
        {
            get
            {
                if (vm == null)
                    vm = new ILVirtualMachine();
                return vm;
            }
        }
        private static ILVirtualMachine vm;

        public static void MethodReturnVoidWrapper(object[] objList)
        {
            var methodId = (System.Int32)objList[0];
            var methodInfo = GetMethodInfo(methodId);
            Assert.IsFalse(methodInfo.Invalid);

            var offset = 1;     // skip methodId
            var len = objList.Length - offset;
            var param = new object[len];
            for (var i = 0; i != len; ++i)
                param[i] = objList[i + offset];
            VM.Execute(methodInfo.methodDef.Body.Instructions, param);
        }

        public static object MethodReturnObjectWrapper(object[] objList)
        {
            var methodId = (System.Int32)objList[0];
            var methodInfo = GetMethodInfo(methodId);
            Assert.IsFalse(methodInfo.Invalid);

            var offset = 1;     // skip methodId
            var len = objList.Length - offset;
            var param = new object[len];
            for (var i = 0; i != len; ++i)
                param[i] = objList[i + offset];
            return VM.Execute(methodInfo.methodDef.Body.Instructions, param);
        }
    }
}