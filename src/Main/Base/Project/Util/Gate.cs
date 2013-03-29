// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ICSharpCode.SharpDevelop
{
	/// <summary>
	/// A simple gate. Wait() calls will block until Release() is called.
	/// There is no way to reset the gate.
	/// </summary>
	public class Gate
	{
		TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();
		
		public void Release()
		{
			tcs.TrySetResult(null);
		}
		
		public Task WaitAsync(CancellationToken cancellationToken)
		{
			if (cancellationToken.CanBeCanceled)
				return WaitAsyncWithCancellation(cancellationToken);
			else
				return tcs.Task;
		}
		
		async Task WaitAsyncWithCancellation(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			TaskCompletionSource<object> cancellation = new TaskCompletionSource<object>();
			using (cancellationToken.Register(delegate { cancellation.TrySetCanceled(); })) {
				await await TaskEx.WhenAny(tcs.Task, cancellation.Task);
			}
		}
	}
}
