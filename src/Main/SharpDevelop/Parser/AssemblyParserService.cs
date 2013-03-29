﻿// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)

using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using ICSharpCode.Core;
using ICSharpCode.NRefactory.Documentation;
using ICSharpCode.NRefactory.TypeSystem;
using ICSharpCode.NRefactory.Utils;
using Mono.Cecil;

namespace ICSharpCode.SharpDevelop.Parser
{
	/// <summary>
	/// Portions of parser service that deal with loading external assemblies for code completion.
	/// </summary>
	sealed class AssemblyParserService : IAssemblyParserService
	{
		#region Get Assembly By File Name
		sealed class LoadedAssembly
		{
			public readonly Task<IUnresolvedAssembly> ProjectContent;
			public readonly DateTime AssemblyFileLastWriteTime;
			
			public LoadedAssembly(Task<IUnresolvedAssembly> projectContent, DateTime assemblyFileLastWriteTime)
			{
				this.ProjectContent = projectContent;
				this.AssemblyFileLastWriteTime = assemblyFileLastWriteTime;
			}
		}
		
		// TODO: use weak reference to IProjectContent (not to LoadedAssembly!) so that unused assemblies can be unloaded
		Dictionary<FileName, LoadedAssembly> projectContentDictionary = new Dictionary<FileName, LoadedAssembly>();
		
		[ThreadStatic] static Dictionary<FileName, LoadedAssembly> up2dateProjectContents;
		
		public IUnresolvedAssembly GetAssembly(FileName fileName, CancellationToken cancellationToken = default(CancellationToken))
		{
			// We currently do not support cancelling the load operation itself, because another GetAssembly() call
			// with a different cancellation token might request the same assembly.
			bool isNewTask;
			LoadedAssembly asm = GetLoadedAssembly(fileName, out isNewTask);
			if (isNewTask)
				asm.ProjectContent.RunSynchronously();
			else
				asm.ProjectContent.Wait(cancellationToken);
			return asm.ProjectContent.Result;
		}
		
		public Task<IUnresolvedAssembly> GetAssemblyAsync(FileName fileName, CancellationToken cancellationToken = default(CancellationToken))
		{
			bool isNewTask;
			LoadedAssembly asm = GetLoadedAssembly(fileName, out isNewTask);
			if (isNewTask)
				asm.ProjectContent.Start();
			return asm.ProjectContent;
		}
		
		/// <summary>
		/// "using (AssemblyParserService.AvoidRedundantChecks())"
		/// Within the using block, the AssemblyParserService will only check once per assembly if the
		/// existing cached project content (if any) is up to date.
		/// Any additional accesses will return that cached project content without causing an update check.
		/// This applies only to the thread that called AvoidRedundantChecks() - other threads will
		/// perform update checks as usual.
		/// </summary>
		public IDisposable AvoidRedundantChecks()
		{
			if (up2dateProjectContents != null)
				return null;
			up2dateProjectContents = new Dictionary<FileName, LoadedAssembly>();
			return new CallbackOnDispose(
				delegate {
					up2dateProjectContents = null;
					lock (projectContentDictionary) {
						CleanWeakDictionary();
					}
				});
		}
		
		void CleanWeakDictionary()
		{
			//Debug.Assert(Monitor.IsEntered(projectContentDictionary));
			List<FileName> removed = new List<FileName>();
			foreach (var pair in projectContentDictionary) {
				//if (!pair.Value.IsAlive)
				//	removed.Add(pair.Key);
			}
			foreach (var key in removed)
				projectContentDictionary.Remove(key);
		}
		
