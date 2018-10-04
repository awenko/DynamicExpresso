using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using DynamicExpresso.Exceptions;
using DynamicExpresso.Resources;

namespace DynamicExpresso.Parsing
{
	internal class Parser
	{
		public static Expression Parse(ParserArguments arguments)
		{
			return new Parser(arguments).Parse();
		}

		private const NumberStyles ParseLiteralNumberStyle = NumberStyles.AllowLeadingSign;
		private const NumberStyles ParseLiteralUnsignedNumberStyle = NumberStyles.AllowLeadingSign;
		private const NumberStyles ParseLiteralDecimalNumberStyle = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
		private static readonly CultureInfo ParseCulture = CultureInfo.InvariantCulture;

		private readonly ParserArguments _arguments;

		private int _parsePosition;
		private readonly string _expressionText;
		private readonly int _expressionTextLength;
		private char _parseChar;
		private Token _token;

		private readonly BindingFlags _bindingCase;
		private readonly MemberFilter _memberFilterCase;

		private Parser(ParserArguments arguments)
		{
			_arguments = arguments;

			_bindingCase = arguments.Settings.CaseInsensitive ? BindingFlags.IgnoreCase : BindingFlags.Default;
			_memberFilterCase = arguments.Settings.CaseInsensitive ? Type.FilterNameIgnoreCase : Type.FilterName;

			_expressionText = arguments.ExpressionText ?? string.Empty;
			_expressionTextLength = _expressionText.Length;

			SetTextPos(0);
			NextToken();
		}

		private Expression Parse()
		{
			var expr = ParseExpressionSegment(_arguments.ExpressionReturnType);

			ValidateToken(TokenId.End, ErrorMessages.SyntaxError);

			return expr;
		}

		private Expression ParseExpressionSegment(Type returnType)
		{
			var errorPos = _token.Pos;
			var expression = ParseExpressionSegment();

		    return returnType != typeof (void) ? GenerateConversion(expression, returnType, errorPos) : expression;
		}

		private Expression ParseExpressionSegment()
		{
			return ParseAssignement();
		}

		private Expression ParseAssignement()
		{
			var left = ParseConditional();

		    if (_token.ID != TokenId.Equal)
		    {
		        return left;
		    }

		    if (!_arguments.Settings.AssignmentOperators.HasFlag(AssignmentOperators.AssignmentEqual))
		    {
		        throw new AssignmentOperatorDisabledException("=", _token.Pos);
		    }

		    NextToken();

		    var right = ParseAssignement();

            CheckAndPromoteOperands(typeof(ParseSignatures.IEqualitySignatures), ref left, ref right);

            return Expression.Assign(left, right);
		}

		private Expression ParseConditional()
		{
			var errorPos = _token.Pos;
			var expr = ParseLogicalOr();

		    if (_token.ID != TokenId.Question)
		    {
		        return expr;
		    }

		    NextToken();

            var expr1 = ParseExpressionSegment();

            ValidateToken(TokenId.Colon, ErrorMessages.ColonExpected);
		    NextToken();

            var expr2 = ParseExpressionSegment();

            return GenerateConditional(expr, expr1, expr2, errorPos);
		}

		private Expression ParseLogicalOr()
		{
			var left = ParseLogicalAnd();

			while (_token.ID == TokenId.DoubleBar)
			{
				NextToken();

				var right = ParseLogicalAnd();

                CheckAndPromoteOperands(typeof(ParseSignatures.ILogicalSignatures), ref left, ref right);

                left = Expression.OrElse(left, right);
			}

			return left;
		}

		private Expression ParseLogicalAnd()
		{
			var left = ParseComparison();

            while (_token.ID == TokenId.DoubleAmphersand)
			{
				NextToken();

                var right = ParseComparison();

                CheckAndPromoteOperands(typeof(ParseSignatures.ILogicalSignatures), ref left, ref right);

                left = Expression.AndAlso(left, right);
			}

            return left;
		}

		private Expression ParseComparison()
		{
			var left = ParseTypeTesting();

            while (_token.ID == TokenId.DoubleEqual || _token.ID == TokenId.ExclamationEqual ||
					_token.ID == TokenId.GreaterThan || _token.ID == TokenId.GreaterThanEqual ||
					_token.ID == TokenId.LessThan || _token.ID == TokenId.LessThanEqual)
			{
				var op = _token;

                NextToken();

                var right = ParseAdditive();
				var isEquality = op.ID == TokenId.DoubleEqual || op.ID == TokenId.ExclamationEqual;

				CheckAndPromoteOperands(
					isEquality ? typeof(ParseSignatures.IEqualitySignatures) : typeof(ParseSignatures.IRelationalSignatures),
					ref left,
					ref right);

				switch (op.ID)
				{
					case TokenId.DoubleEqual:
						left = GenerateEqual(left, right);
						break;
					case TokenId.ExclamationEqual:
						left = GenerateNotEqual(left, right);
						break;
					case TokenId.GreaterThan:
						left = GenerateGreaterThan(left, right);
						break;
					case TokenId.GreaterThanEqual:
						left = GenerateGreaterThanEqual(left, right);
						break;
					case TokenId.LessThan:
						left = GenerateLessThan(left, right);
						break;
					case TokenId.LessThanEqual:
						left = GenerateLessThanEqual(left, right);
						break;
				}
			}

			return left;
		}

		private Expression ParseTypeTesting()
		{
			var left = ParseAdditive();

			while (_token.Text == ParserConstants.KeywordIs || _token.Text == ParserConstants.KeywordAs)
			{
				var typeOperator = _token.Text;

				var op = _token;

				NextToken();

				Type knownType;

			    if (!_arguments.TryGetKnownType(_token.Text, out knownType))
                {
                    throw CreateParseException(op.Pos, ErrorMessages.TypeIdentifierExpected);
                }

			    switch (typeOperator)
			    {
			        case ParserConstants.KeywordIs:
			            left = Expression.TypeIs(left, knownType);
			            break;
			        case ParserConstants.KeywordAs:
			            left = Expression.TypeAs(left, knownType);
			            break;
			        default:
			            throw CreateParseException(_token.Pos, ErrorMessages.SyntaxError);
			    }

			    NextToken();
			}

			return left;
		}

		private Expression ParseAdditive()
		{
			var left = ParseMultiplicative();

            while (_token.ID == TokenId.Plus || _token.ID == TokenId.Minus)
			{
				var op = _token;

                NextToken();

                var right = ParseMultiplicative();

                switch (op.ID)
				{
					case TokenId.Plus:
						if (left.Type == typeof(string) || right.Type == typeof(string))
						{
							left = GenerateStringConcat(left, right);
						}
						else
						{
							CheckAndPromoteOperands(typeof(ParseSignatures.IAddSignatures), ref left, ref right);
							left = GenerateAdd(left, right);
						}
						break;
					case TokenId.Minus:
						CheckAndPromoteOperands(typeof(ParseSignatures.ISubtractSignatures), ref left, ref right);
						left = GenerateSubtract(left, right);
						break;
				}
			}

			return left;
		}

