using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ILVMTest {

    public class Test_TypeCast
    {
        public string Func()
        {
            var vBool = true;
            var vInt16 = (Int16)1;
            var vInt = (int)1;
            var vUInt = (uint)1;
            var vFloat = 1.0f;
            var vDouble = 1.0;
            var vLong = (long)1;

            var ret = true;
            ret &= NeedBool(vBool);
            ret &= NeedInt16(vInt16);
            ret &= NeedInt(vInt);
            ret &= NeedUInt(vUInt);
            ret &= NeedLong(vLong);
            ret &= NeedFloat(vFloat);
            ret &= NeedDouble(vDouble);

            // implicit cast 
            ret &= NeedLong(vInt16);
            ret &= NeedLong(vInt);
            ret &= NeedLong(vUInt);
            ret &= NeedDouble(vFloat);

            // explicit cast
            ret &= NeedUInt((uint)vInt);
            ret &= NeedInt((int)vLong);
            ret &= NeedFloat((float)vInt);

            // invalid cast
            // ret &= NeedBool((bool)vInt);
            // ret &= NeedInt((int)vBool);


            // type cast
            object obj = (object)vBool;
            if (obj as List<int> != null)
                return "invalid";
            
            
            return "hello";
        }

        private bool NeedBool(bool v)
        {
            return v;
        }
        
        private bool NeedInt16(Int16 v)
        {
            return v != 0;
        }

        private bool NeedInt(int v)
        {
            return v != 0;
        }
        
        private bool NeedUInt(uint v)
        {
            return v != 0;
        }
        
        private bool NeedLong(long v)
        {
            return v != 0;
        }
        
        private bool NeedFloat(float v)
        {
            return v != 0;
        }
        
        private bool NeedDouble(double v)
        {
            return v != 0;
        }
    }
}