		LoadedAssembly GetLoadedAssembly(FileName fileName, out bool isNewTask)
		{
			isNewTask = false;
			LoadedAssembly asm;
			var up2dateProjectContents = AssemblyParserService.up2dateProjectContents;
			if (up2dateProjectContents != null) {
				if (up2dateProjectContents.TryGetValue(fileName, out asm))
					return asm;
			}
			DateTime lastWriteTime = File.GetLastWriteTimeUtc(fileName);
			lock (projectContentDictionary) {
				if (projectContentDictionary.TryGetValue(fileName, out asm)) {
					if (asm.AssemblyFileLastWriteTime == lastWriteTime) {
						return asm;
					}
				} else {
					asm = null;
				}
				var task = new Task<IUnresolvedAssembly>(() => LoadAssembly(fileName, CancellationToken.None));
				isNewTask = true;
				asm = new LoadedAssembly(task, lastWriteTime);
				
				if (up2dateProjectContents == null)
					CleanWeakDictionary();
				// The assembly might already be in the dictionary if we had loaded it before,
				// but now the lastWriteTime changed.
				projectContentDictionary[fileName] = asm;
				
				return asm;
			}
		}
		#endregion
		
		#region Load Assembly
		IUnresolvedAssembly LoadAssembly(FileName fileName, CancellationToken cancellationToken)
		{
			DateTime lastWriteTime = File.GetLastWriteTimeUtc(fileName);
			string cacheFileName = GetCacheFileName(fileName);
			IUnresolvedAssembly pc = TryReadFromCache(cacheFileName, lastWriteTime);
			if (pc != null)
				return pc;
			
			//LoggingService.Debug("Loading " + fileName);
			cancellationToken.ThrowIfCancellationRequested();
			var param = new ReaderParameters();
			param.AssemblyResolver = new DummyAssemblyResolver();
			AssemblyDefinition asm = AssemblyDefinition.ReadAssembly(fileName, param);
			
			CecilLoader l = new CecilLoader();
			string xmlDocFile = FindXmlDocumentation(fileName, asm.MainModule.Runtime);
			if (xmlDocFile != null) {
				try {
					l.DocumentationProvider = new XmlDocumentationProvider(xmlDocFile);
				} catch (XmlException ex) {
					LoggingService.Warn("Ignoring error while reading xml doc from " + xmlDocFile, ex);
				} catch (IOException ex) {
					LoggingService.Warn("Ignoring error while reading xml doc from " + xmlDocFile, ex);
				} catch (UnauthorizedAccessException ex) {
					LoggingService.Warn("Ignoring error while reading xml doc from " + xmlDocFile, ex);
				}
			}
			l.CancellationToken = cancellationToken;
			pc = l.LoadAssembly(asm);
			SaveToCacheAsync(cacheFileName, lastWriteTime, pc).FireAndForget();
			//SaveToCache(cacheFileName, lastWriteTime, pc);
			return pc;
		}
		
		// used to prevent Cecil from loading referenced assemblies
		sealed class DummyAssemblyResolver : IAssemblyResolver
		{
			public AssemblyDefinition Resolve(AssemblyNameReference name)
			{
				return null;
			}
			
			public AssemblyDefinition Resolve(string fullName)
			{
				return null;
			}
			
			public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
			{
				return null;
			}
			
			public AssemblyDefinition Resolve(string fullName, ReaderParameters parameters)
			{
				return null;
			}
		}
		#endregion
		
		#region Lookup XML documentation
		static readonly string referenceAssembliesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Reference Assemblies\Microsoft\\Framework");
		static readonly string frameworkPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Microsoft.NET\Framework");
		
		static string FindXmlDocumentation(string assemblyFileName, TargetRuntime runtime)
		{
			string fileName;
			switch (runtime) {
				case TargetRuntime.Net_1_0:
					fileName = LookupLocalizedXmlDoc(Path.Combine(frameworkPath, "v1.0.3705", assemblyFileName));
					break;
				case TargetRuntime.Net_1_1:
					fileName = LookupLocalizedXmlDoc(Path.Combine(frameworkPath, "v1.1.4322", assemblyFileName));
					break;
				case TargetRuntime.Net_2_0:
					fileName = LookupLocalizedXmlDoc(Path.Combine(frameworkPath, "v2.0.50727", assemblyFileName))
						?? LookupLocalizedXmlDoc(Path.Combine(referenceAssembliesPath, "v3.5"))
						?? LookupLocalizedXmlDoc(Path.Combine(referenceAssembliesPath, "v3.0"))
						?? LookupLocalizedXmlDoc(Path.Combine(referenceAssembliesPath, @".NETFramework\v3.5\Profile\Client"));
					break;
				case TargetRuntime.Net_4_0:
				default:
					fileName = LookupLocalizedXmlDoc(Path.Combine(referenceAssembliesPath, @".NETFramework\v4.0", assemblyFileName))
						?? LookupLocalizedXmlDoc(Path.Combine(frameworkPath, "v4.0.30319", assemblyFileName));
					break;
			}
			return fileName;
		}
		
