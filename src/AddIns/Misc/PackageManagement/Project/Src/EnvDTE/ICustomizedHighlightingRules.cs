﻿// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)

using System;
using System.Collections.Generic;
using ICSharpCode.AvalonEdit.AddIn;
using ICSharpCode.SharpDevelop;

namespace ICSharpCode.PackageManagement.EnvDTE
{
	public interface ICustomizedHighlightingRules
	{
		IReadOnlyList<CustomizedHighlightingColor> LoadColors();
		void SaveColors(IEnumerable<CustomizedHighlightingColor> colors);
	}
}
