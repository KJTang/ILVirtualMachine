using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ILVMTest
{

    public class Test_VarTypeInference
    {
        Dictionary<int, string> dict = new Dictionary<int, string>();

        public string Func()
        {
            dict.Add(1, "one");
            dict.Add(2, "two");
            dict.Add(3, "three");

            var result1 = "";
            foreach (var kv in dict)
            {
                if (kv.Key == 2)
                    result1 = kv.Value;
            }
            if (result1 != "two")
                return "invalid";

            var result2 = dict[2];
            if (result2 != "two")
                return "invalid";
            
            string result3;
            if (!dict.TryGetValue(2, out result3) || result3 != "two")
                return "invalid";

            return "hello";
        }
    }
}