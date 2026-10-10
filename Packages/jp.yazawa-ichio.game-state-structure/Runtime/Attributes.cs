using System;

namespace GameStateStructure
{
	[AttributeUsage(AttributeTargets.Property)]
	public sealed class ArgAttribute : UnityEngine.Scripting.PreserveAttribute
	{
		public bool Option { get; set; }
	}

	[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
	public sealed class GoToAttribute : Attribute
	{
		public Type Type { get; private set; }

		public string Name { get; private set; }

		public GoToAttribute(Type type)
		{
			Type = type;
		}

		public GoToAttribute(Type type, string name)
		{
			Type = type;
			Name = name;
		}

	}

	[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
	public sealed class PushAttribute : Attribute
	{
		public Type Type { get; private set; }

		public string Name { get; private set; }

		/// <summary>子を入れる車線。未指定なら主軸。指定できるのは IProcess を実装した遷移先だけ。</summary>
		public Type Branch { get; set; }

		public PushAttribute(Type type)
		{
			Type = type;
		}

		public PushAttribute(Type type, string name)
		{
			Type = type;
			Name = name;
		}
	}

	[AttributeUsage(AttributeTargets.Method | AttributeTargets.Interface)]
	public sealed class SubscribeEventAttribute : Attribute
	{
		public bool Broadcast { get; set; } = false;

		public bool ChildOnly { get; set; } = true;

		/// <summary>true にすると発行関数を生成しない。</summary>
		public bool Internal { get; set; } = false;

		/// <summary>このイベントによる遷移先。遷移図が参照するだけで実行時の挙動には影響しない。</summary>
		public Type Transition { get; set; }
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