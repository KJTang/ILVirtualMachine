using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ILVMTest {

    public class Test_AssignMember
    {
        private int count = 0;

        public string Func()
        {
            // SimpleAssignmentExpression
            count = 1;

            // AddAssignmentExpression
            count += 1;

            // SubtractAssignmentExpression
            count -= 3;

            // MultiplyAssignmentExpression
            count *= -1;

            // LeftShiftAssignmentExpression
            count <<= 1;

            if (count == 2)
                return "hello";
            return "invalid";
        }
    }
}