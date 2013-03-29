// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ICSharpCode.SharpDevelop;

namespace ICSharpCode.SharpDevelop
{
	public interface IReadOnlyCollection<out T> : IEnumerable<T>
	{
		int Count { get; }
	}
	
	public interface IReadOnlyList<out T> : IReadOnlyCollection<T>
	{
		T this[int index] { get; }
	}
	
	public interface IReadOnlyDictionary<TKey, TValue> : IReadOnlyCollection<KeyValuePair<TKey, TValue>>, IEnumerable<KeyValuePair<TKey, TValue>>, IEnumerable
	{
		TValue this[TKey key] { get; }
		IEnumerable<TKey> Keys { get; }
		IEnumerable<TValue> Values { get; }
		bool ContainsKey(TKey key);
		bool TryGetValue(TKey key, out TValue value);
	}
	
	public class ListWithReadOnlySupport<T> : List<T>, IReadOnlyList<T>
	{
		public ListWithReadOnlySupport() {}
		public ListWithReadOnlySupport(int capacity) : base(capacity) {}
		public ListWithReadOnlySupport(IEnumerable<T> collection) : base(collection) {}
	}
	
	public class ObservableCollectionWithReadOnlySupport<T> : ObservableCollection<T>, IReadOnlyList<T>
	{
	}
	
	sealed class EmptyList<T>
	{
		public static readonly ReadOnlyListWrapper<T> Instance = new T[0].AsReadOnly();
	}
	
	public class ReadOnlyListWrapper<T> : ReadOnlyCollection<T>, IReadOnlyList<T>
	{
		public ReadOnlyListWrapper(IList<T> list) : base(list) {}
	}
	
	public class ReadOnlyDictionaryWrapper<TKey, TValue> : ReadOnlyCollectionWrapper<KeyValuePair<TKey, TValue>>, IReadOnlyDictionary<TKey, TValue>
	{
		readonly IDictionary<TKey, TValue> dict;
		
		public ReadOnlyDictionaryWrapper(IDictionary<TKey, TValue> dict) : base(dict)
		{
			this.dict = dict;
		}
		
		#region IReadOnlyDictionary implementation

		public bool ContainsKey(TKey key)
		{
			return dict.ContainsKey(key);
		}

		public bool TryGetValue(TKey key, out TValue value)
		{
			return dict.TryGetValue(key, out value);
		}

		public TValue this[TKey key] {
			get { return dict[key]; }
		}

		public IEnumerable<TKey> Keys {
			get { return dict.Keys; }
		}

		public IEnumerable<TValue> Values {
			get { return dict.Values; }
		}

		#endregion
	}
	
	/// <summary>
	/// Wraps any collection to make it read-only.
	/// </summary>
	public class ReadOnlyCollectionWrapper<T> : ICollection<T>, IReadOnlyCollection<T>
	{
		readonly ICollection<T> c;
		
		public ReadOnlyCollectionWrapper(ICollection<T> c)
		{
			if (c == null)
				throw new ArgumentNullException("c");
			this.c = c;
		}
		
		public int Count {
			get {
				return c.Count;
			}
		}
		
		public bool IsReadOnly {
			get {
				return true;
			}
		}
		
		void ICollection<T>.Add(T item)
		{
			throw new NotSupportedException();
		}
		
		void ICollection<T>.Clear()
		{
			throw new NotSupportedException();
		}
		
		public bool Contains(T item)
		{
			return c.Contains(item);
		}
		
		public void CopyTo(T[] array, int arrayIndex)
		{
			c.CopyTo(array, arrayIndex);
		}
		
		bool ICollection<T>.Remove(T item)
		{
			throw new NotSupportedException();
		}
		
		public IEnumerator<T> GetEnumerator()
		{
			return c.GetEnumerator();
		}
		
		IEnumerator IEnumerable.GetEnumerator()
		{
			return ((IEnumerable)c).GetEnumerator();
		}
	}
}
