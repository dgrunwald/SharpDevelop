﻿// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.Core;
using ICSharpCode.NRefactory;
using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.CSharp.Resolver;
using ICSharpCode.NRefactory.CSharp.TypeSystem;
using ICSharpCode.NRefactory.Editor;
using ICSharpCode.NRefactory.Semantics;
using ICSharpCode.NRefactory.TypeSystem;
using ICSharpCode.SharpDevelop;
using ICSharpCode.SharpDevelop.Editor.Search;
using ICSharpCode.SharpDevelop.Parser;
using ICSharpCode.SharpDevelop.Project;
using ICSharpCode.SharpDevelop.Refactoring;

namespace CSharpBinding.Parser
{
	public class TParser : IParser
	{
		public IReadOnlyList<string> TaskListTokens { get; set; }
		
		public bool CanParse(string fileName)
		{
			return Path.GetExtension(fileName).Equals(".CS", StringComparison.OrdinalIgnoreCase);
		}
		
		/*
		void RetrieveRegions(ISyntaxTree cu, ICSharpCode.NRefactory.Parser.SpecialTracker tracker)
		{
			for (int i = 0; i < tracker.CurrentSpecials.Count; ++i) {
				ICSharpCode.NRefactory.PreprocessingDirective directive = tracker.CurrentSpecials[i] as ICSharpCode.NRefactory.PreprocessingDirective;
				if (directive != null) {
					if (directive.Cmd == "#region") {
						int deep = 1;
						for (int j = i + 1; j < tracker.CurrentSpecials.Count; ++j) {
							ICSharpCode.NRefactory.PreprocessingDirective nextDirective = tracker.CurrentSpecials[j] as ICSharpCode.NRefactory.PreprocessingDirective;
							if (nextDirective != null) {
								switch (nextDirective.Cmd) {
									case "#region":
										++deep;
										break;
									case "#endregion":
										--deep;
										if (deep == 0) {
											cu.FoldingRegions.Add(new FoldingRegion(directive.Arg.Trim(), DomRegion.FromLocation(directive.StartPosition, nextDirective.EndPosition)));
											goto end;
										}
										break;
								}
							}
						}
						end: ;
					}
				}
			}
		}
		 */
		
		public ITextSource GetFileContent(FileName fileName)
		{
			return SD.FileService.GetFileContent(fileName);
		}
		
		public ParseInformation Parse(FileName fileName, ITextSource fileContent, bool fullParseInformationRequested,
		                              IProject parentProject, CancellationToken cancellationToken)
		{
			var csharpProject = parentProject as CSharpProject;
			
			CSharpParser parser = new CSharpParser(csharpProject != null ? csharpProject.CompilerSettings : null);
			parser.GenerateTypeSystemMode = !fullParseInformationRequested;
			
			SyntaxTree cu = parser.Parse(fileContent, fileName);
			cu.Freeze();
			
			CSharpUnresolvedFile file = cu.ToTypeSystem();
			ParseInformation parseInfo;
			
			if (fullParseInformationRequested)
				parseInfo = new CSharpFullParseInformation(file, fileContent.Version, cu);
			else
				parseInfo = new ParseInformation(file, fileContent.Version, fullParseInformationRequested);
			
			IDocument document = fileContent as IDocument;
			AddCommentTags(cu, parseInfo.TagComments, fileContent, parseInfo.FileName, ref document);
			if (fullParseInformationRequested) {
				if (document == null)
					document = new ReadOnlyDocument(fileContent, parseInfo.FileName);
				((CSharpFullParseInformation)parseInfo).newFoldings = CreateNewFoldings(cu, document);
			}
			
			return parseInfo;
		}
		
		#region AddCommentTags
		void AddCommentTags(SyntaxTree cu, IList<TagComment> tagComments, ITextSource fileContent, FileName fileName, ref IDocument document)
		{
			foreach (var comment in cu.Descendants.OfType<Comment>()) {
				if (comment.CommentType == CommentType.InactiveCode)
					continue;
				string match;
				if (comment.Content.ContainsAny(TaskListTokens, 0, out match)) {
					if (document == null)
						document = new ReadOnlyDocument(fileContent, fileName);
					int commentSignLength = comment.CommentType == CommentType.Documentation || comment.CommentType == CommentType.MultiLineDocumentation ? 3 : 2;
					int commentEndSignLength = comment.CommentType == CommentType.MultiLine || comment.CommentType == CommentType.MultiLineDocumentation ? 2 : 0;
					int commentStartOffset = document.GetOffset(comment.StartLocation) + commentSignLength;
					int commentEndOffset = document.GetOffset(comment.EndLocation) - commentEndSignLength;
					int endOffset;
					int searchOffset = 0;
					do {
						int start = commentStartOffset + searchOffset;
						int absoluteOffset = document.IndexOf(match, start, document.TextLength - start, StringComparison.Ordinal);
						var startLocation = document.GetLocation(absoluteOffset);
						endOffset = Math.Min(document.GetLineByNumber(startLocation.Line).EndOffset, commentEndOffset);
						string content = document.GetText(absoluteOffset, endOffset - absoluteOffset);
						if (content.Length < match.Length) {
							// HACK: workaround parser bug with multi-line documentation comments
							break;
						}
						tagComments.Add(new TagComment(content.Substring(0, match.Length), new DomRegion(cu.FileName, startLocation.Line, startLocation.Column), content.Substring(match.Length)));
						searchOffset = endOffset - commentStartOffset;
					} while (comment.Content.ContainsAny(TaskListTokens, searchOffset, out match));
				}
			}
		}
		#endregion
		
