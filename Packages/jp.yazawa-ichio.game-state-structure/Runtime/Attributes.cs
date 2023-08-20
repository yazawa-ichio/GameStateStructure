using System;

namespace GameStateStructure
{
	[AttributeUsage(AttributeTargets.Property)]
	public sealed class ArgAttribute : Attribute
	{
		public bool Required { get; set; }
	}

	public sealed class ChangeToAttribute : Attribute
	{
		public Type Type { get; private set; }

		public ChangeToAttribute(Type type)
		{
			Type = type;
		}
	}

	public sealed class PushAttribute : Attribute
	{
		public Type Type { get; private set; }

		public PushAttribute(Type type)
		{
			Type = type;
		}
	}

}