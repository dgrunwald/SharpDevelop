// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)

using System;
using System.Collections.Generic;
using ICSharpCode.AvalonEdit.AddIn;
using ICSharpCode.PackageManagement.EnvDTE;
using ICSharpCode.SharpDevelop;

namespace PackageManagement.Tests.EnvDTE
{
	public class FakeCustomizedHighlightingRules : ICustomizedHighlightingRules
	{
		public ListWithReadOnlySupport<CustomizedHighlightingColor> Colors = new ListWithReadOnlySupport<CustomizedHighlightingColor>();
		
		public IReadOnlyList<CustomizedHighlightingColor> LoadColors()
		{
			return Colors;
		}
		
		public List<CustomizedHighlightingColor> ColorsSaved;
		
		public void SaveColors(IEnumerable<CustomizedHighlightingColor> colors)
		{
			ColorsSaved = new List<CustomizedHighlightingColor>(colors);
		}
	}
}