		private Expression ParseMultiplicative()
		{
			var left = ParseUnary();

            while (_token.ID == TokenId.Asterisk || _token.ID == TokenId.Slash || _token.ID == TokenId.Percent)
			{
				var op = _token;

				NextToken();

                var right = ParseUnary();

				CheckAndPromoteOperands(typeof(ParseSignatures.IArithmeticSignatures), ref left, ref right);

				switch (op.ID)
				{
					case TokenId.Asterisk:
						left = Expression.Multiply(left, right);
						break;
					case TokenId.Slash:
						left = Expression.Divide(left, right);
						break;
					case TokenId.Percent:
						left = Expression.Modulo(left, right);
						break;
				}
			}

			return left;
		}

		private Expression ParseUnary()
		{
			if (_token.ID == TokenId.Minus || _token.ID == TokenId.Exclamation || _token.ID == TokenId.Plus)
			{
				var op = _token;

				NextToken();

				if (_token.ID == TokenId.IntegerLiteral || _token.ID == TokenId.RealLiteral)
				{
				    switch (op.ID)
				    {
				        case TokenId.Minus:
				            _token.Text = "-" + _token.Text;
				            _token.Pos = op.Pos;

				            return ParsePrimary();
				        case TokenId.Plus:
				            _token.Text = "+" + _token.Text;
				            _token.Pos = op.Pos;

				            return ParsePrimary();
				    }
				}

			    var expr = ParseUnary();

                switch (op.ID)
                {
                    case TokenId.Minus:
                        CheckAndPromoteOperand(typeof(ParseSignatures.INegationSignatures), ref expr);
                        expr = Expression.Negate(expr);
                        break;
                    case TokenId.Plus:
                        break;
                    case TokenId.Exclamation:
                        CheckAndPromoteOperand(typeof(ParseSignatures.INotSignatures), ref expr);
                        expr = Expression.Not(expr);
                        break;
                }

				return expr;
			}

			return ParsePrimary();
		}

		private Expression ParsePrimary()
		{
			var tokenPos = _token.Pos;
			var expr = ParsePrimaryStart();

			while (true)
			{
				if (_token.ID == TokenId.Dot)
				{
					NextToken();
					expr = ParseMemberAccess(null, expr);
				}
				else if (_token.ID == TokenId.OpenBracket)
				{
					expr = ParseElementAccess(expr);
				}
				else if (_token.ID == TokenId.OpenParen)
				{
					var lambda = expr as LambdaExpression;

				    if (lambda != null)
				    {
				        return ParseLambdaInvocation(lambda, tokenPos);
				    }

				    if (typeof (Delegate).IsAssignableFrom(expr.Type))
				    {
				        expr = ParseDelegateInvocation(expr, tokenPos);
				    }
				    else
				    {
				        throw CreateParseException(tokenPos, ErrorMessages.InvalidMethodCall, GetTypeName(expr.Type));
				    }
				}
				else
				{
					break;
				}
			}

			return expr;
		}

		private Expression ParsePrimaryStart()
		{
			switch (_token.ID)
			{
				case TokenId.Identifier:
					return ParseIdentifier();
				case TokenId.CharLiteral:
					return ParseCharLiteral();
				case TokenId.StringLiteral:
					return ParseStringLiteral();
				case TokenId.IntegerLiteral:
					return ParseIntegerLiteral();
				case TokenId.RealLiteral:
					return ParseRealLiteral();
				case TokenId.OpenParen:
					return ParseParenExpression();
				case TokenId.End:
					return Expression.Empty();
				default:
					throw CreateParseException(_token.Pos, ErrorMessages.ExpressionExpected);
			}
		}

		private Expression ParseCharLiteral()
		{
			ValidateToken(TokenId.CharLiteral);

			var s = _token.Text.Substring(1, _token.Text.Length - 2);

			s = EvalEscapeStringLiteral(s);

		    if (s.Length != 1)
		    {
		        throw CreateParseException(_token.Pos, ErrorMessages.InvalidCharacterLiteral);
		    }

		    NextToken();

            return CreateLiteral(s[0]);
		}

		private Expression ParseStringLiteral()
		{
			ValidateToken(TokenId.StringLiteral);

			var s = _token.Text.Substring(1, _token.Text.Length - 2);

			s = EvalEscapeStringLiteral(s);

			NextToken();

			return CreateLiteral(s);
		}

		private string EvalEscapeStringLiteral(string source)
		{
			var builder = new StringBuilder();

			for (var i = 0; i < source.Length; i++)
			{
				var c = source[i];

			    if (c == '\\')
			    {
			        if (i + 1 == source.Length)
			        {
			            throw CreateParseException(_token.Pos, ErrorMessages.InvalidEscapeSequence);
			        }

			        builder.Append(EvalEscapeChar(source[++i]));
			    }
			    else
			    {
			        builder.Append(c);
			    }
			}

			return builder.ToString();
		}

		private char EvalEscapeChar(char source)
		{
			switch (source)
			{
				case '\'':
					return '\'';
				case '"':
					return '"';
				case '\\':
					return '\\';
				case '0':
					return '\0';
				case 'a':
					return '\a';
				case 'b':
					return '\b';
				case 'f':
					return '\f';
				case 'n':
					return '\n';
				case 'r':
					return '\r';
				case 't':
					return '\t';
				case 'v':
					return '\v';
				default:
					throw CreateParseException(_token.Pos, ErrorMessages.InvalidEscapeSequence);
			}
		}

		private Expression ParseIntegerLiteral()
		{
			ValidateToken(TokenId.IntegerLiteral);

			var text = _token.Text;

		    if (text[0] != '-')
			{
			    ulong ulValue;

			    if (!ulong.TryParse(text, ParseLiteralUnsignedNumberStyle, ParseCulture, out ulValue))
			    {
			        throw CreateParseException(_token.Pos, ErrorMessages.InvalidIntegerLiteral, text);
			    }

			    NextToken();

			    if (ulValue <= int.MaxValue)
			    {
			        return CreateLiteral((int) ulValue);
			    }

			    if (ulValue <= uint.MaxValue)
			    {
			        return CreateLiteral((uint) ulValue);
			    }

			    if (ulValue <= long.MaxValue)
			    {
			        return CreateLiteral((long) ulValue);
			    }

			    return CreateLiteral(ulValue);
			}

            long lValue;

		    if (!long.TryParse(text, ParseLiteralNumberStyle, ParseCulture, out lValue))
		    {
		        throw CreateParseException(_token.Pos, ErrorMessages.InvalidIntegerLiteral, text);
		    }

		    NextToken();

		    if (lValue >= int.MinValue && lValue <= int.MaxValue)
		    {
		        return CreateLiteral((int) lValue);
		    }

		    return CreateLiteral(lValue);
		}

