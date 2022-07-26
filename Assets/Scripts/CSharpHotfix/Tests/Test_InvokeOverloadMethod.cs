using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ILVMTest {

    public class Test_InvokeOverloadMethod
    {
        private int invokeCnt = 0;

        public string Func()
        {
            InvokeFunc();
            InvokeFunc(0);
            return "hello";
        }

        private void InvokeFunc()
        {
            invokeCnt = invokeCnt + 1;
        }

        private void InvokeFunc(int i)
        {
            invokeCnt = invokeCnt + 1;
        }
    }
}