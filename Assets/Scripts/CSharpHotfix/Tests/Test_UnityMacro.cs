#define MACRO_TEST

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ILVMTest {

    public class Test_UnityMacro
    {
        public string Func()
        {
            #if UNITY_EDITOR && MACRO_TEST
                return "hello";
            #endif

            return "";
        }
    }
}