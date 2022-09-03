using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using ILVM;

public class HelloBtn : MonoBehaviour
{
    void Start()
    {
        var btn = gameObject.GetComponent<Button>();
        btn.onClick.AddListener(this.OnClick);
    }

    public void OnClick()
    {
        // default parse from: "Library/ScriptAssemblies/Assembly-CSharp.dll"
        using (var assemblyHandle = new AssemblyHandle())
        {
            // get MethodDefinition of HelloBtn.SayHello
            var assemblyDef = assemblyHandle.GetAssembly();
            var classTypeDef = assemblyDef.MainModule.GetType("HelloBtn");
            var methodTypeDef = classTypeDef.Methods.First(m => m.Name == "SayHello");

            // invoke method by Virual Machine
            var vm = new ILVirtualMachine();
            var parameters = new object[] { this };
            vm.Execute(methodTypeDef, parameters);
        }
    }        

    public void SayHello()
    {
        Debug.Log("Hello World");
    }
}
