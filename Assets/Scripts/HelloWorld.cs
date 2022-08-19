using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;

public class HelloWorld : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        var btn = gameObject.GetComponent<Button>();
        btn.onClick.AddListener(this.OnClick);
    }

    // Update is called once per frame
    void Update()
    {
        if (profiling)
            RunProfiling();

    }

    private bool profiling = false;
    public void OnClick()
    {
        Debug.Log("HelloWorld");
        profiling = !profiling;
    }

    private void RunProfiling()
    {
        Profiler.BeginSample("ILVM: load assembly");
        var assemblyHandle = new ILVM.AssemblyHandle();
        var assembly = assemblyHandle.GetAssembly();
        if (assembly == null)
        {
            Profiler.EndSample();
            assemblyHandle.Dispose();
            return;
        }
        Profiler.EndSample();

        Profiler.BeginSample("ILVM: get test cases");
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
        Profiler.EndSample();

        Profiler.BeginSample("ILVM: run tests");
        // execute 10 times
        //for (var i = 0; i != 10; ++i)
        {
            Profiler.BeginSample("ILVM: create vm");
            var vm = new ILVM.ILVirtualMachine();
            Profiler.EndSample();
            var testCounter = 0;
            var succCounter = 0;
            foreach (var method in methodToTest)
            {
                testCounter += 1;
                try
                {
                    var rfClsInst = Activator.CreateInstance(method.DeclaringType);
                    var rfRet = method.Invoke(rfClsInst, null);

                    var classTypeDef = assembly.MainModule.GetType(method.DeclaringType.FullName);
                    var methodTypeDef = classTypeDef.Methods.First(m => m.Name == "Func");

                    var vmClsInst = Activator.CreateInstance(method.DeclaringType);
                    var parameters = new object[] { vmClsInst };
                    Profiler.BeginSample("ILVM: vm execute");
                    var vmRet = vm.Execute(methodTypeDef, parameters);
                    Profiler.EndSample();
                    ILVM.Logger.Error("#ILVM_Test# {0} \treflection ret: {1} \tvm ret: {2} \tsucc: {3}", method.DeclaringType, rfRet, vmRet, (string)rfRet == (string)vmRet ? "<color=green>succ</color>" :  "<color=red>failed</color>");

                    if ((string)rfRet == (string)vmRet)
                        succCounter += 1;
                }
                catch (Exception e)
                {
                    ILVM.Logger.Error("ILVmEditor: exception: {0} \n{1}", method.DeclaringType, e);
                }
            }
            ILVM.Logger.Error("#ILVM_Test# Tests: total: {0} \tsucc: {1}", testCounter, succCounter);
        }
        Profiler.EndSample();

        assemblyHandle.Dispose();
    }
}
