// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace System.Diagnostics.Debug.SymbolReader
{

    public class SymbolReader
    {
    
        /// <summary>
        /// Checks availability of debugging information for given assembly.
        /// </summary>
        /// <param name="assemblyFileName">file name of the assembly</param>
        /// <returns>true if debugging information is available</returns>
        public static bool LoadSymbolsForModule(string assemblyFileName)
        {
            MetadataReader peReader, pdbReader;
            
            return GetReaders(assemblyFileName, out peReader, out pdbReader);
        }
    
        /// <summary>
        /// Returns method token and IL offset for given source line number.
        /// </summary>
        /// <param name="assemblyFileName">file name of the assembly</param>
        /// <param name="fileName">source file name</param>
        /// <param name="lineNumber">source line number</param>
        /// <param name="methToken">method token return</param>
        /// <param name="ilOffset">IL offset return</param>
        public static void ResolveSequencePoint(string assemblyFileName, string fileName, int lineNumber, out int methToken, out int ilOffset)
        {
            MetadataReader peReader, pdbReader;
            methToken = 0;
            ilOffset = 0;
            
            if (!GetReaders(assemblyFileName, out peReader, out pdbReader))
                return;
            
            foreach (MethodDefinitionHandle methodDefHandle in peReader.MethodDefinitions)
            {
                MethodDebugInformation methodDebugInfo = pdbReader.GetMethodDebugInformation(methodDefHandle);
                SequencePointCollection sequencePoints = methodDebugInfo.GetSequencePoints();
                foreach (SequencePoint point in sequencePoints)
                {
                    string sourceName = pdbReader.GetString(pdbReader.GetDocument(point.Document).Name);
                    if (Path.GetFileName(sourceName) == Path.GetFileName(fileName) && point.StartLine == lineNumber)
                    {
                        methToken = MetadataTokens.GetToken(peReader, methodDefHandle);
                        ilOffset = point.Offset;
                        return;
                    }
                }
                
            }
        }
        
        /// <summary>
        /// Returns local variable name for given local index and IL offset.
        /// </summary>
        /// <param name="assemblyFileName">file name of the assembly</param>
        /// <param name="methodToken">method token</param>
        /// <param name="ilOffset">IL ofsset</param>
        /// <param name="localIndex">local variable index</param>
        /// <param name="localVarName">local variable name return</param>
        /// <returns>true if name has been found</returns>
        public static bool GetLocalVariableName(string assemblyFileName, int methodToken, int ilOffset, int localIndex, out IntPtr localVarName)
        {
            MetadataReader peReader, pdbReader;
            localVarName = IntPtr.Zero;
            
            if (!GetReaders(assemblyFileName, out peReader, out pdbReader))
                return false;

            Handle handle = MetadataTokens.Handle(methodToken);
            if (handle.Kind != HandleKind.MethodDefinition)
                return false;

            MethodDebugInformationHandle methodDebugHandle = ((MethodDefinitionHandle)handle).ToDebugInformationHandle();
            LocalScopeHandleCollection localScopes = pdbReader.GetLocalScopes(methodDebugHandle);
            LocalScope? bestScope = null;
            foreach (LocalScopeHandle scopeHandle in localScopes)
            {
                LocalScope scope = pdbReader.GetLocalScope(scopeHandle);
                if (!bestScope.HasValue)
                    bestScope = scope;
                else if (scope.StartOffset > ilOffset)
                    break;
                else if (scope.StartOffset >= bestScope.Value.StartOffset)
                    bestScope = scope;
            }
            
            if (!bestScope.HasValue)
                return false;
            LocalVariableHandleCollection localVars = bestScope.Value.GetLocalVariables();
            foreach (LocalVariableHandle varHandle in localVars)
            {
                LocalVariable localVar = pdbReader.GetLocalVariable(varHandle);
                if (localVar.Index == localIndex)
                {
                    if (localVar.Attributes == LocalVariableAttributes.DebuggerHidden)
                        return false;
                    localVarName = Marshal.StringToBSTR(pdbReader.GetString(localVar.Name));
                    return true;
                }
            }
            return false;
        }
    
        /// <summary>
        /// Returns metadata readers for assembly PE file and portable PDB.
        /// </summary>
        /// <param name="assemblyFileName">file name of the assembly</param>
        /// <param name="peReader">PE metadata reader return</param>
        /// <param name="pdbReader">PDB metadata reader return</param>
        /// <returns>true if debugging information is available</returns>
        private static bool GetReaders(string assemblyFileName, out MetadataReader peReader, out MetadataReader pdbReader)
        {
            peReader = null;
            pdbReader = null;
            
            if (!File.Exists(assemblyFileName))
            {
                return false;
            }
            Stream peStream = File.OpenRead(assemblyFileName);
            PEReader reader = new PEReader(peStream);
            string pdbPath = null;
            
            foreach (DebugDirectoryEntry entry in reader.ReadDebugDirectory())
            {
                if (entry.Type == DebugDirectoryEntryType.CodeView)
                {
                    CodeViewDebugDirectoryData codeViewData = reader.ReadCodeViewDebugDirectoryData(entry);
                    pdbPath = codeViewData.Path;
                    break;
                }
            }
            if (pdbPath == null)
            {
                return false;
            }
            if (!File.Exists(pdbPath))
            {
                pdbPath = Path.GetFileName(pdbPath);
                if (!File.Exists(pdbPath))
                {
                    return false;
                }
            }
            
            peReader = reader.GetMetadataReader();
            Stream pdbStream = File.OpenRead(pdbPath);
            MetadataReaderProvider provider = MetadataReaderProvider.FromPortablePdbStream(pdbStream);
            pdbReader = provider.GetMetadataReader();
            
            return true;
            
        }
    }

}
