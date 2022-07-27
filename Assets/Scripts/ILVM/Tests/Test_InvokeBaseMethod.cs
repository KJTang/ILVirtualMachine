using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ILVMTest
{
    public class Inner_Test_InvokeBaseMethodBase
    { 
        public virtual string Func()
        {
            return "lo";
        }
    }

    public class Test_InvokeBaseMethod : Inner_Test_InvokeBaseMethodBase
    {
        public override string Func()
        {
            var str = "hel";
            str += base.Func();
            return str;
        }
    }
}