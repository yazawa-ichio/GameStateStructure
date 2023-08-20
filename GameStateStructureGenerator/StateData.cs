using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;

namespace GameStateStructure.Generator
{
	class StateArg
	{
		public bool Required;
		public string Name;
		public string ArgName;
		public string Type;
		public bool IsValueType;
	}

	class StateData
	{
		public string Name;
		public string ShortName;
		public bool Modal;
		public string Result;
		public List<StateArg> Args = new();


		public static StateData Create(AttributeData data)
		{
			var symbol = data.ConstructorArguments[0].Value as ITypeSymbol;
			return Create(symbol);
		}

		public static StateData Create(ITypeSymbol symbol)
		{
			var name = symbol.ToDisplayString();
			var ret = new StateData();
			ret.Name = name;
			ret.ShortName = symbol.Name;

			foreach (var interfaceType in symbol.Interfaces)
			{
				if (interfaceType.ToDisplayString().Contains("GameStateStructure.IModule"))
				{
					ret.Modal = true;
					if (interfaceType.TypeArguments.Length > 0)
					{
						ret.Result = interfaceType.TypeArguments[0].ToDisplayString();
					}
					break;
				}
			}

			foreach (var member in symbol.GetMembers())
			{
				var attr = member.GetAttributes().FirstOrDefault(x => x.AttributeClass.ToDisplayString() == "GameStateStructure.ArgAttribute");
				if (attr == null)
				{
					continue;
				}
				StateArg arg = new();
				ret.Args.Add(arg);
				foreach (var kvp in attr.NamedArguments)
				{
					switch (kvp.Key)
					{
						case "Required":
							arg.Required = (bool)kvp.Value.Value;
							break;
					}
				}
				if (member is IPropertySymbol property)
				{
					arg.Name = member.Name;
					arg.ArgName = char.ToLower(member.Name[0], System.Globalization.CultureInfo.InvariantCulture) + member.Name.Substring(1);
					arg.Type = property.Type.ToDisplayString();
					arg.IsValueType = property.Type.IsValueType;
				}
			}

			ret.Args.Sort((a, b) => -a.Required.CompareTo(b.Required));

			return ret;
		}
	}

}