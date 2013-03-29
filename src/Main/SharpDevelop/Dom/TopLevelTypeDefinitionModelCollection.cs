﻿// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under the GNU LGPL (for details please see \doc\license.txt)

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using ICSharpCode.NRefactory;
using ICSharpCode.NRefactory.TypeSystem;

namespace ICSharpCode.SharpDevelop.Dom
{
	/// <summary>
	/// A TypeDefinitionModel-collection that holds models for all top-level types in a project content.
	/// </summary>
	sealed class TopLevelTypeDefinitionModelCollection : ITypeDefinitionModelCollection
	{
		readonly IEntityModelContext context;
		Dictionary<TopLevelTypeName, TypeDefinitionModel> dict = new Dictionary<TopLevelTypeName, TypeDefinitionModel>();
		
		public TopLevelTypeDefinitionModelCollection(IEntityModelContext context)
		{
			if (context == null)
				throw new ArgumentNullException("context");
			this.context = context;
		}
		
		public event ModelCollectionChangedEventHandler<ITypeDefinitionModel> CollectionChanged;
		
		public IReadOnlyCollection<ITypeDefinitionModel> CreateSnapshot()
		{
			return dict.Values.ToListWithReadOnlySupport();
		}
		
		public int Count {
			get { return dict.Count; }
		}
		
		public ITypeDefinitionModel this[TopLevelTypeName topLevelName] {
			get {
				TypeDefinitionModel model;
				if (dict.TryGetValue(topLevelName, out model))
					return model;
				else
					return null;
			}
		}
		
		public ITypeDefinitionModel this[FullTypeName fullTypeName] {
			get {
				ITypeDefinitionModel model = this[fullTypeName.TopLevelTypeName];
				for (int i = 0; i < fullTypeName.NestingLevel && model != null; i++) {
					string name = fullTypeName.GetNestedTypeName(i);
					int atpc = fullTypeName.GetNestedTypeAdditionalTypeParameterCount(i);
					model = model.GetNestedType(name, atpc);
				}
				return model;
			}
		}
		
		/// <summary>
		/// Updates the parse information.
		/// </summary>
		public void Update(IUnresolvedFile oldFile, IUnresolvedFile newFile)
		{
			ListWithReadOnlySupport<ITypeDefinitionModel> oldModels = null;
			ListWithReadOnlySupport<ITypeDefinitionModel> newModels = null;
			bool[] oldTypeDefHandled = null;
			if (oldFile != null) {
				oldTypeDefHandled = new bool[oldFile.TopLevelTypeDefinitions.Count];
			}
			if (newFile != null) {
				foreach (var newPart in newFile.TopLevelTypeDefinitions) {
					FullTypeName newFullTypeName = newPart.FullTypeName;
					TypeDefinitionModel model;
					if (dict.TryGetValue(newFullTypeName.TopLevelTypeName, out model)) {
						// Existing type changed
						// Find a matching old part:
						IUnresolvedTypeDefinition oldPart = null;
						if (oldFile != null) {
							for (int i = 0; i < oldTypeDefHandled.Length; i++) {
								if (oldTypeDefHandled[i])
									continue;
								if (oldFile.TopLevelTypeDefinitions[i].FullTypeName == newFullTypeName) {
									oldTypeDefHandled[i] = true;
									oldPart = oldFile.TopLevelTypeDefinitions[i];
									break;
								}
							}
						}
						model.Update(oldPart, newPart);
					} else {
						// New type added
						model = new TypeDefinitionModel(context, newPart);
						dict.Add(newFullTypeName.TopLevelTypeName, model);
						if (newModels == null)
							newModels = new ListWithReadOnlySupport<ITypeDefinitionModel>();
						newModels.Add(model);
					}
				}
			}
			// Remove all old parts that weren't updated:
			if (oldFile != null) {
				for (int i = 0; i < oldTypeDefHandled.Length; i++) {
					if (!oldTypeDefHandled[i]) {
						IUnresolvedTypeDefinition oldPart = oldFile.TopLevelTypeDefinitions[i];
						TopLevelTypeName topLevelTypeName = oldPart.FullTypeName.TopLevelTypeName;
						TypeDefinitionModel model;
						if (dict.TryGetValue(topLevelTypeName, out model)) {
							// Remove the part from the model
							if (model.Parts.Count > 1) {
								model.Update(oldPart, null);
							} else {
								dict.Remove(topLevelTypeName);
								if (oldModels == null)
									oldModels = new ListWithReadOnlySupport<ITypeDefinitionModel>();
								oldModels.Add(model);
							}
						}
					}
				}
			}
			// Raise the event if necessary:
			if (CollectionChanged != null && (oldModels != null || newModels != null)) {
				IReadOnlyCollection<ITypeDefinitionModel> emptyList = EmptyList<ITypeDefinitionModel>.Instance;
				CollectionChanged(oldModels ?? emptyList, newModels ?? emptyList);
			}
		}
		
		IEnumerator<ITypeDefinitionModel> IEnumerable<ITypeDefinitionModel>.GetEnumerator()
		{
			return dict.Values.GetEnumerator();
		}
		
		IEnumerator IEnumerable.GetEnumerator()
		{
			return dict.Values.GetEnumerator();
		}
	}
}
