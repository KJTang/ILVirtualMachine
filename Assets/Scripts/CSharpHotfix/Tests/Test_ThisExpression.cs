using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ILVMTest {

    public class Test_ThisExpression
    {
        public int i;

        public string Func()
        {
            this.InvokeFunc(this.i);
            return "hello";
        }

        public void InvokeFunc(int i)
        {
        }
    }
}