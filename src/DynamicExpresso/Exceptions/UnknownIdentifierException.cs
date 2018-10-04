using System;
using System.Runtime.Serialization;
using System.Security.Permissions;

namespace DynamicExpresso.Exceptions
{
	[Serializable]
	public class UnknownIdentifierException : ParseException
	{
		public UnknownIdentifierException(string identifier, int position)
			: base($"Unknown identifier '{identifier}'", position) 
		{
			Identifier = identifier;
		}

		public string Identifier { get; }

		protected UnknownIdentifierException(SerializationInfo info, StreamingContext context)
			: base(info, context) 
		{
			Identifier = info.GetString("Identifier");
		}

		[SecurityPermission(SecurityAction.Demand, SerializationFormatter = true)]
		public override void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			info.AddValue("Identifier", Identifier);

			base.GetObjectData(info, context);
		}
	}
}