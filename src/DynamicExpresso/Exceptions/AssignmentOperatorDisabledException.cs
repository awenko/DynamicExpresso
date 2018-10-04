using System;
using System.Runtime.Serialization;
using System.Security.Permissions;

namespace DynamicExpresso.Exceptions
{
	[Serializable]
	public class AssignmentOperatorDisabledException : ParseException
	{
		public AssignmentOperatorDisabledException(string operatorString, int position)
			: base($"Assignment operator '{operatorString}' not allowed", position) 
		{
			OperatorString = operatorString;
		}

		public string OperatorString { get; }

		protected AssignmentOperatorDisabledException(
			SerializationInfo info,
			StreamingContext context)
			: base(info, context) 
		{
			OperatorString = info.GetString("OperatorString");
		}

		[SecurityPermission(SecurityAction.Demand, SerializationFormatter = true)]
		public override void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			info.AddValue("OperatorString", OperatorString);

			base.GetObjectData(info, context);
		}
	}
}