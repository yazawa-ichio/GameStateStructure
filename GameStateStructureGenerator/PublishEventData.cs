using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace GameStateStructure.Generator
{

	internal class EventMethod
	{
		public IMethodSymbol Symbol;

		public SubscribeEventAttribute Subscribe { get; set; }

		public bool IsAsync()
		{
			var returnType = Symbol.ReturnType;
			if (returnType == null)
			{
				return false;
			}
			switch (returnType.ToDisplayString())
			{
				case "System.Threading.Tasks.Task":
				case "System.Threading.Tasks.ValueTask":
				case "Cysharp.Threading.Tasks.UniTask":
					return true;
			}
			return false;
		}
	}

	internal class PublishEventData
	{
		public static bool TryGet(AttributeData data, out PublishEventData attr)
		{
			if (data.AttributeClass.ToDisplayString() != "GameStateStructure.PublishEventAttribute")
			{
				attr = null;
				return false;
			}
			attr = new PublishEventData(data);
			return true;
		}

		public ITypeSymbol Type { get; private set; }

		public string Prefix { get; private set; }

		public List<EventMethod> Events = new();

		public PublishEventData(AttributeData data)
		{
			Type = data.ConstructorArguments[0].Value as ITypeSymbol;
			foreach (var kvp in data.NamedArguments)
			{
				switch (kvp.Key)
				{
					case "Prefix":
						Prefix = kvp.Value.Value as string;
						break;
				}
			}

			SubscribeEventAttribute all = null;
			foreach (var attr in Type.GetAttributes())
			{
				if (SubscribeEventAttribute.TryGet(attr, out all))
				{
					break;
				}
			}
			foreach (var member in Type.GetMembers())
			{
				if (all != null)
				{
					TrySetEvent(member, all);
					continue;
				}
				foreach (var attr in member.GetAttributes())
				{
					if (SubscribeEventAttribute.TryGet(attr, out var subscribe))
					{
						TrySetEvent(member, subscribe);
					}
				}
			}
		}


		void TrySetEvent(ISymbol member, SubscribeEventAttribute subscribe)
		{
			if (member is IMethodSymbol method && member.DeclaredAccessibility == Accessibility.Public)
			{
				var data = new EventMethod
				{
					Symbol = method,
					Subscribe = subscribe,
				};
				Events.Add(data);
			}
		}
	}
}