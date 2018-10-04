using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DynamicExpresso.Reflection
{
	internal static class ReflectionExtensions
	{
		public static DelegateInfo GetDelegateInfo(Type delegateType, params string[] parametersNames)
		{
			var method = delegateType.GetMethod("Invoke");

		    if (method == null)
		    {
		        throw new ArgumentException("The specified type is not a delegate");
		    }

		    var delegateParameters = method.GetParameters();
			var parameters = new Parameter[delegateParameters.Length];

			var useCustomNames = parametersNames != null && parametersNames.Length > 0;

		    if (useCustomNames && parametersNames.Length != parameters.Length)
		    {
		        throw new ArgumentException(
		            $"Provided parameters names doesn't match delegate parameters, {parameters.Length} parameters expected.");
		    }

		    for (var i = 0; i < parameters.Length; i++)
			{
				var paramName = useCustomNames ? parametersNames[i] : delegateParameters[i].Name;
				var paramType = delegateParameters[i].ParameterType;

				parameters[i] = new Parameter(paramName, paramType);
			}

			return new DelegateInfo(method.ReturnType, parameters);
		}

		public static IEnumerable<MethodInfo> GetExtensionMethods(Type type)
		{
			if (type.IsSealed && type.IsAbstract && !type.IsGenericType && !type.IsNested)
			{
			    return type
			        .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
			        .Where(method => method.IsDefined(typeof (System.Runtime.CompilerServices.ExtensionAttribute), false));
			}

			return Enumerable.Empty<MethodInfo>();
		}

		public class DelegateInfo
		{
			public DelegateInfo(Type returnType, Parameter[] parameters)
			{
				ReturnType = returnType;
				Parameters = parameters;
			}

			public Type ReturnType { get; private set; }

			public Parameter[] Parameters { get; private set; }
		}
	}
}
