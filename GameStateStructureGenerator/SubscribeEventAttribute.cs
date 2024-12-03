using Microsoft.CodeAnalysis;

namespace GameStateStructure.Generator
{
	class SubscribeEventAttribute
	{
		public static bool TryGet(AttributeData data, out SubscribeEventAttribute attr)
		{
			if (data.AttributeClass.ToDisplayString() != "GameStateStructure.SubscribeEventAttribute")
			{
				attr = null;
				return false;
			}
			attr = new SubscribeEventAttribute(data);
			return true;
		}

		private AttributeData m_Data;

		public bool Broadcast { get; private set; } = false;

		public bool ChildOnly { get; private set; } = true;

		public SubscribeEventAttribute(AttributeData data)
		{
			m_Data = data;

			foreach (var kvp in data.NamedArguments)
			{
				switch (kvp.Key)
				{
					case "Broadcast":
						Broadcast = (bool)kvp.Value.Value;
						break;
					case "ChildOnly":
						ChildOnly = (bool)kvp.Value.Value;
						break;
				}
			}
		}

	}
}