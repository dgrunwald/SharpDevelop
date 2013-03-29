// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)

using System;

namespace ICSharpCode.SharpDevelop
{
	public sealed class WeakReference<T> where T : class
	{
		readonly WeakReference wr;
		
		public WeakReference(T target)
		{
			this.wr = new WeakReference(target);
		}
		
		public WeakReference(T target, bool trackResurrection)
		{
			this.wr = new WeakReference(target, trackResurrection);
		}
		
		public void SetTarget(T target)
		{
			wr.Target = target;
		}
		
		public bool TryGetTarget(out T target)
		{
			T target2 = wr.Target as T;
			target = target2;
			return target2 != null;
		}
	}
}
