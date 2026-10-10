using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GameStateStructure.Diagnostics
{
	/// <summary>宣言された遷移から mermaid の遷移図を生成する。</summary>
	public static class TransitionDiagram
	{
		enum EdgeKind
		{
			Push,
			GoTo,
			Process,
			Event,
		}

		readonly struct Edge : IEquatable<Edge>
		{
			public string From { get; }
			public string To { get; }
			public EdgeKind Kind { get; }

			/// <summary>辺の付加情報。車線の名前、またはイベント名。</summary>
			public string Branch { get; }

			public Edge(string from, string to, EdgeKind kind, string branch = null)
			{
				From = from;
				To = to;
				Kind = kind;
				Branch = branch;
			}

			public bool Equals(Edge other)
			{
				return From == other.From && To == other.To && Kind == other.Kind && Branch == other.Branch;
			}

			public override bool Equals(object obj) => obj is Edge other && Equals(other);

			public override int GetHashCode()
			{
				var hash = From?.GetHashCode() ?? 0;
				hash = (hash * 397) ^ (To?.GetHashCode() ?? 0);
				hash = (hash * 397) ^ (int)Kind;
				return (hash * 397) ^ (Branch?.GetHashCode() ?? 0);
			}
		}

		/// <summary>遷移図を生成する。入力の順序によらず同じ結果になる。</summary>
		public static string GenerateMermaid(IEnumerable<Type> states)
		{
			var nodes = new SortedDictionary<string, Type>(StringComparer.Ordinal);
			var edges = new List<Edge>();

			foreach (var type in states ?? Array.Empty<Type>())
			{
				if (type == null)
				{
					continue;
				}
				Register(nodes, type);
				foreach (var attribute in type.GetCustomAttributes(typeof(PushAttribute), true).Cast<PushAttribute>())
				{
					AddEdge(
						nodes,
						edges,
						type,
						attribute.Type,
						IsProcess(attribute.Type) ? EdgeKind.Process : EdgeKind.Push,
						attribute.Branch?.Name);
				}
				foreach (var attribute in type.GetCustomAttributes(typeof(GoToAttribute), true).Cast<GoToAttribute>())
				{
					AddEdge(nodes, edges, type, attribute.Type, EdgeKind.GoTo);
				}
				foreach (var entry in EventCatalog.Get(type))
				{
					AddEdge(nodes, edges, type, entry.Transition, EdgeKind.Event, entry.Label);
				}
			}

			var builder = new StringBuilder();
			builder.AppendLine("flowchart LR");
			builder.AppendLine("    classDef process stroke-dasharray: 4 3");

			foreach (var pair in nodes)
			{
				builder.Append("    ").Append(pair.Key).Append("[\"").Append(pair.Value.Name).Append("\"]");
				if (IsProcess(pair.Value))
				{
					builder.Append(":::process");
				}
				builder.AppendLine();
			}

			foreach (var edge in edges
				.Distinct()
				.OrderBy(x => x.From, StringComparer.Ordinal)
				.ThenBy(x => x.To, StringComparer.Ordinal)
				.ThenBy(x => x.Kind)
				.ThenBy(x => x.Branch, StringComparer.Ordinal))
			{
				builder.Append("    ").Append(edge.From);
				switch (edge.Kind)
				{
					case EdgeKind.Event:
						builder.Append($" -.->|Event:{edge.Branch}| ");
						break;
					case EdgeKind.GoTo:
						builder.Append(" -.->|GoTo| ");
						break;
					default:
						builder.Append(edge.Branch != null ? $" ==>|Branch:{edge.Branch}| "
							: edge.Kind == EdgeKind.Process ? " ==>|Process| " : " -->|Push| ");
						break;
				}
				builder.AppendLine(edge.To);
			}

			return builder.ToString();
		}

		static void AddEdge(IDictionary<string, Type> nodes, ICollection<Edge> edges, Type from, Type to, EdgeKind kind, string branch = null)
		{
			if (to == null)
			{
				return;
			}
			Register(nodes, to);
			edges.Add(new Edge(GetId(from), GetId(to), kind, branch));
		}

		static void Register(IDictionary<string, Type> nodes, Type type)
		{
			nodes[GetId(type)] = type;
		}

		static string GetId(Type type)
		{
			var name = type.FullName ?? type.Name;
			var builder = new StringBuilder(name.Length);
			foreach (var c in name)
			{
				builder.Append(char.IsLetterOrDigit(c) ? c : '_');
			}
			return builder.ToString();
		}

		static bool IsProcess(Type type)
		{
			if (type == null)
			{
				return false;
			}
			foreach (var interfaceType in type.GetInterfaces())
			{
				if (interfaceType == typeof(IProcess))
				{
					return true;
				}
				if (interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof(IProcess<>))
				{
					return true;
				}
			}
			return false;
		}
	}
}