using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ILVMTest
{

    public class Test_GenericMethod
    {
        public string Func()
        {
            var intA = GenericMethod1<System.Int32>();
            if (intA != 0)
                return "invalid";

            var intB = GenericMethod2<System.Int32>(1);
            if (intB != 1)
                return "invalid";
                
            var intC = GenericMethod3<System.Int32, string>(1, "2");
            if (intC != 1)
                return "invalid";
                
            var intD = GenericMethod4<System.Int32>(1, 3);
            if (intD != 3)
                return "invalid";

            return "hello";
        }

        public T GenericMethod1<T>()
        {
            return default(T);
        }
        
        public T GenericMethod2<T>(T v)
        {
            return v;
        }
        
        public T GenericMethod3<T, K>(T v, K w)
        {
            return v;
        }
        
        public T GenericMethod4<T>(T v, T v2)
        {
            return v2;
        }
    }
}