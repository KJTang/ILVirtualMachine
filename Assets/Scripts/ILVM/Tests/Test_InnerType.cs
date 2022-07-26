using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ILVMTest {

    public class Test_InnerType
    {
        public enum ETYPE {
            NONE = 0, 
            ONE = 1, 
            TWO = 2, 
        };

        public struct TestStruct
        {
            public int x;
            public int y;
        }
        
        public TestStruct TestFunc(TestStruct s)
        {
            s.x = 1;
            return s;
        }

        public string Func()
        {
            TestStruct s1 = new TestStruct();
            s1.x = 0;
            s1.y = 0;

            s1 = TestFunc(s1);

            var dict = new Dictionary<int, TestStruct>();
            dict.Add(1, s1);

            if (s1.x != (int)ETYPE.ONE || dict.Count != 1)
                return "invalid";

            return "hello";
        }

    }
}