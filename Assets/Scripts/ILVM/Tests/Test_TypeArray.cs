using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ILVMTest {

    public class Test_TypeArray
    {
        public Vector3[] vecArray = new Vector3[128];
        
        public string Func()
        {
            vecArray[0] = Vector3.one;
            if (vecArray[0].x != 1)
                return "invalid";
            return "hello";
        }
    }
}