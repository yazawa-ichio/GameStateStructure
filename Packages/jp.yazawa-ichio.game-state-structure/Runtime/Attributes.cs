using System;

namespace GameStateStructure
{
	[AttributeUsage(AttributeTargets.Property)]
	public sealed class ArgAttribute : Attribute
	{
		public bool Option { get; set; }
	}

	[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
	public sealed class GoToAttribute : Attribute
	{
		public Type Type { get; private set; }

		public GoToAttribute(Type type)
		{
			Type = type;
		}
	}

	[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
	public sealed class PushAttribute : Attribute
	{
		public Type Type { get; private set; }

		public PushAttribute(Type type)
		{
			Type = type;
		}
	}

}