		private Expression ParseRealLiteral()
		{
			ValidateToken(TokenId.RealLiteral);

			var text = _token.Text;
			object value = null;
			var last = text[text.Length - 1];

		    switch (last)
		    {
		        case 'F':
		        case 'f':
		            float fValue;

		            if (float.TryParse(text.Substring(0, text.Length - 1), ParseLiteralDecimalNumberStyle, ParseCulture, out fValue))
		            {
		                value = fValue;
		            }
		            break;
		        case 'M':
		        case 'm':
		            decimal dcValue;

		            if (decimal.TryParse(text.Substring(0, text.Length - 1), ParseLiteralDecimalNumberStyle, ParseCulture, out dcValue))
		            {
		                value = dcValue;
		            }
		            break;
		        default:
		            double dValue;

		            if (double.TryParse(text, ParseLiteralDecimalNumberStyle, ParseCulture, out dValue))
		            {
		                value = dValue;
		            }
		            break;
		    }

		    if (value == null)
		    {
		        throw CreateParseException(_token.Pos, ErrorMessages.InvalidRealLiteral, text);
		    }

		    NextToken();

			return CreateLiteral(value);
		}

		private static Expression CreateLiteral(object value)
		{
			return Expression.Constant(value);
		}

		private Expression ParseParenExpression()
		{
			ValidateToken(TokenId.OpenParen, ErrorMessages.OpenParenExpected);
			NextToken();

			var innerParenthesesExpression = ParseExpressionSegment();

            ValidateToken(TokenId.CloseParen, ErrorMessages.CloseParenOrOperatorExpected);

            var constExp = innerParenthesesExpression as ConstantExpression;

			if (constExp?.Value is Type)
			{
				NextToken();

				var nextExpression = ParseExpressionSegment();

				return Expression.Convert(nextExpression, (Type)constExp.Value);
			}

			NextToken();

			return innerParenthesesExpression;
		}

		private Expression ParseIdentifier()
		{
			ValidateToken(TokenId.Identifier);

		    switch (_token.Text)
		    {
		        case ParserConstants.KeywordNew:
		            return ParseNew();
		        case ParserConstants.KeywordTypeof:
		            return ParseTypeof();
		    }

		    Expression keywordExpression;

            if (_arguments.TryGetIdentifier(_token.Text, out keywordExpression))
			{
				NextToken();

				return keywordExpression;
			}

		    ParameterExpression parameterExpression;

            if (_arguments.TryGetParameters(_token.Text, out parameterExpression))
			{
				NextToken();
				return parameterExpression;
			}

		    Type knownType;

            if (_arguments.TryGetKnownType(_token.Text, out knownType))
			{
				return ParseTypeKeyword(knownType);
			}

			throw new UnknownIdentifierException(_token.Text, _token.Pos);
		}

		private Expression ParseTypeof()
		{
			var errorPos = _token.Pos;

			NextToken();

			var args = ParseArgumentList();

		    if (args.Length != 1)
		    {
		        throw CreateParseException(errorPos, ErrorMessages.TypeofRequiresOneArg);
		    }

		    var constExp = args[0] as ConstantExpression;

		    if (!(constExp?.Value is Type))
		    {
		        throw CreateParseException(errorPos, ErrorMessages.TypeofRequiresAType);
		    }

		    return constExp;
		}

		private static Expression GenerateConditional(Expression test, Expression expr1, Expression expr2, int errorPos)
		{
		    if (test.Type != typeof (bool))
		    {
		        throw CreateParseException(errorPos, ErrorMessages.FirstExprMustBeBool);
		    }

		    if (expr1.Type == expr2.Type)
		    {
		        return Expression.Condition(test, expr1, expr2);
		    }

		    var expr1As2 = expr2 != ParserConstants.NullLiteralExpression ? PromoteExpression(expr1, expr2.Type, true) : null;
		    var expr2As1 = expr1 != ParserConstants.NullLiteralExpression ? PromoteExpression(expr2, expr1.Type, true) : null;

            if (expr1As2 != null && expr2As1 == null)
		    {
		        expr1 = expr1As2;
		    }
		    else if (expr2As1 != null && expr1As2 == null)
		    {
		        expr2 = expr2As1;
		    }
		    else
		    {
		        var type1 = expr1 != ParserConstants.NullLiteralExpression ? expr1.Type.Name : "null";
		        var type2 = expr2 != ParserConstants.NullLiteralExpression ? expr2.Type.Name : "null";

		        if (expr1As2 != null)
		        {
		            throw CreateParseException(errorPos, ErrorMessages.BothTypesConvertToOther, type1, type2);
		        }

		        throw CreateParseException(errorPos, ErrorMessages.NeitherTypeConvertsToOther, type1, type2);
		    }

		    return Expression.Condition(test, expr1, expr2);
		}

		private Expression ParseNew()
		{
			NextToken();
			ValidateToken(TokenId.Identifier, ErrorMessages.IdentifierExpected);

			Type newType;

            if (!_arguments.TryGetKnownType(_token.Text, out newType))
		    {
		        throw new UnknownIdentifierException(_token.Text, _token.Pos);
		    }

		    NextToken();

            var args = ParseArgumentList();
			var constructor = newType.GetConstructor(args.Select(p => p.Type).ToArray());

		    if (constructor == null)
		    {
		        throw CreateParseException(_token.Pos, ErrorMessages.NoApplicableConstructor, newType);
		    }

		    return Expression.MemberInit(Expression.New(constructor, args));
		}

		private Expression ParseLambdaInvocation(LambdaExpression lambda, int errorPos)
		{
			var args = ParseArgumentList();

		    if (!PrepareDelegateInvoke(lambda.Type, ref args))
		    {
		        throw CreateParseException(errorPos, ErrorMessages.ArgsIncompatibleWithLambda);
		    }

		    return Expression.Invoke(lambda, args);
		}

		private Expression ParseDelegateInvocation(Expression delegateExp, int errorPos)
		{
			var args = ParseArgumentList();

		    if (!PrepareDelegateInvoke(delegateExp.Type, ref args))
		    {
		        throw CreateParseException(errorPos, ErrorMessages.ArgsIncompatibleWithDelegate);
		    }

		    return Expression.Invoke(delegateExp, args);
		}

		private bool PrepareDelegateInvoke(Type type, ref Expression[] args)
		{
			var applicableMethods = FindMethods(type, "Invoke", false, args);

		    if (applicableMethods.Length != 1)
		    {
		        return false;
		    }

		    args = applicableMethods[0].PromotedParameters;

			return true;
		}

		private Expression ParseTypeKeyword(Type type)
		{
			var errorPos = _token.Pos;

            NextToken();

            if (_token.ID == TokenId.Question)
			{
			    if (!type.IsValueType || IsNullableType(type))
			    {
			        throw CreateParseException(errorPos, ErrorMessages.TypeHasNoNullableForm, GetTypeName(type));
			    }

			    type = typeof(Nullable<>).MakeGenericType(type);

                NextToken();
			}

			if (_token.ID == TokenId.CloseParen)
			{
				return Expression.Constant(type);
			}

			ValidateToken(TokenId.Dot, ErrorMessages.DotOrOpenParenExpected);
			NextToken();

            return ParseMemberAccess(type, null);
		}

