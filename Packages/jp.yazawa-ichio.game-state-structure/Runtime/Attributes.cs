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

	[AttributeUsage(AttributeTargets.Method | AttributeTargets.Interface)]
	public sealed class SubscribeEventAttribute : Attribute
	{
		public bool Broadcast { get; set; } = false;

		public bool ChildOnly { get; set; } = true;
	}

	[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
	public sealed class PublishEventAttribute : Attribute
	{
		public Type Type { get; private set; }

		public string Prefix { get; set; }

		public PublishEventAttribute(Type type)
		{
			Type = type;
		}
	}

}