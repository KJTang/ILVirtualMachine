using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ILVMTest {

    public class Test_InvokePrivateMethod
    {
        public string Func()
        {
            InvokeFunc();
            InvokeParamFunc(0);
            if (InvokeReturnValueFunc())
                return "hello";
            return "";
        }

        private void InvokeFunc()
        {
        }

        private int InvokeParamFunc(int i)
        {
            return i;
        }

        private bool InvokeReturnValueFunc()
        {
            return true;
        }
    }
}