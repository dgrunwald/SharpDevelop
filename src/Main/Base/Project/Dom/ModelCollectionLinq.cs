﻿// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ICSharpCode.SharpDevelop.Dom
{
	/// <summary>
	/// Provides LINQ operators for <see cref="IModelCollection{T}"/>.
	/// </summary>
	public static class ModelCollectionLinq
	{
		// A general 'AsObservableCollection()' would be nice; but I don't see any good way
		// to implement that without leaking memory.
		// The problem is that IModelCollection is unordered; but ObservableCollection requires us to maintain a stable order.
		
		#region OfType / Cast
		public static IModelCollection<TResult> OfType<TResult>(this IModelCollection<object> source)
		{
			return SelectMany(source, item => item is TResult ? new ImmutableModelCollection<TResult>(new[] { (TResult)item }) : ImmutableModelCollection<TResult>.Empty);
		}
		
		public static IModelCollection<TResult> Cast<TResult>(this IModelCollection<object> source)
		{
			return SelectMany(source, item => new ImmutableModelCollection<TResult>(new[] { (TResult)item }));
		}
		#endregion
		
		#region Where
		public static IModelCollection<TSource> Where<TSource>(this IModelCollection<TSource> source, Func<TSource, bool> predicate)
		{
			return SelectMany(source, item => predicate(item) ? new ImmutableModelCollection<TSource>(new[] { item }) : ImmutableModelCollection<TSource>.Empty);
		}
		#endregion
		
		#region Select
		public static IModelCollection<TResult> Select<TSource, TResult>(this IModelCollection<TSource> source, Func<TSource, TResult> selector)
		{
			return SelectMany(source, item => new ImmutableModelCollection<TResult>(new[] { selector(item) }));
		}
		#endregion
		
		#region SelectMany
		public static IModelCollection<TResult> SelectMany<TSource, TResult>(this IModelCollection<TSource> input, Func<TSource, IModelCollection<TResult>> selector)
		{
			return SelectMany(input, selector, (a, b) => b);
		}
		
		public static IModelCollection<TResult> SelectMany<TSource, TCollection, TResult>(this IModelCollection<TSource> source, Func<TSource, IModelCollection<TCollection>> collectionSelector, Func<TSource, TCollection, TResult> resultSelector)
		{
			if (source == null)
				throw new ArgumentNullException("source");
			if (collectionSelector == null)
				throw new ArgumentNullException("collectionSelector");
			if (resultSelector == null)
				throw new ArgumentNullException("resultSelector");
			return new SelectManyModelCollection<TSource, TCollection, TResult>(source, collectionSelector, resultSelector);
		}
		
		sealed class SelectManyModelCollection<TSource, TCollection, TResult> : IModelCollection<TResult>
		{
			readonly IModelCollection<TSource> source;
			readonly Func<TSource, IModelCollection<TCollection>> collectionSelector;
			readonly Func<TSource, TCollection, TResult> resultSelector;
			List<InputCollection> inputCollections;
			
			public SelectManyModelCollection(IModelCollection<TSource> source, Func<TSource, IModelCollection<TCollection>> collectionSelector, Func<TSource, TCollection, TResult> resultSelector)
			{
				this.source = source;
				this.collectionSelector = collectionSelector;
				this.resultSelector = resultSelector;
			}
			
			ModelCollectionChangedEventHandler<TResult> collectionChanged;
			
			public event ModelCollectionChangedEventHandler<TResult> CollectionChanged {
				add {
					if (value == null)
						return;
					if (inputCollections == null) {
						source.CollectionChanged += OnSourceCollectionChanged;
						inputCollections = new List<InputCollection>();
						foreach (var item in source) {
							var inputCollection = new InputCollection(this, item);
							inputCollection.RegisterEvent();
							inputCollections.Add(inputCollection);
						}
					}
					collectionChanged += value;
				}
				remove {
					if (collectionChanged == null)
						return;
					collectionChanged -= value;
					if (collectionChanged == null) {
						source.CollectionChanged -= OnSourceCollectionChanged;
						foreach (var inputCollection in inputCollections) {
							inputCollection.UnregisterEvent();
						}
						inputCollections = null;
					}
				}
			}
			
			void OnCollectionChanged(IReadOnlyCollection<TResult> removedItems, IReadOnlyCollection<TResult> addedItems)
			{
				if (collectionChanged != null)
					collectionChanged(removedItems, addedItems);
			}
			
			void OnSourceCollectionChanged(IReadOnlyCollection<TSource> removedItems, IReadOnlyCollection<TSource> addedItems)
			{
				var removedResults = new ListWithReadOnlySupport<TResult>();
				foreach (TSource removedSource in removedItems) {
					for (int i = 0; i < inputCollections.Count; i++) {
						var inputCollection = inputCollections[i];
						if (EqualityComparer<TSource>.Default.Equals(inputCollection.source, removedSource)) {
							inputCollection.AddResultsToList(removedResults);
							inputCollection.UnregisterEvent();
							inputCollections.RemoveAt(i);
							break;
						}
					}
				}
				var addedResults = new ListWithReadOnlySupport<TResult>();
				foreach (TSource addedSource in addedItems) {
					var inputCollection = new InputCollection(this, addedSource);
					inputCollection.AddResultsToList(addedResults);
					inputCollection.RegisterEvent();
					inputCollections.Add(inputCollection);
				}
				OnCollectionChanged(removedResults, addedResults);
			}
			
			class InputCollection
			{
				readonly SelectManyModelCollection<TSource, TCollection, TResult> parent;
				internal readonly TSource source;
				readonly IModelCollection<TCollection> collection;
				
				public InputCollection(SelectManyModelCollection<TSource, TCollection, TResult> parent, TSource source)
				{
					this.parent = parent;
					this.source = source;
					this.collection = parent.collectionSelector(source);
				}
				
				public void AddResultsToList(List<TResult> output)
				{
					foreach (var item in collection) {
						output.Add(parent.resultSelector(source, item));
					}
				}
				
				public void RegisterEvent()
				{
					collection.CollectionChanged += OnCollectionChanged;
				}
				
				public void UnregisterEvent()
				{
					collection.CollectionChanged -= OnCollectionChanged;
				}
				
				IReadOnlyCollection<TResult> GetResults(IReadOnlyCollection<TCollection> itemsCollection)
				{
					var results = new ListWithReadOnlySupport<TResult>(itemsCollection.Count);
					foreach (var item in itemsCollection) {
						results.Add(parent.resultSelector(source, item));
					}
					return results;
				}
				
				void OnCollectionChanged(IReadOnlyCollection<TCollection> removedItems, IReadOnlyCollection<TCollection> addedItems)
				{
					parent.OnCollectionChanged(GetResults(removedItems), GetResults(addedItems));
				}
			}
			
			IReadOnlyCollection<TResult> IModelCollection<TResult>.CreateSnapshot()
			{
				return this.ToListWithReadOnlySupport();
			}
			
			public IEnumerator<TResult> GetEnumerator()
			{
				return source.AsEnumerable().SelectMany(collectionSelector, resultSelector).GetEnumerator();
			}
			
			System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
			{
				return GetEnumerator();
			}
			
			public int Count {
				get {
					return source.Sum(c => collectionSelector(c).Count);
				}
			}
		}
		#endregion
	}
}
