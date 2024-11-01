using DynamicExpresso.Parsing;
using DynamicExpresso.Reflection;
using DynamicExpresso.Visitors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using DynamicExpresso.Exceptions;

namespace DynamicExpresso
{
	/// <summary>
	/// Class used to parse and compile a text expression into an Expression or a Delegate that can be invoked. Expression are written using a subset of C# syntax.
	/// Only get properties, Parse and Eval methods are thread safe.
	/// </summary>
	public class Interpreter
	{
		private readonly ParserSettings _settings;
		private readonly ISet<ExpressionVisitor> _visitors = new HashSet<ExpressionVisitor>();

		#region Constructors

		/// <summary>
		/// Creates a new Interpreter using InterpreterOptions.Default.
		/// </summary>
		public Interpreter()
			: this(InterpreterOptions.Default)
		{
		}

		/// <summary>
		/// Creates a new Interpreter using the specified options.
		/// </summary>
		/// <param name="options"></param>
		public Interpreter(InterpreterOptions options)
		{
			var caseInsensitive = options.HasFlag(InterpreterOptions.CaseInsensitive);

			var lateBindObject = options.HasFlag(InterpreterOptions.LateBindObject);

			_settings = new ParserSettings(caseInsensitive, lateBindObject);

			if ((options & InterpreterOptions.SystemKeywords) == InterpreterOptions.SystemKeywords)
			{
				SetIdentifiers(LanguageConstants.Literals);
			}

			if ((options & InterpreterOptions.PrimitiveTypes) == InterpreterOptions.PrimitiveTypes)
			{
				Reference(LanguageConstants.PrimitiveTypes);
				Reference(LanguageConstants.CSharpPrimitiveTypes);
			}

			if ((options & InterpreterOptions.CommonTypes) == InterpreterOptions.CommonTypes)
			{
				Reference(LanguageConstants.CommonTypes);
			}

			if ((options & InterpreterOptions.LambdaExpressions) == InterpreterOptions.LambdaExpressions)
			{
				_settings.LambdaExpressions = true;
			}

			_visitors.Add(new DisableReflectionVisitor());
		}

		/// <summary>
		/// Create a new interpreter with the settings copied from another interpreter
		/// </summary>
		internal Interpreter(ParserSettings settings)
		{
			_settings = settings;
		}

		#endregion

		#region Properties

		public bool CaseInsensitive
		{
			get
			{
				return _settings.CaseInsensitive;
			}
		}

		/// <summary>
		/// Gets a list of registeres types. Add types by using the Reference method.
		/// </summary>
		public IEnumerable<ReferenceType> ReferencedTypes
		{
			get
			{
				return _settings.KnownTypes
					.Select(p => p.Value)
					.ToList();
			}
		}

		/// <summary>
		/// Gets a list of known identifiers. Add identifiers using SetVariable, SetFunction or SetExpression methods.
		/// </summary>
		public IEnumerable<Identifier> Identifiers
		{
			get
			{
				return _settings.Identifiers
					.Select(p => p.Value)
					.ToList();
			}
		}

		/// <summary>
		/// Gets the available assignment operators.
		/// </summary>
		public AssignmentOperators AssignmentOperators
		{
			get { return _settings.AssignmentOperators; }
		}

		#endregion

		#region Options

		/// <summary>
		/// Allow to set de default numeric type when no suffix is specified (Int by default, Double if real number)
		/// </summary>
		/// <param name="defaultNumberType"></param>
		/// <returns></returns>
		public Interpreter SetDefaultNumberType(DefaultNumberType defaultNumberType)
		{
			_settings.DefaultNumberType = defaultNumberType;
			return this;
		}

		/// <summary>
		/// Allows to enable/disable assignment operators. 
		/// For security when expression are generated by the users is more safe to disable assignment operators.
		/// </summary>
		/// <param name="assignmentOperators"></param>
		/// <returns></returns>
		public Interpreter EnableAssignment(AssignmentOperators assignmentOperators)
		{
			_settings.AssignmentOperators = assignmentOperators;

			return this;
		}

		#endregion

		#region Visitors

		public ISet<ExpressionVisitor> Visitors
		{
			get { return _visitors; }
		}

		/// <summary>
		/// Enable reflection expression (like x.GetType().GetMethod() or typeof(double).Assembly) by removing the DisableReflectionVisitor.
		/// </summary>
		/// <returns></returns>
		public Interpreter EnableReflection()
		{
			var visitor = Visitors.FirstOrDefault(p => p is DisableReflectionVisitor);
			if (visitor != null)
				Visitors.Remove(visitor);

			return this;
		}

