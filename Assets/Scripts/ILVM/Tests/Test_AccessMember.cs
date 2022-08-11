using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ILVMTest {

    public class Test_AccessMember
    {
        public int fieldTest = 0;
        public bool PropTest { get; set; }

        public class InnerMemberTestClass
        {
            public int fieldTest = 1;
        }

        public struct InnerMemberTestStruct
        {
            public int value;
            public Vector3 vec;
        }

        public void MethodTest()
        {
            if (fieldTest >= 0)
                PropTest = true;
        }

        public string Func()
        {
            MethodTest();
            var testCls = new InnerMemberTestClass();
            var testStt = new InnerMemberTestStruct();

            testStt.value = testCls.fieldTest;
            if (testStt.value != 1)
                return "invalid";

            testStt.vec.x = testCls.fieldTest;
            if (testStt.vec.x != 1)
                return "invalid";
            
            return "hello";
        }
    }
}