		#region CreateNewFoldings
		List<NewFolding> CreateNewFoldings(SyntaxTree syntaxTree, IDocument document)
		{
			FoldingVisitor v = new FoldingVisitor();
			v.document = document;
			syntaxTree.AcceptVisitor(v);
			return v.foldings;
		}
		#endregion
		
		public ResolveResult Resolve(ParseInformation parseInfo, TextLocation location, ICompilation compilation, CancellationToken cancellationToken)
		{
			var csParseInfo = parseInfo as CSharpFullParseInformation;
			if (csParseInfo == null)
				throw new ArgumentException("Parse info does not have SyntaxTree");
			
			return ResolveAtLocation.Resolve(compilation, csParseInfo.UnresolvedFile, csParseInfo.SyntaxTree, location, cancellationToken);
		}
		
		public void FindLocalReferences(ParseInformation parseInfo, ITextSource fileContent, IVariable variable, ICompilation compilation, Action<SearchResultMatch> callback, CancellationToken cancellationToken)
		{
			var csParseInfo = parseInfo as CSharpFullParseInformation;
			if (csParseInfo == null)
				throw new ArgumentException("Parse info does not have SyntaxTree");
			
			ReadOnlyDocument document = null;
			IHighlighter highlighter = null;
			
			new FindReferences().FindLocalReferences(
				variable, csParseInfo.UnresolvedFile, csParseInfo.SyntaxTree, compilation,
				delegate (AstNode node, ResolveResult result) {
					if (document == null) {
						document = new ReadOnlyDocument(fileContent, parseInfo.FileName);
						highlighter = SD.EditorControlService.CreateHighlighter(document);
						highlighter.BeginHighlighting();
					}
					var region = new DomRegion(parseInfo.FileName, node.StartLocation, node.EndLocation);
					int offset = document.GetOffset(node.StartLocation);
					int length = document.GetOffset(node.EndLocation) - offset;
					var builder = SearchResultsPad.CreateInlineBuilder(node.StartLocation, node.EndLocation, document, highlighter);
					var defaultTextColor = highlighter != null ? highlighter.DefaultTextColor : null;
					callback(new SearchResultMatch(parseInfo.FileName, node.StartLocation, node.EndLocation, offset, length, builder, defaultTextColor));
				}, cancellationToken);
			
			if (highlighter != null) {
				highlighter.Dispose();
			}
		}
		
		static readonly Lazy<IAssemblyReference[]> defaultReferences = new Lazy<IAssemblyReference[]>(
			delegate {
				Assembly[] assemblies = {
					typeof(object).Assembly,
					typeof(Uri).Assembly,
					typeof(Enumerable).Assembly
				};
				return assemblies.Select(asm => SD.AssemblyParserService.GetAssembly(FileName.Create(asm.Location))).ToArray();
			});
		
		public ICompilation CreateCompilationForSingleFile(FileName fileName, IUnresolvedFile unresolvedFile)
		{
			return new CSharpProjectContent()
				.AddAssemblyReferences(defaultReferences.Value)
				.AddOrUpdateFiles(unresolvedFile)
				.CreateCompilation();
		}
		
		public ResolveResult ResolveSnippet(ParseInformation parseInfo, TextLocation location, string codeSnippet, ICompilation compilation, CancellationToken cancellationToken)
		{
			var csParseInfo = parseInfo as CSharpFullParseInformation;
			if (csParseInfo == null)
				throw new ArgumentException("Parse info does not have SyntaxTree");
			CSharpAstResolver contextResolver = new CSharpAstResolver(compilation, csParseInfo.SyntaxTree, csParseInfo.UnresolvedFile);
			var node = csParseInfo.SyntaxTree.GetNodeAt(location);
			CSharpResolver context;
			if (node != null)
				context = contextResolver.GetResolverStateAfter(node, cancellationToken);
			else
				context = new CSharpResolver(compilation);
			CSharpParser parser = new CSharpParser();
			var expr = parser.ParseExpression(codeSnippet);
			if (parser.HasErrors)
				return new ErrorResolveResult(SpecialType.UnknownType, PrintErrorsAsString(parser.Errors), TextLocation.Empty);
			CSharpAstResolver snippetResolver = new CSharpAstResolver(context, expr);
			return snippetResolver.Resolve(expr, cancellationToken);
		}
		
		string PrintErrorsAsString(IEnumerable<Error> errors)
		{
			StringBuilder builder = new StringBuilder();
			
			foreach (var error in errors)
				builder.AppendLine(error.Message);
			
			return builder.ToString();
		}
	}
}