		#endregion

		#region Register identifiers

		/// <summary>
		/// Allow the specified function delegate to be called from a parsed expression.
		/// Overloads can be added (ie. multiple delegates can be registered with the same name).
		/// A delegate will replace any delegate with the exact same signature that is already registered.
		/// </summary>
		/// <param name="name"></param>
		/// <param name="value"></param>
		/// <returns></returns>
		public Interpreter SetFunction(string name, Delegate value)
		{
			if (string.IsNullOrWhiteSpace(name))
				throw new ArgumentNullException(nameof(name));

			if (_settings.Identifiers.TryGetValue(name, out var identifier) &&
			    identifier is FunctionIdentifier fIdentifier)
			{
				fIdentifier.AddOverload(value);
			}
			else
			{
				SetIdentifier(new FunctionIdentifier(name, value));
			}

			return this;
		}

		/// <summary>
		/// Allow the specified variable to be used in a parsed expression.
		/// </summary>
		/// <param name="name"></param>
		/// <param name="value"></param>
		/// <returns></returns>
		public Interpreter SetVariable(string name, object value)
		{
			if (string.IsNullOrWhiteSpace(name))
				throw new ArgumentNullException(nameof(name));

			return SetExpression(name, Expression.Constant(value));
		}

		/// <summary>
		/// Allow the specified variable to be used in a parsed expression.
		/// </summary>
		/// <param name="name"></param>
		/// <param name="value"></param>
		/// <returns></returns>
		public Interpreter SetVariable<T>(string name, T value)
		{
			return SetVariable(name, value, typeof(T));
		}

		/// <summary>
		/// Allow the specified variable to be used in a parsed expression.
		/// </summary>
		/// <param name="name"></param>
		/// <param name="value"></param>
		/// <param name="type"></param>
		/// <returns></returns>
		public Interpreter SetVariable(string name, object value, Type type)
		{
			if (type == null)
				throw new ArgumentNullException(nameof(type));
			if (string.IsNullOrWhiteSpace(name))
				throw new ArgumentNullException(nameof(name));

			return SetExpression(name, Expression.Constant(value, type));
		}

		/// <summary>
		/// Allow the specified Expression to be used in a parsed expression.
		/// Basically add the specified expression as a known identifier.
		/// </summary>
		/// <param name="name"></param>
		/// <param name="expression"></param>
		/// <returns></returns>
		public Interpreter SetExpression(string name, Expression expression)
		{
			return SetIdentifier(new Identifier(name, expression));
		}

		/// <summary>
		/// Allow the specified list of identifiers to be used in a parsed expression.
		/// Basically add the specified expressions as a known identifier.
		/// </summary>
		/// <param name="identifiers"></param>
		/// <returns></returns>
		public Interpreter SetIdentifiers(IEnumerable<Identifier> identifiers)
		{
			foreach (var i in identifiers)
				SetIdentifier(i);

			return this;
		}

		/// <summary>
		/// Allow the specified identifier to be used in a parsed expression.
		/// Basically add the specified expression as a known identifier.
		/// </summary>
		/// <param name="identifier"></param>
		/// <returns></returns>
		public Interpreter SetIdentifier(Identifier identifier)
		{
			if (identifier == null)
				throw new ArgumentNullException(nameof(identifier));

			if (LanguageConstants.ReservedKeywords.Contains(identifier.Name))
				throw new InvalidOperationException($"{identifier.Name} is a reserved word");

			_settings.Identifiers[identifier.Name] = identifier;

			return this;
		}

		/// <summary>
		/// Remove <paramref name="name"/> from the list of known identifiers.
		/// </summary>
		/// <param name="name"></param>>
		/// <returns></returns>
		public Interpreter UnsetFunction(string name)
		{
			return UnsetIdentifier(name);
		}

		/// <summary>
		/// Remove <paramref name="name"/> from the list of known identifiers.
		/// </summary>
		/// <param name="name"></param>>
		/// <returns></returns>
		public Interpreter UnsetVariable(string name)
		{
			return UnsetIdentifier(name);
		}

		/// <summary>
		/// Remove <paramref name="name"/> from the list of known identifiers.
		/// </summary>
		/// <param name="name"></param>>
		/// <returns></returns>
		public Interpreter UnsetExpression(string name)
		{
			return UnsetIdentifier(name);
		}

