using Microsoft.CodeAnalysis;

namespace GameStateStructure.Generator
{

	internal class TransAttribute
	{
		public enum ChangeType
		{
			None,
			GoTo,
			Push,
		}

		public static bool TryGet(AttributeData data, out TransAttribute attr)
		{
			ChangeType type = GetChangeType(data);
			if (type == ChangeType.None)
			{
				attr = null;
				return false;
			}
			attr = new TransAttribute(data, type);
			return true;

			static ChangeType GetChangeType(AttributeData data)
			{
				return data.AttributeClass.ToDisplayString() switch
				{
					"GameStateStructure.GoToAttribute" => ChangeType.GoTo,
					"GameStateStructure.PushAttribute" => ChangeType.Push,
					_ => ChangeType.None,
				};
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
			if (data.ConstructorArguments.Length >= 2)
			{
				Name = data.ConstructorArguments[1].Value?.ToString();
			}
			foreach (System.Collections.Generic.KeyValuePair<string, TypedConstant> arg in data.NamedArguments)
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