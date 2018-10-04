using System;
using System.Linq.Expressions;
using JetBrains.Annotations;

namespace DynamicExpresso
{
	public class Identifier
	{
		public Expression Expression { get; private set; }
		public string Name { get; private set; }

		public Identifier([NotNull] string name, [NotNull] Expression expression)
		{
			if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
			if (expression == null) throw new ArgumentNullException(nameof(expression));

			Expression = expression;
			Name = name;
		}
	}
}