using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using GenericParameterAttributes = System.Reflection.GenericParameterAttributes;
using UnityEngine.Assertions;

namespace ILVM
{
    public class VMAddr : IDisposable
    {
        protected object obj;
        protected ulong addrIdx = 0;

        protected VMAddr(object o)
        {
            obj = o;
            AddAddr(this);
        }

        public virtual void SetObj(object o)
        {
            obj = o;
        }

        public virtual object GetObj()
        {
            return obj;
        }

        public void Dispose()
        {
            obj = null;
            DelAddr(this);
        }

        public override string ToString()
        {
            return string.Format("VMAddr({0}): {1}", addrIdx, obj);
        }

        protected static Dictionary<ulong, VMAddr> addrDict = new Dictionary<ulong, VMAddr>(1024);
        protected static ulong addrIndexer = 0;

        public static VMAddr Create(object o)
        {
            if (o is VMAddr)
                return o as VMAddr;

            var addr = new VMAddr(o);
            return addr;
        }

        public static void Clear()
        {
            addrIndexer = 0;
            addrDict.Clear();
        }

        protected static void AddAddr(VMAddr addr)
        {
            addr.addrIdx = addrIndexer++;
            addrDict.Add(addr.addrIdx, addr);
        }

        protected static void DelAddr(VMAddr addr)
        {
            addrDict.Remove(addr.addrIdx);
        }
    }


    public class VMAddrForFieldInfo : VMAddr
    {
        public class VMAddrForFieldInfoData
        {
            public FieldInfo field;
            public object inst;

            public VMAddrForFieldInfoData(FieldInfo f, object o)
            {
                field = f;
                inst = o;
            }
        }

        private VMAddrForFieldInfoData fieldInfoData;

        protected VMAddrForFieldInfo(object o, VMAddrForFieldInfoData data) : base(o)
        {
            fieldInfoData = data;
        }

        public override void SetObj(object o)
        {
            fieldInfoData.field.SetValue(fieldInfoData.inst, o);
        }

        public override object GetObj()
        {
            var val = fieldInfoData.field.GetValue(fieldInfoData.inst);
            return val;
        }

        
        public static VMAddr Create(VMAddrForFieldInfoData data)
        {
            var addr = new VMAddrForFieldInfo(null, data);
            return addr;
        }
    }


    public class VMAddrForArray : VMAddr
    {
        public class VMAddrForArrayData
        {
            public int index;
            public Array inst;

            public VMAddrForArrayData(Array a, int i)
            {
                inst = a;
                index = i;
            }
        }
        private VMAddrForArrayData arrayData;

        protected VMAddrForArray(object o, VMAddrForArrayData data) : base(o)
        {
            arrayData = data;
        }

        public override void SetObj(object o)
        {
            arrayData.inst.SetValue(o, arrayData.index);
        }

        public override object GetObj()
        {
            var val = arrayData.inst.GetValue(arrayData.index);
            return val;
        }

        
        public static VMAddr Create(VMAddrForArrayData data)
        {
            var addr = new VMAddrForArray(null, data);
            return addr;
        }
    }
}