		/// <summary>
		/// Remove <paramref name="name"/> from the list of known identifiers.
		/// </summary>
		/// <param name="name"></param>
		/// <returns></returns>
		public Interpreter UnsetIdentifier(string name)
		{
			if (string.IsNullOrWhiteSpace(name))
				throw new ArgumentNullException(nameof(name));

			_settings.Identifiers.Remove(name);
			return this;
		}

		#endregion

		#region Register referenced types

		/// <summary>
		/// Allow the specified type to be used inside an expression. The type will be available using its name.
		/// If the type contains method extensions methods they will be available inside expressions.
		/// </summary>
		/// <param name="type"></param>
		/// <returns></returns>
		public Interpreter Reference(Type type)
		{
			if (type == null)
				throw new ArgumentNullException(nameof(type));

			return Reference(type, type.Name);
		}

		/// <summary>
		/// Allow the specified type to be used inside an expression.
		/// See Reference(Type, string) method.
		/// </summary>
		/// <param name="types"></param>
		/// <returns></returns>
		public Interpreter Reference(IEnumerable<ReferenceType> types)
		{
			if (types == null)
				throw new ArgumentNullException(nameof(types));

			foreach (var t in types)
				Reference(t);

			return this;
		}

		/// <summary>
		/// Allow the specified type to be used inside an expression by using a custom alias.
		/// If the type contains extensions methods they will be available inside expressions.
		/// </summary>
		/// <param name="type"></param>
		/// <param name="typeName">Public name that must be used in the expression.</param>
		/// <returns></returns>
		public Interpreter Reference(Type type, string typeName)
		{
			return Reference(new ReferenceType(typeName, type));
		}

		/// <summary>
		/// Allow the specified type to be used inside an expression by using a custom alias.
		/// If the type contains extensions methods they will be available inside expressions.
		/// </summary>
		/// <param name="type"></param>
		/// <returns></returns>
		public Interpreter Reference(ReferenceType type)
		{
			if (type == null)
				throw new ArgumentNullException(nameof(type));

			_settings.KnownTypes[type.Name] = type;

			foreach (var extensionMethod in type.ExtensionMethods)
			{
				_settings.ExtensionMethods.Add(extensionMethod);
			}

			return this;
		}

		#endregion

		#region Parse

		/// <summary>
		/// Parse a text expression and returns a Lambda class that can be used to invoke it.
		/// </summary>
		/// <param name="expressionText">Expression statement</param>
		/// <param name="parameters"></param>
		/// <returns></returns>
		/// <exception cref="ParseException"></exception>
		public Lambda Parse(string expressionText, params Parameter[] parameters)
		{
			return Parse(expressionText, typeof(void), parameters);
		}

		/// <summary>
		/// Parse a text expression and returns a Lambda class that can be used to invoke it.
		/// If the expression cannot be converted to the type specified in the expressionType parameter
		/// an exception is throw.
		/// </summary>
		/// <param name="expressionText">Expression statement</param>
		/// <param name="expressionType">The expected return type. Use void or object type if there isn't an expected return type.</param>
		/// <param name="parameters"></param>
		/// <returns></returns>
		/// <exception cref="ParseException"></exception>
		public Lambda Parse(string expressionText, Type expressionType, params Parameter[] parameters)
		{
			return ParseAsLambda(expressionText, expressionType, parameters);
		}

		[Obsolete("Use ParseAsDelegate<TDelegate>(string, params string[])")]
		public TDelegate Parse<TDelegate>(string expressionText, params string[] parametersNames)
		{
			return ParseAsDelegate<TDelegate>(expressionText, parametersNames);
		}

		/// <summary>
		/// Parse a text expression and convert it into a delegate.
		/// </summary>
		/// <typeparam name="TDelegate">Delegate to use</typeparam>
		/// <param name="expressionText">Expression statement</param>
		/// <param name="parametersNames">Names of the parameters. If not specified the parameters names defined inside the delegate are used.</param>
		/// <returns></returns>
		/// <exception cref="ParseException"></exception>
		public TDelegate ParseAsDelegate<TDelegate>(string expressionText, params string[] parametersNames)
		{
			var lambda = ParseAs<TDelegate>(expressionText, parametersNames);
			return lambda.Compile<TDelegate>();
		}

