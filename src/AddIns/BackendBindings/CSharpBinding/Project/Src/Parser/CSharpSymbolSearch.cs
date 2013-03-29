﻿// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CSharpBinding.Parser;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.Core;
using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.CSharp.Resolver;
using ICSharpCode.NRefactory.CSharp.TypeSystem;
using ICSharpCode.NRefactory.Editor;
using ICSharpCode.NRefactory.Semantics;
using ICSharpCode.NRefactory.TypeSystem;
using ICSharpCode.NRefactory.Utils;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Editor;
using ICSharpCode.SharpDevelop.Editor.Search;
using ICSharpCode.SharpDevelop.Gui;
using ICSharpCode.SharpDevelop.Parser;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Refactoring;

namespace CSharpBinding
{
	/// <summary>
	/// C# backend implementation for 'find references'.
	/// </summary>
	public class CSharpSymbolSearch : ISymbolSearch
	{
		IProject project;
		ICompilation compilation;
		FindReferences fr = new FindReferences();
		IList<IFindReferenceSearchScope> searchScopes;
		IList<string>[] interestingFileNames;
		int workAmount;
		double workAmountInverse;
		
		public CSharpSymbolSearch(IProject project, IEntity entity)
		{
			this.project = project;
			searchScopes = fr.GetSearchScopes(entity);
			compilation = SD.ParserService.GetCompilation(project);
			interestingFileNames = new IList<string>[searchScopes.Count];
			for (int i = 0; i < searchScopes.Count; i++) {
				interestingFileNames[i] = fr.GetInterestingFiles(searchScopes[i], compilation).Select(f => f.FileName).ToList();
				workAmount += interestingFileNames[i].Count;
			}
			workAmountInverse = 1.0 / workAmount;
		}
		
		public double WorkAmount {
			get { return workAmount; }
		}
		
		public Task FindReferencesAsync(SymbolSearchArgs args, Action<SearchedFile> callback)
		{
			if (callback == null)
				throw new ArgumentNullException("callback");
			var cancellationToken = args.ProgressMonitor.CancellationToken;
			return TaskEx.Run(
				() => {
					for (int i = 0; i < searchScopes.Count; i++) {
						IFindReferenceSearchScope searchScope = searchScopes[i];
						object progressLock = new object();
						Parallel.ForEach(
							interestingFileNames[i],
							new ParallelOptions {
								MaxDegreeOfParallelism = Environment.ProcessorCount,
								CancellationToken = cancellationToken
							},
							delegate (string fileName) {
								FindReferencesInFile(args, searchScope, FileName.Create(fileName), callback, cancellationToken);
								lock (progressLock)
									args.ProgressMonitor.Progress += workAmountInverse;
							});
					}
				}, cancellationToken
			);
		}
		
		void FindReferencesInFile(SymbolSearchArgs args, IFindReferenceSearchScope searchScope, FileName fileName, Action<SearchedFile> callback, CancellationToken cancellationToken)
		{
			ITextSource textSource = args.ParseableFileContentFinder.Create(fileName);
			if (textSource == null)
				return;
			if (searchScope.SearchTerm != null) {
				if (textSource.IndexOf(searchScope.SearchTerm, 0, textSource.TextLength, StringComparison.Ordinal) < 0)
					return;
			}
			
			var parseInfo = SD.ParserService.Parse(fileName, textSource) as CSharpFullParseInformation;
			if (parseInfo == null)
				return;
			ReadOnlyDocument document = null;
			IHighlighter highlighter = null;
			var results = new ListWithReadOnlySupport<Reference>();
			
			// Grab the unresolved file matching the compilation version
			// (this may differ from the version created by re-parsing the project)
			CSharpUnresolvedFile unresolvedFile = null;
			IProjectContent pc = compilation.MainAssembly.UnresolvedAssembly as IProjectContent;
			if (pc != null) {
				unresolvedFile = pc.GetFile(fileName) as CSharpUnresolvedFile;
			}
			
			fr.FindReferencesInFile(
				searchScope, unresolvedFile, parseInfo.SyntaxTree, compilation,
				delegate (AstNode node, ResolveResult result) {
					if (document == null) {
						document = new ReadOnlyDocument(textSource, fileName);
						highlighter = SD.EditorControlService.CreateHighlighter(document);
						highlighter.BeginHighlighting();
					}
					Identifier identifier = node.GetChildByRole(Roles.Identifier);
					if (!identifier.IsNull)
						node = identifier;
					var region = new DomRegion(fileName, node.StartLocation, node.EndLocation);
					int offset = document.GetOffset(node.StartLocation);
					int length = document.GetOffset(node.EndLocation) - offset;
					var builder = SearchResultsPad.CreateInlineBuilder(node.StartLocation, node.EndLocation, document, highlighter);
					var defaultTextColor = highlighter != null ? highlighter.DefaultTextColor : null;
					results.Add(new Reference(region, result, offset, length, builder, defaultTextColor));
				}, cancellationToken);
			if (highlighter != null) {
				highlighter.Dispose();
			}
			if (results.Count > 0)
				callback(new SearchedFile(fileName, results));
		}
	}
}
