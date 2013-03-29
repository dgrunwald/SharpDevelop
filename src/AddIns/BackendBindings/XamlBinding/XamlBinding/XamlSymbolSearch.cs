﻿// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.Core;
using ICSharpCode.NRefactory.CSharp.Resolver;
using ICSharpCode.NRefactory.Editor;
using ICSharpCode.NRefactory.Semantics;
using ICSharpCode.NRefactory.TypeSystem;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Editor;
using ICSharpCode.SharpDevelop.Editor.Search;
using ICSharpCode.SharpDevelop.Parser;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Refactoring;

namespace ICSharpCode.XamlBinding
{
	/// <summary>
	/// Description of XamlSymbolSearch.
	/// </summary>
	public class XamlSymbolSearch : ISymbolSearch
	{
		ICompilation compilation;
		IEntity entity;
		List<FileName> interestingFileNames;
		int workAmount;
		double workAmountInverse;
		
		public XamlSymbolSearch(IProject project, IEntity entity)
		{
			this.entity = entity;
			compilation = SD.ParserService.GetCompilation(project);
			interestingFileNames = new List<FileName>();
			foreach (var item in project.ParentSolution.Projects.SelectMany(p => p.Items).OfType<FileProjectItem>().Where(i => i.FileName.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)))
				interestingFileNames.Add(new FileName(item.FileName));
			workAmount = interestingFileNames.Count;
			workAmountInverse = 1.0 / workAmount;
		}
		
		public double WorkAmount {
			get { return workAmount; }
		}
		
		public Task FindReferencesAsync(SymbolSearchArgs searchArguments, Action<SearchedFile> callback)
		{
			if (callback == null)
				throw new ArgumentNullException("callback");
			var cancellationToken = searchArguments.ProgressMonitor.CancellationToken;
			return TaskEx.Run(
				() => {
					object progressLock = new object();
					Parallel.ForEach(
						interestingFileNames,
						new ParallelOptions {
							MaxDegreeOfParallelism = Environment.ProcessorCount,
							CancellationToken = cancellationToken
						},
						delegate (FileName fileName) {
							FindReferencesInFile(searchArguments, entity, fileName, callback, cancellationToken);
							lock (progressLock)
								searchArguments.ProgressMonitor.Progress += workAmountInverse;
						});
				}, cancellationToken
			);
		}
		
		void FindReferencesInFile(SymbolSearchArgs searchArguments, IEntity entity, FileName fileName, Action<SearchedFile> callback, CancellationToken cancellationToken)
		{
			ITextSource textSource = searchArguments.ParseableFileContentFinder.Create(fileName);
			if (textSource == null)
				return;
			int offset = textSource.IndexOf(entity.Name, 0, textSource.TextLength, StringComparison.Ordinal);
			if (offset < 0)
				return;
			
			var parseInfo = SD.ParserService.Parse(fileName, textSource) as XamlFullParseInformation;
			if (parseInfo == null)
				return;
			ReadOnlyDocument document = null;
			IHighlighter highlighter = null;
			var results = new ListWithReadOnlySupport<Reference>();
			XamlResolver resolver = new XamlResolver();
			do {
				if (document == null) {
					document = new ReadOnlyDocument(textSource, fileName);
					highlighter = SD.EditorControlService.CreateHighlighter(document);
					highlighter.BeginHighlighting();
				}
				var result = resolver.Resolve(parseInfo, document.GetLocation(offset + entity.Name.Length / 2 + 1), compilation, cancellationToken);
				int length = entity.Name.Length;
				if ((result is TypeResolveResult && ((TypeResolveResult)result).Type.Equals(entity)) || (result is MemberResolveResult && ((MemberResolveResult)result).Member.Equals(entity))) {
					var region = new DomRegion(fileName, document.GetLocation(offset), document.GetLocation(offset + length));
					var builder = SearchResultsPad.CreateInlineBuilder(region.Begin, region.End, document, highlighter);
					results.Add(new Reference(region, result, offset, length, builder, highlighter.DefaultTextColor));
				}
				offset = textSource.IndexOf(entity.Name, offset + length, textSource.TextLength - offset - length, StringComparison.OrdinalIgnoreCase);
			} while (offset > 0);
			if (highlighter != null) {
				highlighter.Dispose();
			}
			if (results.Count > 0)
				callback(new SearchedFile(fileName, results));
		}
	}
}