		private static Expression GenerateConversion(Expression expr, Type type, int errorPos)
		{
			var exprType = expr.Type;

            if (exprType == type)
			{
				return expr;
			}

			try
			{
				return Expression.ConvertChecked(expr, type);
			}
			catch (InvalidOperationException)
			{
				throw CreateParseException(errorPos, ErrorMessages.CannotConvertValue, GetTypeName(exprType), GetTypeName(type));
			}
		}

		private Expression ParseMemberAccess(Type type, Expression instance)
		{
		    if (instance != null)
		    {
		        type = instance.Type;
		    }

			var errorPos = _token.Pos;
			var id = GetIdentifier();

            NextToken();

		    return _token.ID == TokenId.OpenParen
		        ? ParseMethodInvocation(type, instance, errorPos, id)
		        : GeneratePropertyOrFieldExpression(type, instance, errorPos, id);
		}

		private Expression GeneratePropertyOrFieldExpression(Type type, Expression instance, int errorPos, string propertyOrFieldName)
		{
			var member = FindPropertyOrField(type, propertyOrFieldName, instance == null);

			if (member != null)
			{
			    return member is PropertyInfo
			        ? Expression.Property(instance, (PropertyInfo) member)
			        : Expression.Field(instance, (FieldInfo) member);
			}

		    if (IsDynamicType(type) || IsDynamicExpression(instance))
		    {
		        return ParseDynamicProperty(type, instance, propertyOrFieldName);
		    }

		    throw CreateParseException(errorPos, ErrorMessages.UnknownPropertyOrField, propertyOrFieldName, GetTypeName(type));
		}

		private Expression ParseMethodInvocation(Type type, Expression instance, int errorPos, string methodName)
		{
			var args = ParseArgumentList();
			var methodInvocationExpression = ParseNormalMethodInvocation(type, instance, errorPos, methodName, args);

            if (methodInvocationExpression == null && instance != null)
			{
				methodInvocationExpression = ParseExtensionMethodInvocation(type, instance, errorPos, methodName, args);
			}

		    if (methodInvocationExpression != null)
		    {
		        return methodInvocationExpression;
		    }

		    if (IsDynamicType(type) || IsDynamicExpression(instance))
		    {
		        return ParseDynamicMethodInvocation(type, instance, methodName, args);
		    }

		    throw new NoApplicableMethodException(methodName, GetTypeName(type), errorPos);
		}

		private Expression ParseExtensionMethodInvocation(Type type, Expression instance, int errorPos, string id, Expression[] args)
		{
			var extensionMethodsArguments = new Expression[args.Length + 1];

            extensionMethodsArguments[0] = instance;

            args.CopyTo(extensionMethodsArguments, 1);

			var extensionMethods = FindExtensionMethods(id, extensionMethodsArguments);

		    if (extensionMethods.Length > 1)
		    {
		        throw CreateParseException(errorPos, ErrorMessages.AmbiguousMethodInvocation, id, GetTypeName(type));
		    }

		    if (extensionMethods.Length != 1)
		    {
		        return null;
		    }

		    var method = extensionMethods[0];

		    extensionMethodsArguments = method.PromotedParameters;

		    return Expression.Call((MethodInfo)method.MethodBase, extensionMethodsArguments);
		}

		private Expression ParseNormalMethodInvocation(Type type, Expression instance, int errorPos, string id, Expression[] args)
		{
			var applicableMethods = FindMethods(type, id, instance == null, args);

		    if (applicableMethods.Length > 1)
		    {
		        throw CreateParseException(errorPos, ErrorMessages.AmbiguousMethodInvocation, id, GetTypeName(type));
		    }

		    if (applicableMethods.Length != 1)
		    {
		        return null;
		    }

		    var method = applicableMethods[0];

		    return Expression.Call(instance, (MethodInfo)method.MethodBase, method.PromotedParameters);
		}

		private static Expression ParseDynamicProperty(Type type, Expression instance, string propertyOrFieldName)
		{
		    var binder = Microsoft.CSharp.RuntimeBinder.Binder.GetMember(
		        Microsoft.CSharp.RuntimeBinder.CSharpBinderFlags.None,
		        propertyOrFieldName,
		        type,
		        new[]
		        {
		            Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo.Create(Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfoFlags.None, null)
		        });

			return Expression.Dynamic(binder, typeof(object), instance);
		}

		private static Expression ParseDynamicMethodInvocation(Type type, Expression instance, string methodName, Expression[] args)
		{
			var argsDynamic = args.ToList();

			argsDynamic.Insert(0, instance);

		    var binderM = Microsoft.CSharp.RuntimeBinder.Binder.InvokeMember(
		        Microsoft.CSharp.RuntimeBinder.CSharpBinderFlags.None,
		        methodName,
		        null,
		        type,
		        argsDynamic.Select(x => Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo.Create(Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfoFlags.None, null)));

			return Expression.Dynamic(binderM, typeof(object), argsDynamic);
		}

		private Expression[] ParseArgumentList()
		{
			ValidateToken(TokenId.OpenParen, ErrorMessages.OpenParenExpected);
			NextToken();

			var args = _token.ID != TokenId.CloseParen ? ParseArguments() : new Expression[0];

            ValidateToken(TokenId.CloseParen, ErrorMessages.CloseParenOrCommaExpected);
			NextToken();

            return args;
		}

		private Expression[] ParseArguments()
		{
			var argList = new List<Expression>();

            while (true)
			{
				argList.Add(ParseExpressionSegment());

                if (_token.ID != TokenId.Comma)
			    {
			        break;
			    }

				NextToken();
			}

            return argList.ToArray();
		}

		private Expression ParseElementAccess(Expression expr)
		{
			var errorPos = _token.Pos;

			ValidateToken(TokenId.OpenBracket, ErrorMessages.OpenParenExpected);
			NextToken();

			var args = ParseArguments();

            ValidateToken(TokenId.CloseBracket, ErrorMessages.CloseBracketOrCommaExpected);
            NextToken();

            if (expr.Type.IsArray)
			{
			    if (expr.Type.GetArrayRank() != 1 || args.Length != 1)
			    {
			        throw CreateParseException(errorPos, ErrorMessages.CannotIndexMultiDimArray);
			    }

			    var index = PromoteExpression(args[0], typeof(int), true);

			    if (index == null)
			    {
			        throw CreateParseException(errorPos, ErrorMessages.InvalidIndex);
			    }

			    return Expression.ArrayIndex(expr, index);
			}

			var applicableMethods = FindIndexer(expr.Type, args);

            if (applicableMethods.Length == 0)
			{
				throw CreateParseException(errorPos, ErrorMessages.NoApplicableIndexer, GetTypeName(expr.Type));
			}

			if (applicableMethods.Length > 1)
			{
				throw CreateParseException(errorPos, ErrorMessages.AmbiguousIndexerInvocation, GetTypeName(expr.Type));
			}

			var method = applicableMethods[0];

			return Expression.Call(expr, (MethodInfo)method.MethodBase, method.PromotedParameters);
		}

