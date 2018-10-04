using System;
using System.Linq.Expressions;
using JetBrains.Annotations;

namespace DynamicExpresso
{
	/// <summary>
	/// An expression parameter. This class is thread safe.
	/// </summary>
	public class Parameter
	{
		public Parameter(string name, [NotNull] object value)
		{
			if (value == null) throw new ArgumentNullException(nameof(value));

			Name = name;
			Type = value.GetType();
			Value = value;

			Expression = System.Linq.Expressions.Expression.Parameter(Type, name);
		}

		public Parameter(string name, Type type, object value = null)
		{
			Name = name;
			Type = type;
			Value = value;

			Expression = System.Linq.Expressions.Expression.Parameter(type, name);
		}

		public string Name { get; private set; }

		public Type Type { get; }

		public object Value { get; private set; }

		public ParameterExpression Expression { get; private set; }
	}
}