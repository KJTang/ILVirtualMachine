using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ILVMTest {

    public class Test_ValueTypeConstruct
    {
        public string Func()
        {
            Vector3 v1;
            Vector3 v2 = new Vector3(0, 1, 0);
            Vector3 v3 = new Vector3(0, 2, 0);

            v1.y = v2.y + v3.y;
            if (v1.y != 3)
                return "invalid";
            return "hello";
        }
    }
}