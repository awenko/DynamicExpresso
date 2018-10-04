using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace DynamicExpresso
{
	/// <summary>
	/// Represents a lambda expression that can be invoked. This class is thread safe.
	/// </summary>
	public class Lambda
	{
	    private readonly ParserArguments _parserArguments;

		private readonly Delegate _delegate;

		internal Lambda(Expression expression, ParserArguments parserArguments)
		{
			if (expression == null) throw new ArgumentNullException(nameof(expression));
			if (parserArguments == null) throw new ArgumentNullException(nameof(parserArguments));

			Expression = expression;
			_parserArguments = parserArguments;

			// Note: I always compile the generic lambda. Maybe in the future this can be a setting because if I generate a typed delegate this compilation is not required.
			var lambdaExpression = Expression.Lambda(Expression,
                _parserArguments.UsedParameters.Select(p => p.Expression).ToArray());

			_delegate = lambdaExpression.Compile();
		}

		public Expression Expression { get; }

	    public bool CaseInsensitive => _parserArguments.Settings.CaseInsensitive;

	    public string ExpressionText => _parserArguments.ExpressionText;

	    public Type ReturnType => _delegate.Method.ReturnType;

	    /// <summary>
		/// Gets the parameters actually used in the expression parsed.
		/// </summary>
		/// <value>The used parameters.</value>
		[Obsolete("Use UsedParameters or DeclaredParameters")]
		public IEnumerable<Parameter> Parameters => _parserArguments.UsedParameters;

	    /// <summary>
		/// Gets the parameters actually used in the expression parsed.
		/// </summary>
		/// <value>The used parameters.</value>
		public IEnumerable<Parameter> UsedParameters => _parserArguments.UsedParameters;

	    /// <summary>
		/// Gets the parameters declared when parsing the expression.
		/// </summary>
		/// <value>The declared parameters.</value>
		public IEnumerable<Parameter> DeclaredParameters => _parserArguments.DeclaredParameters;

	    public IEnumerable<ReferenceType> Types => _parserArguments.UsedTypes;

	    public IEnumerable<Identifier> Identifiers => _parserArguments.UsedIdentifiers;

	    public object Invoke()
		{
			return InvokeWithUsedParameters(new object[0]);
		}

		public object Invoke(params Parameter[] parameters)
		{
			return Invoke((IEnumerable<Parameter>)parameters);
		}

		public object Invoke(IEnumerable<Parameter> parameters)
		{
		    var parameterCollection = parameters as IReadOnlyCollection<Parameter> ?? parameters.ToList();

		    var args = (from usedParameter in UsedParameters
		        from actualParameter in parameterCollection
		        where usedParameter.Name.Equals(actualParameter.Name, _parserArguments.Settings.KeyComparison)
		        select actualParameter.Value)
		        .ToArray();

			return InvokeWithUsedParameters(args);
		}

		/// <summary>
		/// Invoke the expression with the given parameters values.
		/// </summary>
		/// <param name="args">Order of parameters must be the same of the parameters used during parse (DeclaredParameters).</param>
		/// <returns></returns>
		public object Invoke(params object[] args)
		{
		    if (args == null)
		    {
		        return Invoke(Enumerable.Empty<Parameter>());
            }

            var declaredParameters = DeclaredParameters.ToArray();

            if (declaredParameters.Length != args.Length)
		    {
		        throw new InvalidOperationException("Arguments count mismatch.");
            }

		    var parameters = args
		        .Select((t, i) => new Parameter(declaredParameters[i].Name, declaredParameters[i].Type, t));

		    return Invoke(parameters);
		}

		private object InvokeWithUsedParameters(object[] orderedArgs)
		{
			try
			{
				return _delegate.DynamicInvoke(orderedArgs);
			}
			catch (TargetInvocationException exc)
			{
			    if (exc.InnerException != null)
			    {
			        ExceptionDispatchInfo.Capture(exc.InnerException).Throw();
			    }

			    throw;
			}
		}

		public override string ToString()
		{
			return ExpressionText;
		}

		/// <summary>
		/// Generate the given delegate by compiling the lambda expression.
		/// </summary>
		/// <typeparam name="TDelegate">The delegate to generate. Delegate parameters must match the one defined when creating the expression, see UsedParameters.</typeparam>
		public TDelegate Compile<TDelegate>()
		{
			var lambdaExpression = LambdaExpression<TDelegate>();

			return lambdaExpression.Compile();
		}

		[Obsolete("Use Compile<TDelegate>()")]
		public TDelegate Compile<TDelegate>(IEnumerable<Parameter> parameters)
		{
			var lambdaExpression = Expression.Lambda<TDelegate>(Expression, parameters.Select(p => p.Expression).ToArray());

			return lambdaExpression.Compile();
		}

		/// <summary>
		/// Generate a lambda expression.
		/// </summary>
		/// <returns>The lambda expression.</returns>
		/// <typeparam name="TDelegate">The delegate to generate. Delegate parameters must match the one defined when creating the expression, see UsedParameters.</typeparam>
		public Expression<TDelegate> LambdaExpression<TDelegate>()
		{
			return Expression.Lambda<TDelegate>(Expression, DeclaredParameters.Select(p => p.Expression).ToArray());
		}
	}
}