		private static bool IsNullableType(Type type)
		{
			return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
		}

		private static bool IsDynamicType(Type type)
		{
			return typeof(IDynamicMetaObjectProvider).IsAssignableFrom(type);
		}

		private static bool IsDynamicExpression(Expression instance)
		{
			return instance != null && instance.NodeType == ExpressionType.Dynamic;
		}

		private static Type GetNonNullableType(Type type)
		{
			return IsNullableType(type) ? type.GetGenericArguments()[0] : type;
		}

		private static string GetTypeName(Type type)
		{
			var baseType = GetNonNullableType(type);
			var s = baseType.Name;

		    if (type != baseType)
		    {
		        s += '?';
		    }

            return s;
		}

		private static bool IsSignedIntegralType(Type type)
		{
			return GetNumericTypeKind(type) == 2;
		}

		private static bool IsUnsignedIntegralType(Type type)
		{
			return GetNumericTypeKind(type) == 3;
		}

		private static int GetNumericTypeKind(Type type)
		{
			type = GetNonNullableType(type);

		    if (type.IsEnum)
		    {
		        return 0;
		    }

			switch (Type.GetTypeCode(type))
			{
				case TypeCode.Char:
				case TypeCode.Single:
				case TypeCode.Double:
				case TypeCode.Decimal:
					return 1;
				case TypeCode.SByte:
				case TypeCode.Int16:
				case TypeCode.Int32:
				case TypeCode.Int64:
					return 2;
				case TypeCode.Byte:
				case TypeCode.UInt16:
				case TypeCode.UInt32:
				case TypeCode.UInt64:
					return 3;
				default:
					return 0;
			}
		}

		private void CheckAndPromoteOperand(Type signatures, ref Expression expr)
		{
			var args = new[] { expr };

			args = PrepareOperandArguments(signatures, args);

			expr = args[0];
		}

		private void CheckAndPromoteOperands(Type signatures, ref Expression left, ref Expression right)
		{
			var args = new[] { left, right };

			args = PrepareOperandArguments(signatures, args);

			left = args[0];
			right = args[1];
		}

		private Expression[] PrepareOperandArguments(Type signatures, Expression[] args)
		{
			var applicableMethods = FindMethods(signatures, "F", false, args);

		    return applicableMethods.Length == 1 ? applicableMethods[0].PromotedParameters : args;
		}

		private MemberInfo FindPropertyOrField(Type type, string memberName, bool staticAccess)
		{
			var flags = BindingFlags.Public |
                BindingFlags.DeclaredOnly |
                (staticAccess ? BindingFlags.Static : BindingFlags.Instance) |
                _bindingCase;

		    return SelfAndBaseTypes(type)
		        .Select(t => t.FindMembers(MemberTypes.Property | MemberTypes.Field, flags, _memberFilterCase, memberName))
		        .Where(members => members.Length != 0)
		        .Select(members => members[0])
                .FirstOrDefault();
		}

		private MethodData[] FindMethods(Type type, string methodName, bool staticAccess, Expression[] args)
		{
			var flags = BindingFlags.Public |
                BindingFlags.DeclaredOnly |
                (staticAccess ? BindingFlags.Static : BindingFlags.Instance) |
                _bindingCase;

			foreach (var t in SelfAndBaseTypes(type))
			{
				var members = t.FindMembers(MemberTypes.Method, flags, _memberFilterCase, methodName);
				var applicableMethods = FindBestMethod(members.Cast<MethodBase>(), args);

			    if (applicableMethods.Length > 0)
			    {
			        return applicableMethods;
			    }
			}

			return new MethodData[0];
		}

		private MethodData[] FindExtensionMethods(string methodName, Expression[] args)
		{
			var matchMethods = _arguments.GetExtensionMethods(methodName);

			return FindBestMethod(matchMethods, args);
		}

		private static MethodData[] FindIndexer(Type type, Expression[] args)
		{
			foreach (var t in SelfAndBaseTypes(type))
			{
				var members = t.GetDefaultMembers();

			    if (members.Length == 0)
			    {
			        continue;
			    }

			    var methods = members
			        .OfType<PropertyInfo>()
			        .Select(p => (MethodBase)p.GetGetMethod())
			        .Where(m => m != null);

			    var applicableMethods = FindBestMethod(methods, args);

			    if (applicableMethods.Length > 0)
			    {
			        return applicableMethods;
			    }
			}

			return new MethodData[0];
		}

		private static IEnumerable<Type> SelfAndBaseTypes(Type type)
		{
		    if (!type.IsInterface)
		    {
		        return SelfAndBaseClasses(type);
		    }

		    var types = new List<Type>();

            AddInterface(types, type);

		    types.Add(typeof(object));

		    return types;
		}

		private static IEnumerable<Type> SelfAndBaseClasses(Type type)
		{
			while (type != null)
			{
				yield return type;
				type = type.BaseType;
			}
		}

		private static void AddInterface(ICollection<Type> types, Type type)
		{
		    if (types.Contains(type))
		    {
		        return;
		    }

		    types.Add(type);

		    foreach (var t in type.GetInterfaces())
		    {
		        AddInterface(types, t);
		    }
		}

		private static MethodData[] FindBestMethod(IEnumerable<MethodBase> methods, Expression[] args)
		{
			var applicable = methods
                .Select(m => new MethodData { MethodBase = m, Parameters = m.GetParameters() })
                .Where(m => CheckIfMethodIsApplicableAndPrepareIt(m, args))
                .ToArray();

		    return applicable.Length > 1
		        ? applicable.Where(m => applicable.All(n => m == n || MethodHasPriority(args, m, n))).ToArray()
		        : applicable;
		}

