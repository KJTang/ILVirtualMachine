using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ILVMTest {

    public class Test_TypeEnum
    {
        enum EnumType
        {
            Zero = 0, 
            One = 1, 
            Two = 2, 
        }
        
        public string Func()
        {
            var enumZ = EnumType.Zero;
            if ((int)enumZ != 0)
                return "invalid";
            return "hello";
        }
    }
}