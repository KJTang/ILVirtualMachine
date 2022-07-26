using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ILVMTest {

    public class Test_InvokeMethod
    {
        public string Func()
        {
            return InvokeFunc();
        }

        public string InvokeFunc()
        {
            return "hello";
        }
    }
}