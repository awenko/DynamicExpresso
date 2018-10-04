using System;
using System.Collections.Generic;
using System.Reflection;

namespace DynamicExpresso.Parsing
{
	internal class ParserSettings
	{
		private readonly Dictionary<string, Identifier> _identifiers;
		private readonly Dictionary<string, ReferenceType> _knownTypes;

	    public ParserSettings(bool caseInsensitive)
		{
			CaseInsensitive = caseInsensitive;

			KeyComparer = CaseInsensitive ? StringComparer.InvariantCultureIgnoreCase : StringComparer.InvariantCulture;

			KeyComparison = CaseInsensitive ? StringComparison.InvariantCultureIgnoreCase : StringComparison.InvariantCulture;

			_identifiers = new Dictionary<string, Identifier>(KeyComparer);

			_knownTypes = new Dictionary<string, ReferenceType>(KeyComparer);

			ExtensionMethods = new HashSet<MethodInfo>();

			AssignmentOperators = AssignmentOperators.All;
		}

		public IDictionary<string, ReferenceType> KnownTypes => _knownTypes;

	    public IDictionary<string, Identifier> Identifiers => _identifiers;

	    public HashSet<MethodInfo> ExtensionMethods { get; }

	    public bool CaseInsensitive
		{
			get;
        }

		public StringComparison KeyComparison
		{
			get;
			private set;
		}

		public IEqualityComparer<string> KeyComparer
		{
			get;
        }

		public AssignmentOperators AssignmentOperators
		{
			get;
			set;
		}
	}
}