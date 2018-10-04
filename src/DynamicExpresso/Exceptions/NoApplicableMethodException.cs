using System;
using System.Runtime.Serialization;
using System.Security.Permissions;

namespace DynamicExpresso.Exceptions
{
	[Serializable]
	public class NoApplicableMethodException : ParseException
	{
		public NoApplicableMethodException(string methodName, string methodTypeName, int position)
			: base($"No applicable method '{methodName}' exists in type '{methodTypeName}'", position) 
		{
			MethodTypeName = methodTypeName;
			MethodName = methodName;
		}

		public string MethodTypeName { get; }

		public string MethodName { get; }

		protected NoApplicableMethodException(SerializationInfo info, StreamingContext context)
			: base(info, context) 
		{
			MethodTypeName = info.GetString("MethodTypeName");
			MethodName = info.GetString("MethodName");
		}

		[SecurityPermission(SecurityAction.Demand, SerializationFormatter = true)]
		public override void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			info.AddValue("MethodName", MethodName);
			info.AddValue("MethodTypeName", MethodTypeName);

			base.GetObjectData(info, context);
		}
	}
}