		static string LookupLocalizedXmlDoc(string fileName)
		{
			return XmlDocumentationProvider.LookupLocalizedXmlDoc(fileName);
		}
		#endregion
		
		#region DomPersistence
		/// <summary>
		/// Gets/Sets the directory for cached project contents.
		/// </summary>
		public string DomPersistencePath { get; set; }
		
		string GetCacheFileName(FileName assemblyFileName)
		{
			if (DomPersistencePath == null)
				return null;
			string cacheFileName = Path.GetFileNameWithoutExtension(assemblyFileName);
			if (cacheFileName.Length > 32)
				cacheFileName = cacheFileName.Substring(cacheFileName.Length - 32); // use 32 last characters
			cacheFileName = Path.Combine(DomPersistencePath, cacheFileName + "." + assemblyFileName.ToString().ToUpperInvariant().GetStableHashCode().ToString("x8") + ".dat");
			return cacheFileName;
		}
		
		static IUnresolvedAssembly TryReadFromCache(string cacheFileName, DateTime lastWriteTime)
		{
			if (cacheFileName == null || !File.Exists(cacheFileName))
				return null;
			//LoggingService.Debug("Deserializing " + cacheFileName);
			try {
				using (FileStream fs = new FileStream(cacheFileName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, FileOptions.SequentialScan)) {
					using (BinaryReader reader = new BinaryReaderWith7BitEncodedInts(fs)) {
						if (reader.ReadInt64() != lastWriteTime.Ticks) {
							LoggingService.Debug("Timestamp mismatch, deserialization aborted. (" + cacheFileName + ")");
							return null;
						}
						FastSerializer s = new FastSerializer();
						return (IUnresolvedAssembly)s.Deserialize(reader);
					}
				}
			} catch (IOException ex) {
				LoggingService.Warn(ex);
				return null;
			} catch (UnauthorizedAccessException ex) {
				LoggingService.Warn(ex);
				return null;
			} catch (SerializationException ex) {
				LoggingService.Warn(ex);
				return null;
			}
		}
		
		Task SaveToCacheAsync(string cacheFileName, DateTime lastWriteTime, IUnresolvedAssembly pc)
		{
			if (cacheFileName == null)
				return TaskEx.FromResult<object>(null);
			
			// Call SaveToCache on a background task:
			var shutdownService = SD.ShutdownService;
			var task = IOTaskScheduler.Factory.StartNew(delegate { SaveToCache(cacheFileName, lastWriteTime, pc); }, shutdownService.ShutdownToken);
			shutdownService.AddBackgroundTask(task);
			return task;
		}
		
		void SaveToCache(string cacheFileName, DateTime lastWriteTime, IUnresolvedAssembly pc)
		{
			if (cacheFileName == null)
				return;
			LoggingService.Debug("Serializing to " + cacheFileName);
			try {
				Directory.CreateDirectory(DomPersistencePath);
				using (FileStream fs = new FileStream(cacheFileName, FileMode.Create, FileAccess.Write)) {
					using (BinaryWriter writer = new BinaryWriterWith7BitEncodedInts(fs)) {
						writer.Write(lastWriteTime.Ticks);
						FastSerializer s = new FastSerializer();
						s.Serialize(writer, pc);
					}
				}
			} catch (IOException ex) {
				LoggingService.Warn(ex);
				// Can happen if two SD instances are trying to access the file at the same time.
				// We'll just let one of them win, and instance that got the exception won't write to the cache at all.
				// Similarly, we also ignore the other kinds of IO exceptions.
			} catch (UnauthorizedAccessException ex) {
				LoggingService.Warn(ex);
			}
		}
		#endregion
	}
}
