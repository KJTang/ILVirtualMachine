using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ILVMTest
{

    public class Test_GenericTypes
    {
        private class CompareItem : IComparable
        {
            private int value;
            public CompareItem(int v)
            {
                value = v;
            }

            public void SetValue(int v)
            {
                value = v;
            }

            public int GetValue()
            {
                return value;
            }
            
            public int CompareTo(object obj)
            {
                if (ReferenceEquals(obj, this))
                    return 0;

                if (!(obj is CompareItem))
                    return 0;            
                
                var other = obj as CompareItem;
                if (this.value < other.value)
                    return -1;
                else if (this.value > other.value)
                    return 1;
                else
                    return 0;
            }
        }
        
        public string Func()
        {
            var lst = new List<CompareItem>();
            lst.Add(new CompareItem(1001));
            lst.Add(new CompareItem(1005));
            lst.Add(new CompareItem(1002));
            lst.Add(new CompareItem(1003));
            lst.Add(new CompareItem(1004));

            lst.Sort((a, b) => a.CompareTo(b));
            
            if (lst[0].GetValue() != 1001)
                return "invalid";

            return "hello";
        }
    }
}