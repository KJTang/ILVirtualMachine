using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ILVMTest
{

    public class Test_InvokeMethodWithNull
    {
        public string Func()
        {
            var str = ConcatMethod(1, null);
            if (str != "2")
                return "invalid";
            return "hello";
        }

        private string ConcatMethod(int num1, object num2)
        {
            return num1.ToString() + num2?.ToString();
        }
        
        private string ConcatMethod(int num1, string num2)
        {
            return (num1 + 1).ToString() + num2?.ToString();
        }
        
        private string ConcatMethod(int num1, int num2)
        {
            return (num1 + 2).ToString() + num2.ToString();
        }
    }
}