		private static bool CheckIfMethodIsApplicableAndPrepareIt(MethodData method, IReadOnlyCollection<Expression> args)
		{
		    if (method.Parameters.Count(y => !y.HasDefaultValue) > args.Count)
		    {
		        return false;
		    }

		    var promotedArgs = new List<Expression>();
			var declaredWorkingParameters = 0;

			Type paramsArrayTypeFound = null;
			List<Expression> paramsArrayPromotedArgument = null;

			foreach (var currentArgument in args)
			{
				Type parameterType;

				if (paramsArrayTypeFound != null)
				{
					parameterType = paramsArrayTypeFound;
				}
				else
				{
					if (declaredWorkingParameters >= method.Parameters.Length)
					{
						return false;
					}

					var parameterDeclaration = method.Parameters[declaredWorkingParameters];

					if (parameterDeclaration.IsOut)
					{
						return false;
					}

					parameterType = parameterDeclaration.ParameterType;

					if (HasParamsArrayType(parameterDeclaration))
					{
						paramsArrayTypeFound = parameterType;
					}

					declaredWorkingParameters++;
				}

				if (paramsArrayPromotedArgument == null)
				{
					if (parameterType.IsGenericParameter)
					{
						promotedArgs.Add(currentArgument);
						continue;
					}

					var promoted = PromoteExpression(currentArgument, parameterType, true);

					if (promoted != null)
					{
						promotedArgs.Add(promoted);
						continue;
					}
				}

				if (paramsArrayTypeFound != null)
				{
					var promoted = PromoteExpression(currentArgument, paramsArrayTypeFound.GetElementType(), true);

					if (promoted != null)
					{
						paramsArrayPromotedArgument = paramsArrayPromotedArgument ?? new List<Expression>();
						paramsArrayPromotedArgument.Add(promoted);

						continue;
					}
				}

				return false;
			}

			if (paramsArrayPromotedArgument != null)
			{
				method.HasParamsArray = true;

                var paramsArrayElementType = paramsArrayTypeFound.GetElementType();

			    if (paramsArrayElementType == null)
			    {
			        throw new Exception("Type is not an array, element not found");
			    }

			    promotedArgs.Add(Expression.NewArrayInit(paramsArrayElementType, paramsArrayPromotedArgument));
			}

			promotedArgs.AddRange(method.Parameters.Skip(promotedArgs.Count).Select(x => Expression.Constant(x.DefaultValue)));

			method.PromotedParameters = promotedArgs.ToArray();

			if (method.MethodBase.IsGenericMethodDefinition && method.MethodBase is MethodInfo)
			{
			    var methodInfo = (MethodInfo) method.MethodBase;

				var actualGenericArgs = ExtractActualGenericArguments(
					method.Parameters.Select(p => p.ParameterType).ToArray(),
					method.PromotedParameters.Select(p => p.Type).ToArray());

				var genericArgs = methodInfo
                    .GetGenericArguments()
					.Select(p => actualGenericArgs[p.Name])
					.ToArray();

				method.MethodBase = methodInfo.MakeGenericMethod(genericArgs);
			}

			return true;
		}

		private static Dictionary<string, Type> ExtractActualGenericArguments(
            IReadOnlyList<Type> methodGenericParameters,
			IReadOnlyList<Type> methodActualParameters)
		{
			var extractedGenericTypes = new Dictionary<string, Type>();

			for (var i = 0; i < methodGenericParameters.Count; i++)
			{
				var requestedType = methodGenericParameters[i];
				var actualType = methodActualParameters[i];

				if (requestedType.IsGenericParameter)
				{
					extractedGenericTypes[requestedType.Name] = actualType;
				}
				else if (requestedType.ContainsGenericParameters)
				{
					var innerGenericTypes = ExtractActualGenericArguments(
                        requestedType.GetGenericArguments(),
                        actualType.GetGenericArguments());

				    foreach (var innerGenericType in innerGenericTypes)
				    {
				        extractedGenericTypes[innerGenericType.Key] = innerGenericType.Value;
				    }
				}
			}

			return extractedGenericTypes;
		}

		private static Expression PromoteExpression(Expression expr, Type type, bool exact)
		{
		    if (expr.Type == type)
		    {
		        return expr;
		    }

		    var ce = expr as ConstantExpression;

		    if (ce != null && ce == ParserConstants.NullLiteralExpression)
		    {
		        if (!type.IsValueType || IsNullableType(type))
		        {
		            return Expression.Constant(null, type);
		        }
		    }

		    if (type.IsGenericType)
			{
				var genericType = FindAssignableGenericType(expr.Type, type.GetGenericTypeDefinition());

			    if (genericType != null)
			    {
			        return Expression.Convert(expr, genericType);
			    }
			}

			if (IsCompatibleWith(expr.Type, type))
			{
				if (type.IsValueType || exact)
				{
					return Expression.Convert(expr, type);
				}

				return expr;
			}

			return null;
		}

		private static bool IsCompatibleWith(Type source, Type target)
		{
			if (source == target)
			{
				return true;
			}

			if (!target.IsValueType)
			{
				return target.IsAssignableFrom(source);
			}

			var st = GetNonNullableType(source);
            var tt = GetNonNullableType(target);

		    if (st != source && tt == target)
		    {
		        return false;
		    }

            var sc = st.IsEnum ? TypeCode.Object : Type.GetTypeCode(st);
			var tc = tt.IsEnum ? TypeCode.Object : Type.GetTypeCode(tt);

            switch (sc)
			{
				case TypeCode.SByte:
					switch (tc)
					{
						case TypeCode.SByte:
						case TypeCode.Int16:
						case TypeCode.Int32:
						case TypeCode.Int64:
						case TypeCode.Single:
						case TypeCode.Double:
						case TypeCode.Decimal:
							return true;
					}
					break;
				case TypeCode.Byte:
					switch (tc)
					{
						case TypeCode.Byte:
						case TypeCode.Int16:
						case TypeCode.UInt16:
						case TypeCode.Int32:
						case TypeCode.UInt32:
						case TypeCode.Int64:
						case TypeCode.UInt64:
						case TypeCode.Single:
						case TypeCode.Double:
						case TypeCode.Decimal:
							return true;
					}
					break;
				case TypeCode.Int16:
					switch (tc)
					{
						case TypeCode.Int16:
						case TypeCode.Int32:
						case TypeCode.Int64:
						case TypeCode.Single:
						case TypeCode.Double:
						case TypeCode.Decimal:
							return true;
					}
					break;
				case TypeCode.UInt16:
					switch (tc)
					{
						case TypeCode.UInt16:
						case TypeCode.Int32:
						case TypeCode.UInt32:
						case TypeCode.Int64:
						case TypeCode.UInt64:
						case TypeCode.Single:
						case TypeCode.Double:
						case TypeCode.Decimal:
							return true;
					}
					break;
				case TypeCode.Int32:
					switch (tc)
					{
						case TypeCode.Int32:
						case TypeCode.Int64:
						case TypeCode.Single:
						case TypeCode.Double:
						case TypeCode.Decimal:
							return true;
					}
					break;
				case TypeCode.UInt32:
					switch (tc)
					{
						case TypeCode.UInt32:
						case TypeCode.Int64:
						case TypeCode.UInt64:
						case TypeCode.Single:
						case TypeCode.Double:
						case TypeCode.Decimal:
							return true;
					}
					break;
				case TypeCode.Int64:
					switch (tc)
					{
						case TypeCode.Int64:
						case TypeCode.Single:
						case TypeCode.Double:
						case TypeCode.Decimal:
							return true;
					}
					break;
				case TypeCode.UInt64:
					switch (tc)
					{
						case TypeCode.UInt64:
						case TypeCode.Single:
						case TypeCode.Double:
						case TypeCode.Decimal:
							return true;
					}
					break;
				case TypeCode.Single:
					switch (tc)
					{
						case TypeCode.Single:
						case TypeCode.Double:
							return true;
					}
					break;
				default:
			        if (st == tt)
			        {
			            return true;
			        }
					break;
			}

			return false;
		}

