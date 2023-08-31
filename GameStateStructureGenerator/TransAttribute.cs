using Microsoft.CodeAnalysis;

namespace GameStateStructure.Generator
{

	public class TransAttribute
	{
		public enum ChangeType
		{
			None,
			GoTo,
			Push,
		}

		public static bool TryGet(AttributeData data, out TransAttribute attr)
		{
			var type = GetChangeType(data);
			if (type == ChangeType.None)
			{
				attr = null;
				return false;
			}
			attr = new TransAttribute(data, type);
			return true;

			ChangeType GetChangeType(AttributeData data)
			{
				switch (data.AttributeClass.ToDisplayString())
				{
					case "GameStateStructure.GoToAttribute":
						return ChangeType.GoTo;
					case "GameStateStructure.PushAttribute":
						return ChangeType.Push;
				}
				return ChangeType.None;
			}
		}

		private AttributeData m_Data;

		public ChangeType Type { get; private set; }

		public ITypeSymbol Symbol { get; private set; }

		public string Name { get; private set; }

		public TransAttribute(AttributeData data, ChangeType type)
		{
			m_Data = data;
			Type = type;
			Symbol = data.ConstructorArguments[0].Value as ITypeSymbol;
			foreach (var arg in data.NamedArguments)
			{
				switch (arg.Key)
				{
					case "Name":
						Name = arg.Value.Value as string;
						break;
				}
			}
		}

	}
}