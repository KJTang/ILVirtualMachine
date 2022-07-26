using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace ILVM
{
    public class DebugTimer
    {
        private bool finished = true;
        private string timerName = "";
        private System.Diagnostics.Stopwatch stopWatch;

        public DebugTimer()
        {
            stopWatch = System.Diagnostics.Stopwatch.StartNew();
        }

        ~DebugTimer()
        {
            if (finished)
                return;
            
            UnityEngine.Debug.LogErrorFormat("Timer: {0} \t{1}ms \t(quit unexpectly)", timerName, stopWatch.ElapsedMilliseconds);
            stopWatch.Stop();
        }

        public void Start(string name)
        {
            if (!finished)
                Stop();
            finished = false;
            timerName = name;
            stopWatch.Reset();
            stopWatch.Start();
        }

        public void Stop()
        {
            if (stopWatch == null)
                return;

            finished = true;
            stopWatch.Stop();
            UnityEngine.Debug.LogErrorFormat("Timer: {0} \t{1}ms", timerName, stopWatch.ElapsedMilliseconds);
        }
    }

    public class Logger
    {
        public static void Log(string message, params object[] args)
        {
            UnityEngine.Debug.LogErrorFormat("#ILVM# " + message, args);
        }

        public static void Error(string message, params object[] args)
        {
            UnityEngine.Debug.LogErrorFormat("#ILVM_ERROR# " + message, args);
        }
    }
    

    public class AssemblyHandle : IDisposable
    {
        private AssemblyDefinition assemblyDef;

        public AssemblyHandle()
        {
            Init();
        }

        private void Init()
        {
            var path = Application.dataPath.Replace("/Assets", "/Library/ScriptAssemblies/Assembly-CSharp.dll");
            
            // read assembly
            AssemblyDefinition assembly = null;
            var readSymbols = true;
            try
            {
                // try read with symbols
                assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadSymbols = true });
            }
            catch
            {
                // read with symbols failed, just don't read them
                Logger.Log("AssemblyHandle: read assembly with symbol failed: {0}", path);
                try
                {
                    readSymbols = false;
                    assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadSymbols = false });
                }
                catch (Exception e)
                {
                    Logger.Error("AssemblyHandle: read assembly failed: {0}", e);
                }
            }
            if (assembly == null)
                return;

            // init resolver search path
            var initResolverSucc = true;
            var resolver = assembly.MainModule.AssemblyResolver as BaseAssemblyResolver;
            
            foreach (var searchPath in
                (from asm in AppDomain.CurrentDomain.GetAssemblies()
                    select System.IO.Path.GetDirectoryName(asm.ManifestModule.FullyQualifiedName)).Distinct())
            {
                try
                {
                    //UnityEngine.Debug.LogError("searchPath:" + searchPath);
                    resolver.AddSearchDirectory(searchPath);
                }
                catch (Exception e) 
                { 
                    initResolverSucc = false;
                    Logger.Error("AssemblyHandle: init resolver failed: {0}", e);
                }
            }
            if (!initResolverSucc)
                return;

            assemblyDef = assembly;
        }

        public void Dispose()
        {
            if (assemblyDef == null)
                return;
            
            // clear symbol reader, incase lock file on windows
            if (assemblyDef.MainModule.SymbolReader != null)
            {
                assemblyDef.MainModule.SymbolReader.Dispose();
            }
            assemblyDef.Dispose();
            assemblyDef = null;
        }

        public AssemblyDefinition GetAssembly()
        {
            return assemblyDef;
        }
    }


    public class ILVmManager
    {
        //
    }
}