	    private static Type FindAssignableGenericType(Type givenType, Type genericTypeDefinition)
	    {
	        while (true)
	        {
	            var interfaceTypes = givenType.GetInterfaces();

	            foreach (var it in interfaceTypes.Where(it => it.IsGenericType && it.GetGenericTypeDefinition() == genericTypeDefinition))
	            {
	                return it;
	            }

	            if (givenType.IsGenericType && givenType.GetGenericTypeDefinition() == genericTypeDefinition)
	            {
	                return givenType;
	            }

	            var baseType = givenType.BaseType;

	            if (baseType == null)
	            {
	                return null;
	            }

	            givenType = baseType;
	        }
	    }

	    private static bool HasParamsArrayType(ICustomAttributeProvider parameterInfo)
		{
		    return parameterInfo.IsDefined(typeof (ParamArrayAttribute), false);
		}

		private static Type GetParameterType(ParameterInfo parameterInfo)
		{
			var isParamsArray = HasParamsArrayType(parameterInfo);

		    return isParamsArray
		        ? parameterInfo.ParameterType.GetElementType()
		        : parameterInfo.ParameterType;
		}

		private static bool MethodHasPriority(IReadOnlyList<Expression> args, MethodData method, MethodData otherMethod)
		{
		    if (!method.HasParamsArray && otherMethod.HasParamsArray)
		    {
		        return true;
		    }

		    if (method.HasParamsArray && !otherMethod.HasParamsArray)
		    {
		        return false;
		    }

			var better = false;

            for (var i = 0; i < args.Count; i++)
			{
				var methodParam = method.Parameters[i];
				var otherMethodParam = otherMethod.Parameters[i];
				var methodParamType = GetParameterType(methodParam);
				var otherMethodParamType = GetParameterType(otherMethodParam);
				var c = CompareConversions(args[i].Type, methodParamType, otherMethodParamType);

			    if (c < 0)
			    {
			        return false;
			    }

			    if (c > 0)
			    {
			        better = true;
			    }

			    if (HasParamsArrayType(methodParam) || HasParamsArrayType(otherMethodParam))
			    {
			        break;
			    }
			}

			return better;
		}

		private static int CompareConversions(Type s, Type t1, Type t2)
		{
		    if (t1 == t2)
		    {
		        return 0;
		    }

		    if (s == t1)
		    {
		        return 1;
		    }

		    if (s == t2)
		    {
		        return -1;
		    }

			var assignableT1 = t1.IsAssignableFrom(s);
			var assignableT2 = t2.IsAssignableFrom(s);

		    if (assignableT1 && !assignableT2)
		    {
		        return 1;
		    }

		    if (assignableT2 && !assignableT1)
		    {
		        return -1;
		    }

			var compatibleT1T2 = IsCompatibleWith(t1, t2);
			var compatibleT2T1 = IsCompatibleWith(t2, t1);

		    if (compatibleT1T2 && !compatibleT2T1)
		    {
		        return 1;
		    }

		    if (compatibleT2T1 && !compatibleT1T2)
		    {
		        return -1;
		    }

		    if (IsSignedIntegralType(t1) && IsUnsignedIntegralType(t2))
		    {
		        return 1;
		    }

		    if (IsSignedIntegralType(t2) && IsUnsignedIntegralType(t1))
		    {
		        return -1;
		    }

			return 0;
		}

		private static Expression GenerateEqual(Expression left, Expression right)
		{
			return Expression.Equal(left, right);
		}

		private static Expression GenerateNotEqual(Expression left, Expression right)
		{
			return Expression.NotEqual(left, right);
		}

		private static Expression GenerateGreaterThan(Expression left, Expression right)
		{
		    return left.Type == typeof (string)
		        ? Expression.GreaterThan(GenerateStaticMethodCall("Compare", left, right), Expression.Constant(0))
		        : Expression.GreaterThan(left, right);
		}

	    private static Expression GenerateGreaterThanEqual(Expression left, Expression right)
	    {
	        return left.Type == typeof (string)
	            ? Expression.GreaterThanOrEqual(GenerateStaticMethodCall("Compare", left, right), Expression.Constant(0))
	            : Expression.GreaterThanOrEqual(left, right);
	    }

	    private static Expression GenerateLessThan(Expression left, Expression right)
	    {
	        return left.Type == typeof (string)
	            ? Expression.LessThan(GenerateStaticMethodCall("Compare", left, right), Expression.Constant(0))
	            : Expression.LessThan(left, right);
	    }

	    private static Expression GenerateLessThanEqual(Expression left, Expression right)
	    {
	        return left.Type == typeof (string)
	            ? Expression.LessThanOrEqual(GenerateStaticMethodCall("Compare", left, right), Expression.Constant(0))
	            : Expression.LessThanOrEqual(left, right);
	    }

	    private static Expression GenerateAdd(Expression left, Expression right)
		{
			return Expression.Add(left, right);
		}

		private static Expression GenerateSubtract(Expression left, Expression right)
		{
			return Expression.Subtract(left, right);
		}

		private static Expression GenerateStringConcat(Expression left, Expression right)
		{
			var concatMethod = typeof(string).GetMethod("Concat", new[] {typeof(object), typeof(object)});

		    if (concatMethod == null)
		    {
		        throw new Exception("String concat method not found");
		    }

		    var rightObj = right.Type.IsValueType ? Expression.ConvertChecked(right, typeof(object)) : right;
			var leftObj = left.Type.IsValueType ? Expression.ConvertChecked(left, typeof(object)) : left;

			return Expression.Call(null, concatMethod, new[] { leftObj, rightObj });
		}

		private static MethodInfo GetStaticMethod(string methodName, Expression left, Expression right)
		{
			return left.Type.GetMethod(methodName, new[] { left.Type, right.Type });
		}

		private static Expression GenerateStaticMethodCall(string methodName, Expression left, Expression right)
		{
			return Expression.Call(null, GetStaticMethod(methodName, left, right), new[] { left, right });
		}

		private void SetTextPos(int pos)
		{
			_parsePosition = pos;
			_parseChar = _parsePosition < _expressionTextLength ? _expressionText[_parsePosition] : '\0';
		}

		private void NextChar()
		{
		    if (_parsePosition < _expressionTextLength)
		    {
		        _parsePosition++;
		    }

		    _parseChar = _parsePosition < _expressionTextLength ? _expressionText[_parsePosition] : '\0';
		}

		private void PreviousChar()
		{
			SetTextPos(_parsePosition - 1);
		}

