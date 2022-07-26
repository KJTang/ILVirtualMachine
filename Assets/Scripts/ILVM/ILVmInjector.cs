using UnityEngine;
using System;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Collections;
using System.Collections.Generic;

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

            var jsonData = File.ReadAllText(Application.dataPath + "/Scripts/ILVM/Editor/ILVmInjectConfig.json");
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

        private static bool FilterMethod(MethodInfo method)
        {
            var methodFullName = method.DeclaringType.FullName + method.Name;
            if (filterMethod.Contains(methodFullName))
                return false;

            if (method.IsAbstract || method.Name == "op_Equality" || method.Name == "op_Inequality")
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

        public static void Inject()
        {
            // TODO: inject dll
            foreach (var typeInfo in GetTypeToInject())
            {
                Logger.Log("type: {0}", typeInfo.FullName);
                foreach (var methodInfo in typeInfo.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (methodInfo.DeclaringType != typeInfo)
                        continue;
                    if (!FilterMethod(methodInfo))
                        continue;
                    Logger.Log("method: {0} \t{1}", methodInfo, typeInfo.FullName);
                }
            }
        }
    }
}
