using System;
using System.Runtime.Serialization;
using System.Security.Permissions;

namespace DynamicExpresso.Exceptions
{
	[Serializable]
	public class DuplicateParameterException : DynamicExpressoException
	{
		public DuplicateParameterException(string identifier)
			: base($"The parameter '{identifier}' was defined more than once") 
		{
			Identifier = identifier;
		}

		public string Identifier { get; }

		protected DuplicateParameterException(SerializationInfo info, StreamingContext context)
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