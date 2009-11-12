﻿// <file>
//     <copyright see="prj:///doc/copyright.txt"/>
//     <license see="prj:///doc/license.txt"/>
//     <owner name="David Srbecký" email="dsrbecky@gmail.com"/>
//     <version>$Revision$</version>
// </file>

using System;
using System.Runtime.InteropServices;

namespace Debugger.Interop.CorSym
{
	public static partial class CorSymExtensionMethods
	{
		static void ProcessOutParameter(object parameter)
		{
			TrackedComObjects.ProcessOutParameter(parameter);
		}
		
		// ISymUnmanagedBinder
		
		public static ISymUnmanagedReader GetReaderForFile(this ISymUnmanagedBinder symBinder, object importer, string filename, string searchPath)
		{
			IntPtr pfilename = Marshal.StringToCoTaskMemUni(filename);
			IntPtr psearchPath = Marshal.StringToCoTaskMemUni(searchPath);
			ISymUnmanagedReader res = symBinder.GetReaderForFile(importer, pfilename, psearchPath);
			Marshal.FreeCoTaskMem(pfilename);
			Marshal.FreeCoTaskMem(psearchPath);
			return res;
		}
		
		// ISymUnmanagedDocument
		
		public static string GetURL(this ISymUnmanagedDocument symDoc)
		{
			return Util.GetCorSymString(symDoc.GetURL);
		}
		
		public static unsafe byte[] GetCheckSum(this ISymUnmanagedDocument symDoc)
		{
			uint actualLength;
			byte[] checkSum = new byte[20];
			fixed(byte* pCheckSum = checkSum)
				symDoc.GetCheckSum((uint)checkSum.Length, out actualLength, new IntPtr(pCheckSum));
			if (actualLength > checkSum.Length) {
				checkSum = new byte[actualLength];
				fixed(byte* pCheckSum = checkSum)
					symDoc.GetCheckSum((uint)checkSum.Length, out actualLength, new IntPtr(pCheckSum));
			}
			Array.Resize(ref checkSum, (int)actualLength);
			return checkSum;
		}
		
		// ISymUnmanagedMethod
		
		public static SequencePoint[] GetSequencePoints(this ISymUnmanagedMethod symMethod)
		{
			uint count = symMethod.GetSequencePointCount();
			
			ISymUnmanagedDocument[] documents = new ISymUnmanagedDocument[count];
			uint[] offsets    = new uint[count];
			uint[] lines      = new uint[count];
			uint[] columns    = new uint[count];
			uint[] endLines   = new uint[count];
			uint[] endColumns = new uint[count];
			                  
			symMethod.GetSequencePoints(
				count,
				out count,
				offsets,
				documents,
				lines,
				columns,
				endLines,
				endColumns
			);
			
			SequencePoint[] sequencePoints = new SequencePoint[count];
			
			for(int i = 0; i < count; i++) {
				sequencePoints[i] = new SequencePoint() {
					Document = documents[i],
					Offset = offsets[i],
					Line = lines[i],
					Column = columns[i],
					EndLine = endLines[i],
					EndColumn = endColumns[i]
				};
			}
			
			return sequencePoints;
		}
		
		// ISymUnmanagedReader
		
		public static ISymUnmanagedDocument GetDocument(this ISymUnmanagedReader symReader, string url, System.Guid language, System.Guid languageVendor, System.Guid documentType)
		{
			IntPtr p = Marshal.StringToCoTaskMemUni(url);
			ISymUnmanagedDocument res = symReader.GetDocument(p, language, languageVendor, documentType);
			Marshal.FreeCoTaskMem(p);
			return res;
		}
		
		// ISymUnmanagedScope
		
		public static ISymUnmanagedScope[] GetChildren(this ISymUnmanagedScope symScope)
		{
			uint count;
			symScope.GetChildren(0, out count, new ISymUnmanagedScope[0]);
			ISymUnmanagedScope[] children = new ISymUnmanagedScope[count];
			symScope.GetChildren(count, out count, children);
			return children;
		}
		
		public static ISymUnmanagedVariable[] GetLocals(this ISymUnmanagedScope symScope)
		{
			uint count;
			symScope.GetLocals(0, out count, new ISymUnmanagedVariable[0]);
			ISymUnmanagedVariable[] locals = new ISymUnmanagedVariable[count];
			symScope.GetLocals(count, out count, locals);
			return locals;
		}
		
		public static ISymUnmanagedNamespace[] GetNamespaces(this ISymUnmanagedScope symScope)
		{
			uint count;
			symScope.GetNamespaces(0, out count, new ISymUnmanagedNamespace[0]);
			ISymUnmanagedNamespace[] namespaces = new ISymUnmanagedNamespace[count];
			symScope.GetNamespaces(count, out count, namespaces);
			return namespaces;
		}
		
		// ISymUnmanagedVariable
		
		public static string GetName(this ISymUnmanagedVariable symVar)
		{
			return Util.GetCorSymString(symVar.GetName);
		}
		
		const int defaultSigSize = 8;
		
		public static unsafe byte[] GetSignature(this ISymUnmanagedVariable symVar)
		{
			byte[] sig = new byte[defaultSigSize];
			uint acualSize;
			fixed(byte* pSig = sig)
				symVar.GetSignature((uint)sig.Length, out acualSize, new IntPtr(pSig));
			Array.Resize(ref sig, (int)acualSize);
			if (acualSize > defaultSigSize)
				fixed(byte* pSig = sig)
					symVar.GetSignature((uint)sig.Length, out acualSize, new IntPtr(pSig));
			return sig;
		}
	}
	
	public class SequencePoint: IComparable<SequencePoint>
	{
		public ISymUnmanagedDocument Document { get; internal set; }
		public uint Offset { get; internal set; }
		public uint Line { get; internal set; }
		public uint Column { get; internal set; }
		public uint EndLine { get; internal set; }
		public uint EndColumn { get; internal set; }
		
		public int CompareTo(SequencePoint other)
		{
			if (this.Line == other.Line) {
				return this.Column.CompareTo(other.Column);
			} else {
				return this.Line.CompareTo(other.Line);
			}
		}
	}
}
