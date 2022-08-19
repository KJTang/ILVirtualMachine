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
        public static bool EnableLog = false;

        public static void Log(string message, params object[] args)
        {
            if (!EnableLog)
                return;

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
    
	public static class ListPool<T>
	{
		/** Internal pool */
		static List<List<T>> pool;
		
		/** Static constructor */
		static ListPool ()
		{
			pool = new List<List<T>> ();
		}
		
		/** Claim a list.
		 * Returns a pooled list if any are in the pool.
		 * Otherwise it creates a new one.
		 * After usage, this list should be released using the Release function (though not strictly necessary).
		 */
		public static List<T> Claim () {
			if (pool.Count > 0) {
				List<T> ls = pool[pool.Count-1];
				pool.RemoveAt(pool.Count-1);
				return ls;
			} else {
				return new List<T>();
			}
		}
		
		/** Claim a list with minimum capacity
		 * Returns a pooled list if any are in the pool.
		 * Otherwise it creates a new one.
		 * After usage, this list should be released using the Release function (though not strictly necessary).
		 * This list returned will have at least the capacity specified.
		 */
		public static List<T> Claim (int capacity) {
			if (pool.Count > 0) {
				//List<T> list = pool.Pop();
				List<T> list = pool[pool.Count-1];
				pool.RemoveAt(pool.Count-1);
				
				if (list.Capacity < capacity) list.Capacity = capacity;
				
				return list;
			} else {
				return new List<T>(capacity);
			}
		}
		
		/** Makes sure the pool contains at least \a count pooled items with capacity \a size.
		 * This is good if you want to do all allocations at start.
		 */
		public static void Warmup (int count, int size) {
			List<T>[] tmp = new List<T>[count];
			for (int i=0;i<count;i++) tmp[i] = Claim (size);
			for (int i=0;i<count;i++) Release (tmp[i]);
		}
		
		/** Releases a list.
		 * After the list has been released it should not be used anymore.
		 * 
		 * \throws System.InvalidOperationException
		 * Releasing a list when it has already been released will cause an exception to be thrown.
		 * 
		 * \see Claim
		 */
		public static void Release (List<T> list) {
			
			for (int i=0;i<pool.Count;i++)
				if (pool[i] == list)
					throw new System.InvalidOperationException ("The List is released even though it is in the pool");
			
			list.Clear ();
			pool.Add (list);
		}
		
		/** Clears the pool for lists of this type.
		 * This is an O(n) operation, where n is the number of pooled lists.
		 */
		public static void Clear () {
			pool.Clear ();
		}
		
		/** Number of lists of this type in the pool */
		public static int GetSize () {
			return pool.Count;
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

            var objArrType = typeof(Array);
            tr_SystemObjectArr = assemblyDef.MainModule.ImportReference(objArrType);

            var vmAddrType = assemblyDef.MainModule.GetType("ILVM.VMAddr");
            tr_VMAddr = assemblyDef.MainModule.ImportReference(vmAddrType);

            var vmAddrCtorType = tr_VMAddr.Resolve().Methods.First(mtd => mtd.Name == "Create");
            mr_VMAddrCtor = assemblyDef.MainModule.ImportReference(vmAddrCtorType);

            var vmAddrGetObjType = tr_VMAddr.Resolve().Methods.First(mtd => mtd.Name == "GetObj");
            mr_VMAddrGetObj = assemblyDef.MainModule.ImportReference(vmAddrGetObjType);

            var vmAddrSetObjType = tr_VMAddr.Resolve().Methods.First(mtd => mtd.Name == "SetObj");
            mr_VMAddrSetObj = assemblyDef.MainModule.ImportReference(vmAddrSetObjType);
        }

        private void ClearReference()
        {
            mr_HasMethodInfo = null;
            mr_MethodReturnVoidWrapper = null;
            mr_MethodReturnObjectWrapper = null;

            tr_SystemObject = null;
            tr_SystemInt = null;
            tr_SystemObjectArr = null;
            tr_VMAddr = null;
            mr_VMAddrCtor = null;
            mr_VMAddrGetObj = null;
            mr_VMAddrSetObj = null;
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

        public TypeReference TR_SystemObjectArr
        {
            get { return tr_SystemObjectArr; }
        }
        private TypeReference tr_SystemObjectArr;

        public TypeReference TR_VMAddr
        {
            get { return tr_VMAddr; }
        }
        private TypeReference tr_VMAddr;

        public MethodReference MR_VMAddrCtor
        {
            get { return mr_VMAddrCtor; }
        }
        private MethodReference mr_VMAddrCtor;

        public MethodReference MR_VMAddrGetObj
        {
            get { return mr_VMAddrGetObj; }
        }
        private MethodReference mr_VMAddrGetObj;

        public MethodReference MR_VMAddrSetObj
        {
            get { return mr_VMAddrSetObj; }
        }
        private MethodReference mr_VMAddrSetObj;
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

        #region method id
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
        #endregion


        #region method info wrap
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

            var vm = VMPool.GetFromPool();
            vm.Execute(methodInfo.methodDef, param);
            VMPool.BackToPool(vm);
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

            var vm = VMPool.GetFromPool();
            var ret = vm.Execute(methodInfo.methodDef, param);
            VMPool.BackToPool(vm);
            return ret;
        }

        #endregion


        #region VM: mono Type to reflection Type 

        private static Dictionary<string, Type> cacheTypeRefToTypeInfo = new Dictionary<string, Type>(1024);

        public static Type GetVMTypeInfo(TypeReference typeRef)
        {
            if (typeRef == null)
                return null;

            Type typeInfo;
            cacheTypeRefToTypeInfo.TryGetValue(typeRef.FullName, out typeInfo);
            return typeInfo;
        }

        public static void SetVMTypeInfo(TypeReference typeRef, Type typeInfo)
        {
            var key = typeRef.FullName;
            if (cacheTypeRefToTypeInfo.ContainsKey(key))
                cacheTypeRefToTypeInfo[key] = typeInfo;
            else
                cacheTypeRefToTypeInfo.Add(key, typeInfo);
        }

        public static void ClearVMTypeInfo()
        {
            cacheTypeRefToTypeInfo.Clear();
        }

        
        private static Dictionary<string, MethodInfo> cacheMethodRefToMethodInfo = new Dictionary<string, MethodInfo>(1024);

        public static MethodInfo GetVMMethodInfo(MethodReference methodRef)
        {
            if (methodRef == null)
                return null;

            MethodInfo methodInfo;
            cacheMethodRefToMethodInfo.TryGetValue(methodRef.FullName, out methodInfo);
            return methodInfo;
        }

        public static void SetVMMethodInfo(MethodReference methodRef, MethodInfo methodInfo)
        {
            var key = methodRef.FullName;
            if (cacheMethodRefToMethodInfo.ContainsKey(key))
                cacheMethodRefToMethodInfo[key] = methodInfo;
            else
                cacheMethodRefToMethodInfo.Add(key, methodInfo);
        }

        public static void ClearVMMethodInfo()
        {
            cacheMethodRefToMethodInfo.Clear();
        }

        
        private static Dictionary<string, ConstructorInfo> cacheMethodRefToConstructorInfo = new Dictionary<string, ConstructorInfo>(1024);

        public static ConstructorInfo GetVMConstructorInfo(MethodReference methodRef)
        {
            if (methodRef == null)
                return null;

            ConstructorInfo constructorInfo;
            cacheMethodRefToConstructorInfo.TryGetValue(methodRef.FullName, out constructorInfo);
            return constructorInfo;
        }

        public static void SetVMConstructorInfo(MethodReference methodRef, ConstructorInfo constructorInfo)
        {
            var key = methodRef.FullName;
            if (cacheMethodRefToConstructorInfo.ContainsKey(key))
                cacheMethodRefToConstructorInfo[key] = constructorInfo;
            else
                cacheMethodRefToConstructorInfo.Add(key, constructorInfo);
        }

        public static void ClearVMConstructorInfo()
        {
            cacheMethodRefToConstructorInfo.Clear();
        }

        
        private static Dictionary<string, PropertyInfo> cacheMethodRefToPropertyInfo = new Dictionary<string, PropertyInfo>(1024);

        public static PropertyInfo GetVMPropertyInfo(MethodReference methodRef)
        {
            if (methodRef == null)
                return null;

            PropertyInfo propInfo;
            cacheMethodRefToPropertyInfo.TryGetValue(methodRef.FullName, out propInfo);
            return propInfo;
        }

        public static void SetVMPropertyInfo(MethodReference methodRef, PropertyInfo propInfo)
        {
            var key = methodRef.FullName;
            if (cacheMethodRefToPropertyInfo.ContainsKey(key))
                cacheMethodRefToPropertyInfo[key] = propInfo;
            else
                cacheMethodRefToPropertyInfo.Add(key, propInfo);
        }

        public static void ClearVMPropertyInfo()
        {
            cacheMethodRefToPropertyInfo.Clear();
        }
        


        private static Dictionary<string, FieldInfo> cacheFieldDefToFieldInfo = new Dictionary<string, FieldInfo>(1024);

        public static FieldInfo GetVMFieldInfo(FieldDefinition fieldDef)
        {
            if (fieldDef == null)
                return null;

            FieldInfo fieldInfo;
            cacheFieldDefToFieldInfo.TryGetValue(fieldDef.FullName, out fieldInfo);
            return fieldInfo;
        }

        public static void SetVMFieldInfo(FieldDefinition fieldDef, FieldInfo fieldInfo)
        {
            var key = fieldDef.FullName;
            if (cacheFieldDefToFieldInfo.ContainsKey(key))
                cacheFieldDefToFieldInfo[key] = fieldInfo;
            else
                cacheFieldDefToFieldInfo.Add(key, fieldInfo);
        }

        public static void ClearVMFieldInfo()
        {
            cacheFieldDefToFieldInfo.Clear();
        }

        
        private static Dictionary<string, Type> cacheNameToTypeInfo = new Dictionary<string, Type>(1024);

        public static Type GetVMTypeInfoByName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return null;

            Type typeInfo;
            cacheNameToTypeInfo.TryGetValue(fullName, out typeInfo);
            return typeInfo;
        }

        public static void SetVMTypeInfoByName(string fullName, Type typeInfo)
        {
            if (cacheNameToTypeInfo.ContainsKey(fullName))
                cacheNameToTypeInfo[fullName] = typeInfo;
            else
                cacheNameToTypeInfo.Add(fullName, typeInfo);
        }

        public static void ClearVMTypeInfoByName()
        {
            cacheNameToTypeInfo.Clear();
        }

        #endregion
    }
}