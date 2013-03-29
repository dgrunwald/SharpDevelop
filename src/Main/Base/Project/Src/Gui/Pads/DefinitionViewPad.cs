﻿// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)

using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.Core;
using ICSharpCode.NRefactory.Semantics;
using ICSharpCode.NRefactory.TypeSystem;
using ICSharpCode.SharpDevelop.Editor;
using ICSharpCode.SharpDevelop.Parser;
using ICSharpCode.SharpDevelop.Workbench;

namespace ICSharpCode.SharpDevelop.Gui
{
	/// <summary>
	/// Description of the pad content
	/// </summary>
	public class DefinitionViewPad : AbstractPadContent
	{
		AvalonEdit.TextEditor ctl;
		
		/// <summary>
		/// The control representing the pad
		/// </summary>
		public override object Control {
			get {
				return ctl;
			}
		}
		
		/// <summary>
		/// Creates a new DefinitionViewPad object
		/// </summary>
		public DefinitionViewPad()
		{
			ctl = Editor.AvalonEditTextEditorAdapter.CreateAvalonEditInstance();
			ctl.IsReadOnly = true;
			ctl.MouseDoubleClick += OnDoubleClick;
			throw new NotImplementedException();
			//ParserService.ParserUpdateStepFinished += OnParserUpdateStep;
			ctl.IsVisibleChanged += delegate { UpdateTick(null); };
		}
		
		/// <summary>
		/// Cleans up all used resources
		/// </summary>
		public override void Dispose()
		{
			//ParserService.ParserUpdateStepFinished -= OnParserUpdateStep;
			ctl.Document = null;
			base.Dispose();
		}
		
		void OnDoubleClick(object sender, EventArgs e)
		{
			string fileName = currentFileName;
			if (fileName != null) {
				var caret = ctl.TextArea.Caret;
				FileService.JumpToFilePosition(fileName, caret.Line, caret.Column);
				
				// refresh DefinitionView to show the definition of the expression that was double-clicked
				UpdateTick(null);
			}
		}
		
		void OnParserUpdateStep(object sender, ParserUpdateStepEventArgs e)
		{
			UpdateTick(e);
		}
		
		async void UpdateTick(ParserUpdateStepEventArgs e)
		{
			if (!ctl.IsVisible) return;
			LoggingService.Debug("DefinitionViewPad.Update");
			
			ResolveResult res = await ResolveAtCaretAsync(e);
			if (res == null) return;
			var pos = res.GetDefinitionRegion();
			if (pos.IsEmpty) return;
			OpenFile(pos);
		}
		
		Task<ResolveResult> ResolveAtCaretAsync(ParserUpdateStepEventArgs e)
		{
			IWorkbenchWindow window = SD.Workbench.ActiveWorkbenchWindow;
			if (window == null) 
				return TaskEx.FromResult<ResolveResult>(null);
			IViewContent viewContent = window.ActiveViewContent;
			if (viewContent == null) 
				return TaskEx.FromResult<ResolveResult>(null);
			ITextEditor editor = viewContent.GetService<ITextEditor>();
			if (editor == null)
				return TaskEx.FromResult<ResolveResult>(null);
			
			// e might be null when this is a manually triggered update
			// don't resolve when an unrelated file was changed
			if (e != null && editor.FileName != e.FileName)
				return TaskEx.FromResult<ResolveResult>(null);
			
			return SD.ParserService.ResolveAsync(editor.FileName, editor.Caret.Location, editor.Document);
		}
		
		DomRegion oldPosition;
		string currentFileName;
		
		void OpenFile(DomRegion pos)
		{
			if (pos.Equals(oldPosition)) return;
			oldPosition = pos;
			if (pos.FileName != currentFileName)
				LoadFile(pos.FileName);
			ctl.TextArea.Caret.Location = pos.Begin;
			Rect r = ctl.TextArea.Caret.CalculateCaretRectangle();
			if (!r.IsEmpty) {
				ctl.ScrollToVerticalOffset(r.Top - 4);
			}
		}
		
		/// <summary>
		/// Loads the file from the corresponding text editor window if it is
		/// open otherwise the file is loaded from the file system.
		/// </summary>
		void LoadFile(string fileName)
		{
			// Load the text into the definition view's text editor.
			ctl.Document = new TextDocument(SD.FileService.GetFileContent(fileName));
			ctl.Document.FileName = fileName;
			currentFileName = fileName;
			ctl.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(Path.GetExtension(fileName));
		}
	}
}