		/// <summary>
		/// Parse a text expression and convert it into a lambda expression.
		/// </summary>
		/// <typeparam name="TDelegate">Delegate to use</typeparam>
		/// <param name="expressionText">Expression statement</param>
		/// <param name="parametersNames">Names of the parameters. If not specified the parameters names defined inside the delegate are used.</param>
		/// <returns></returns>
		/// <exception cref="ParseException"></exception>
		public Expression<TDelegate> ParseAsExpression<TDelegate>(string expressionText,
			params string[] parametersNames)
		{
			var lambda = ParseAs<TDelegate>(expressionText, parametersNames);
			return lambda.LambdaExpression<TDelegate>();
		}

		internal LambdaExpression ParseAsExpression(Type delegateType, string expressionText,
			params string[] parametersNames)
		{
			var delegateInfo = ReflectionExtensions.GetDelegateInfo(delegateType, parametersNames);

			// return type is object means that we have no information beforehand
			// => we force it to typeof(void) so that no conversion expression is emitted by the parser
			//    and the actual expression type is preserved
			var returnType = delegateInfo.ReturnType;
			if (returnType == typeof(object))
				returnType = typeof(void);

			var lambda = ParseAsLambda(expressionText, returnType, delegateInfo.Parameters);
			return lambda.LambdaExpression(delegateType);
		}

		public Lambda ParseAs<TDelegate>(string expressionText, params string[] parametersNames)
		{
			return ParseAs(typeof(TDelegate), expressionText, parametersNames);
		}

		internal Lambda ParseAs(Type delegateType, string expressionText, params string[] parametersNames)
		{
			var delegateInfo = ReflectionExtensions.GetDelegateInfo(delegateType, parametersNames);

			return ParseAsLambda(expressionText, delegateInfo.ReturnType, delegateInfo.Parameters);
		}

		#endregion

		#region Eval

		/// <summary>
		/// Parse and invoke the specified expression.
		/// </summary>
		/// <param name="expressionText"></param>
		/// <param name="parameters"></param>
		/// <returns></returns>
		public object Eval(string expressionText, params Parameter[] parameters)
		{
			return Eval(expressionText, typeof(void), parameters);
		}

		/// <summary>
		/// Parse and invoke the specified expression.
		/// </summary>
		/// <param name="expressionText"></param>
		/// <param name="parameters"></param>
		/// <returns></returns>
		public T Eval<T>(string expressionText, params Parameter[] parameters)
		{
			return (T)Eval(expressionText, typeof(T), parameters);
		}

		/// <summary>
		/// Parse and invoke the specified expression.
		/// </summary>
		/// <param name="expressionText"></param>
		/// <param name="expressionType">The return type of the expression. Use void or object if you don't know the expected return type.</param>
		/// <param name="parameters"></param>
		/// <returns></returns>
		public object Eval(string expressionText, Type expressionType, params Parameter[] parameters)
		{
			return Parse(expressionText, expressionType, parameters).Invoke(parameters);
		}

		#endregion

		#region Detection

		public IdentifiersInfo DetectIdentifiers(string expression, bool includeChildName = false)
		{
			var detector = new Detector(_settings);

			return detector.DetectIdentifiers(expression, includeChildName);
		}

		#endregion

		#region Private methods

		private Lambda ParseAsLambda(string expressionText, Type expressionType, Parameter[] parameters)
		{
			var arguments = new ParserArguments(
				expressionText,
				_settings,
				expressionType,
				parameters);

			var expression = Parser.Parse(arguments);

			foreach (var visitor in Visitors)
				expression = visitor.Visit(expression);

			var lambda = new Lambda(expression, arguments);

#if TEST_DetectIdentifiers
			AssertDetectIdentifiers(lambda);
#endif

			return lambda;
		}

#if TEST_DetectIdentifiers
		private void AssertDetectIdentifiers(Lambda lambda)
		{
			var info = DetectIdentifiers(lambda.ExpressionText);

			if (info.Identifiers.Count() != lambda.Identifiers.Count())
				throw new Exception("Detected identifiers doesn't match actual identifiers");
			if (info.Types.Count() != lambda.Types.Count())
				throw new Exception("Detected types doesn't match actual types");
			if (info.UnknownIdentifiers.Count() != lambda.UsedParameters.Count())
				throw new Exception("Detected unknown identifiers doesn't match actual parameters");
		}
#endif

		#endregion
	}
}
