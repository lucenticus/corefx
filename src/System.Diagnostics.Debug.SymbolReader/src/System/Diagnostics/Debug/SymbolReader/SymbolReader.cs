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
        /// Returns source line number and source file name for given IL offset and method token.
        /// </summary>
        /// <param name="assemblyFileName">file name of the assembly</param>
        /// <param name="methToken">method token</param>
        /// <param name="ilOffset">IL offset</param>
        /// <param name="lineNumber">source line number return</param>
        /// <param name="fileName">source file name</param>
        public static int GetLineByILOffset(string assemblyFileName, int methodToken, int ilOffset, out int lineNumber, out IntPtr fileName)
        {
            MetadataReader peReader, pdbReader;
            lineNumber = 0;
            fileName = IntPtr.Zero;
            if (!GetReaders(assemblyFileName, out peReader, out pdbReader))
                return -1;
            Handle handle = MetadataTokens.Handle(methodToken);
            if (handle.Kind != HandleKind.MethodDefinition)
                return -1;

            MethodDebugInformationHandle methodDebugHandle = ((MethodDefinitionHandle)handle).ToDebugInformationHandle();
            MethodDebugInformation methodDebugInfo = pdbReader.GetMethodDebugInformation(methodDebugHandle);
            SequencePointCollection sequencePoints = methodDebugInfo.GetSequencePoints();

            SequencePoint nearestPoint = sequencePoints.GetEnumerator().Current;
            foreach (SequencePoint point in sequencePoints)
            {
                if (point.Offset <= ilOffset)
                    nearestPoint = point;
                else
                {
                    if (nearestPoint.StartLine == 0)
                        return -1;
                    lineNumber = nearestPoint.StartLine;
                    fileName = Marshal.StringToBSTR(pdbReader.GetString(pdbReader.GetDocument(nearestPoint.Document).Name));
                    return 0;
                }
            }
            return -1;
        }
        
        /// <summary>
        /// Returns local variable name for given local index and IL offset.
        /// </summary>
        /// <param name="assemblyFileName">file name of the assembly</param>
        /// <param name="methodToken">method token</param>
        /// <param name="localIndex">local variable index</param>
        /// <param name="localVarName">local variable name return</param>
        /// <returns>true if name has been found</returns>
        public static bool GetLocalVariableName(string assemblyFileName, int methodToken, int localIndex, out IntPtr localVarName)
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
            foreach (LocalScopeHandle scopeHandle in localScopes)
            {
                LocalScope scope = pdbReader.GetLocalScope(scopeHandle);
                LocalVariableHandleCollection localVars = scope.GetLocalVariables();
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
