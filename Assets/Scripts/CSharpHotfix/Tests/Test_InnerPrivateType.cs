//using System.Collections;
//using System.Collections.Generic;
//using UnityEngine;

//namespace ILVMTest {

//    public class Test_InnerPrivateType
//    {
//        private enum ETYPE {
//            NONE = 0, 
//            ONE = 1, 
//            TWO = 2, 
//        };

//        private struct TestStruct
//        {
//            public int x;
//            public int y;
//        }

//        public string Func()
//        {
//            TestStruct s1 = new TestStruct();
//            s1.x = 1;
//            s1.y = 2;

//            if (s1.x != (int)ETYPE.ONE)
//                return "invalid";

//            return "hello";
//        }

//    }
//}