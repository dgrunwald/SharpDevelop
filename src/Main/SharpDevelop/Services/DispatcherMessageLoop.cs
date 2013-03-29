// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)

using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace ICSharpCode.SharpDevelop
{
	sealed class DispatcherMessageLoop : IMessageLoop, ISynchronizeInvoke
	{
		readonly Dispatcher dispatcher;
		readonly SynchronizationContext synchronizationContext;
		
		public DispatcherMessageLoop(Dispatcher dispatcher, SynchronizationContext synchronizationContext)
		{
			this.dispatcher = dispatcher;
			this.synchronizationContext = synchronizationContext;
		}
		
		public Thread Thread {
			get { return dispatcher.Thread; }
		}
		
		public Dispatcher Dispatcher {
			get { return dispatcher; }
		}
		
		public SynchronizationContext SynchronizationContext {
			get { return synchronizationContext; }
		}
		
		public ISynchronizeInvoke SynchronizingObject {
			get { return this; }
		}
		
		public bool InvokeRequired {
			get { return !dispatcher.CheckAccess(); }
		}
		
		public bool CheckAccess()
		{
			return dispatcher.CheckAccess();
		}
		
		public void VerifyAccess()
		{
			dispatcher.VerifyAccess();
		}
		
		public void InvokeIfRequired(Action callback)
		{
			InvokeIfRequired(callback, DispatcherPriority.Send);
		}
		
		public void InvokeIfRequired(Action callback, DispatcherPriority priority)
		{
			if (dispatcher.CheckAccess())
				callback();
			else
				dispatcher.Invoke(callback, priority);
		}
		
		public void InvokeIfRequired(Action callback, DispatcherPriority priority, CancellationToken cancellationToken)
		{
			if (dispatcher.CheckAccess())
				callback();
			else
				InvokeAsync(callback, priority, cancellationToken).GetAwaiter().GetResult();
		}
		
		public T InvokeIfRequired<T>(Func<T> callback)
		{
			return InvokeIfRequired(callback, DispatcherPriority.Send);
		}
		
		public T InvokeIfRequired<T>(Func<T> callback, DispatcherPriority priority)
		{
			if (dispatcher.CheckAccess())
				return callback();
			else
				return (T)dispatcher.Invoke(callback, priority);
		}
		
		public T InvokeIfRequired<T>(Func<T> callback, DispatcherPriority priority, CancellationToken cancellationToken)
		{
			if (dispatcher.CheckAccess())
				return callback();
			else
				return InvokeAsync(callback, priority, cancellationToken).GetAwaiter().GetResult();
		}
		
		public Task InvokeAsync(Action callback)
		{
			return InvokeAsync(callback, DispatcherPriority.Send, CancellationToken.None);
		}
		
		public Task InvokeAsync(Action callback, DispatcherPriority priority)
		{
			return InvokeAsync(callback, priority, CancellationToken.None);
		}
		
		public Task InvokeAsync(Action callback, DispatcherPriority priority, CancellationToken cancellationToken)
		{
			return InvokeAsync<object>(delegate { callback(); return null; }, priority, cancellationToken);
		}
		
		public Task<T> InvokeAsync<T>(Func<T> callback)
		{
			return InvokeAsync(callback, DispatcherPriority.Send, CancellationToken.None);
		}
		
		public Task<T> InvokeAsync<T>(Func<T> callback, DispatcherPriority priority)
		{
			return InvokeAsync(callback, priority, CancellationToken.None);
		}
		
		public Task<T> InvokeAsync<T>(Func<T> callback, DispatcherPriority priority, CancellationToken cancellationToken)
		{
			TaskCompletionSource<T> tcs = new TaskCompletionSource<T>();
			if (cancellationToken.IsCancellationRequested) {
				tcs.TrySetCanceled();
				return tcs.Task;
			}
			var cancellationRegistration = cancellationToken.Register(delegate { tcs.TrySetCanceled(); });
			dispatcher.BeginInvoke(new Action(
				delegate {
					try {
						if (!cancellationToken.IsCancellationRequested)
							tcs.TrySetResult(callback());
					} catch (Exception exception) {
						tcs.TrySetException(exception);
					} finally {
						cancellationRegistration.Dispose();
					}
				}), priority);
			return tcs.Task;
		}
		
		public void InvokeAsyncAndForget(Action callback)
		{
			dispatcher.BeginInvoke(callback);
		}
		
		public void InvokeAsyncAndForget(Action callback, DispatcherPriority priority)
		{
			dispatcher.BeginInvoke(callback, priority);
		}
		
		public async void CallLater(TimeSpan delay, Action method)
		{
			await TaskEx.Delay(delay).ConfigureAwait(false);
			InvokeAsyncAndForget(method);
		}
		
		IAsyncResult ISynchronizeInvoke.BeginInvoke(Delegate method, object[] args)
		{
			TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();
			var op = dispatcher.BeginInvoke(method, args);
			op.Completed += delegate { tcs.TrySetResult(null); };
			// check after registering the event handler to handle race condition
			if (op.Status == DispatcherOperationStatus.Completed)
				tcs.TrySetResult(null);
			return tcs.Task;
		}
		
		object ISynchronizeInvoke.EndInvoke(IAsyncResult result)
		{
			return ((Task<object>)result).Result;
		}
		
		object ISynchronizeInvoke.Invoke(Delegate method, object[] args)
		{
			return dispatcher.Invoke(method, args);
		}
	}
}
