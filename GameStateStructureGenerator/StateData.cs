using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;

namespace GameStateStructure.Generator
{
	internal class StateArg
	{
		public bool Option;
		public string Name;
		public string ArgName;
		public string Type;
	}

	internal class StateData
	{
		public string Name;
		public string ShortName;
		public bool IsProcess;
		public string Result;
		public List<StateArg> Args = new();

		/// <summary>遷移先が IProcess かどうか。診断と生成で同じ判定を使う。</summary>
		public static bool IsProcessType(ITypeSymbol symbol)
		{
			foreach (INamedTypeSymbol interfaceType in symbol.AllInterfaces)
			{
				if (interfaceType.ToDisplayString().Contains("GameStateStructure.IProcess"))
				{
					return true;
				}
			}
			return false;
		}

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
			// 基底で実装された IProcess も拾う。Interfaces だと直接実装しか見えない
			foreach (INamedTypeSymbol interfaceType in symbol.AllInterfaces)
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

			// [Arg] は基底にも付けられる。実行時の代入は基底のプロパティも拾うため、
			// ここで宣言型だけを見ると引数が黙って消える。
			var seen = new HashSet<string>();
			for (INamedTypeSymbol type = symbol as INamedTypeSymbol; type != null; type = type.BaseType)
			{
				foreach (ISymbol member in type.GetMembers())
				{
					foreach (AttributeData attr in member.GetAttributes())
					{
						TrySetArg(ret, member, attr, seen);
					}
				}
			}

			// 派生側の引数の位置が変わらないよう、安定な並べ替えを使う
			ret.Args = ret.Args.OrderBy(x => x.Option).ToList();

			return ret;
		}

		private static void TrySetArg(StateData ret, ISymbol member, AttributeData attr, HashSet<string> seen)
		{
			if (member is IPropertySymbol property)
			{
				if (attr.AttributeClass.ToDisplayString() != "GameStateStructure.ArgAttribute")
				{
					return;
				}
				// 派生で同名を隠している場合は派生側を使う
				if (!seen.Add(member.Name))
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
			}
		}


	}

}