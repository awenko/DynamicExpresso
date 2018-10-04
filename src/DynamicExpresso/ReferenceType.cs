using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DynamicExpresso.Reflection;
using JetBrains.Annotations;

namespace DynamicExpresso
{
	public class ReferenceType
	{
		public Type Type { get; private set; }

		/// <summary>
		/// Public name that must be used in the expression.
		/// </summary>
		public string Name { get; private set; }

		public IList<MethodInfo> ExtensionMethods { get; private set; }

		public ReferenceType([NotNull] string name, [NotNull] Type type)
		{
			if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
			if (type == null) throw new ArgumentNullException(nameof(type));

			Type = type;
			Name = name;
			ExtensionMethods = ReflectionExtensions.GetExtensionMethods(type).ToList();
		}

		public ReferenceType([NotNull] Type type)
		{
			if (type == null) throw new ArgumentNullException(nameof(type));

			Type = type;
			Name = type.Name;
			ExtensionMethods = ReflectionExtensions.GetExtensionMethods(type).ToList();
		}
	}
}