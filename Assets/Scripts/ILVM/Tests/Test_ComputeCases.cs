using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ILVMTest
{

    public class Test_ComputeCases
    {
        public string Func()
        {
            var result = (1001 % 10) + (int)Mathf.Floor((float)2 / 3);
            result <<= (int)BoxTest(0);
            result = result | 1;
            return result.ToString();
        }

        private static object BoxTest(object num, int add = 999)
        {
            var intNum = (int)num;
            intNum = intNum + add;
            return (object)intNum;
        }
    }
}