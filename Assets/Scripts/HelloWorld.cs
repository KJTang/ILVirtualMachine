using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
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
        
    }

    public void OnClick()
    {
        Debug.Log("HelloWorld");

        RunProfiling();
    }

    private void RunProfiling()
    {
        using (var assemblyHandle = new ILVM.AssemblyHandle())
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

            // execute 10 times
            for (var i = 0; i != 10; ++i)
            {
                var vm = new ILVM.ILVirtualMachine();
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
                        var vmRet = vm.Execute(methodTypeDef, parameters);
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
        }
    }
}
