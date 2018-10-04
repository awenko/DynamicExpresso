using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace DynamicExpresso
{
	public class IdentifiersInfo
    {
        public IEnumerable<string> UnknownIdentifiers { get; private set; }

        public IEnumerable<Identifier> Identifiers { get; private set; }

        public IEnumerable<ReferenceType> Types { get; private set; }

        public IdentifiersInfo([NotNull] IEnumerable<string> unknownIdentifiers,
            [NotNull] IEnumerable<Identifier> identifiers, [NotNull] IEnumerable<ReferenceType> types)
		{
            if (unknownIdentifiers == null) throw new ArgumentNullException(nameof(unknownIdentifiers));
            if (identifiers == null) throw new ArgumentNullException(nameof(identifiers));
            if (types == null) throw new ArgumentNullException(nameof(types));

            UnknownIdentifiers = unknownIdentifiers.ToList();
			Identifiers = identifiers.ToList();
			Types = types.ToList();
		}

	}
}