		private void NextToken()
		{
		    while (char.IsWhiteSpace(_parseChar))
		    {
		        NextChar();
		    }

		    TokenId t;
			var tokenPos = _parsePosition;

            switch (_parseChar)
			{
				case '!':
					NextChar();

					if (_parseChar == '=')
					{
						NextChar();
						t = TokenId.ExclamationEqual;
					}
					else
					{
						t = TokenId.Exclamation;
					}
					break;
				case '%':
					NextChar();
					t = TokenId.Percent;
					break;
				case '&':
					NextChar();

                    if (_parseChar == '&')
					{
						NextChar();
						t = TokenId.DoubleAmphersand;
					}
					else
					{
						throw CreateParseException(_parsePosition, ErrorMessages.InvalidCharacter, _parseChar);
					}
					break;
				case '(':
					NextChar();
					t = TokenId.OpenParen;
					break;
				case ')':
					NextChar();
					t = TokenId.CloseParen;
					break;
				case '*':
					NextChar();
					t = TokenId.Asterisk;
					break;
				case '+':
					NextChar();
					t = TokenId.Plus;
					break;
				case ',':
					NextChar();
					t = TokenId.Comma;
					break;
				case '-':
					NextChar();
					t = TokenId.Minus;
					break;
				case '.':
					NextChar();

					if (char.IsDigit(_parseChar))
					{
						t = TokenId.RealLiteral;

						do
						{
							NextChar();
						} while (char.IsDigit(_parseChar));

                        if (_parseChar == 'E' || _parseChar == 'e')
						{
							t = TokenId.RealLiteral;
							NextChar();

						    if (_parseChar == '+' || _parseChar == '-')
						    {
						        NextChar();
						    }

						    ValidateDigit();

                            do
							{
								NextChar();
							} while (char.IsDigit(_parseChar));
						}

					    if (_parseChar == 'F' || _parseChar == 'f' || _parseChar == 'M' || _parseChar == 'm')
					    {
					        NextChar();
					    }
					    break;
					}

					t = TokenId.Dot;
					break;
				case '/':
					NextChar();
					t = TokenId.Slash;
					break;
				case ':':
					NextChar();
					t = TokenId.Colon;
					break;
				case '<':
					NextChar();

                    if (_parseChar == '=')
					{
						NextChar();
						t = TokenId.LessThanEqual;
					}
					else
					{
						t = TokenId.LessThan;
					}
					break;
				case '=':
					NextChar();

                    if (_parseChar == '=')
					{
						NextChar();
						t = TokenId.DoubleEqual;
					}
					else
					{
						t = TokenId.Equal;
					}
					break;
				case '>':
					NextChar();

                    if (_parseChar == '=')
					{
						NextChar();
						t = TokenId.GreaterThanEqual;
					}
					else
					{
						t = TokenId.GreaterThan;
					}
					break;
				case '?':
					NextChar();
					t = TokenId.Question;
					break;
				case '[':
					NextChar();
					t = TokenId.OpenBracket;
					break;
				case ']':
					NextChar();
					t = TokenId.CloseBracket;
					break;
				case '|':
					NextChar();

                    if (_parseChar == '|')
					{
						NextChar();
						t = TokenId.DoubleBar;
					}
					else
					{
						t = TokenId.Bar;
					}
					break;
				case '"':
					NextChar();

                    var isEscapeS = false;
					var isEndS = _parseChar == '\"';

                    while (_parsePosition < _expressionTextLength && !isEndS)
					{
						isEscapeS = _parseChar == '\\' && !isEscapeS;

						NextChar();

						isEndS = _parseChar == '\"' && !isEscapeS;
					}

			        if (_parsePosition == _expressionTextLength)
			        {
			            throw CreateParseException(_parsePosition, ErrorMessages.UnterminatedStringLiteral);
			        }

			        NextChar();

					t = TokenId.StringLiteral;
					break;
				case '\'':
					NextChar();

                    var isEscapeC = false;
					var isEndC = false;

                    while (_parsePosition < _expressionTextLength && !isEndC)
					{
						isEscapeC = _parseChar == '\\' && !isEscapeC;

						NextChar();

                        isEndC = (_parseChar == '\'' && !isEscapeC);
					}

			        if (_parsePosition == _expressionTextLength)
			        {
			            throw CreateParseException(_parsePosition, ErrorMessages.UnterminatedStringLiteral);
			        }

			        NextChar();

					t = TokenId.CharLiteral;
					break;
				default:
					if (char.IsLetter(_parseChar) || _parseChar == '@' || _parseChar == '_')
					{
						do
						{
							NextChar();
						} while (char.IsLetterOrDigit(_parseChar) || _parseChar == '_');

                        t = TokenId.Identifier;
						break;
					}

					if (char.IsDigit(_parseChar))
					{
						t = TokenId.IntegerLiteral;

                        do
						{
							NextChar();
						} while (char.IsDigit(_parseChar));

						if (_parseChar == '.')
						{
							NextChar();

                            if (char.IsDigit(_parseChar))
							{
								t = TokenId.RealLiteral;

                                do
								{
									NextChar();
								} while (char.IsDigit(_parseChar));
							}
							else
							{
								PreviousChar();
								break;
							}
						}

						if (_parseChar == 'E' || _parseChar == 'e')
						{
							t = TokenId.RealLiteral;

                            NextChar();

						    if (_parseChar == '+' || _parseChar == '-')
						    {
						        NextChar();
						    }

                            ValidateDigit();

                            do
							{
								NextChar();
							} while (char.IsDigit(_parseChar));
						}

						if (_parseChar == 'F' || _parseChar == 'f' || _parseChar == 'M' || _parseChar == 'm')
						{
							t = TokenId.RealLiteral;
                            NextChar();
						}

						break;
					}

					if (_parsePosition == _expressionTextLength)
					{
						t = TokenId.End;
						break;
					}

					throw CreateParseException(_parsePosition, ErrorMessages.InvalidCharacter, _parseChar);
			}

			_token.ID = t;
			_token.Text = _expressionText.Substring(tokenPos, _parsePosition - tokenPos);
			_token.Pos = tokenPos;
		}

		private string GetIdentifier()
		{
			ValidateToken(TokenId.Identifier, ErrorMessages.IdentifierExpected);

            var id = _token.Text;

		    return id.Length > 1 && id[0] == '@' ? id.Substring(1) : id;
		}

		private void ValidateDigit()
		{
		    if (!char.IsDigit(_parseChar))
		    {
		        throw CreateParseException(_parsePosition, ErrorMessages.DigitExpected);
		    }
		}

	    // ReSharper disable once UnusedParameter.Local
		private void ValidateToken(TokenId t, string errorMessage)
		{
		    if (_token.ID != t)
		    {
		        throw CreateParseException(_token.Pos, errorMessage);
		    }
		}

	    // ReSharper disable once UnusedParameter.Local
		private void ValidateToken(TokenId t)
		{
		    if (_token.ID != t)
		    {
		        throw CreateParseException(_token.Pos, ErrorMessages.SyntaxError);
		    }
		}

		private static Exception CreateParseException(int pos, string format, params object[] args)
		{
			return new ParseException(string.Format(format, args), pos);
		}

		private class MethodData
		{
			public MethodBase MethodBase;

			public ParameterInfo[] Parameters;

			public Expression[] PromotedParameters;

			public bool HasParamsArray;
		}
	}
}