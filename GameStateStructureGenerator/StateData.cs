using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace GameStateStructure.Generator
{
	internal class StateArg
	{
		public bool Option;
		public string Name;
		public string ArgName;
		public string Type;
		public bool IsValueType;
	}

	internal class StateData
	{
		public string Name;
		public string ShortName;
		public bool IsProcess;
		public string Result;
		public List<StateArg> Args = new();

		public static StateData Create(AttributeData data)
		{
			ITypeSymbol symbol = data.ConstructorArguments[0].Value as ITypeSymbol;
			return Create(symbol);
		}

		public static StateData Create(ITypeSymbol symbol)
		{
			string name = symbol.ToDisplayString();
			StateData ret = new()
			{
				Name = name,
				ShortName = symbol.Name
			};
			foreach (INamedTypeSymbol interfaceType in symbol.Interfaces)
			{
				if (interfaceType.ToDisplayString().Contains("GameStateStructure.IProcess"))
				{
					ret.IsProcess = true;
					if (interfaceType.TypeArguments.Length > 0)
					{
						ret.Result = interfaceType.TypeArguments[0].ToDisplayString();
					}
					break;
				}
			}

			foreach (ISymbol member in symbol.GetMembers())
			{
				foreach (AttributeData attr in member.GetAttributes())
				{
					TrySetArg(ret, member, attr);
				}
			}

			ret.Args.Sort((a, b) => a.Option.CompareTo(b.Option));

			return ret;
		}

		private static void TrySetArg(StateData ret, ISymbol member, AttributeData attr)
		{
			if (member is IPropertySymbol property)
			{
				if (attr.AttributeClass.ToDisplayString() != "GameStateStructure.ArgAttribute")
				{
					return;
				}
				StateArg arg = new();
				ret.Args.Add(arg);
				foreach (KeyValuePair<string, TypedConstant> kvp in attr.NamedArguments)
				{
					switch (kvp.Key)
					{
						case "Option":
							arg.Option = (bool)kvp.Value.Value;
							break;
					}
				}
				arg.Name = member.Name;
				arg.ArgName = char.ToLower(member.Name[0], System.Globalization.CultureInfo.InvariantCulture) + member.Name.Substring(1);
				arg.Type = property.Type.ToDisplayString();
				arg.IsValueType = property.Type.IsValueType;
			}
		}


	}

}